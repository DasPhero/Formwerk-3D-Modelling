using System.Globalization;
using System.Numerics;
using System.Text;
using Formwerk.Features.Shapes;
using Formwerk.Geometry;
using Formwerk.IO;

namespace Formwerk.Tests.IO;

/// <summary>Writing OBJ files (E-03).</summary>
public class ObjWriterTests
{
    private static MeshData Box() =>
        new BoxGenerator().Generate(new Dictionary<string, double> { ["width"] = 10, ["depth"] = 20, ["height"] = 30 });

    /// <summary>A single triangle with chosen coordinates.</summary>
    private static MeshData Triangle(Vector3 a, Vector3 b, Vector3 c) => new([a, b, c], [0, 1, 2]);

    private static string Write(params ObjObject[] objects)
    {
        using var stream = new MemoryStream();
        ObjWriter.Write(stream, objects);
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static string[] Lines(string text) => text.Split('\n', StringSplitOptions.RemoveEmptyEntries);

    [Fact]
    public void An_object_is_written_with_its_name_its_points_and_its_faces()
    {
        var mesh = Box();

        var lines = Lines(Write(new ObjObject("Plate", mesh)));

        Assert.Equal("o Plate", lines.Single(l => l.StartsWith("o ")));
        Assert.Equal(mesh.VertexCount, lines.Count(l => l.StartsWith("v ")));
        Assert.Equal(mesh.TriangleCount, lines.Count(l => l.StartsWith("f ")));
    }

    [Fact]
    public void The_file_says_where_it_comes_from_and_which_unit_it_uses()
    {
        var lines = Lines(Write(new ObjObject("A", Box())));

        Assert.StartsWith("# Formwerk", lines[0]);
        Assert.Contains(lines.Where(l => l.StartsWith('#')), l => l.Contains("millimetres"));
    }

    [Fact]
    public void Face_indices_start_at_one_and_continue_across_objects()
    {
        var first = Triangle(Vector3.Zero, Vector3.UnitX, Vector3.UnitY);
        var second = Triangle(new Vector3(5, 0, 0), new Vector3(6, 0, 0), new Vector3(5, 1, 0));

        var lines = Lines(Write(new ObjObject("One", first), new ObjObject("Two", second)));

        Assert.Equal(["f 1 2 3", "f 4 5 6"], lines.Where(l => l.StartsWith("f ")));
        Assert.Equal(["o One", "o Two"], lines.Where(l => l.StartsWith("o ")));
        // every object comes with its own points directly after its name
        var firstObject = Array.FindIndex(lines, l => l == "o One");
        var secondObject = Array.FindIndex(lines, l => l == "o Two");
        Assert.True(secondObject > firstObject);
        Assert.Equal(3, lines.Skip(firstObject).Take(secondObject - firstObject).Count(l => l.StartsWith("v ")));
    }

    [Fact]
    public void Coordinates_use_the_dot_whatever_the_language_of_the_computer()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            var text = Write(new ObjObject("A", Triangle(new Vector3(1.5f, -2.25f, 0.125f), Vector3.UnitX, Vector3.UnitY)));

            Assert.Contains("v 1.5 -2.25 0.125", text);
            Assert.All(Lines(text).Where(l => l.StartsWith("v ")), l => Assert.DoesNotContain(",", l));
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void Numbers_have_no_exponent_and_negative_zero_is_plain_zero()
    {
        var text = Write(new ObjObject("A", Triangle(new Vector3(-0f, 1e-7f, 12345.5f), new Vector3(1e-5f, 0, 0), new Vector3(0, 1, 0))));

        Assert.Contains("v 0 0 12345.5", text);
        Assert.Contains("v 0.00001 0 0", text);
        Assert.DoesNotContain("E", text);
        Assert.DoesNotContain("e-", text);
    }

    [Theory]
    [InlineData("Plate", "Plate")]
    [InlineData("Box 2", "Box 2")]
    [InlineData("  padded  ", "padded")]
    [InlineData("line\nbreak", "line break")]
    [InlineData("tab\there\r\n", "tab here")]
    [InlineData("", "Object")]
    [InlineData("   ", "Object")]
    public void Names_cannot_break_the_line_they_are_on(string name, string expected)
    {
        var lines = Lines(Write(new ObjObject(name, Box())));

        Assert.Equal("o " + expected, lines.Single(l => l.StartsWith("o ")));
    }

    [Fact]
    public void Objects_without_triangles_are_left_out()
    {
        var lines = Lines(Write(new ObjObject("Empty", MeshData.Empty), new ObjObject("Real", Box())));

        Assert.DoesNotContain("o Empty", lines);
        Assert.Contains("o Real", lines);
        Assert.Equal(Box().TriangleCount, lines.Count(l => l.StartsWith("f ")));
    }

    [Fact]
    public void Nothing_to_write_gives_a_file_with_only_comments()
    {
        var lines = Lines(Write());

        Assert.All(lines, l => Assert.StartsWith("#", l));
    }

    [Fact]
    public void The_file_is_utf8_without_a_byte_order_mark_and_uses_unix_line_ends()
    {
        using var stream = new MemoryStream();
        ObjWriter.Write(stream, [new ObjObject("Größe", Box())]);
        var bytes = stream.ToArray();

        Assert.NotEqual(0xEF, bytes[0]);
        Assert.DoesNotContain((byte)'\r', bytes);
        Assert.Contains("Größe", Encoding.UTF8.GetString(bytes));
    }

    [Fact]
    public void The_stream_stays_open_for_the_caller()
    {
        using var stream = new MemoryStream();

        ObjWriter.Write(stream, [new ObjObject("A", Box())]);

        Assert.True(stream.CanWrite);
        Assert.True(stream.Length > 0);
    }

    [Fact]
    public void What_is_written_can_be_read_back_with_the_same_shape()
    {
        var box = Box();
        using var stream = new MemoryStream();
        ObjWriter.Write(stream, [new ObjObject("Box", box)]);
        stream.Position = 0;

        var read = ObjReader.Read(stream);

        Assert.Equal(box.TriangleCount, read.TriangleCount);
        Assert.Equal(box.ComputeVolume(), read.ComputeVolume(), 3);
        Assert.True(read.IsWatertight());
        var a = box.ComputeBounds();
        var b = read.ComputeBounds();
        Assert.Equal(a.Min, b.Min);
        Assert.Equal(a.Max, b.Max);
    }

    [Fact]
    public void A_round_shape_survives_the_round_trip()
    {
        var sphere = new SphereGenerator().Generate(new Dictionary<string, double> { ["diameter"] = 20, ["segments"] = 32, ["rings"] = 16 });
        using var stream = new MemoryStream();
        ObjWriter.Write(stream, [new ObjObject("Ball", sphere)]);
        stream.Position = 0;

        var read = ObjReader.Read(stream);

        Assert.Equal(sphere.ComputeVolume(), read.ComputeVolume(), 2);
        Assert.True(read.IsWatertight());
    }
}
