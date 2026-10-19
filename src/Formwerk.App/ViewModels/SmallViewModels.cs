using CommunityToolkit.Mvvm.ComponentModel;
using Formwerk.Core.Model;

namespace Formwerk.App.ViewModels;

public sealed record ShapeEntryViewModel(string Id, string DisplayName);

public sealed class HistoryEntryViewModel(int index, string name, bool isFuture)
{
    /// <summary>Number of applied commands in the state this entry stands for (0 = initial state).</summary>
    public int Index { get; } = index;

    public string Name { get; } = name;

    /// <summary>The entry was undone and can be redone.</summary>
    public bool IsFuture { get; } = isFuture;
}

/// <summary>One row of the object list.</summary>
public sealed partial class NodeItemViewModel : ObservableObject
{
    private readonly Action<Guid, bool> _setVisible;
    private readonly Action<NodeItemViewModel, bool> _selectionChanged;
    private bool _visible;
    private bool _isSelected;

    public NodeItemViewModel(Node node, int depth, Action<Guid, bool> setVisible, Action<NodeItemViewModel, bool> selectionChanged)
    {
        Id = node.Id;
        Depth = depth;
        _setVisible = setVisible;
        _selectionChanged = selectionChanged;
        Update(node);
    }

    public Guid Id { get; }

    public int Depth { get; private set; }

    public double Indent => Depth * 14;

    public string Name { get; private set; } = "";

    public string Glyph { get; private set; } = "";

    public bool IsHole { get; private set; }

    [ObservableProperty]
    public partial string? ErrorText { get; set; }

    public bool HasError => !string.IsNullOrEmpty(ErrorText);

    /// <summary>The object reaches outside the build volume (F-VIS-05).</summary>
    [ObservableProperty]
    public partial bool IsOutside { get; set; }

    public bool Visible
    {
        get => _visible;
        set
        {
            if (_visible == value) return;
            _visible = value;
            OnPropertyChanged();
            _setVisible(Id, value);
        }
    }

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value) return;
            _isSelected = value;
            OnPropertyChanged();
            _selectionChanged(this, value);
        }
    }

    partial void OnErrorTextChanged(string? value) => OnPropertyChanged(nameof(HasError));

    /// <summary>Copies the display data from the node without raising the user-edit callbacks.</summary>
    public void Update(Node node, int? depth = null)
    {
        if (depth is not null) Depth = depth.Value;
        Name = node.Name;
        IsHole = node.Role == NodeRole.Hole;
        Glyph = node switch
        {
            GroupNode => "▤",
            BooleanNode => "⊖",
            MergeNode => "⊎",
            MeshNode => "△",
            _ => "▣",
        };
        _visible = node.Visible;
        OnPropertyChanged(string.Empty);
    }

    public void SetSelectedSilently(bool value)
    {
        if (_isSelected == value) return;
        _isSelected = value;
        OnPropertyChanged(nameof(IsSelected));
    }
}
