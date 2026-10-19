using System.Numerics;
using Formwerk.Viewport;

namespace Formwerk.Tests.Viewport;

/// <summary>The ViewCube (F-VIS-05, F-VIS-06): which view a field stands for, and the geometry of the cube.</summary>
public class ViewCubeTests
{
    private static readonly CameraState Current = new(new Vector3(10, 20, 30), 250, -55, 28);

    // ---- regions -----------------------------------------------------------------------------------

    [Fact]
    public void There_are_26_regions_6_faces_12_edges_and_8_corners()
    {
        var all = ViewCubeRegion.All;

        Assert.Equal(26, all.Count);
        Assert.Equal(26, all.Distinct().Count());
        Assert.Equal(6, all.Count(r => r.Kind == ViewCubeKind.Face));
        Assert.Equal(12, all.Count(r => r.Kind == ViewCubeKind.Edge));
        Assert.Equal(8, all.Count(r => r.Kind == ViewCubeKind.Corner));
        Assert.DoesNotContain(new ViewCubeRegion(0, 0, 0), all);
    }

    [Fact]
    public void The_direction_is_the_sum_of_the_faces_it_touches()
    {
        Assert.Equal(new Vector3(1, -1, 1), new ViewCubeRegion(1, -1, 1).Direction);
        Assert.Equal(new Vector3(0, -1, 0), new ViewCubeRegion(0, -1, 0).Direction);
    }

    // ---- views -------------------------------------------------------------------------------------

    [Theory]
    [InlineData(0, -1, 0, ViewPreset.Front)]
    [InlineData(0, 1, 0, ViewPreset.Back)]
    [InlineData(-1, 0, 0, ViewPreset.Left)]
    [InlineData(1, 0, 0, ViewPreset.Right)]
    [InlineData(0, 0, 1, ViewPreset.Top)]
    [InlineData(0, 0, -1, ViewPreset.Bottom)]
    public void A_face_leads_to_the_same_view_as_the_key_for_it(int x, int y, int z, ViewPreset preset)
    {
        var camera = new OrbitCamera { State = Current };

        var state = ViewCubeMath.StateFor(new ViewCubeRegion(x, y, z), camera.State);

        Assert.Equal(camera.PresetState(preset), state);
    }

    [Fact]
    public void An_edge_looks_from_the_middle_between_two_faces()
    {
        var frontTop = ViewCubeMath.StateFor(new ViewCubeRegion(0, -1, 1), Current);
        var frontRight = ViewCubeMath.StateFor(new ViewCubeRegion(1, -1, 0), Current);

        Assert.Equal(-90, frontTop.YawDegrees, 6);
        Assert.Equal(45, frontTop.PitchDegrees, 6);
        Assert.Equal(-45, frontRight.YawDegrees, 6);
        Assert.Equal(0, frontRight.PitchDegrees, 6);
    }

    [Fact]
    public void A_corner_gives_an_isometric_view()
    {
        var frontRightTop = ViewCubeMath.StateFor(new ViewCubeRegion(1, -1, 1), Current);
        var backLeftBottom = ViewCubeMath.StateFor(new ViewCubeRegion(-1, 1, -1), Current);

        Assert.Equal(-45, frontRightTop.YawDegrees, 6);
        Assert.Equal(35.2644, frontRightTop.PitchDegrees, 3);
        Assert.Equal(135, backLeftBottom.YawDegrees, 6);
        Assert.Equal(-35.2644, backLeftBottom.PitchDegrees, 3);
    }

    [Fact]
    public void Every_region_keeps_the_target_and_the_distance()
    {
        foreach (var region in ViewCubeRegion.All)
        {
            var state = ViewCubeMath.StateFor(region, Current);

            Assert.Equal(Current.Target, state.Target);
            Assert.Equal(Current.Distance, state.Distance);
        }
    }

    [Fact]
    public void Every_region_looks_from_the_side_it_names()
    {
        foreach (var region in ViewCubeRegion.All)
        {
            var camera = new OrbitCamera { State = ViewCubeMath.StateFor(region, Current) };
            var fromTarget = Vector3.Normalize(camera.Position - camera.Target);
            var wanted = Vector3.Normalize(region.Direction);

            // the top and bottom views stop half a degree short of the pole so that the view never flips
            Assert.True(Vector3.Dot(fromTarget, wanted) > 0.9999f, $"region {region}");
        }
    }

    [Fact]
    public void The_cube_camera_has_the_viewing_direction_of_the_main_camera()
    {
        var forward = Vector3.Normalize(new Vector3(1, 2, -1));

        var position = ViewCubeMath.CameraPosition(forward);

        Assert.Equal(-forward * (float)ViewCubeMath.CameraDistance, position);
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(2, false)]
    [InlineData(3, false)]
    public void Only_the_first_click_of_a_double_click_counts(int clickCount, bool acts)
    {
        // the cube is already turning after the first click: the second one must not pick another field of the moving cube
        Assert.Equal(acts, ViewCubeMath.IsActingClick(clickCount));
    }

    // ---- geometry ----------------------------------------------------------------------------------

    [Fact]
    public void Each_of_the_six_faces_is_divided_into_nine_fields()
    {
        var quads = ViewCubeGeometry.Quads;

        Assert.Equal(54, quads.Count);
        foreach (var face in ViewCubeRegion.All.Where(r => r.Kind == ViewCubeKind.Face))
            Assert.Equal(9, quads.Count(q => q.Face == face));
    }

    [Fact]
    public void The_fields_belong_to_the_right_regions()
    {
        var quads = ViewCubeGeometry.Quads;

        Assert.Equal(6, quads.Count(q => q.Region.Kind == ViewCubeKind.Face));
        Assert.Equal(24, quads.Count(q => q.Region.Kind == ViewCubeKind.Edge)); // an edge has a field on each of its two faces
        Assert.Equal(24, quads.Count(q => q.Region.Kind == ViewCubeKind.Corner)); // a corner on each of its three
        foreach (var region in ViewCubeRegion.All)
            Assert.Equal(region.Kind switch { ViewCubeKind.Face => 1, ViewCubeKind.Edge => 2, _ => 3 }, quads.Count(q => q.Region == region));
    }

    [Fact]
    public void The_fields_of_a_face_lie_on_that_face_and_look_out_of_the_cube()
    {
        foreach (var quad in ViewCubeGeometry.Quads)
        {
            var normal = quad.Face.Direction;
            Assert.All(quad.Corners, c => Assert.Equal(ViewCubeGeometry.Half, Vector3.Dot(c, normal), 4));

            // counter-clockwise seen from outside: the triangle normal points the same way as the face
            var triangleNormal = Vector3.Cross(quad.Corners[1] - quad.Corners[0], quad.Corners[2] - quad.Corners[0]);
            Assert.True(Vector3.Dot(triangleNormal, normal) > 0, $"face {quad.Face} region {quad.Region}");
        }
    }

    [Fact]
    public void The_fields_cover_each_face_completely_without_overlap()
    {
        foreach (var face in ViewCubeRegion.All.Where(r => r.Kind == ViewCubeKind.Face))
        {
            var area = ViewCubeGeometry.Quads.Where(q => q.Face == face).Sum(q => Vector3.Cross(q.Corners[1] - q.Corners[0], q.Corners[3] - q.Corners[0]).Length());

            Assert.Equal(1.0, area, 4); // the face is 1 x 1
        }
    }

    [Fact]
    public void The_texture_coordinates_of_the_fields_cover_the_square_once()
    {
        foreach (var face in ViewCubeRegion.All.Where(r => r.Kind == ViewCubeKind.Face))
        {
            var area = ViewCubeGeometry.Quads.Where(q => q.Face == face)
                .Sum(q => Math.Abs((q.Uv[1].X - q.Uv[0].X) * (q.Uv[3].Y - q.Uv[0].Y)));

            Assert.Equal(1.0, area, 4);
            Assert.All(ViewCubeGeometry.Quads.Where(q => q.Face == face).SelectMany(q => q.Uv), uv =>
            {
                Assert.InRange(uv.X, -1e-6, 1 + 1e-6);
                Assert.InRange(uv.Y, -1e-6, 1 + 1e-6);
            });
        }
    }

    [Fact]
    public void The_faces_are_labelled_upright_when_seen_from_outside()
    {
        // right x up = the direction out of the face, for all six faces: the label is not mirrored
        foreach (var face in ViewCubeRegion.All.Where(r => r.Kind == ViewCubeKind.Face))
        {
            var (right, up) = ViewCubeGeometry.FrameOf(face);

            Assert.Equal(face.Direction, Vector3.Cross(right, up));
        }

        var (frontRight, frontUp) = ViewCubeGeometry.FrameOf(new ViewCubeRegion(0, -1, 0));
        Assert.Equal(Vector3.UnitX, frontRight);
        Assert.Equal(Vector3.UnitZ, frontUp);
    }

    [Fact]
    public void The_middle_field_is_the_biggest_and_the_corner_fields_the_smallest()
    {
        var front = new ViewCubeRegion(0, -1, 0);
        float Area(ViewCubeQuad q) => Vector3.Cross(q.Corners[1] - q.Corners[0], q.Corners[3] - q.Corners[0]).Length();
        var onFront = ViewCubeGeometry.Quads.Where(q => q.Face == front).ToList();

        var middle = onFront.Single(q => q.Region == front);
        var corner = onFront.First(q => q.Region.Kind == ViewCubeKind.Corner);

        Assert.True(Area(middle) > 3 * Area(corner));
    }
}
