using CommunityToolkit.Mvvm.ComponentModel;

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

    partial void OnErrorChanged(string? value) => OnPropertyChanged(nameof(HasError));

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
