using System.Globalization;
using System.Numerics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Formwerk.App.Resources;
using Formwerk.Viewport;

namespace Formwerk.App.ViewModels;

/// <summary>
/// The pivot of the Rotate tool (F-ROT-03): what the selection turns around. The center of the selection (default), a corner of its
/// bounding box, a point picked in the view (corners, edge middles and face centers snap) or the world origin. The rotate handles sit at
/// the pivot. The choice is only used while the Rotate tool is active; it is a state of the view and is not saved.
/// </summary>
public sealed partial class MainViewModel
{
    /// <summary>0 center of the selection, 1 corner of the bounding box, 2 a picked point, 3 the world origin.</summary>
    public IReadOnlyList<AxisChoice> PivotModes { get; } =
    [
        new(0, Texts.Get("Pivot.Center")),
        new(1, Texts.Get("Pivot.Corner")),
        new(2, Texts.Get("Pivot.Point")),
        new(3, Texts.Get("Pivot.World")),
    ];

    /// <summary>The eight corners; bit 0 = right (X max), bit 1 = back (Y max), bit 2 = top (Z max), see <c>PivotChoice.Corner</c>.</summary>
    public IReadOnlyList<AxisChoice> PivotCorners { get; } =
    [
        .. Enumerable.Range(0, 8).Select(i => new AxisChoice(i, string.Join(" ",
            Texts.Get((i & 1) != 0 ? "Pivot.Right" : "Pivot.Left"),
            Texts.Get((i & 2) != 0 ? "Pivot.Back" : "Pivot.Front"),
            Texts.Get((i & 4) != 0 ? "Pivot.Top" : "Pivot.Bottom")))),
    ];

    [ObservableProperty]
    public partial int PivotMode { get; set; }

    [ObservableProperty]
    public partial int PivotCorner { get; set; }

    /// <summary>The point that was picked (world coordinates), or null while none was.</summary>
    [ObservableProperty]
    public partial Vector3? CustomPivot { get; set; }

    /// <summary>Waiting for a click that sets the pivot point (a face pick mode, like the measuring tool).</summary>
    [ObservableProperty]
    public partial bool IsPickingPivot { get; set; }

    /// <summary>The panel with the pivot choice is shown: the Rotate tool is active and something is selected.</summary>
    public bool IsPivotVisible => ActiveTool == ViewportTool.Rotate && _selected.Count > 0;

    public bool IsPivotCornerVisible => PivotMode == 1;

    public bool IsPivotPointVisible => PivotMode == 2;

    /// <summary>The picked point as text for the panel.</summary>
    public string CustomPivotText => CustomPivot is { } p
        ? string.Create(CultureInfo.InvariantCulture, $"X {Math.Round(p.X, 2)}  Y {Math.Round(p.Y, 2)}  Z {Math.Round(p.Z, 2)}")
        : "";

    partial void OnPivotModeChanged(int value)
    {
        OnPropertyChanged(nameof(IsPivotCornerVisible));
        OnPropertyChanged(nameof(IsPivotPointVisible));
        IsPickingPivot = false;
        if (value == 2 && CustomPivot is null) StartPivotPicking(); // a point is needed: ask for it at once
        UpdatePivot();
    }

    partial void OnPivotCornerChanged(int value) => UpdatePivot();

    partial void OnCustomPivotChanged(Vector3? value)
    {
        OnPropertyChanged(nameof(CustomPivotText));
        UpdatePivot();
    }

    partial void OnIsPickingPivotChanged(bool value)
    {
        OnPropertyChanged(nameof(IsFacePickActive));
        RefreshScaleHint();
    }

    private void StartPivotPicking()
    {
        IsPickingFace = false;
        IsPlacingHole = false;
        IsPlacingEdgeCut = false;
        IsPickingSectionFace = false;
        IsMeasuring = false;
        IsPickingMirrorFace = false;
        EndFaceToFaceMode();
        IsPickingPivot = true;
    }

    /// <summary>The button "Pick point": starts or ends the picking of the pivot point.</summary>
    [RelayCommand]
    private void PivotPick()
    {
        if (IsPickingPivot) IsPickingPivot = false;
        else StartPivotPicking();
    }

    /// <summary>The click of the mode: the point snaps like in the measuring tool (Alt or Shift: as clicked).</summary>
    private void PivotPicked(FacePick pick)
    {
        CustomPivot = SnapMeasured(pick, pick.Normal);
        IsPickingPivot = false;
        SetNotice(Texts.Format("Msg.PivotSet", CustomPivotText));
    }

    /// <summary>The pivot for the chosen mode inside the bounds of the selection.</summary>
    private Vector3 ChosenPivot(Vector3 min, Vector3 max)
    {
        if (ActiveTool != ViewportTool.Rotate) return Core.Model.PivotChoice.Center(min, max);
        return PivotMode switch
        {
            1 => Core.Model.PivotChoice.Corner(min, max, PivotCorner),
            2 when CustomPivot is { } point => point,
            3 => Vector3.Zero,
            _ => Core.Model.PivotChoice.Center(min, max),
        };
    }
}
