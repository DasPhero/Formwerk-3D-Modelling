using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Formwerk.App.Resources;
using Formwerk.IO;
using Formwerk.Library;

namespace Formwerk.App.ViewModels;

/// <summary>One entry of the folder choice of the library: its number (what <c>LibraryCategoryIndex</c> holds) and its text, which changes when a folder is renamed.</summary>
public sealed partial class LibraryFolderChoice(int index, string label) : ObservableObject
{
    public int Index { get; } = index;

    [ObservableProperty]
    public partial string Label { get; set; } = label;
}

/// <summary>An own folder as a small button under the folder buttons: a click shows the folder, a tile dropped on it goes into it.</summary>
public sealed partial class FolderChipViewModel(LibraryFolderChoice choice) : ObservableObject
{
    public LibraryFolderChoice Choice { get; } = choice;

    public string Name => Choice.Label;

    /// <summary>The number of the folder in the choice (what <c>LibraryCategoryIndex</c> holds while it is shown).</summary>
    public int Index => Choice.Index;

    /// <summary>The folder is the one that is shown now.</summary>
    [ObservableProperty]
    public partial bool IsShown { get; set; }

    internal void NameChanged() => OnPropertyChanged(nameof(Name));
}

/// <summary>A tile of the library that was dropped on an own folder (by name) to be put into it.</summary>
public sealed record LibraryFolderDrop(string Folder, string ItemId);

/// <summary>The check mark of one own folder in the menu of an element: whether the element is in that folder.</summary>
public sealed partial class FolderMembershipViewModel(string name, bool isMember, Action<FolderMembershipViewModel> changed) : ObservableObject
{
    public string Name { get; } = name;

    [ObservableProperty]
    public partial bool IsMember { get; set; } = isMember;

    partial void OnIsMemberChanged(bool value) => changed(this);
}

/// <summary>
/// Folders in the library (F-LIB-02): the built-in categories have sub folders (Fasteners > Nuts) and the user can make own folders. An
/// own folder holds links: an element or template in it also stays in its place in the library, and it can be in several folders.
/// The folders are remembered in the program settings. The numbers of the choice: 0 all, 1 favorites, 2 + category, then
/// <see cref="SubFolderBase"/> + the position of a built-in sub folder, then <see cref="UserFolderBase"/> + the key of an own folder.
/// </summary>
public sealed partial class MainViewModel
{
    private const int SubFolderBase = 100;
    private const int UserFolderBase = 1000;

    private sealed class UserFolder(int key, string name, LibraryFolderChoice choice, List<string> ids)
    {
        public int Key { get; } = key;

        public FolderChipViewModel Chip { get; } = new(choice);

        public string Name { get; set; } = name;

        public LibraryFolderChoice Choice { get; } = choice;

        public List<string> Ids { get; } = ids;
    }

    private readonly List<UserFolder> _userFolders = [];

    /// <summary>The own folders as buttons: the place to drop a tile on.</summary>
    public ObservableCollection<FolderChipViewModel> UserFolderChips { get; } = [];
    private int _nextFolderKey;

    /// <summary>The built-in sub folders in the order of the choice: their category and name.</summary>
    private static readonly IReadOnlyList<(LibraryCategory Category, string Name)> SubFolderList =
    [
        .. Enum.GetValues<LibraryCategory>().SelectMany(c => LibraryFolders.SubFolders(c).Select(name => (c, name))),
    ];

    /// <summary>0 = all, 1 = favorites, the categories with their sub folders below them, then the own folders.</summary>
    public ObservableCollection<LibraryFolderChoice> LibraryCategoryChoices { get; } = BuiltInChoices();

    private static ObservableCollection<LibraryFolderChoice> BuiltInChoices()
    {
        var choices = new ObservableCollection<LibraryFolderChoice> { new(0, Texts.Get("Library.All")), new(1, Texts.Get("Library.Favorites")) };
        foreach (var category in Enum.GetValues<LibraryCategory>())
        {
            choices.Add(new LibraryFolderChoice(2 + (int)category, category.DisplayName()));
            for (var i = 0; i < SubFolderList.Count; i++)
            {
                if (SubFolderList[i].Category == category) choices.Add(new LibraryFolderChoice(SubFolderBase + i, "    " + SubFolderList[i].Name));
            }
        }

        return choices;
    }

    /// <summary>The names of the own folders in the order they were made.</summary>
    public IReadOnlyList<string> UserLibraryFolderNames => [.. _userFolders.Select(f => f.Name)];

    /// <summary>An own folder is shown (so that it can be renamed or deleted).</summary>
    public bool IsUserFolderSelected => FolderOfChoice(LibraryCategoryIndex) is not null;

    private UserFolder? FolderOfChoice(int index) => index >= UserFolderBase ? _userFolders.FirstOrDefault(f => f.Key == index - UserFolderBase) : null;

    /// <summary>Takes the folders from the settings (at the start and when the settings dialog was closed).</summary>
    private void ApplyUserFolders(FolderList folders)
    {
        var selected = FolderOfChoice(LibraryCategoryIndex)?.Name;
        foreach (var folder in _userFolders) LibraryCategoryChoices.Remove(folder.Choice);
        _userFolders.Clear();
        UserFolderChips.Clear();
        foreach (var folder in folders.Folders) AddUserFolder(folder.Name, folder.Items.Ids);

        var again = selected is null ? null : _userFolders.FirstOrDefault(f => string.Equals(f.Name, selected, StringComparison.OrdinalIgnoreCase));
        if (selected is not null) LibraryCategoryIndex = again is null ? 0 : UserFolderBase + again.Key;
        RebuildFolderMemberships();
        RefreshLibrary();
        OnPropertyChanged(nameof(UserLibraryFolderNames));
        OnPropertyChanged(nameof(IsUserFolderSelected));
    }

    private UserFolder AddUserFolder(string name, IEnumerable<string> ids)
    {
        var key = _nextFolderKey++;
        var folder = new UserFolder(key, name, new LibraryFolderChoice(UserFolderBase + key, name), [.. ids]);
        _userFolders.Add(folder);
        LibraryCategoryChoices.Add(folder.Choice);
        UserFolderChips.Add(folder.Chip);
        return folder;
    }

    private FolderList CurrentFolderList() => new([.. _userFolders.Select(f => new UserLibraryFolder(f.Name, new IdList([.. f.Ids])))]);

    partial void OnLibraryCategoryIndexChanged(int value)
    {
        RefreshLibrary();
        OnPropertyChanged(nameof(IsUserFolderSelected));
        RenameLibraryFolderCommand.NotifyCanExecuteChanged();
        DeleteLibraryFolderCommand.NotifyCanExecuteChanged();
        foreach (var chip in UserFolderChips) chip.IsShown = chip.Index == value;
    }

    /// <summary>The click on the button of an own folder (or any other choice by its number).</summary>
    [RelayCommand]
    private void ShowLibraryFolder(int index) => LibraryCategoryIndex = index;

    /// <summary>A tile dropped on the button of an own folder: put into the folder (once), like the check mark in the menu of the tile.</summary>
    [RelayCommand]
    private void DropOnLibraryFolder(LibraryFolderDrop? drop)
    {
        if (drop is null) return;
        var folder = _userFolders.FirstOrDefault(f => string.Equals(f.Name, drop.Folder, StringComparison.OrdinalIgnoreCase));
        var entry = _libraryAll.Concat<LibraryEntryViewModel>(_templateEntries).FirstOrDefault(e => string.Equals(e.Item.Id, drop.ItemId, StringComparison.OrdinalIgnoreCase));
        if (folder is null || entry is null) return;

        var membership = entry.FolderMemberships.FirstOrDefault(m => string.Equals(m.Name, folder.Name, StringComparison.OrdinalIgnoreCase));
        if (membership is { IsMember: false }) membership.IsMember = true; // the check mark does the rest: saves and refreshes
        SetNotice(Texts.Format("Folder.Added", entry.Name, folder.Name));
    }

    /// <summary>Every entry gets one check mark per own folder (made again whenever the folders change).</summary>
    private void RebuildFolderMemberships()
    {
        foreach (var entry in _libraryAll.Concat<LibraryEntryViewModel>(_templateEntries)) RebuildFolderMemberships(entry);
    }

    private void RebuildFolderMemberships(LibraryEntryViewModel entry)
    {
        entry.FolderMemberships.Clear();
        foreach (var folder in _userFolders)
        {
            var captured = folder;
            var member = folder.Ids.Contains(entry.Item.Id, StringComparer.OrdinalIgnoreCase);
            entry.FolderMemberships.Add(new FolderMembershipViewModel(folder.Name, member, m => SetMembership(entry, captured, m.IsMember)));
        }

        entry.NotifyFoldersChanged();
    }

    private void SetMembership(LibraryEntryViewModel entry, UserFolder folder, bool member)
    {
        folder.Ids.RemoveAll(id => string.Equals(id, entry.Item.Id, StringComparison.OrdinalIgnoreCase));
        if (member) folder.Ids.Add(entry.Item.Id);
        PersistUserSettings();
        if (LibraryCategoryIndex == UserFolderBase + folder.Key) RefreshLibrary(); // an element taken out leaves the folder at once
    }

    // ---- making, renaming and deleting folders ---------------------------------------------------

    [RelayCommand]
    private void NewLibraryFolder()
    {
        var name = _dialogs.AskText(Texts.Get("Folder.NewTitle"), Texts.Get("Folder.NamePrompt"), "");
        if (name is null || !CheckFolderName(ref name, null)) return;

        var folder = AddUserFolder(name, []);
        RebuildFolderMemberships();
        PersistUserSettings();
        OnPropertyChanged(nameof(UserLibraryFolderNames));
        LibraryCategoryIndex = UserFolderBase + folder.Key;
    }

    [RelayCommand(CanExecute = nameof(IsUserFolderSelected))]
    private void RenameLibraryFolder()
    {
        if (FolderOfChoice(LibraryCategoryIndex) is not { } folder) return;
        var name = _dialogs.AskText(Texts.Get("Folder.RenameTitle"), Texts.Get("Folder.NamePrompt"), folder.Name);
        if (name is null || !CheckFolderName(ref name, folder)) return;

        folder.Name = name;
        folder.Choice.Label = name;
        folder.Chip.NameChanged();
        RebuildFolderMemberships();
        PersistUserSettings();
        OnPropertyChanged(nameof(UserLibraryFolderNames));
    }

    [RelayCommand(CanExecute = nameof(IsUserFolderSelected))]
    private void DeleteLibraryFolder()
    {
        if (FolderOfChoice(LibraryCategoryIndex) is not { } folder) return;
        if (!_dialogs.Confirm(Texts.Format("Folder.DeleteConfirm", folder.Name))) return;

        LibraryCategoryIndex = 0; // first, so that the choice never points at a folder that is gone
        _userFolders.Remove(folder);
        LibraryCategoryChoices.Remove(folder.Choice);
        UserFolderChips.Remove(folder.Chip);
        RebuildFolderMemberships();
        PersistUserSettings();
        OnPropertyChanged(nameof(UserLibraryFolderNames));
    }

    /// <summary>The name trimmed, or false (after telling the user why) when it cannot be used. <paramref name="renamed"/> may keep its own name.</summary>
    private bool CheckFolderName(ref string name, UserFolder? renamed)
    {
        var trimmed = name.Trim();
        name = trimmed;
        string? problem = null;
        if (trimmed.Length == 0) problem = Texts.Get("Folder.NameEmpty");
        else if (trimmed.Length > FolderList.MaxNameLength) problem = Texts.Format("Folder.NameTooLong", FolderList.MaxNameLength);
        else if (_userFolders.Any(f => f != renamed && string.Equals(f.Name, trimmed, StringComparison.OrdinalIgnoreCase))
                 || LibraryCategoryChoices.Any(c => c.Index < UserFolderBase && string.Equals(c.Label.Trim(), trimmed, StringComparison.OrdinalIgnoreCase)))
            problem = Texts.Get("Folder.NameTaken");
        else if (_userFolders.Count >= FolderList.MaxCount && renamed is null) problem = Texts.Get("Folder.TooMany");

        if (problem is null) return true;
        _dialogs.ShowError(problem);
        return false;
    }
}
