using System.Numerics;

namespace Formwerk.Viewport;

/// <summary>
/// A dimension of the selected object shown in the 3D view (F-PAR-02): a line along an edge of its box from
/// <see cref="From"/> to <see cref="To"/> (world space), moved a little in the direction <see cref="Outward"/> so that it does not lie
/// on the object, with the value as <see cref="Text"/>. <see cref="Id"/> says which parameter a typed value belongs to.
/// </summary>
public sealed record ViewportDimension(string Id, Vector3 From, Vector3 To, Vector3 Outward, string Text);

/// <summary>The user typed <see cref="Text"/> into the dimension <see cref="Id"/> (reported by the control).</summary>
public sealed record DimensionEdit(string Id, string Text);
