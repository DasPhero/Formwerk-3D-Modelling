using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using System.Numerics;
using System.Xml;
using System.Xml.Linq;
using Formwerk.Features.Shapes;
using Formwerk.Geometry;
using Formwerk.IO;

namespace Formwerk.Tests.IO;

/// <summary>3MF export (E-03): a valid package that slicers can read, with names, colors and exact geometry.</summary>
public sealed class ThreeMfWriterTests : IDisposable
{
    private static readonly XNamespace Core = "http://schemas.microsoft.com/3dmanufacturing/core/2015/02";
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"formwerk-3mf-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true);
    }

    private static MeshData Box(double w = 10, double d = 20, double h = 30) =>
        new BoxGenerator().Generate(new Dictionary<string, double> { ["width"] = w, ["depth"] = d, ["height"] = h });

    private static ThreeMfObject Obj(string name = "Box", MeshData? mesh = null, uint color = 0xFFB0B8C4) => new(name, mesh ?? Box(), color);

    private static byte[] Write(params ThreeMfObject[] objects)
    {
        using var stream = new MemoryStream();
        ThreeMfWriter.Write(stream, objects);
        return stream.ToArray();
    }

    private static XDocument ReadModel(byte[] bytes)
    {
        using var zip = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
        using var entry = zip.GetEntry("3D/3dmodel.model")!.Open();
        using var reader = XmlReader.Create(entry, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
        return XDocument.Load(reader);
    }

    private static string ReadText(byte[] bytes, string name)
    {
        using var zip = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
        using var reader = new StreamReader(zip.GetEntry(name)!.Open());
        return reader.ReadToEnd();
    }

    private static IEnumerable<XElement> Objects(XDocument model) => model.Root!.Element(Core + "resources")!.Elements(Core + "object");

    /// <summary>Volume of one object computed from the written vertices and triangles (positive when the triangles face outward).</summary>
    private static double VolumeOf(XElement obj)
    {
        var mesh = obj.Element(Core + "mesh")!;
        var vertices = mesh.Element(Core + "vertices")!.Elements(Core + "vertex")
            .Select(v => new Vector3(Num(v, "x"), Num(v, "y"), Num(v, "z"))).ToList();
        var volume = 0.0;
        foreach (var t in mesh.Element(Core + "triangles")!.Elements(Core + "triangle"))
        {
            var a = vertices[int.Parse(t.Attribute("v1")!.Value, CultureInfo.InvariantCulture)];
            var b = vertices[int.Parse(t.Attribute("v2")!.Value, CultureInfo.InvariantCulture)];
            var c = vertices[int.Parse(t.Attribute("v3")!.Value, CultureInfo.InvariantCulture)];
            volume += Vector3.Dot(a, Vector3.Cross(b, c)) / 6.0;
        }

        return volume;
    }

    private static float Num(XElement e, string attribute) => float.Parse(e.Attribute(attribute)!.Value, CultureInfo.InvariantCulture);

    // ---- package structure -----------------------------------------------------------------------

    [Fact]
    public void The_package_has_the_three_required_parts()
    {
        var bytes = Write(Obj());

        using var zip = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
        Assert.Equal(["[Content_Types].xml", "_rels/.rels", "3D/3dmodel.model"], zip.Entries.Select(e => e.FullName).ToArray());

        var types = XDocument.Parse(ReadText(bytes, "[Content_Types].xml"));
        var defaults = types.Root!.Elements().ToDictionary(e => e.Attribute("Extension")!.Value, e => e.Attribute("ContentType")!.Value);
        Assert.Equal("application/vnd.openxmlformats-package.relationships+xml", defaults["rels"]);
        Assert.Equal("application/vnd.ms-package.3dmanufacturing-3dmodel+xml", defaults["model"]);

        var rels = XDocument.Parse(ReadText(bytes, "_rels/.rels"));
        var rel = Assert.Single(rels.Root!.Elements());
        Assert.Equal("/3D/3dmodel.model", rel.Attribute("Target")!.Value);
        Assert.Equal("http://schemas.microsoft.com/3dmanufacturing/2013/01/3dmodel", rel.Attribute("Type")!.Value);
        Assert.False(string.IsNullOrEmpty(rel.Attribute("Id")!.Value));
    }

    [Fact]
    public void The_model_uses_millimeters_and_the_core_namespace()
    {
        var model = ReadModel(Write(Obj()));

        Assert.Equal(Core + "model", model.Root!.Name);
        Assert.Equal("millimeter", model.Root.Attribute("unit")!.Value);
    }

    [Fact]
    public void Every_object_is_built_once()
    {
        var model = ReadModel(Write(Obj("A"), Obj("B"), Obj("C")));

        var ids = Objects(model).Select(o => o.Attribute("id")!.Value).ToList();
        var built = model.Root!.Element(Core + "build")!.Elements(Core + "item").Select(i => i.Attribute("objectid")!.Value).ToList();

        Assert.Equal(3, ids.Count);
        Assert.Equal(3, ids.Distinct().Count());
        Assert.Equal(ids, built);
        Assert.All(Objects(model), o => Assert.Equal("model", o.Attribute("type")!.Value));
    }

    // ---- geometry --------------------------------------------------------------------------------

    [Fact]
    public void Counts_and_volume_match_the_mesh()
    {
        var box = Box();
        var model = ReadModel(Write(Obj(mesh: box)));
        var mesh = Objects(model).Single().Element(Core + "mesh")!;

        Assert.Equal(box.VertexCount, mesh.Element(Core + "vertices")!.Elements().Count());
        Assert.Equal(box.TriangleCount, mesh.Element(Core + "triangles")!.Elements().Count());
        Assert.Equal(6000, VolumeOf(Objects(model).Single()), 3); // positive: the winding faces outward
    }

    [Fact]
    public void All_triangle_indices_point_at_existing_vertices()
    {
        var model = ReadModel(Write(Obj(mesh: new SphereGenerator().Generate(new Dictionary<string, double> { ["diameter"] = 20 }))));
        var mesh = Objects(model).Single().Element(Core + "mesh")!;
        var vertexCount = mesh.Element(Core + "vertices")!.Elements().Count();

        foreach (var t in mesh.Element(Core + "triangles")!.Elements())
        {
            foreach (var attribute in new[] { "v1", "v2", "v3" })
                Assert.InRange(int.Parse(t.Attribute(attribute)!.Value, CultureInfo.InvariantCulture), 0, vertexCount - 1);
        }
    }

    [Fact]
    public void Positions_are_kept_in_world_coordinates()
    {
        var moved = Box().Transformed(Matrix4x4.CreateTranslation(100, -50, 7));
        var model = ReadModel(Write(Obj(mesh: moved)));
        var xs = Objects(model).Single().Descendants(Core + "vertex").Select(v => Num(v, "x")).ToList();

        Assert.Equal(moved.ComputeBounds().Min.X, xs.Min(), 4);
        Assert.Equal(moved.ComputeBounds().Max.X, xs.Max(), 4);
    }

    [Fact]
    public void Numbers_have_no_exponent_and_no_culture_dependent_separator()
    {
        var mesh = new MeshData(
            [new Vector3(1e-7f, -0f, 12345.678f), new Vector3(1.25f, 0.5f, 0), new Vector3(0, 1.5f, 0.001f), new Vector3(0, 0, 2)],
            [0, 1, 2, 0, 2, 3, 0, 3, 1, 1, 3, 2]);
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo("de-DE");
        string text;
        try { text = ReadText(Write(Obj(mesh: mesh)), "3D/3dmodel.model"); }
        finally { CultureInfo.CurrentCulture = previous; }

        Assert.DoesNotMatch("[xyz]=\"[^\"]*[eE][^\"]*\"", text); // no 1E-07 style numbers in coordinates
        Assert.Contains("x=\"1.25\"", text);
        Assert.Contains("z=\"12345.678\"", text);
        Assert.DoesNotContain("-0\"", text);
        Assert.DoesNotContain("1,25", text);
    }

    // ---- names and colors ------------------------------------------------------------------------

    [Fact]
    public void Names_survive_xml_special_characters()
    {
        var model = ReadModel(Write(Obj("A & B <1> \"quoted\" 'x'"), Obj("Würfel äöü ß 日本")));

        var names = Objects(model).Select(o => o.Attribute("name")!.Value).ToList();
        Assert.Equal(["A & B <1> \"quoted\" 'x'", "Würfel äöü ß 日本"], names);
    }

    [Fact]
    public void Control_characters_are_removed_and_empty_names_are_replaced()
    {
        var model = ReadModel(Write(Obj("Bad\u0000Name\u0007\n"), Obj("   "), Obj("")));

        var names = Objects(model).Select(o => o.Attribute("name")!.Value).ToList();
        Assert.Equal("BadName", names[0]);
        Assert.Equal("Object 2", names[1]);
        Assert.Equal("Object 3", names[2]);
    }

    [Fact]
    public void A_very_long_name_is_shortened()
    {
        var model = ReadModel(Write(Obj(new string('x', 5000))));

        Assert.Equal(200, Objects(model).Single().Attribute("name")!.Value.Length);
    }

    [Fact]
    public void Colors_are_written_as_base_materials_and_referenced_by_the_objects()
    {
        var model = ReadModel(Write(Obj("Red", color: 0xFFFF0000), Obj("Grey", color: 0xFFB0B8C4), Obj("Red2", color: 0xFFFF0000)));
        var resources = model.Root!.Element(Core + "resources")!;
        var materials = resources.Element(Core + "basematerials")!;
        var bases = materials.Elements(Core + "base").ToList();

        Assert.Equal(["#FF0000FF", "#B0B8C4FF"], bases.Select(b => b.Attribute("displaycolor")!.Value).ToArray()); // equal colors share one entry
        var objects = Objects(model).ToList();
        Assert.All(objects, o => Assert.Equal(materials.Attribute("id")!.Value, o.Attribute("pid")!.Value));
        Assert.Equal(["0", "1", "0"], objects.Select(o => o.Attribute("pindex")!.Value).ToArray());
        Assert.NotEqual(materials.Attribute("id")!.Value, objects[0].Attribute("id")!.Value); // resource ids are unique
    }

    [Fact]
    public void The_alpha_channel_becomes_the_last_hex_pair()
    {
        var model = ReadModel(Write(Obj(color: 0x80102030)));

        Assert.Equal("#10203080", model.Descendants(Core + "base").Single().Attribute("displaycolor")!.Value);
    }

    // ---- input handling --------------------------------------------------------------------------

    [Fact]
    public void Nothing_to_export_is_refused()
    {
        Assert.Throws<ArgumentException>(() => Write());
        Assert.Throws<ArgumentException>(() => Write(Obj(mesh: MeshData.Empty)));
    }

    [Fact]
    public void Empty_meshes_are_skipped_when_there_are_others()
    {
        var model = ReadModel(Write(Obj("Nothing", MeshData.Empty), Obj("Box")));

        Assert.Equal("Box", Objects(model).Single().Attribute("name")!.Value);
    }

    [Fact]
    public void The_same_input_gives_the_same_bytes()
    {
        Assert.Equal(Write(Obj("A"), Obj("B", color: 0xFF00FF00)), Write(Obj("A"), Obj("B", color: 0xFF00FF00)));
    }

    [Fact]
    public void The_xml_is_plain_without_doctype_or_entities()
    {
        var text = ReadText(Write(Obj("<!DOCTYPE x [<!ENTITY y SYSTEM \"file:///c:/x\">]>")), "3D/3dmodel.model");

        Assert.DoesNotContain("<!DOCTYPE", text);
        Assert.DoesNotContain("<!ENTITY", text);
        Assert.Contains("&lt;!DOCTYPE", text); // only as escaped text inside the name
    }

    // ---- files -----------------------------------------------------------------------------------

    [Fact]
    public void Saving_writes_the_file_without_leaving_a_temp_file()
    {
        var path = Path.Combine(_folder, "out.3mf");
        Directory.CreateDirectory(_folder);

        ThreeMfWriter.SaveAtomic(path, [Obj()]);

        Assert.True(File.Exists(path));
        Assert.False(File.Exists(path + ".tmp"));
        Assert.Equal(6000, VolumeOf(Objects(ReadModel(File.ReadAllBytes(path))).Single()), 3);
    }

    [Fact]
    public void A_failed_save_keeps_the_existing_file()
    {
        var path = Path.Combine(_folder, "keep.3mf");
        Directory.CreateDirectory(_folder);
        File.WriteAllText(path, "old content");

        Assert.Throws<ArgumentException>(() => ThreeMfWriter.SaveAtomic(path, []));

        Assert.Equal("old content", File.ReadAllText(path));
        Assert.False(File.Exists(path + ".tmp"));
    }

    [Fact]
    public void Saving_into_a_missing_folder_is_an_io_error()
    {
        Assert.ThrowsAny<IOException>(() => ThreeMfWriter.SaveAtomic(Path.Combine(_folder, "nope", "x.3mf"), [Obj()]));
    }

    // ---- speed -----------------------------------------------------------------------------------

    [Fact]
    public void A_model_with_a_million_triangles_is_written_in_reasonable_time()
    {
        const int n = 710; // (n - 1)^2 * 2 = 1,006,... triangles
        var vertices = new Vector3[n * n];
        for (var y = 0; y < n; y++)
        {
            for (var x = 0; x < n; x++) vertices[y * n + x] = new Vector3(x * 0.1f, y * 0.1f, MathF.Sin(x * 0.05f) * MathF.Cos(y * 0.05f));
        }

        var indices = new List<int>((n - 1) * (n - 1) * 6);
        for (var y = 0; y < n - 1; y++)
        {
            for (var x = 0; x < n - 1; x++)
            {
                int a = y * n + x, b = a + 1, c = a + n, d = c + 1;
                indices.AddRange([a, b, d, a, d, c]);
            }
        }

        var big = new MeshData(vertices, [.. indices]);
        Assert.True(big.TriangleCount > 1_000_000);
        var best = TimeSpan.MaxValue;
        long size = 0;
        for (var run = 0; run < 3; run++)
        {
            using var stream = new MemoryStream();
            var watch = Stopwatch.StartNew();
            ThreeMfWriter.Write(stream, [Obj("Big", big)]);
            watch.Stop();
            if (watch.Elapsed < best) best = watch.Elapsed;
            size = stream.Length;
        }

        Assert.True(best < TimeSpan.FromSeconds(8), $"took {best.TotalSeconds:0.0} s");
        Assert.True(size > 1_000_000);
    }
}
