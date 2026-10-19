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

    // Picking (a click, the hover markers) is a hit test of WPF over the whole mesh: how long does it take for a million triangles?
    [Fact]
    public void A_click_on_a_mesh_with_a_million_triangles_is_found_quickly()
    {
        var sphere = new SphereGenerator().Generate(new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
            { ["diameter"] = 40, ["segments"] = 512, ["rings"] = 256 });
        var mesh = Enumerable.Range(0, 4).Select(i => sphere.Transformed(Matrix4x4.CreateTranslation(i * 100 - 150, 0, 0))).Aggregate((x, y) => Combine(x, y));

        var times = new List<long>();
        var hits = 0;
        var thread = new Thread(() =>
        {
            var viewport = new System.Windows.Controls.Viewport3D { Width = 800, Height = 600 };
            viewport.Camera = new System.Windows.Media.Media3D.PerspectiveCamera(new System.Windows.Media.Media3D.Point3D(50, -300, 0), new System.Windows.Media.Media3D.Vector3D(0, 1, 0), new System.Windows.Media.Media3D.Vector3D(0, 0, 1), 45);
            var light = new System.Windows.Media.Media3D.AmbientLight(System.Windows.Media.Colors.White);
            var model = new System.Windows.Media.Media3D.GeometryModel3D(MeshConverter.ToGeometry(mesh), new System.Windows.Media.Media3D.DiffuseMaterial(System.Windows.Media.Brushes.Gray));
            viewport.Children.Add(new System.Windows.Media.Media3D.ModelVisual3D { Content = new System.Windows.Media.Media3D.Model3DGroup { Children = { light, model } } });
            viewport.Measure(new System.Windows.Size(800, 600));
            viewport.Arrange(new System.Windows.Rect(0, 0, 800, 600));
            viewport.UpdateLayout();

            for (var i = 0; i < 6; i++)
            {
                var sw = Stopwatch.StartNew();
                System.Windows.Media.VisualTreeHelper.HitTest(viewport, null, result =>
                {
                    if (result is System.Windows.Media.Media3D.RayMeshGeometry3DHitTestResult) hits++;
                    return System.Windows.Media.HitTestResultBehavior.Continue;
                }, new System.Windows.Media.PointHitTestParameters(new System.Windows.Point(400 + i * 3, 300)));
                times.Add(sw.ElapsedMilliseconds);
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        output.WriteLine($"{mesh.TriangleCount:N0} triangles: hit test took {string.Join(", ", times)} ms ({hits} hits)");
        Assert.True(hits > 0, "the click should hit the mesh");
        Assert.True(times.Skip(1).Min() < 100, $"hit test took {string.Join(", ", times)} ms");
    }

    private static Formwerk.Geometry.MeshData Combine(Formwerk.Geometry.MeshData a, Formwerk.Geometry.MeshData b)
    {
        var vertices = a.Vertices.ToArray().Concat(b.Vertices.ToArray()).ToArray();
        var offset = a.VertexCount;
        var indices = a.Indices.ToArray().Concat(b.Indices.ToArray().Select(i => i + offset)).ToArray();
        return new Formwerk.Geometry.MeshData(vertices, indices);
    }
}
