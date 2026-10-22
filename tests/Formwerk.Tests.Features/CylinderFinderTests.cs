using System.Numerics;
using Formwerk.Core.Model;
using Formwerk.Features.Shapes;
using Formwerk.Geometry;
using Formwerk.Kernel;

namespace Formwerk.Tests.Features;

/// <summary>Finding the cylinder that a clicked piece of a round wall belongs to (E-07, diameter).</summary>
public class CylinderFinderTests
{
    private static Dictionary<string, double> P(params (string, double)[] values) =>
        values.ToDictionary(v => v.Item1, v => v.Item2, StringComparer.OrdinalIgnoreCase);

    private static CylinderInfo? FindAt(MeshData mesh, Vector3 point)
    {
        // a point on the wall: the centroid of the nearest triangle, so that it lies inside it
        var vertices = mesh.Vertices.ToArray();
        var indices = mesh.Indices.ToArray();
        var best = float.MaxValue;
        Vector3 spot = point, normal = Vector3.UnitZ;
        for (var t = 0; t < indices.Length; t += 3)
        {
            Vector3 a = vertices[indices[t]], b = vertices[indices[t + 1]], c = vertices[indices[t + 2]];
            var centroid = (a + b + c) / 3;
            var distance = Vector3.Distance(centroid, point);
            if (distance >= best) continue;
            best = distance;
            spot = centroid;
            normal = Vector3.Normalize(Vector3.Cross(b - a, c - a));
        }

        return CylinderFinder.Find(mesh, spot, normal);
    }

    [Fact]
    public void The_wall_of_a_cylinder_gives_its_radius_and_axis()
    {
        var cylinder = new CylinderGenerator().Generate(P(("diameter", 20), ("height", 10), ("segments", 64)));

        var info = FindAt(cylinder, new Vector3(10, 0, 5));

        Assert.NotNull(info);
        Assert.Equal(10, info!.Radius, 0.01);
        Assert.Equal(1, Math.Abs(Vector3.Dot(info.Axis, Vector3.UnitZ)), 1e-3);
        Assert.False(info.IsHole);
        Assert.Equal(0, new Vector2(info.Center.X, info.Center.Y).Length(), 0.02);
    }

    [Fact]
    public void The_center_is_on_the_axis_at_the_height_of_the_click()
    {
        var cylinder = new CylinderGenerator().Generate(P(("diameter", 20), ("height", 10), ("segments", 64)));

        var info = FindAt(cylinder, new Vector3(0, 10, 7));

        Assert.NotNull(info);
        Assert.Equal(0, info!.Center.X, 0.02);
        Assert.Equal(0, info.Center.Y, 0.02);
        Assert.InRange(info.Center.Z, 5, 9); // the height of the clicked piece of the wall
    }

    [Fact]
    public void The_wall_of_a_hole_is_found_as_a_hole()
    {
        var tube = new TubeGenerator().Generate(P(("outerDiameter", 20), ("wall", 2), ("height", 10), ("segments", 64)));

        var info = FindAt(tube, new Vector3(-8, 0, 5));

        Assert.NotNull(info);
        Assert.Equal(8, info!.Radius, 0.01);
        Assert.True(info.IsHole);
    }

    [Fact]
    public void The_outer_wall_of_the_same_tube_is_not_a_hole()
    {
        var tube = new TubeGenerator().Generate(P(("outerDiameter", 20), ("wall", 2), ("height", 10), ("segments", 64)));

        var info = FindAt(tube, new Vector3(0, 10, 5));

        Assert.Equal(10, info!.Radius, 0.01);
        Assert.False(info.IsHole);
    }

    [Fact]
    public void A_hole_drilled_with_the_kernel_is_measured()
    {
        var box = new BoxGenerator().Generate(P());
        var drill = new CylinderGenerator().Generate(P(("diameter", 5), ("height", 40), ("segments", 48))).Transformed(Matrix4x4.CreateTranslation(0, 0, -10));
        var drilled = new ManifoldKernel().Boolean(BooleanOperation.Difference, [box, drill], CancellationToken.None);

        var info = FindAt(drilled, new Vector3(2.5f, 0, 10));

        Assert.NotNull(info);
        Assert.Equal(2.5, info!.Radius, 0.02);
        Assert.True(info.IsHole);
    }

    [Fact]
    public void A_cylinder_lying_on_its_side_is_found_too()
    {
        var cylinder = new CylinderGenerator().Generate(P(("diameter", 14), ("height", 30), ("segments", 64)))
            .Transformed(Matrix4x4.CreateRotationY(MathF.PI / 2));

        var info = FindAt(cylinder, new Vector3(15, 7, 0));

        Assert.NotNull(info);
        Assert.Equal(7, info!.Radius, 0.01);
        Assert.Equal(1, Math.Abs(Vector3.Dot(info.Axis, Vector3.UnitX)), 1e-3);
    }

    [Fact]
    public void A_flat_face_is_no_cylinder()
    {
        var box = new BoxGenerator().Generate(P());

        Assert.Null(CylinderFinder.Find(box, new Vector3(0, 0, 20), Vector3.UnitZ));
        Assert.Null(CylinderFinder.Find(box, new Vector3(10, 0, 10), Vector3.UnitX));
    }

    [Fact]
    public void The_flat_cap_of_a_cylinder_is_no_cylinder()
    {
        var cylinder = new CylinderGenerator().Generate(P(("segments", 64)));

        Assert.Null(CylinderFinder.Find(cylinder, new Vector3(2, 1, 20), Vector3.UnitZ));
    }

    [Fact]
    public void A_cone_and_a_sphere_are_no_cylinders()
    {
        var cone = new ConeGenerator().Generate(P(("segments", 64)));
        var sphere = new SphereGenerator().Generate(P());

        Assert.Null(FindAt(cone, new Vector3(5, 0, 10)));
        Assert.Null(FindAt(sphere, new Vector3(10, 0, 0)));
    }

    [Fact]
    public void A_wall_with_few_corners_is_flat_pieces_and_not_a_cylinder()
    {
        var prism = new PrismGenerator().Generate(P(("sides", 6)));

        Assert.Null(FindAt(prism, new Vector3(10, 0, 10)));
    }

    [Fact]
    public void A_click_that_is_on_no_face_finds_nothing()
    {
        var cylinder = new CylinderGenerator().Generate(P(("segments", 64)));

        Assert.Null(CylinderFinder.Find(cylinder, new Vector3(50, 50, 50), Vector3.UnitX));
        Assert.Null(CylinderFinder.Find(MeshData.Empty, Vector3.Zero, Vector3.UnitX));
        Assert.Null(CylinderFinder.Find(cylinder, new Vector3(10, 0, 5), Vector3.Zero));
    }

    [Fact]
    public void The_diameter_is_exact_to_a_hundredth_for_awkward_sizes()
    {
        var cylinder = new CylinderGenerator().Generate(P(("diameter", 17.37), ("height", 12), ("segments", 128)));

        var info = FindAt(cylinder, new Vector3(0, 8.685f, 6));

        Assert.Equal(17.37, info!.Radius * 2, 0.01);
    }
}
