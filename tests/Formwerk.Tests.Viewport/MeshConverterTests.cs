using System.Numerics;
using Formwerk.Features.Shapes;
using Formwerk.Geometry;
using Formwerk.Viewport;

namespace Formwerk.Tests.Viewport;

public class MeshConverterTests
{
    private static Dictionary<string, double> P(params (string, double)[] v) =>
        v.ToDictionary(x => x.Item1, x => x.Item2, StringComparer.OrdinalIgnoreCase);

    private static Vector3 N(System.Windows.Media.Media3D.Vector3D v) => new((float)v.X, (float)v.Y, (float)v.Z);

    private static Vector3 Pt(System.Windows.Media.Media3D.Point3D p) => new((float)p.X, (float)p.Y, (float)p.Z);

    [Fact]
    public void Box_keeps_sharp_edges()
    {
        var geometry = MeshConverter.ToGeometry(new BoxGenerator().Generate(P()));

        Assert.Equal(24, geometry.Positions.Count); // a corner of a box has one vertex per face that meets there: 8 x 3
        Assert.Equal(24, geometry.Normals.Count);
        Assert.Equal(36, geometry.TriangleIndices.Count);
        Assert.All(geometry.TriangleIndices, i => Assert.InRange(i, 0, 23));
        foreach (var normal in geometry.Normals.Select(N))
        {
            Assert.Equal(1f, normal.Length(), 3);
            // Each normal is exactly one of the six axis directions: no smoothing across the 90° edges.
            Assert.Equal(1f, Math.Max(Math.Abs(normal.X), Math.Max(Math.Abs(normal.Y), Math.Abs(normal.Z))), 3);
        }
    }

    [Fact]
    public void Sphere_is_shaded_smoothly()
    {
        var mesh = new SphereGenerator().Generate(P(("diameter", 20), ("segments", 48), ("rings", 24)));
        var geometry = MeshConverter.ToGeometry(mesh);
        var center = new Vector3(0, 0, 10);

        for (var i = 0; i < geometry.Positions.Count; i++)
        {
            var radial = Vector3.Normalize(Pt(geometry.Positions[i]) - center);
            Assert.True(Vector3.Dot(radial, N(geometry.Normals[i])) > 0.98f, $"vertex {i}");
        }
    }

    [Fact]
    public void Cylinder_has_smooth_sides_and_flat_caps()
    {
        var mesh = new CylinderGenerator().Generate(P(("diameter", 20), ("height", 10), ("segments", 64)));
        var geometry = MeshConverter.ToGeometry(mesh);

        for (var i = 0; i < geometry.Positions.Count; i++)
        {
            var p = Pt(geometry.Positions[i]);
            var n = N(geometry.Normals[i]);
            if (Math.Abs(n.Z) > 0.5f)
            {
                Assert.Equal(1f, Math.Abs(n.Z), 3); // caps are exactly flat
            }
            else
            {
                var radial = Vector3.Normalize(new Vector3(p.X, p.Y, 0));
                Assert.True(Vector3.Dot(radial, n) > 0.99f);
            }
        }
    }

    [Fact]
    public void Empty_mesh_gives_empty_geometry()
    {
        var geometry = MeshConverter.ToGeometry(MeshData.Empty);
        Assert.Empty(geometry.Positions);
        Assert.True(geometry.IsFrozen);
    }

    [Fact]
    public void Large_mesh_converts_in_reasonable_time()
    {
        var mesh = new SphereGenerator().Generate(P(("diameter", 20), ("segments", 256), ("rings", 128)));
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var geometry = MeshConverter.ToGeometry(mesh);
        sw.Stop();
        Assert.InRange(geometry.Positions.Count, mesh.VertexCount * 0.95, mesh.VertexCount * 1.2); // smooth: about one vertex per source vertex
        Assert.True(sw.ElapsedMilliseconds < 3000, $"{mesh.TriangleCount} triangles took {sw.ElapsedMilliseconds} ms");
    }

    [Fact]
    public void A_smooth_surface_shares_its_vertices_and_needs_far_fewer_than_three_per_triangle()
    {
        var mesh = new SphereGenerator().Generate(P(("diameter", 20), ("segments", 64), ("rings", 32)));

        var geometry = MeshConverter.ToGeometry(mesh);

        Assert.True(geometry.Positions.Count * 4 < mesh.TriangleCount * 3, $"{geometry.Positions.Count} vertices for {mesh.TriangleCount} triangles");
        Assert.Equal(mesh.TriangleCount * 3, geometry.TriangleIndices.Count);
        Assert.All(geometry.TriangleIndices, i => Assert.InRange(i, 0, geometry.Positions.Count - 1));
    }

    [Fact]
    public void Every_triangle_keeps_its_corners_and_its_orientation_after_sharing_vertices()
    {
        var mesh = new SphereGenerator().Generate(P(("diameter", 20), ("segments", 24), ("rings", 12)));
        var geometry = MeshConverter.ToGeometry(mesh);

        for (var t = 0; t < mesh.TriangleCount; t++)
        {
            for (var c = 0; c < 3; c++)
            {
                var original = mesh.Vertices[mesh.Indices[t * 3 + c]];
                var shown = Pt(geometry.Positions[geometry.TriangleIndices[t * 3 + c]]);
                Assert.Equal(original.X, shown.X, 4);
                Assert.Equal(original.Y, shown.Y, 4);
                Assert.Equal(original.Z, shown.Z, 4);
            }
        }
    }

    [Fact]
    public void A_cylinder_is_smooth_on_the_side_and_sharp_at_the_rims()
    {
        var mesh = new CylinderGenerator().Generate(P(("diameter", 20), ("height", 30)));
        var geometry = MeshConverter.ToGeometry(mesh);

        var axisNormals = geometry.Normals.Select(N).Count(n => Math.Abs(n.Z) > 0.999f);
        var sideNormals = geometry.Normals.Select(N).Count(n => Math.Abs(n.Z) < 0.01f);
        Assert.True(axisNormals > 0 && sideNormals > 0);
        Assert.Equal(geometry.Normals.Count, axisNormals + sideNormals); // nothing in between: no smoothing across the 90 degree rim
    }
}
