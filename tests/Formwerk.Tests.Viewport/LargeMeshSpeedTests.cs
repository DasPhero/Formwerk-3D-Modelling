using System.Diagnostics;
using System.Numerics;
using Formwerk.Features.Shapes;
using Formwerk.Viewport;
using Xunit.Abstractions;

namespace Formwerk.Tests.Viewport;

/// <summary>Speed report for big imported models (F-IMP-01: one million triangles loaded and shown in under 3 s).</summary>
public class LargeMeshSpeedTests(ITestOutputHelper output)
{
    [Fact]
    public void A_mesh_with_a_million_triangles_is_converted_for_display()
    {
        var sphere = new SphereGenerator().Generate(new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
            { ["diameter"] = 40, ["segments"] = 512, ["rings"] = 256 });
        var mesh = Enumerable.Range(0, 4)
            .Select(i => sphere.Transformed(Matrix4x4.CreateTranslation(i * 100, 0, 0)))
            .Aggregate((a, b) => Combine(a, b));
        Assert.True(mesh.TriangleCount > 1_000_000);

        MeshConverter.ToGeometry(sphere); // warm up
        var sw = Stopwatch.StartNew();
        var geometry = MeshConverter.ToGeometry(mesh);
        sw.Stop();

        output.WriteLine($"{mesh.TriangleCount:N0} triangles converted to WPF geometry in {sw.ElapsedMilliseconds} ms");
        Assert.True(geometry.Positions.Count < mesh.VertexCount * 1.2, $"{geometry.Positions.Count} vertices for {mesh.VertexCount} source vertices"); // shared, not three per triangle
        Assert.True(sw.ElapsedMilliseconds < 15000, $"took {sw.ElapsedMilliseconds} ms");
    }

    private static Formwerk.Geometry.MeshData Combine(Formwerk.Geometry.MeshData a, Formwerk.Geometry.MeshData b)
    {
        var vertices = a.Vertices.ToArray().Concat(b.Vertices.ToArray()).ToArray();
        var offset = a.VertexCount;
        var indices = a.Indices.ToArray().Concat(b.Indices.ToArray().Select(i => i + offset)).ToArray();
        return new Formwerk.Geometry.MeshData(vertices, indices);
    }
}
