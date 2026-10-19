using Formwerk.Geometry;

namespace Formwerk.Viewport;

public enum ViewportItemKind
{
    /// <summary>Opaque printed material.</summary>
    Solid,

    /// <summary>Transparent hole shown as a ghost (F-VIS-04).</summary>
    Ghost,
}

/// <summary>
/// One mesh the viewport draws. <see cref="NodeId"/> is reported back when the user clicks it.
/// <see cref="Outside"/> tints a solid red: it reaches outside the build volume. <see cref="Dimmed"/> draws it faint (outside the
/// group that is open, F-BOO-04): no edges, not taken by a selection rectangle.
/// </summary>
public sealed record ViewportItem(Guid NodeId, MeshData Mesh, ViewportItemKind Kind, uint ColorArgb, bool Outside = false, bool Dimmed = false);

/// <summary>Result of a click in the viewport. <see cref="NodeId"/> is null when empty space was clicked.</summary>
public sealed record PickRequest(Guid? NodeId, bool Additive);

public enum ViewPreset
{
    Front,
    Back,
    Left,
    Right,
    Top,
    Bottom,
    Isometric,
}
