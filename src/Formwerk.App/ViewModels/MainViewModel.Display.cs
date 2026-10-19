using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Formwerk.App.Resources;
using Formwerk.Viewport;

namespace Formwerk.App.ViewModels;

/// <summary>How the 3D view draws objects and edges (F-VIS-02, F-VIS-03).</summary>
public sealed partial class MainViewModel
{
    [ObservableProperty]
    public partial ViewportDisplayMode DisplayMode { get; set; } = ViewportDisplayMode.ShadedWithEdges;

    /// <summary>Fold angle in degrees from which the edge between two faces is drawn; remembered between sessions (F-SET-02).</summary>
    [ObservableProperty]
    public partial double EdgeAngle { get; set; } = 30;

    public bool IsShadedMode
    {
        get => DisplayMode == ViewportDisplayMode.Shaded;
        set { if (value) DisplayMode = ViewportDisplayMode.Shaded; else OnPropertyChanged(); }
    }

    public bool IsShadedWithEdgesMode
    {
        get => DisplayMode == ViewportDisplayMode.ShadedWithEdges;
        set { if (value) DisplayMode = ViewportDisplayMode.ShadedWithEdges; else OnPropertyChanged(); }
    }

    public bool IsWireframeMode
    {
        get => DisplayMode == ViewportDisplayMode.Wireframe;
        set { if (value) DisplayMode = ViewportDisplayMode.Wireframe; else OnPropertyChanged(); }
    }

    public bool IsXRayMode
    {
        get => DisplayMode == ViewportDisplayMode.XRay;
        set { if (value) DisplayMode = ViewportDisplayMode.XRay; else OnPropertyChanged(); }
    }

    partial void OnDisplayModeChanged(ViewportDisplayMode value)
    {
        OnPropertyChanged(nameof(IsShadedMode));
        OnPropertyChanged(nameof(IsShadedWithEdgesMode));
        OnPropertyChanged(nameof(IsWireframeMode));
        OnPropertyChanged(nameof(IsXRayMode));
    }

    /// <summary>Key Z: the next display mode, after the last one the first again.</summary>
    [RelayCommand]
    private void CycleDisplayMode() => DisplayMode = (ViewportDisplayMode)(((int)DisplayMode + 1) % 4);

    /// <summary>Orthographic instead of perspective view (F-VIS-06). Not part of the document and not remembered.</summary>
    [ObservableProperty]
    public partial bool IsOrthographic { get; set; }

    /// <summary>Key P: switches between the perspective and the orthographic view.</summary>
    [RelayCommand]
    private void ToggleProjection() => IsOrthographic = !IsOrthographic;

    /// <summary>The names written on the faces of the ViewCube (F-VIS-05).</summary>
    public ViewCubeLabels ViewCubeLabels { get; } = new(
        Texts.Get("ViewCube.Front"), Texts.Get("ViewCube.Back"), Texts.Get("ViewCube.Left"),
        Texts.Get("ViewCube.Right"), Texts.Get("ViewCube.Top"), Texts.Get("ViewCube.Bottom"));
}
