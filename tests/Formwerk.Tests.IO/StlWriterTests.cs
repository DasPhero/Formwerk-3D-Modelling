using System.Numerics;
using Formwerk.Features.Shapes;
using Formwerk.IO;

namespace Formwerk.Tests.IO;

public class StlWriterTests
{
    private static Formwerk.Geometry.MeshData Box() =>
        new BoxGenerator().Generate(new Dictionary<string, double> { ["width"] = 10, ["depth"] = 20, ["height"] = 30 });

    [Fact]
    public void Binary_stl_has_the_exact_size_and_triangle_count()
    {
        using var stream = new MemoryStream();
        StlWriter.WriteBinary(stream, [Box(), Box()]);

        Assert.Equal(StlWriter.SizeFor(24), stream.Length);
        var bytes = stream.ToArray();
        Assert.Equal(24u, BitConverter.ToUInt32(bytes, 80));
    }

    [Fact]
    public void Triangles_carry_outward_unit_normals_and_the_vertex_positions()
    {
        using var stream = new MemoryStream();
        StlWriter.WriteBinary(stream, [Box()]);
        var bytes = stream.ToArray();

        // Sum of all signed volumes from the written triangles must equal the box volume.
        var volume = 0.0;
        for (var t = 0; t < 12; t++)
        {
            var o = 84 + t * 50;
            var n = new Vector3(BitConverter.ToSingle(bytes, o), BitConverter.ToSingle(bytes, o + 4), BitConverter.ToSingle(bytes, o + 8));
            Assert.Equal(1f, n.Length(), 3);
            var a = ReadVector(bytes, o + 12);
            var b = ReadVector(bytes, o + 24);
            var c = ReadVector(bytes, o + 36);
            Assert.True(Vector3.Dot(Vector3.Normalize(Vector3.Cross(b - a, c - a)), n) > 0.999f);
            volume += Vector3.Dot(a, Vector3.Cross(b, c)) / 6.0;
        }

        Assert.Equal(6000, volume, 2);
    }

    [Fact]
    public void Empty_scene_writes_a_valid_empty_file()
    {
        using var stream = new MemoryStream();
        StlWriter.WriteBinary(stream, []);
        Assert.Equal(84, stream.Length);
    }

    [Fact]
    public void Long_header_text_is_truncated_to_80_bytes()
    {
        using var stream = new MemoryStream();
        StlWriter.WriteBinary(stream, [], new string('x', 200));
        Assert.Equal(84, stream.Length);
    }

    private static Vector3 ReadVector(byte[] b, int o) =>
        new(BitConverter.ToSingle(b, o), BitConverter.ToSingle(b, o + 4), BitConverter.ToSingle(b, o + 8));
}
