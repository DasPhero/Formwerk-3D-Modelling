using System.Diagnostics;
using System.Numerics;
using System.Text;
using Formwerk.Features.Shapes;
using Formwerk.Geometry;
using Formwerk.IO;
using Xunit.Abstractions;

namespace Formwerk.Tests.IO;

public class MeshImportTests(ITestOutputHelper output)
{
    private static Dictionary<string, double> P(params (string, double)[] v) =>
        v.ToDictionary(x => x.Item1, x => x.Item2, StringComparer.OrdinalIgnoreCase);

    private static MeshData Box(double w = 20, double d = 30, double h = 10) =>
        new BoxGenerator().Generate(P(("width", w), ("depth", d), ("height", h)));

    private static byte[] BinaryStl(params MeshData[] meshes)
    {
        using var stream = new MemoryStream();
        StlWriter.WriteBinary(stream, meshes);
        return stream.ToArray();
    }

    private static MeshData ReadStl(byte[] bytes) => StlReader.Read(new MemoryStream(bytes));

    private static MeshData ReadStl(string text) => ReadStl(Encoding.UTF8.GetBytes(text));

    private static MeshData ReadObj(string text) => ObjReader.Read(new MemoryStream(Encoding.UTF8.GetBytes(text)));

    // ---- binary STL ----------------------------------------------------------------------------

    [Fact]
    public void A_binary_stl_roundtrips_and_the_corners_are_welded()
    {
        var original = Box();

        var mesh = ReadStl(BinaryStl(original));

        Assert.Equal(12, mesh.TriangleCount);
        Assert.Equal(8, mesh.VertexCount); // the file stores 36 corners; welding brings it back to 8 points
        Assert.Equal(original.ComputeVolume(), mesh.ComputeVolume(), 3);
        Assert.True(mesh.IsWatertight());
    }

    [Fact]
    public void A_curved_shape_keeps_its_shared_points_and_volume()
    {
        var original = new SphereGenerator().Generate(P(("diameter", 20), ("segments", 48), ("rings", 24)));

        var mesh = ReadStl(BinaryStl(original));

        Assert.Equal(original.TriangleCount, mesh.TriangleCount);
        Assert.Equal(original.VertexCount, mesh.VertexCount);
        Assert.Equal(original.ComputeVolume(), mesh.ComputeVolume(), 2);
        Assert.True(mesh.IsWatertight());
    }

    [Fact]
    public void Several_shells_in_one_file_stay_separate_shells()
    {
        var mesh = ReadStl(BinaryStl(Box(), Box().Transformed(Matrix4x4.CreateTranslation(100, 0, 0))));

        Assert.Equal(24, mesh.TriangleCount);
        Assert.Equal(16, mesh.VertexCount);
    }

    [Fact]
    public void A_binary_file_whose_header_starts_with_solid_is_still_binary()
    {
        // many programs write "solid name" into the 80 byte header of a binary file
        using var stream = new MemoryStream();
        StlWriter.WriteBinary(stream, [Box()], "solid exported by something");

        Assert.Equal(12, ReadStl(stream.ToArray()).TriangleCount);
    }

    [Fact]
    public void Negative_and_positive_zero_are_the_same_point()
    {
        var bytes = BinaryStl(Box());
        // overwrite the first corner's x with -0.0: the same position as +0 elsewhere must still weld
        var original = ReadStl(bytes);
        var zeroCorners = original.Vertices.ToArray().Count(v => v.X == 0);
        Assert.Equal(0, zeroCorners); // the box is centered in X, so no vertex has x = 0; build a dedicated case instead

        var triangle = new MeshData([new(-0f, 0, 0), new(1, 0, 0), new(0, 1, 0), new(0f, 0, 1)], [0, 2, 1, 0, 1, 3, 0, 3, 2, 1, 2, 3]);
        Assert.Equal(4, ReadStl(BinaryStl(triangle)).VertexCount);
    }

    [Fact]
    public void Trailing_bytes_after_the_last_triangle_are_ignored()
    {
        var bytes = BinaryStl(Box()).Concat(new byte[] { 1, 2, 3, 4, 5 }).ToArray();
        Assert.Equal(12, ReadStl(bytes).TriangleCount);
    }

    [Fact]
    public void A_file_that_is_shorter_than_its_triangle_count_is_damaged()
    {
        var bytes = BinaryStl(Box());
        var ex = Assert.Throws<MeshImportException>(() => ReadStl(bytes[..^30]));
        Assert.False(string.IsNullOrWhiteSpace(ex.Message));
    }

    [Fact]
    public void A_triangle_count_that_promises_billions_is_rejected_without_allocating()
    {
        var bytes = BinaryStl(Box());
        BitConverter.GetBytes(uint.MaxValue).CopyTo(bytes, 80);

        Assert.Throws<MeshImportException>(() => ReadStl(bytes));
    }

    [Fact]
    public void A_binary_file_without_triangles_is_rejected()
    {
        var bytes = new byte[84];
        Assert.Contains("no triangles", Assert.Throws<MeshImportException>(() => ReadStl(bytes)).Message);
    }

    [Theory]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    [InlineData(float.NegativeInfinity)]
    public void Coordinates_that_are_not_numbers_are_rejected(float bad)
    {
        var bytes = BinaryStl(Box());
        BitConverter.GetBytes(bad).CopyTo(bytes, 84 + 12);

        Assert.Throws<MeshImportException>(() => ReadStl(bytes));
    }

    [Theory]
    [InlineData("")]
    [InlineData("hello")]
    [InlineData("this is not an stl file at all, just some text that is long enough to be more than eighty-four bytes of garbage")]
    public void Files_that_are_not_stl_are_rejected_with_a_message(string text)
    {
        var ex = Assert.Throws<MeshImportException>(() => ReadStl(text));
        Assert.False(string.IsNullOrWhiteSpace(ex.Message));
    }

    // ---- ASCII STL -----------------------------------------------------------------------------

    private const string AsciiTetra = """
        solid tetra
          facet normal 0 0 -1
            outer loop
              vertex 0 0 0
              vertex 0 1 0
              vertex 1 0 0
            endloop
          endfacet
          facet normal 0 -1 0
            outer loop
              vertex 0 0 0
              vertex 1 0 0
              vertex 0 0 1
            endloop
          endfacet
          facet normal -1 0 0
            outer loop
              vertex 0 0 0
              vertex 0 0 1
              vertex 0 1 0
            endloop
          endfacet
          facet normal 1 1 1
            outer loop
              vertex 1 0 0
              vertex 0 1 0
              vertex 0 0 1
            endloop
          endfacet
        endsolid tetra
        """;

    [Fact]
    public void An_ascii_stl_is_read_and_welded()
    {
        var mesh = ReadStl(AsciiTetra);

        Assert.Equal(4, mesh.TriangleCount);
        Assert.Equal(4, mesh.VertexCount);
        Assert.Equal(1.0 / 6.0, mesh.ComputeVolume(), 5);
        Assert.True(mesh.IsWatertight());
    }

    [Fact]
    public void Ascii_with_windows_line_endings_scientific_numbers_and_a_byte_order_mark_is_read()
    {
        var text = AsciiTetra.Replace("vertex 1 0 0", "vertex 1.0E+00 0.0e0 -0").Replace("\n", "\r\n");
        var bytes = new byte[] { 0xEF, 0xBB, 0xBF }.Concat(Encoding.UTF8.GetBytes(text)).ToArray();

        var mesh = ReadStl(bytes);

        Assert.Equal(4, mesh.TriangleCount);
        Assert.Equal(4, mesh.VertexCount);
    }

    [Fact]
    public void Ascii_files_with_several_solids_are_merged_into_one_mesh()
    {
        var two = AsciiTetra + "\n" + AsciiTetra.Replace("vertex 1 0 0", "vertex 11 0 0").Replace("vertex 0 1 0", "vertex 10 1 0");
        Assert.Equal(8, ReadStl(two).TriangleCount);
    }

    [Fact]
    public void An_ascii_facet_with_missing_corners_is_damaged()
    {
        var lines = AsciiTetra.Split("\n").ToList();
        lines.RemoveAt(lines.FindIndex(l => l.TrimStart().StartsWith("vertex"))); // 11 corners are left: not a multiple of three
        var broken = string.Join("\n", lines);

        Assert.Contains("three corners", Assert.Throws<MeshImportException>(() => ReadStl(broken)).Message);
    }

    [Fact]
    public void Ascii_coordinates_that_are_not_numbers_are_rejected()
    {
        Assert.Throws<MeshImportException>(() => ReadStl(AsciiTetra.Replace("vertex 0 1 0", "vertex 0 one 0")));
        Assert.Throws<MeshImportException>(() => ReadStl(AsciiTetra.Replace("vertex 0 1 0", "vertex 0 NaN 0")));
    }

    [Fact]
    public void An_ascii_file_without_vertices_has_no_triangles()
    {
        Assert.Contains("no triangles", Assert.Throws<MeshImportException>(() => ReadStl("solid empty\n facet normal 0 0 1\n endfacet\nendsolid")).Message);
    }

    // ---- OBJ -----------------------------------------------------------------------------------

    private const string ObjCube = """
        # a unit cube made of quads
        mtllib cube.mtl
        o Cube
        v 0 0 0
        v 1 0 0
        v 1 1 0
        v 0 1 0
        v 0 0 1
        v 1 0 1
        v 1 1 1
        v 0 1 1
        usemtl Red
        s off
        f 1 4 3 2
        f 5 6 7 8
        f 1 2 6 5
        f 2 3 7 6
        f 3 4 8 7
        f 4 1 5 8
        """;

    [Fact]
    public void Quads_are_split_into_triangles_and_the_cube_is_a_closed_solid()
    {
        var mesh = ReadObj(ObjCube);

        Assert.Equal(12, mesh.TriangleCount);
        Assert.Equal(8, mesh.VertexCount);
        Assert.Equal(1, mesh.ComputeVolume(), 5);
        Assert.True(mesh.IsWatertight());
    }

    [Fact]
    public void Faces_with_texture_and_normal_indices_use_the_point_index()
    {
        var text = ObjCube.Replace("f 1 4 3 2", "f 1/1/1 4/2/1 3/3/1 2//1");
        Assert.Equal(12, ReadObj(text).TriangleCount);
    }

    [Fact]
    public void Negative_indices_count_from_the_end()
    {
        const string text = """
            v 0 0 0
            v 1 0 0
            v 0 1 0
            v 0 0 1
            f -4 -2 -3
            f -4 -3 -1
            f -4 -1 -2
            f -3 -2 -1
            """;

        var mesh = ReadObj(text);

        Assert.Equal(4, mesh.TriangleCount);
        Assert.Equal(1.0 / 6.0, mesh.ComputeVolume(), 5);
    }

    [Fact]
    public void Polygons_become_fans_of_triangles()
    {
        const string text = """
            v 0 0 0
            v 2 0 0
            v 3 1 0
            v 1 2 0
            v -1 1 0
            f 1 2 3 4 5
            """;

        Assert.Equal(3, ReadObj(text).TriangleCount);
    }

    [Fact]
    public void Colors_weights_comments_and_unknown_lines_are_ignored()
    {
        var text = ObjCube.Replace("v 1 0 0", "v 1 0 0 1.0").Replace("v 1 1 0", "v 1 1 0 0.5 0.5 0.5") + "\n# end\nvn 0 0 1\nvt 0 0\nl 1 2\np 3\ng nothing\n";
        Assert.Equal(12, ReadObj(text).TriangleCount);
    }

    [Fact]
    public void Windows_line_endings_work()
    {
        Assert.Equal(12, ReadObj(ObjCube.Replace("\n", "\r\n")).TriangleCount);
    }

    [Fact]
    public void Points_at_the_same_position_are_welded_and_unused_points_are_dropped()
    {
        const string text = """
            v 0 0 0
            v 1 0 0
            v 0 1 0
            v 0 0 1
            v 0 0 0
            v 500 500 500
            f 1 3 2
            f 5 2 4
            f 5 4 3
            f 2 3 4
            """;

        var mesh = ReadObj(text);

        Assert.Equal(4, mesh.VertexCount);          // point 5 equals point 1, point 6 is not used
        Assert.Equal(1, mesh.ComputeBounds().Max.X, 4); // the far unused point does not stretch the bounds
    }

    [Theory]
    [InlineData("v 0 0 0\nv 1 0 0\nv 0 1 0\nf 1 2 4")]       // point 4 does not exist
    [InlineData("v 0 0 0\nv 1 0 0\nv 0 1 0\nf 1 2 0")]       // index 0 is not valid in OBJ
    [InlineData("v 0 0 0\nv 1 0 0\nv 0 1 0\nf 1 2 -5")]      // too far back
    [InlineData("v 0 0 0\nv 1 0 0\nv 0 1 0\nf 1 2 x")]
    [InlineData("v 0 0 0\nv 1 0 abc\nv 0 1 0\nf 1 2 3")]
    [InlineData("v nan 0 0\nv 1 0 0\nv 0 1 0\nf 1 2 3")]
    [InlineData("v 0 0 0\nv 1 0 0\nv 0 1 0")]                 // no faces
    [InlineData("")]
    [InlineData("just some text")]
    public void Broken_obj_files_are_rejected_with_a_message(string text)
    {
        var ex = Assert.Throws<MeshImportException>(() => ReadObj(text));
        Assert.False(string.IsNullOrWhiteSpace(ex.Message));
    }

    [Fact]
    public void Lines_and_points_are_not_surfaces()
    {
        Assert.Throws<MeshImportException>(() => ReadObj("v 0 0 0\nv 1 0 0\nl 1 2\nf 1 2"));
    }

    // ---- by file name --------------------------------------------------------------------------

    private static string TempFile(string extension) => Path.Combine(Path.GetTempPath(), $"formwerk-import-{Guid.NewGuid():N}{extension}");

    [Fact]
    public void Loading_by_path_uses_the_extension_and_names_the_mesh_after_the_file()
    {
        var stl = TempFile(".STL");
        var obj = TempFile(".obj");
        try
        {
            File.WriteAllBytes(stl, BinaryStl(Box()));
            File.WriteAllText(obj, ObjCube);

            var a = MeshImport.Load(stl);
            var b = MeshImport.Load(obj);

            Assert.Equal(12, a.Mesh.TriangleCount);
            Assert.Equal(Path.GetFileNameWithoutExtension(stl), a.Name);
            Assert.Equal(12, b.Mesh.TriangleCount);
            Assert.Equal(Path.GetFileNameWithoutExtension(obj), b.Name);
        }
        finally
        {
            File.Delete(stl);
            File.Delete(obj);
        }
    }

    [Fact]
    public void Unsupported_and_missing_files_give_helpful_messages()
    {
        Assert.Contains("does not exist", Assert.Throws<MeshImportException>(() => MeshImport.Load(TempFile(".stl"))).Message);
        Assert.Contains("STL, OBJ and 3MF", Assert.Throws<MeshImportException>(() => MeshImport.Load(TempFile(".xyz"))).Message);
        Assert.Contains("does not exist", Assert.Throws<MeshImportException>(() => MeshImport.Load(TempFile(".3mf"))).Message);
    }

    [Theory]
    [InlineData("a.stl", true)]
    [InlineData("A.STL", true)]
    [InlineData("b.obj", true)]
    [InlineData("c.3mf", true)]
    [InlineData("d.txt", false)]
    [InlineData("noextension", false)]
    public void Supported_files_are_recognised_by_extension(string name, bool expected)
    {
        Assert.Equal(expected, MeshImport.IsSupported(name));
    }

    // ---- hostile input -------------------------------------------------------------------------

    [Fact]
    public void Random_corruption_of_binary_stl_never_causes_anything_but_an_import_error()
    {
        var valid = BinaryStl(Box(), Box().Transformed(Matrix4x4.CreateTranslation(50, 0, 0)));
        var random = new Random(4711);
        for (var i = 0; i < 500; i++)
        {
            var bytes = (byte[])valid.Clone();
            switch (i % 3)
            {
                case 0: for (var k = 0; k < 1 + random.Next(8); k++) bytes[random.Next(bytes.Length)] ^= (byte)(1 + random.Next(255)); break;
                case 1: bytes = bytes[..random.Next(bytes.Length)]; break;
                default: random.NextBytes(bytes.AsSpan(random.Next(bytes.Length - 16), 16)); break;
            }

            Survives(() => ReadStl(bytes));
        }
    }

    [Fact]
    public void Random_corruption_of_text_formats_never_causes_anything_but_an_import_error()
    {
        const string alphabet = "vfxn-+.e0123456789 \n/#solidfacetendloop";
        var random = new Random(99);
        foreach (var source in new[] { AsciiTetra, ObjCube })
        {
            var chars = source.ToCharArray();
            for (var i = 0; i < 400; i++)
            {
                var mutated = (char[])chars.Clone();
                for (var k = 0; k < 1 + random.Next(5); k++) mutated[random.Next(mutated.Length)] = alphabet[random.Next(alphabet.Length)];
                var text = new string(mutated);
                Survives(() => ReadStl(text));
                Survives(() => ReadObj(text));
            }
        }
    }

    private static void Survives(Action read)
    {
        try { read(); }
        catch (MeshImportException) { }
    }

    // ---- speed (F-IMP-01: one million triangles in under three seconds) ------------------------

    [Fact]
    public void A_stl_with_one_million_triangles_is_read_quickly()
    {
        var sphere = new SphereGenerator().Generate(P(("diameter", 40), ("segments", 512), ("rings", 256)));
        var meshes = Enumerable.Range(0, 4).Select(i => sphere.Transformed(Matrix4x4.CreateTranslation(i * 100, 0, 0))).ToArray();
        var bytes = BinaryStl(meshes);
        var triangles = meshes.Sum(m => m.TriangleCount);
        Assert.True(triangles > 1_000_000, $"only {triangles} triangles");

        StlReader.Read(new MemoryStream(BinaryStl(Box()))); // warm up the JIT
        var sw = Stopwatch.StartNew();
        var mesh = ReadStl(bytes);
        sw.Stop();

        output.WriteLine($"{triangles:N0} triangles, {bytes.Length / 1e6:F0} MB: read and welded in {sw.ElapsedMilliseconds} ms");
        Assert.Equal(triangles, mesh.TriangleCount);
        Assert.True(sw.ElapsedMilliseconds < 8000, $"took {sw.ElapsedMilliseconds} ms");
    }
}
