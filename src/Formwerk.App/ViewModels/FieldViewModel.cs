using CommunityToolkit.Mvvm.ComponentModel;
using Formwerk.App.Resources;

namespace Formwerk.App.ViewModels;

/// <summary>
/// A text box that accepts numbers, expressions, relative values and percentages (Leitprinzip 2).
/// Setting <see cref="Text"/> runs the commit callback, which returns an error message or null.
/// </summary>
public sealed partial class FieldViewModel : ObservableObject
{
    private readonly Func<string, string?> _commit;
    private readonly SynchronizationContext? _context = SynchronizationContext.Current;
    private string _text = "";

    public FieldViewModel(string label, string unit, Func<string, string?> commit)
    {
        Label = label;
        Unit = unit;
        _commit = commit;
    }

    public string Label { get; }

    public string Unit { get; }

    [ObservableProperty]
    public partial string? Error { get; set; }

    public bool HasError => !string.IsNullOrEmpty(Error);

    /// <summary>False for a field that does not apply right now (the end diameter of a cylindrical hole).</summary>
    [ObservableProperty]
    public partial bool IsVisible { get; set; } = true;

    /// <summary>True when the value of the field is linked to project variables (F-PAR-03): the field is marked.</summary>
    [ObservableProperty]
    public partial bool IsLinked { get; set; }

    /// <summary>Names the variables of a linked field.</summary>
    [ObservableProperty]
    public partial string? LinkTip { get; set; }

    /// <summary>Marks the field as linked to the given variables, or removes the mark when there are none.</summary>
    public void ShowLinks(IReadOnlyList<string> names)
    {
        IsLinked = names.Count > 0;
        LinkTip = names.Count > 0 ? Texts.Format("Props.LinkedTo", string.Join(", ", names)) : null;
    }

    public string Text
    {
        get => _text;
        set
        {
            if (_text == value) return;
            _text = value;
            OnPropertyChanged();
            Error = _commit(value);

            // The commit may have normalised the text (e.g. "+10" became "25"). WPF ignores a change notification
            // raised while it is still writing the value, so announce the current text once more afterwards.
            if (_context is null) OnPropertyChanged(nameof(Text));
            else _context.Post(_ => OnPropertyChanged(nameof(Text)), null);
        }
    }

    /// <summary>What the tool tip says: the error, or which variables the field is linked to.</summary>
    public string? Tip => HasError ? Error : LinkTip;

    partial void OnErrorChanged(string? value)
    {
        OnPropertyChanged(nameof(HasError));
        OnPropertyChanged(nameof(Tip));
    }

    partial void OnLinkTipChanged(string? value) => OnPropertyChanged(nameof(Tip));

    /// <summary>Shows the model value; clears a previous error.</summary>
    public void SetFromModel(string text)
    {
        if (_text != text)
        {
            _text = text;
            OnPropertyChanged(nameof(Text));
        }

        Error = null;
    }
}
