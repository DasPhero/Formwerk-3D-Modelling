using System.Globalization;
using System.Numerics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Formwerk.Core.Model;
using Formwerk.Features.Shapes;
using Formwerk.Viewport;

namespace Formwerk.App.ViewModels;

/// <summary>Dimensions in the 3D view (F-PAR-02): click a dimension at the object and type a value.</summary>
public sealed partial class MainViewModel
{
    /// <summary>The dimensions of the selected shape, or null (nothing selected, several objects, a group, a locked or imported object).</summary>
    [ObservableProperty]
    public partial IReadOnlyList<ViewportDimension>? ViewportDimensions { get; set; }

    partial void OnIsPickingFaceChanged(bool value)
    {
        OnPropertyChanged(nameof(IsFacePickActive));
        ViewportDimensions = ComputeDimensions();
    }

    /// <summary>
    /// One dimension per parameter that the scale handles bind to an axis (a cylinder's diameter once, on X): the line runs along the
    /// matching edge of the object's own box, so a turned object has its dimensions along its own axes.
    /// </summary>
    private IReadOnlyList<ViewportDimension>? ComputeDimensions()
    {
        if (IsFacePickActive || SelectedTopLevel() is not [PrimitiveNode { Locked: false } node]) return null;
        if (ShapeLibrary.Find(node.GeneratorId) is not { } generator || ComputeScaleBox() is not { } box) return null;

        var frame = TransformOps.FrameOf(node);
        Vector3 World(Vector3 local) => frame.Origin + Vector3.Transform(local, frame.Rotation);
        Vector3 Dir(Vector3 local) => Vector3.Normalize(Vector3.Transform(local, frame.Rotation));

        var dimensions = new List<ViewportDimension>();
        foreach (var binding in generator.ScaleBindings)
        {
            int axis = binding.Axes.HasFlag(ScaleAxes.X) ? 0 : binding.Axes.HasFlag(ScaleAxes.Y) ? 1 : binding.Axes.HasFlag(ScaleAxes.Z) ? 2 : -1;
            if (axis < 0) continue;

            var min = box.Min;
            var max = box.Max;
            var size = SnapBox.Component(max - min, axis);
            // an edge of the box on the near, low side of the other two axes; the line is moved outwards from there
            (Vector3 from, Vector3 to, Vector3 outward) = axis switch
            {
                0 => (new Vector3(min.X, min.Y, min.Z), new Vector3(max.X, min.Y, min.Z), new Vector3(0, -1, 0)),
                1 => (new Vector3(max.X, min.Y, min.Z), new Vector3(max.X, max.Y, min.Z), new Vector3(1, 0, 0)),
                _ => (new Vector3(min.X, min.Y, min.Z), new Vector3(min.X, min.Y, max.Z), new Vector3(-1, -1, 0)),
            };
            dimensions.Add(new ViewportDimension(binding.Parameter, World(from), World(to), Dir(outward), FormatDimension(size)));
        }

        return dimensions.Count == 0 ? null : dimensions;
    }

    private static string FormatDimension(float size) =>
        string.Create(CultureInfo.InvariantCulture, $"{MathF.Round(size, 2):0.##} mm");

    /// <summary>The user typed a value into a dimension: the same as typing it into the parameter field of the panel (expressions, +10 and 150% work, undo included).</summary>
    [RelayCommand]
    private void EditDimension(DimensionEdit? edit)
    {
        if (edit is null || Properties is not { HasParameters: true } properties) return;
        if (SelectedTopLevel() is not [PrimitiveNode { Locked: false } node] || properties.NodeId != node.Id) return;
        if (ShapeLibrary.Find(node.GeneratorId) is not { } generator) return;

        var index = generator.Parameters.ToList().FindIndex(p => p.Name == edit.Id);
        if (index < 0 || index >= properties.Parameters.Count) return;

        var field = properties.Parameters[index];
        field.Text = edit.Text.Trim();
        if (field.HasError) SetNotice(field.Error!);
    }
}
