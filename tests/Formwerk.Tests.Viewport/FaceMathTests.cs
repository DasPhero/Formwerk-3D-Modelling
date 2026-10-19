using System.Numerics;
using Formwerk.Features.Shapes;
using Formwerk.Viewport;

namespace Formwerk.Tests.Viewport;

/// <summary>The outward direction of a clicked face (F-ROT-04).</summary>
public class FaceMathTests
{
    [Fact]
    public void A_counter_clockwise_triangle_looks_at_the_viewer()
    {
        var normal = FaceMath.TriangleNormal(new Vector3(0, 0, 0), new Vector3(1, 0, 0), new Vector3(0, 1, 0));

        Assert.Equal(new Vector3(0, 0, 1), normal);
    }

    [Fact]
    public void The_normal_has_length_one_whatever_the_size_of_the_triangle()
    {
        var normal = FaceMath.TriangleNormal(new Vector3(0, 0, 5), new Vector3(300, 0, 5), new Vector3(0, 200, 5));

        Assert.Equal(1f, normal.Length(), 5);
        Assert.Equal(new Vector3(0, 0, 1), normal);
    }

    [Fact]
    public void A_triangle_without_area_gives_no_direction()
    {
        Assert.Equal(Vector3.Zero, FaceMath.TriangleNormal(Vector3.Zero, Vector3.UnitX, Vector3.UnitX * 2));
        Assert.Equal(Vector3.Zero, FaceMath.TriangleNormal(Vector3.One, Vector3.One, Vector3.UnitY));
    }

    [Theory]
    [InlineData("box")]
    [InlineData("sphere")]
    [InlineData("cylinder")]
    [InlineData("cone")]
    public void The_triangles_the_viewport_shows_have_their_normals_pointing_out_of_the_shape(string id)
    {
        var mesh = ShapeLibrary.Find(id)!.Generate(new Dictionary<string, double>());
        var geometry = MeshConverter.ToGeometry(mesh);
        var center = mesh.ComputeBounds().Center;
        var inside = center; // inside every convex shape
        var checkedTriangles = 0;

        for (var t = 0; t < geometry.TriangleIndices.Count; t += 3)
        {
            Vector3 P(int i) => new((float)geometry.Positions[geometry.TriangleIndices[t + i]].X,
                (float)geometry.Positions[geometry.TriangleIndices[t + i]].Y, (float)geometry.Positions[geometry.TriangleIndices[t + i]].Z);
            var a = P(0);
            var normal = FaceMath.TriangleNormal(a, P(1), P(2));
            if (normal == Vector3.Zero) continue;
            Assert.True(Vector3.Dot(normal, (a + P(1) + P(2)) / 3 - inside) > -1e-3f, $"{id}: triangle {t / 3} looks inwards");
            checkedTriangles++;
        }

        Assert.True(checkedTriangles > 0);
    }
}
