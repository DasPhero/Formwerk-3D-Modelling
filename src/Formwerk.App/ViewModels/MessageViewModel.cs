using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Formwerk.App.Resources;

namespace Formwerk.App.ViewModels;

/// <summary>One button of a message window.</summary>
public sealed record MessageButton(string Id, string Label, bool IsDefault, bool IsCancel);

/// <summary>
/// A question or message of the program (F-VIS-07): shown in a window in the theme of the program instead of the white Windows
/// message box. It knows its buttons and what counts as the answer when the window is closed without one.
/// </summary>
public sealed partial class MessageViewModel : ObservableObject
{
    public const string SaveId = "save";
    public const string DiscardId = "discard";
    public const string CancelId = "cancel";
    public const string YesId = "yes";
    public const string NoId = "no";
    public const string OkId = "ok";

    public MessageViewModel(string text, IReadOnlyList<MessageButton> buttons)
    {
        Text = text;
        Buttons = buttons;
        Result = buttons.FirstOrDefault(b => b.IsCancel)?.Id ?? buttons[^1].Id; // Esc and the close button of the window
    }

    public string Title { get; } = Texts.Get("App.Title");

    public string Text { get; }

    public IReadOnlyList<MessageButton> Buttons { get; }

    /// <summary>The id of the button that was chosen; until then what closing the window means.</summary>
    public string Result { get; private set; }

    public bool IsAnswered { get; private set; }

    [RelayCommand]
    private void Choose(string? id)
    {
        if (id is null || Buttons.All(b => b.Id != id)) return;
        Result = id;
        IsAnswered = true;
        Answered?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Raised when a button was chosen; the window closes.</summary>
    public event EventHandler? Answered;

    /// <summary>"Do you want to save the changes?": save (default), don't save, cancel.</summary>
    public static MessageViewModel UnsavedChanges(string text) => new(text,
    [
        new(SaveId, Texts.Get("Msg.Button.Save"), IsDefault: true, IsCancel: false),
        new(DiscardId, Texts.Get("Msg.Button.DontSave"), IsDefault: false, IsCancel: false),
        new(CancelId, Texts.Get("Msg.Button.Cancel"), IsDefault: false, IsCancel: true),
    ]);

    /// <summary>A yes/no question: yes is the default, closing the window answers no.</summary>
    public static MessageViewModel Question(string text) => new(text,
    [
        new(YesId, Texts.Get("Msg.Button.Yes"), IsDefault: true, IsCancel: false),
        new(NoId, Texts.Get("Msg.Button.No"), IsDefault: false, IsCancel: true),
    ]);

    public static MessageViewModel Error(string text) => new(text, [new(OkId, Texts.Get("Msg.Button.Ok"), IsDefault: true, IsCancel: true)]);
}
