using System.Numerics;
using Formwerk.Geometry;
using Formwerk.Viewport;

namespace Formwerk.Tests.Viewport;

/// <summary>Rectangle selection in the 3D view (F-SEL-01): which objects does a screen rectangle take?</summary>
public class MarqueeSelectionTests
{
    private const double Width = 1000;
    private const double Height = 800;

    private static OrbitCamera FrontCamera()
    {
        var camera = new OrbitCamera { Target = new Vector3(0, 0, 20), Distance = 300 };
        camera.SetPreset(ViewPreset.Front);
        return camera;
    }

    private static MeshData Box(Vector3 center, float half)
    {
        Vector3[] v = new Vector3[8];
        for (var i = 0; i < 8; i++)
            v[i] = center + new Vector3((i & 1) == 0 ? -half : half, (i & 2) == 0 ? -half : half, (i & 4) == 0 ? -half : half);
        int[] quads = [0, 1, 3, 2, 4, 6, 7, 5, 0, 4, 5, 1, 2, 3, 7, 6, 0, 2, 6, 4, 1, 5, 7, 3];
        var indices = new List<int>();
        for (var q = 0; q < quads.Length; q += 4)
        {
            indices.AddRange([quads[q], quads[q + 1], quads[q + 2]]);
            indices.AddRange([quads[q], quads[q + 2], quads[q + 3]]);
        }

        return new MeshData(v, [.. indices]);
    }

    private static ViewportItem Item(Guid id, MeshData mesh, ViewportItemKind kind = ViewportItemKind.Solid) => new(id, mesh, kind, 0xFFCCCCCC);

    /// <summary>A rectangle of the given pixel size around the screen position of a world point.</summary>
    private static ScreenRect Around(OrbitCamera camera, Vector3 world, double halfWidth, double halfHeight)
    {
        var (x, y) = camera.Project(world, Width, Height)!.Value;
        return ScreenRect.FromCorners(x - halfWidth, y - halfHeight, x + halfWidth, y + halfHeight);
    }

    private static IReadOnlyList<Guid> Select(OrbitCamera camera, ScreenRect rect, MarqueeMode mode, params ViewportItem[] items) =>
        MarqueeSelection.Select(items, camera, Width, Height, rect, mode);

    // ---- window ------------------------------------------------------------------------------------

    [Fact]
    public void Window_takes_an_object_that_lies_completely_inside()
    {
        var camera = FrontCamera();
        var id = Guid.NewGuid();

        var hit = Select(camera, Around(camera, new Vector3(0, 0, 20), 300, 300), MarqueeMode.Window, Item(id, Box(new Vector3(0, 0, 20), 10)));

        Assert.Equal([id], hit);
    }

    [Fact]
    public void Window_leaves_an_object_that_is_only_partly_inside()
    {
        var camera = FrontCamera();
        var rect = Around(camera, new Vector3(0, 0, 20), 20, 300); // reaches only into the box
        var hit = Select(camera, rect, MarqueeMode.Window, Item(Guid.NewGuid(), Box(new Vector3(0, 0, 20), 30)));

        Assert.Empty(hit);
    }

    [Fact]
    public void Window_takes_only_the_objects_inside_when_there_are_several()
    {
        var camera = FrontCamera();
        var left = Guid.NewGuid();
        var right = Guid.NewGuid();
        var rect = Around(camera, new Vector3(-60, 0, 20), 100, 200);

        var hit = Select(camera, rect, MarqueeMode.Window,
            Item(left, Box(new Vector3(-60, 0, 20), 10)),
            Item(right, Box(new Vector3(60, 0, 20), 10)));

        Assert.Equal([left], hit);
    }

    // ---- crossing ----------------------------------------------------------------------------------

    [Fact]
    public void Crossing_takes_an_object_that_is_only_partly_inside()
    {
        var camera = FrontCamera();
        var id = Guid.NewGuid();
        var rect = Around(camera, new Vector3(0, 0, 20), 20, 300);

        var hit = Select(camera, rect, MarqueeMode.Crossing, Item(id, Box(new Vector3(0, 0, 20), 30)));

        Assert.Equal([id], hit);
    }

    [Fact]
    public void Crossing_takes_an_object_when_the_rectangle_lies_inside_one_of_its_faces()
    {
        // no corner of the box is in the rectangle and no edge of the box crosses it
        var camera = FrontCamera();
        var id = Guid.NewGuid();
        var rect = Around(camera, new Vector3(0, 0, 20), 3, 3);

        var hit = Select(camera, rect, MarqueeMode.Crossing, Item(id, Box(new Vector3(0, 0, 20), 40)));

        Assert.Equal([id], hit);
    }

    [Fact]
    public void Crossing_leaves_an_object_next_to_the_rectangle()
    {
        var camera = FrontCamera();
        var rect = Around(camera, new Vector3(-100, 0, 20), 20, 20);

        Assert.Empty(Select(camera, rect, MarqueeMode.Crossing, Item(Guid.NewGuid(), Box(new Vector3(60, 0, 20), 10))));
    }

    [Fact]
    public void Crossing_ignores_the_empty_corner_of_the_bounding_box_of_a_thin_diagonal_object()
    {
        // one long thin triangle from bottom left to top right (front view: X to the right, Z up)
        var camera = FrontCamera();
        var thin = new MeshData([new Vector3(-80, 0, -20), new Vector3(80, 0, 120), new Vector3(-79, 0, -20)], [0, 1, 2]);
        var id = Guid.NewGuid();
        var topLeft = Around(camera, new Vector3(-70, 0, 110), 15, 15);   // empty corner of the bounding box
        var middle = Around(camera, new Vector3(0, 0, 50), 15, 15);       // on the diagonal

        Assert.Empty(Select(camera, topLeft, MarqueeMode.Crossing, Item(id, thin)));
        Assert.Equal([id], Select(camera, middle, MarqueeMode.Crossing, Item(id, thin)));
    }

    // ---- both modes --------------------------------------------------------------------------------

    [Theory]
    [InlineData(MarqueeMode.Window)]
    [InlineData(MarqueeMode.Crossing)]
    public void A_rectangle_over_empty_space_takes_nothing(MarqueeMode mode)
    {
        var camera = FrontCamera();
        var rect = Around(camera, new Vector3(-200, 0, 150), 20, 20);

        Assert.Empty(Select(camera, rect, mode, Item(Guid.NewGuid(), Box(new Vector3(0, 0, 20), 10))));
    }

    [Theory]
    [InlineData(MarqueeMode.Window)]
    [InlineData(MarqueeMode.Crossing)]
    public void Objects_behind_the_camera_are_not_taken(MarqueeMode mode)
    {
        var camera = FrontCamera(); // looks along +Y from y = -300
        var rect = new ScreenRect(0, 0, Width, Height);

        Assert.Empty(Select(camera, rect, mode, Item(Guid.NewGuid(), Box(new Vector3(0, -500, 20), 10))));
    }

    [Theory]
    [InlineData(MarqueeMode.Window)]
    [InlineData(MarqueeMode.Crossing)]
    public void Holes_can_be_taken_like_solids(MarqueeMode mode)
    {
        var camera = FrontCamera();
        var id = Guid.NewGuid();

        var hit = Select(camera, Around(camera, new Vector3(0, 0, 20), 300, 300), mode, Item(id, Box(new Vector3(0, 0, 20), 10), ViewportItemKind.Ghost));

        Assert.Equal([id], hit);
    }

    [Theory]
    [InlineData(MarqueeMode.Window)]
    [InlineData(MarqueeMode.Crossing)]
    public void Dimmed_objects_outside_an_open_group_are_not_taken(MarqueeMode mode)
    {
        var camera = FrontCamera();
        var dimmed = Guid.NewGuid();
        var live = Guid.NewGuid();
        var box = Box(new Vector3(0, 0, 20), 10);

        var hit = Select(camera, Around(camera, new Vector3(0, 0, 20), 300, 300), mode,
            Item(dimmed, box) with { Dimmed = true }, Item(live, Box(new Vector3(30, 0, 20), 10)));

        Assert.Equal([live], hit);
    }

    [Fact]
    public void Items_without_triangles_are_ignored()
    {
        var camera = FrontCamera();

        Assert.Empty(Select(camera, new ScreenRect(0, 0, Width, Height), MarqueeMode.Crossing, Item(Guid.NewGuid(), MeshData.Empty)));
    }

    [Fact]
    public void An_object_made_of_several_meshes_needs_all_of_them_inside_for_a_window_and_one_for_a_crossing()
    {
        var camera = FrontCamera();
        var id = Guid.NewGuid();
        var near = Item(id, Box(new Vector3(-60, 0, 20), 10));
        var far = Item(id, Box(new Vector3(60, 0, 20), 10));
        var rect = Around(camera, new Vector3(-60, 0, 20), 100, 200);

        Assert.Empty(Select(camera, rect, MarqueeMode.Window, near, far));
        Assert.Equal([id], Select(camera, rect, MarqueeMode.Crossing, near, far));
        Assert.Equal([id], Select(camera, new ScreenRect(0, 0, Width, Height), MarqueeMode.Window, near, far));
    }

    [Fact]
    public void The_result_follows_the_order_of_the_items_and_has_every_object_once()
    {
        var camera = FrontCamera();
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var all = new ScreenRect(0, 0, Width, Height);

        var hit = Select(camera, all, MarqueeMode.Window,
            Item(a, Box(new Vector3(-60, 0, 20), 10)),
            Item(b, Box(new Vector3(60, 0, 20), 10)),
            Item(a, Box(new Vector3(-60, 0, 60), 10)));

        Assert.Equal([a, b], hit);
    }

    [Fact]
    public void Works_in_the_top_view_and_after_the_camera_moved()
    {
        var camera = FrontCamera();
        camera.SetPreset(ViewPreset.Top);
        camera.Pan(120, -40, Width);
        var id = Guid.NewGuid();
        var center = new Vector3(30, 40, 5);

        var hit = Select(camera, Around(camera, center, 90, 90), MarqueeMode.Window, Item(id, Box(center, 10)));

        Assert.Equal([id], hit);
    }

    // ---- big meshes --------------------------------------------------------------------------------

    private static MeshData ManyTriangles(int count)
    {
        // a thin diagonal bar of tiny triangles
        var vertices = new Vector3[count * 3];
        var indices = new int[count * 3];
        for (var i = 0; i < count; i++)
        {
            var t = (float)i / count;
            var p = new Vector3(-80 + 160 * t, 0, -20 + 140 * t);
            vertices[i * 3] = p;
            vertices[i * 3 + 1] = p + new Vector3(0.01f, 0, 0);
            vertices[i * 3 + 2] = p + new Vector3(0, 0, 0.01f);
            indices[i * 3] = i * 3;
            indices[i * 3 + 1] = i * 3 + 1;
            indices[i * 3 + 2] = i * 3 + 2;
        }

        return new MeshData(vertices, indices);
    }

    [Fact]
    public void A_huge_mesh_is_judged_by_its_bounding_box_in_both_modes()
    {
        var camera = FrontCamera();
        var id = Guid.NewGuid();
        var item = Item(id, ManyTriangles(MarqueeSelection.DetailTriangleLimit + 1));

        var corner = Around(camera, new Vector3(-70, 0, 110), 15, 15); // empty corner of the box, no triangle there
        var all = new ScreenRect(-2000, -2000, 3000, 3000); // the bar reaches the top edge of the view

        Assert.Equal([id], Select(camera, corner, MarqueeMode.Crossing, item));   // the cheap test cannot tell
        Assert.Equal([id], Select(camera, all, MarqueeMode.Window, item));
        Assert.Empty(Select(camera, Around(camera, new Vector3(0, 0, 50), 5, 5), MarqueeMode.Window, item));
    }

    [Fact]
    public void A_mesh_below_the_limit_is_judged_by_its_triangles()
    {
        var camera = FrontCamera();
        var item = Item(Guid.NewGuid(), ManyTriangles(MarqueeSelection.DetailTriangleLimit));

        Assert.Empty(Select(camera, Around(camera, new Vector3(-70, 0, 110), 15, 15), MarqueeMode.Crossing, item));
    }

    // ---- the rectangle itself ----------------------------------------------------------------------

    [Fact]
    public void The_rectangle_does_not_care_which_corner_was_pressed_first()
    {
        var a = ScreenRect.FromCorners(300, 200, 100, 50);
        var b = ScreenRect.FromCorners(100, 50, 300, 200);

        Assert.Equal(b, a);
        Assert.Equal(100, a.Left);
        Assert.Equal(50, a.Top);
        Assert.Equal(300, a.Right);
        Assert.Equal(200, a.Bottom);
        Assert.Equal(200, a.Width);
        Assert.Equal(150, a.Height);
    }

    [Theory]
    [InlineData(100, 300, MarqueeMode.Window)]
    [InlineData(300, 100, MarqueeMode.Crossing)]
    [InlineData(100, 100, MarqueeMode.Window)]
    public void Dragging_to_the_right_is_a_window_and_to_the_left_a_crossing(double startX, double endX, MarqueeMode expected)
    {
        Assert.Equal(expected, MarqueeSelection.ModeFor(startX, endX));
    }
}
