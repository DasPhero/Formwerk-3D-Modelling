using Formwerk.App.ViewModels;

namespace Formwerk.Tests.App;

/// <summary>The questions and messages of the program in its own window instead of the white Windows message box (F-VIS-07).</summary>
public class MessageViewModelTests
{
    [Fact]
    public void The_unsaved_changes_question_offers_save_dont_save_and_cancel_with_save_as_the_default()
    {
        var vm = MessageViewModel.UnsavedChanges("Do you want to save?");

        Assert.Equal(["Save", "Don't save", "Cancel"], vm.Buttons.Select(b => b.Label));
        Assert.Equal(MessageViewModel.SaveId, vm.Buttons.Single(b => b.IsDefault).Id);
        Assert.Equal(MessageViewModel.CancelId, vm.Buttons.Single(b => b.IsCancel).Id);
        Assert.Equal("Do you want to save?", vm.Text);
        Assert.Equal("Formwerk", vm.Title);
    }

    [Fact]
    public void Closing_the_unsaved_changes_question_without_an_answer_cancels()
    {
        var vm = MessageViewModel.UnsavedChanges("x");

        Assert.Equal(MessageViewModel.CancelId, vm.Result);
    }

    [Fact]
    public void A_yes_no_question_has_yes_as_the_default_and_no_when_it_is_closed()
    {
        var vm = MessageViewModel.Question("Recover?");

        Assert.Equal(["Yes", "No"], vm.Buttons.Select(b => b.Label));
        Assert.Equal(MessageViewModel.YesId, vm.Buttons.Single(b => b.IsDefault).Id);
        Assert.Equal(MessageViewModel.NoId, vm.Result);
    }

    [Fact]
    public void An_error_message_has_one_ok_button_that_is_default_and_cancel()
    {
        var vm = MessageViewModel.Error("Something failed");

        var ok = Assert.Single(vm.Buttons);
        Assert.Equal("OK", ok.Label);
        Assert.True(ok.IsDefault && ok.IsCancel);
        Assert.Equal(MessageViewModel.OkId, vm.Result);
    }

    [Fact]
    public void Choosing_a_button_sets_the_result()
    {
        var vm = MessageViewModel.UnsavedChanges("x");

        vm.ChooseCommand.Execute(MessageViewModel.DiscardId);

        Assert.Equal(MessageViewModel.DiscardId, vm.Result);
        Assert.True(vm.IsAnswered);
    }

    [Fact]
    public void An_unknown_button_is_ignored()
    {
        var vm = MessageViewModel.UnsavedChanges("x");

        vm.ChooseCommand.Execute("nonsense");
        vm.ChooseCommand.Execute(null);

        Assert.False(vm.IsAnswered);
        Assert.Equal(MessageViewModel.CancelId, vm.Result);
    }

    [Fact]
    public void The_button_ids_differ()
    {
        string[] ids = [MessageViewModel.SaveId, MessageViewModel.DiscardId, MessageViewModel.CancelId, MessageViewModel.YesId, MessageViewModel.NoId, MessageViewModel.OkId];

        Assert.Equal(ids.Length, ids.Distinct().Count());
    }

    [Fact]
    public void A_long_message_is_kept_as_it_is()
    {
        var text = "The project could not be opened.\nThe file is damaged: " + new string('x', 400);

        Assert.Equal(text, MessageViewModel.Error(text).Text);
    }
}
