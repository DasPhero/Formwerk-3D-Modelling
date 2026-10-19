using Formwerk.Geometry;

namespace Formwerk.Features.Shapes;

/// <summary>
/// One editable dimension of a shape (F-PAR-01). Values are mm unless noted. A parameter that is not an integer is a size; if
/// <paramref name="IsDetail"/> is set it is a size that does not set an extent of the shape (for example the wall of a tube), so the
/// scale handles leave it alone and it has no <see cref="ScaleBinding"/>.
/// </summary>
public sealed record ParameterDefinition(
    string Name, string Label, double Default, double Min, double Max, bool IsInteger = false, bool IsDetail = false);

/// <summary>The local axes of a shape (X = left/right, Y = front/back, Z = up).</summary>
[Flags]
public enum ScaleAxes
{
    None = 0,
    X = 1,
    Y = 2,
    Z = 4,
}

/// <summary>
/// Says that a parameter (in mm) is the size of the shape along <see cref="Axes"/>: changing it by a factor changes the extent
/// along exactly these axes by the same factor. The scale gizmo uses this to resize shapes by their dimensions (F-SCL-05).
/// </summary>
public sealed record ScaleBinding(string Parameter, ScaleAxes Axes);

/// <summary>
/// Creates the mesh of a parametric primitive. The origin is the center of the bottom face,
/// so a new shape rests on the build plate (Z = 0).
/// </summary>
public interface IShapeGenerator
{
    string Id { get; }

    string DisplayName { get; }

    IReadOnlyList<ParameterDefinition> Parameters { get; }

    /// <summary>Which size parameter drives which axes; every parameter in mm appears exactly once.</summary>
    IReadOnlyList<ScaleBinding> ScaleBindings { get; }

    /// <summary>Missing values fall back to the defaults.</summary>
    /// <exception cref="ArgumentException">A value is outside its allowed range.</exception>
    MeshData Generate(IReadOnlyDictionary<string, double> values);
}

public abstract class ShapeGenerator : IShapeGenerator
{
    public abstract string Id { get; }

    public abstract string DisplayName { get; }

    public abstract IReadOnlyList<ParameterDefinition> Parameters { get; }

    public abstract IReadOnlyList<ScaleBinding> ScaleBindings { get; }

    public MeshData Generate(IReadOnlyDictionary<string, double> values)
    {
        var resolved = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in Parameters)
        {
            var value = values.TryGetValue(p.Name, out var v) ? v : p.Default;
            if (!double.IsFinite(value) || value < p.Min || value > p.Max)
                throw new ArgumentException($"{p.Label} must be between {p.Min:0.###} and {p.Max:0.###}.", p.Name);
            resolved[p.Name] = p.IsInteger ? Math.Round(value) : value;
        }

        return Build(resolved);
    }

    protected abstract MeshData Build(IReadOnlyDictionary<string, double> p);
}
