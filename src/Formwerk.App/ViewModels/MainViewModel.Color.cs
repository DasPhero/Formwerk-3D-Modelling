using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Formwerk.App.Resources;
using Formwerk.Core.Commands;
using Formwerk.Core.Model;

namespace Formwerk.App.ViewModels;

/// <summary>
/// Color per object (F-VIS-01): a palette and a hex field in the properties for any selection. A color applies to the selected objects and
/// to everything inside them (group, merge, boolean, pattern, mirror), as one undo step. The viewport, the project file and the 3MF export
/// already use <see cref="Node.Color"/>, so nothing else has to know about this.
/// </summary>
public sealed partial class MainViewModel
{
    private bool _syncingColor;

    /// <summary>The palette: twelve colors that are easy to tell apart, the first one is the default gray.</summary>
    public IReadOnlyList<ColorSwatch> ColorSwatches { get; } =
    [
        new(Texts.Get("Color.Name.Gray"), 0xFFB0B8C4),
        new(Texts.Get("Color.Name.White"), 0xFFF2F2F2),
        new(Texts.Get("Color.Name.Black"), 0xFF2B2B2B),
        new(Texts.Get("Color.Name.Red"), 0xFFD64545),
        new(Texts.Get("Color.Name.Orange"), 0xFFF08A24),
        new(Texts.Get("Color.Name.Yellow"), 0xFFF2D024),
        new(Texts.Get("Color.Name.Green"), 0xFF4CAF50),
        new(Texts.Get("Color.Name.Teal"), 0xFF26A69A),
        new(Texts.Get("Color.Name.Blue"), 0xFF3F7FD9),
        new(Texts.Get("Color.Name.Purple"), 0xFF8E5BC9),
        new(Texts.Get("Color.Name.Pink"), 0xFFE86FA8),
        new(Texts.Get("Color.Name.Brown"), 0xFF8D6240),
    ];

    /// <summary>The color section is shown: something is selected.</summary>
    [ObservableProperty]
    public partial bool IsColorVisible { get; set; }

    /// <summary>The color of everything selected (and inside it), or null when the colors differ or nothing is selected.</summary>
    [ObservableProperty]
    public partial uint? SelectionColor { get; set; }

    /// <summary>The selection has different colors.</summary>
    [ObservableProperty]
    public partial bool IsColorMixed { get; set; }

    /// <summary>The preview swatch: the color as #RRGGBB, or transparent when there is none to show.</summary>
    [ObservableProperty]
    public partial string ColorPreview { get; set; } = "Transparent";

    /// <summary>The hex field: RRGGBB, with or without #. A complete valid value is applied at once.</summary>
    [ObservableProperty]
    public partial string ColorHex { get; set; } = "";

    /// <summary>What is wrong with the hex field; empty when it is fine.</summary>
    [ObservableProperty]
    public partial string ColorError { get; set; } = "";

    partial void OnColorHexChanged(string value)
    {
        if (_syncingColor) return;

        var text = value.Trim();
        if (text.StartsWith('#')) text = text[1..];
        if (text.Length == 0)
        {
            ColorError = "";
            return;
        }

        if (text.Length == 6 && uint.TryParse(text, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var rgb))
        {
            ColorError = "";
            ApplyColor(0xFF000000u | rgb);
        }
        else
        {
            ColorError = Texts.Get("Color.Err.Hex");
        }
    }

    /// <summary>A click on a swatch of the palette.</summary>
    [RelayCommand]
    private void ApplyColor(ColorSwatch swatch) => ApplyColor(swatch.Argb);

    private void ApplyColor(uint argb)
    {
        argb |= 0xFF000000u;
        var top = SelectedTopLevel();
        var nodes = ColorTargets(top).Where(n => n.Color != argb).ToList();
        if (nodes.Count == 0) return;

        var commands = nodes.Select(n => (IDocumentCommand)new SetColorCommand(_document, n, argb)).ToList();
        if (commands.Count == 1) _history.Execute(commands[0]);
        else _history.Execute(new CompositeCommand(top.Count == 1 ? $"Color {top[0].Name}" : $"Color {top.Count} objects", commands));
    }

    /// <summary>The selected objects and everything inside them.</summary>
    private static IEnumerable<Node> ColorTargets(IEnumerable<Node> top) => top.SelectMany(n => n.Descendants().Prepend(n));

    /// <summary>Shows the color of the selection; called after the selection or the document changed.</summary>
    private void RefreshColor()
    {
        var colors = ColorTargets(SelectedTopLevel()).Select(n => n.Color).Distinct().ToList();
        IsColorVisible = colors.Count > 0;
        IsColorMixed = colors.Count > 1;
        SelectionColor = colors.Count == 1 ? colors[0] : null;
        ColorPreview = SelectionColor is { } c ? $"#{c & 0xFFFFFFu:X6}" : "Transparent";

        // keep what the user is typing when it already means this color (no jumping cursor)
        var shown = SelectionColor is { } s ? $"{s & 0xFFFFFFu:X6}" : "";
        var typed = ColorHex.Trim().TrimStart('#');
        if (string.Equals(typed, shown, StringComparison.OrdinalIgnoreCase)) return;

        _syncingColor = true;
        try
        {
            ColorHex = shown;
        }
        finally
        {
            _syncingColor = false;
        }

        ColorError = "";
    }
}

/// <summary>One color of the palette: its name and its ARGB value.</summary>
public sealed record ColorSwatch(string Name, uint Argb)
{
    /// <summary>#RRGGBB, for the brush of the swatch.</summary>
    public string Hex => $"#{Argb & 0xFFFFFFu:X6}";
}
