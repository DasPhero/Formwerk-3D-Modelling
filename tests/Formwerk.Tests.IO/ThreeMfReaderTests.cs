using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using System.Numerics;
using System.Text;
using Formwerk.Features.Shapes;
using Formwerk.Geometry;
using Formwerk.IO;

namespace Formwerk.Tests.IO;

/// <summary>3MF import (F-IMP-01) and the protection against hostile files (N-SEC-01/02/03).</summary>
public sealed class ThreeMfReaderTests : IDisposable
{
    private const string CoreNs = "http://schemas.microsoft.com/3dmanufacturing/core/2015/02";
    private const string ProductionNs = "http://schemas.microsoft.com/3dmanufacturing/production/2015/06";
    private static readonly byte[] XmlAlphabet = "<>\"/=& 0123456789-.#"u8.ToArray();
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"formwerk-3mf-read-{Guid.NewGuid():N}");

    public ThreeMfReaderTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        try { Directory.Delete(_folder, recursive: true); } catch (IOException) { }
    }

    // ---- helpers ---------------------------------------------------------------------------------

    private static MeshData Box(double w = 10, double d = 20, double h = 30) =>
        new BoxGenerator().Generate(new Dictionary<string, double> { ["width"] = w, ["depth"] = d, ["height"] = h });

    private static string Inv(float v) => v.ToString("R", CultureInfo.InvariantCulture);

    /// <summary>The mesh as the inner XML of a 3MF mesh element.</summary>
    private static string MeshXml(MeshData mesh)
    {
        var sb = new StringBuilder("<mesh><vertices>");
        foreach (var v in mesh.Vertices) sb.Append($"<vertex x=\"{Inv(v.X)}\" y=\"{Inv(v.Y)}\" z=\"{Inv(v.Z)}\"/>");
        sb.Append("</vertices><triangles>");
        var i = mesh.Indices;
        for (var t = 0; t + 2 < i.Length; t += 3) sb.Append($"<triangle v1=\"{i[t]}\" v2=\"{i[t + 1]}\" v3=\"{i[t + 2]}\"/>");
        return sb.Append("</triangles></mesh>").ToString();
    }

    private static string Model(string resources, string build, string unit = "millimeter", string extraNamespaces = "") =>
        $"<?xml version=\"1.0\" encoding=\"UTF-8\"?><model unit=\"{unit}\" xmlns=\"{CoreNs}\" {extraNamespaces}><resources>{resources}</resources><build>{build}</build></model>";

    private static string Rels(string target) =>
        $"<?xml version=\"1.0\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Target=\"{target}\" Id=\"rel0\" Type=\"http://schemas.microsoft.com/3dmanufacturing/2013/01/3dmodel\"/></Relationships>";

    private static byte[] Zip(params (string Name, string Text)[] entries)
    {
        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, text) in entries)
            {
                using var writer = new StreamWriter(zip.CreateEntry(name).Open(), new UTF8Encoding(false));
                writer.Write(text);
            }
        }

        return stream.ToArray();
    }

    private static byte[] Package(string modelXml) => Zip(("_rels/.rels", Rels("/3D/3dmodel.model")), ("3D/3dmodel.model", modelXml));

    private static IReadOnlyList<ThreeMfItem> Read(byte[] bytes, ThreeMfLimits? limits = null) => ThreeMfReader.Read(new MemoryStream(bytes), limits);

    private static IReadOnlyList<ThreeMfItem> ReadModel(string modelXml, ThreeMfLimits? limits = null) => Read(Package(modelXml), limits);

    private static MeshImportException Fails(byte[] bytes, ThreeMfLimits? limits = null) =>
        Assert.Throws<MeshImportException>(() => Read(bytes, limits));

    private static string Object(int id, MeshData mesh, string attributes = "") => $"<object id=\"{id}\" type=\"model\" {attributes}>{MeshXml(mesh)}</object>";

    private static string Item(int id, string transform = "") =>
        transform.Length == 0 ? $"<item objectid=\"{id}\"/>" : $"<item objectid=\"{id}\" transform=\"{transform}\"/>";

    // ---- roundtrip with the writer ---------------------------------------------------------------

    [Fact]
    public void What_the_writer_wrote_comes_back()
    {
        var moved = Box(5, 6, 7).Transformed(Matrix4x4.CreateTranslation(100, -50, 3));
        using var stream = new MemoryStream();
        ThreeMfWriter.Write(stream, [new ThreeMfObject("Bracket", Box(), 0xFF112233), new ThreeMfObject("Pin & <Cap>", moved, 0xFFAA5500)]);

        var items = Read(stream.ToArray());

        Assert.Equal(["Bracket", "Pin & <Cap>"], items.Select(i => i.Name).ToArray());
        Assert.Equal([0xFF112233u, 0xFFAA5500u], items.Select(i => i.ColorArgb!.Value).ToArray());
        Assert.Equal(6000, items[0].Mesh.ComputeVolume(), 3);
        Assert.Equal(210, items[1].Mesh.ComputeVolume(), 3);
        Assert.True(items[0].Mesh.IsWatertight());
        Assert.Equal(moved.ComputeBounds().Min.X, items[1].Mesh.ComputeBounds().Min.X, 4); // world positions are kept
        Assert.Equal(moved.ComputeBounds().Max.Z, items[1].Mesh.ComputeBounds().Max.Z, 4);
    }

    [Fact]
    public void A_sphere_survives_the_roundtrip_with_the_same_volume_and_triangle_count()
    {
        var sphere = new SphereGenerator().Generate(new Dictionary<string, double> { ["diameter"] = 30 });
        using var stream = new MemoryStream();
        ThreeMfWriter.Write(stream, [new ThreeMfObject("Ball", sphere, 0xFFB0B8C4)]);

        var back = Read(stream.ToArray()).Single().Mesh;

        Assert.Equal(sphere.TriangleCount, back.TriangleCount);
        Assert.Equal(sphere.ComputeVolume(), back.ComputeVolume(), 2);
        Assert.True(back.IsWatertight());
    }

    // ---- units, transforms -----------------------------------------------------------------------

    [Theory]
    [InlineData("millimeter", 1.0)]
    [InlineData("micron", 0.001)]
    [InlineData("centimeter", 10.0)]
    [InlineData("inch", 25.4)]
    [InlineData("foot", 304.8)]
    [InlineData("meter", 1000.0)]
    public void The_unit_of_the_file_is_converted_to_millimeters(string unit, double factor)
    {
        var item = ReadModel(Model(Object(1, Box(10, 10, 10)), Item(1), unit)).Single();

        Assert.Equal(10 * factor, item.Mesh.ComputeBounds().Size.X, 2);
    }

    [Fact]
    public void Without_a_unit_attribute_millimeters_are_assumed_and_an_unknown_unit_is_refused()
    {
        var noUnit = $"<model xmlns=\"{CoreNs}\"><resources>{Object(1, Box(10, 10, 10))}</resources><build>{Item(1)}</build></model>";
        Assert.Equal(10, ReadModel(noUnit).Single().Mesh.ComputeBounds().Size.X, 3);

        var error = Assert.Throws<MeshImportException>(() => ReadModel(Model(Object(1, Box()), Item(1), "parsec")));
        Assert.Contains("parsec", error.Message);
    }

    [Fact]
    public void A_build_item_transform_moves_and_scales_the_object()
    {
        // row vector convention: nine matrix numbers, then the translation
        var moved = ReadModel(Model(Object(1, Box(10, 10, 10)), Item(1, "1 0 0 0 1 0 0 0 1 100 200 300"))).Single().Mesh.ComputeBounds();
        Assert.Equal(100, moved.Min.X + 5, 3);
        Assert.Equal(200, moved.Min.Y + 5, 3);
        Assert.Equal(300, moved.Min.Z, 3); // the box stands on Z = 0, so its lowest point moves to 300

        var scaled = ReadModel(Model(Object(1, Box(10, 10, 10)), Item(1, "2 0 0 0 3 0 0 0 4 0 0 0"))).Single().Mesh.ComputeBounds();
        Assert.Equal(20, scaled.Size.X, 3);
        Assert.Equal(30, scaled.Size.Y, 3);
        Assert.Equal(40, scaled.Size.Z, 3);
    }

    [Fact]
    public void A_rotation_turns_the_object()
    {
        // 90 degrees about Z: x -> y, y -> -x   (row vectors: [x y z] * M)
        var item = ReadModel(Model(Object(1, Box(10, 20, 30)), Item(1, "0 1 0 -1 0 0 0 0 1 0 0 0"))).Single().Mesh.ComputeBounds();

        Assert.Equal(20, item.Size.X, 3);
        Assert.Equal(10, item.Size.Y, 3);
        Assert.Equal(30, item.Size.Z, 3);
    }

    [Fact]
    public void A_mirroring_transform_keeps_the_volume_positive_and_the_mesh_watertight()
    {
        var mesh = ReadModel(Model(Object(1, Box(10, 20, 30)), Item(1, "-1 0 0 0 1 0 0 0 1 0 0 0"))).Single().Mesh;

        Assert.Equal(6000, mesh.ComputeVolume(), 3); // negative would mean inside-out triangles
        Assert.True(mesh.IsWatertight());
        Assert.Equal(-5, mesh.ComputeBounds().Min.X, 3);
    }

    // ---- structure -------------------------------------------------------------------------------

    [Fact]
    public void Components_are_merged_into_one_object_with_their_transforms()
    {
        var resources = Object(1, Box(10, 10, 10))
            + "<object id=\"2\" type=\"model\" name=\"Pair\"><components>"
            + "<component objectid=\"1\"/><component objectid=\"1\" transform=\"1 0 0 0 1 0 0 0 1 50 0 0\"/>"
            + "</components></object>";

        var item = ReadModel(Model(resources, Item(2))).Single();

        Assert.Equal("Pair", item.Name);
        Assert.Equal(2000, item.Mesh.ComputeVolume(), 3);
        Assert.Equal(60, item.Mesh.ComputeBounds().Size.X, 3);
    }

    [Fact]
    public void Component_and_item_transforms_combine()
    {
        var resources = Object(1, Box(10, 10, 10))
            + "<object id=\"2\" type=\"model\"><components><component objectid=\"1\" transform=\"1 0 0 0 1 0 0 0 1 10 0 0\"/></components></object>";

        var bounds = ReadModel(Model(resources, Item(2, "2 0 0 0 2 0 0 0 2 0 0 0"))).Single().Mesh.ComputeBounds();

        Assert.Equal(20, bounds.Size.X, 3);          // scaled by 2 after the move by 10: center at 20
        Assert.Equal(20, bounds.Center.X, 3);
    }

    [Fact]
    public void Objects_in_other_model_parts_are_followed_like_slicers_write_them()
    {
        var external = Model(Object(1, Box(10, 10, 10), "name=\"Inner\""), "");
        var main = Model(
            $"<object id=\"5\" type=\"model\" name=\"Outer\"><components><component p:path=\"/3D/Objects/part1.model\" objectid=\"1\" transform=\"1 0 0 0 1 0 0 0 1 7 0 0\"/></components></object>",
            Item(5), extraNamespaces: $"xmlns:p=\"{ProductionNs}\"");
        var bytes = Zip(("_rels/.rels", Rels("/3D/3dmodel.model")), ("3D/3dmodel.model", main), ("3D/Objects/part1.model", external));

        var item = Read(bytes).Single();

        Assert.Equal("Outer", item.Name);
        Assert.Equal(1000, item.Mesh.ComputeVolume(), 3);
        Assert.Equal(7, item.Mesh.ComputeBounds().Center.X, 3);
    }

    [Fact]
    public void A_missing_external_part_is_reported()
    {
        var main = Model("<object id=\"5\" type=\"model\"><components><component p:path=\"/3D/Objects/gone.model\" objectid=\"1\"/></components></object>",
            Item(5), extraNamespaces: $"xmlns:p=\"{ProductionNs}\"");

        var error = Fails(Zip(("_rels/.rels", Rels("/3D/3dmodel.model")), ("3D/3dmodel.model", main)));

        Assert.Contains("gone.model", error.Message);
    }

    [Fact]
    public void Support_and_other_object_types_are_not_imported()
    {
        var resources = $"<object id=\"1\" type=\"support\">{MeshXml(Box())}</object>" + Object(2, Box(), "name=\"Real\"");

        var items = ReadModel(Model(resources, Item(1) + Item(2)));

        Assert.Equal(["Real"], items.Select(i => i.Name).ToArray());
        Assert.Contains("nothing", Fails(Package(Model($"<object id=\"1\" type=\"support\">{MeshXml(Box())}</object>", Item(1)))).Message);
    }

    [Fact]
    public void Without_a_build_section_the_top_level_objects_are_used()
    {
        var resources = Object(1, Box(10, 10, 10), "name=\"Used as part\"")
            + "<object id=\"2\" type=\"model\" name=\"Top\"><components><component objectid=\"1\"/></components></object>";

        var items = ReadModel(Model(resources, ""));

        Assert.Equal(["Top"], items.Select(i => i.Name).ToArray());
    }

    [Fact]
    public void An_object_without_a_name_gets_an_empty_name_for_the_caller_to_replace()
    {
        Assert.Equal("", ReadModel(Model(Object(1, Box()), Item(1))).Single().Name);
    }

    [Fact]
    public void The_model_part_is_found_through_the_relationships_and_without_them_at_the_default_place()
    {
        var model = Model(Object(1, Box(), "name=\"Elsewhere\""), Item(1));

        Assert.Equal("Elsewhere", Read(Zip(("_rels/.rels", Rels("/models/main.model")), ("models/main.model", model))).Single().Name);
        Assert.Equal("Elsewhere", Read(Zip(("3D/3dmodel.model", model))).Single().Name);
    }

    // ---- colors ----------------------------------------------------------------------------------

    [Fact]
    public void The_color_comes_from_the_base_material_of_the_object()
    {
        var resources = "<basematerials id=\"1\"><base name=\"a\" displaycolor=\"#FF0000\"/><base name=\"b\" displaycolor=\"#00FF0080\"/></basematerials>"
            + Object(2, Box(), "pid=\"1\" pindex=\"1\"") + Object(3, Box(), "pid=\"1\" pindex=\"0\"") + Object(4, Box());

        var items = ReadModel(Model(resources, Item(2) + Item(3) + Item(4)));

        Assert.Equal(0x8000FF00u, items[0].ColorArgb); // alpha is the last pair in 3MF, the first in ARGB
        Assert.Equal(0xFFFF0000u, items[1].ColorArgb); // no alpha means opaque
        Assert.Null(items[2].ColorArgb);
    }

    [Theory]
    [InlineData("red")]
    [InlineData("#12")]
    [InlineData("#GGGGGG")]
    [InlineData("")]
    public void A_color_that_cannot_be_read_is_ignored(string color)
    {
        var resources = $"<basematerials id=\"1\"><base name=\"a\" displaycolor=\"{color}\"/></basematerials>" + Object(2, Box(), "pid=\"1\" pindex=\"0\"");

        Assert.Null(ReadModel(Model(resources, Item(2))).Single().ColorArgb);
    }

    // ---- geometry cleanup ------------------------------------------------------------------------

    [Fact]
    public void Vertices_at_the_same_position_are_welded()
    {
        // every triangle with its own three vertices, as many exporters write it
        var box = Box(10, 10, 10);
        var vertices = new List<Vector3>();
        var indices = new List<int>();
        foreach (var i in box.Indices)
        {
            indices.Add(vertices.Count);
            vertices.Add(box.Vertices[i]);
        }

        var item = ReadModel(Model(Object(1, new MeshData([.. vertices], [.. indices])), Item(1))).Single();

        Assert.Equal(8, item.Mesh.VertexCount);
        Assert.Equal(12, item.Mesh.TriangleCount);
        Assert.True(item.Mesh.IsWatertight());
    }

    [Fact]
    public void Degenerate_triangles_are_dropped()
    {
        var xml = "<object id=\"1\" type=\"model\"><mesh><vertices>"
            + "<vertex x=\"0\" y=\"0\" z=\"0\"/><vertex x=\"1\" y=\"0\" z=\"0\"/><vertex x=\"0\" y=\"1\" z=\"0\"/><vertex x=\"0\" y=\"0\" z=\"1\"/>"
            + "</vertices><triangles><triangle v1=\"0\" v2=\"1\" v3=\"2\"/><triangle v1=\"0\" v2=\"0\" v3=\"1\"/></triangles></mesh></object>";

        Assert.Equal(1, ReadModel(Model(xml, Item(1))).Single().Mesh.TriangleCount);
    }

    // ---- broken files ----------------------------------------------------------------------------

    [Theory]
    [InlineData("")]
    [InlineData("this is not a zip file at all")]
    [InlineData("PK\u0003\u0004 truncated")]
    public void Something_that_is_not_a_3mf_package_gives_a_message(string content)
    {
        var error = Fails(Encoding.UTF8.GetBytes(content));

        Assert.Contains("3MF", error.Message);
    }

    [Fact]
    public void A_package_without_a_model_is_refused()
    {
        Assert.Contains("3MF", Fails(Zip(("readme.txt", "hello"))).Message);
    }

    [Fact]
    public void A_wrong_root_element_is_refused()
    {
        Assert.Contains("3MF", Fails(Package("<html><body>hi</body></html>")).Message);
        Assert.Contains("3MF", Fails(Package("this is not xml <<<")).Message);
    }

    [Theory]
    [InlineData("<triangle v1=\"0\" v2=\"1\" v3=\"99\"/>")]
    [InlineData("<triangle v1=\"-1\" v2=\"1\" v3=\"2\"/>")]
    [InlineData("<triangle v1=\"a\" v2=\"1\" v3=\"2\"/>")]
    [InlineData("<triangle v1=\"0\" v2=\"1\"/>")]
    public void Triangles_with_bad_indices_are_refused(string triangle)
    {
        var xml = "<object id=\"1\" type=\"model\"><mesh><vertices>"
            + "<vertex x=\"0\" y=\"0\" z=\"0\"/><vertex x=\"1\" y=\"0\" z=\"0\"/><vertex x=\"0\" y=\"1\" z=\"0\"/>"
            + $"</vertices><triangles>{triangle}</triangles></mesh></object>";

        Assert.Contains("3MF", Fails(Package(Model(xml, Item(1)))).Message);
    }

    [Theory]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    [InlineData("1e999")]
    [InlineData("abc")]
    [InlineData("")]
    public void Coordinates_that_are_not_numbers_are_refused(string value)
    {
        var xml = "<object id=\"1\" type=\"model\"><mesh><vertices>"
            + $"<vertex x=\"{value}\" y=\"0\" z=\"0\"/><vertex x=\"1\" y=\"0\" z=\"0\"/><vertex x=\"0\" y=\"1\" z=\"0\"/>"
            + "</vertices><triangles><triangle v1=\"0\" v2=\"1\" v3=\"2\"/></triangles></mesh></object>";

        Assert.Contains("3MF", Fails(Package(Model(xml, Item(1)))).Message);
    }

    [Theory]
    [InlineData("1 0 0 0 1 0 0 0 1")]
    [InlineData("a b c d e f g h i j k l")]
    [InlineData("1 0 0 0 1 0 0 0 1 NaN 0 0")]
    public void A_transform_that_is_not_twelve_numbers_is_refused(string transform)
    {
        Assert.Contains("3MF", Fails(Package(Model(Object(1, Box()), Item(1, transform)))).Message);
    }

    [Fact]
    public void A_build_item_that_points_nowhere_is_refused()
    {
        Assert.Contains("3MF", Fails(Package(Model(Object(1, Box()), Item(42)))).Message);
    }

    [Fact]
    public void Reference_loops_are_detected()
    {
        var self = "<object id=\"1\" type=\"model\"><components><component objectid=\"1\"/></components></object>";
        var pingPong = "<object id=\"1\" type=\"model\"><components><component objectid=\"2\"/></components></object>"
            + "<object id=\"2\" type=\"model\"><components><component objectid=\"1\"/></components></object>";

        Assert.Contains("loop", Fails(Package(Model(self, Item(1)))).Message);
        Assert.Contains("loop", Fails(Package(Model(pingPong, Item(1)))).Message);
    }

    [Fact]
    public void Very_deep_nesting_is_refused()
    {
        var sb = new StringBuilder(Object(1, Box(1, 1, 1)));
        for (var id = 2; id <= 60; id++) sb.Append($"<object id=\"{id}\" type=\"model\"><components><component objectid=\"{id - 1}\"/></components></object>");

        Assert.Contains("deep", Fails(Package(Model(sb.ToString(), Item(60)))).Message);
    }

    // ---- hostile files (N-SEC-01/02/03) ----------------------------------------------------------

    [Fact]
    public void An_external_entity_is_never_resolved()
    {
        var secret = Path.Combine(_folder, "secret.txt");
        File.WriteAllText(secret, "TOP-SECRET-CONTENT");
        var xml = $"<?xml version=\"1.0\"?><!DOCTYPE model [<!ENTITY xxe SYSTEM \"{new Uri(secret).AbsoluteUri}\">]>"
            + $"<model unit=\"millimeter\" xmlns=\"{CoreNs}\"><resources><object id=\"1\" type=\"model\" name=\"&xxe;\">{MeshXml(Box())}</object></resources><build>{Item(1)}</build></model>";

        var error = Fails(Package(xml));

        Assert.DoesNotContain("TOP-SECRET-CONTENT", error.Message);
        Assert.Contains("3MF", error.Message);
    }

    [Fact]
    public void Entity_expansion_bombs_are_refused_quickly()
    {
        var laughs = "<?xml version=\"1.0\"?><!DOCTYPE model [<!ENTITY a \"aaaaaaaaaa\"><!ENTITY b \"&a;&a;&a;&a;&a;&a;&a;&a;&a;&a;\">"
            + "<!ENTITY c \"&b;&b;&b;&b;&b;&b;&b;&b;&b;&b;\"><!ENTITY d \"&c;&c;&c;&c;&c;&c;&c;&c;&c;&c;\">]>"
            + $"<model xmlns=\"{CoreNs}\"><resources><object id=\"1\" type=\"model\" name=\"&d;\">{MeshXml(Box())}</object></resources><build>{Item(1)}</build></model>";
        var watch = Stopwatch.StartNew();

        Fails(Package(laughs));

        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(3));
    }

    [Fact]
    public void A_zip_bomb_is_refused_by_its_compression_ratio()
    {
        // 80 MB of zeros compress to about 80 KB: a ratio of roughly 1000
        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            using var model = zip.CreateEntry("3D/3dmodel.model", CompressionLevel.SmallestSize).Open();
            var zeros = new byte[1 << 20];
            for (var i = 0; i < 80; i++) model.Write(zeros);
        }

        var error = Fails(stream.ToArray());

        Assert.Contains("compress", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void An_entry_that_is_too_large_is_refused_before_it_is_read()
    {
        var bytes = Package(Model(Object(1, Box()), Item(1)));

        var error = Fails(bytes, new ThreeMfLimits { MaxModelBytes = 100 });

        Assert.Contains("large", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Too_many_entries_in_the_package_are_refused()
    {
        var entries = Enumerable.Range(0, 20).Select(i => ($"extra/{i}.txt", "x")).Append(("3D/3dmodel.model", Model(Object(1, Box()), Item(1)))).ToArray();

        Assert.Contains("entries", Fails(Zip(entries), new ThreeMfLimits { MaxEntries = 10 }).Message);
        Assert.Single(Read(Zip(entries))); // within the normal limits it is fine
    }

    [Theory]
    [InlineData("../evil.txt")]
    [InlineData("..\\evil.txt")]
    [InlineData("/etc/passwd")]
    [InlineData("C:\\Windows\\evil.txt")]
    [InlineData("3D/../../evil.txt")]
    public void Entries_that_point_outside_the_package_are_refused(string name)
    {
        var bytes = Zip(("3D/3dmodel.model", Model(Object(1, Box()), Item(1))), (name, "payload"));

        var error = Fails(bytes);

        Assert.Contains("unsafe", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(Path.Combine(_folder, "evil.txt")));
    }

    [Theory]
    [InlineData("../../evil.model")]
    [InlineData("/../evil.model")]
    [InlineData("C:\\evil.model")]
    [InlineData("\\\\server\\share\\evil.model")]
    public void A_relationship_that_points_outside_the_package_is_not_followed(string target)
    {
        var bytes = Zip(("_rels/.rels", Rels(target)), ("3D/3dmodel.model", Model(Object(1, Box()), Item(1))));

        // the fallback place is used or the file is refused; it never leaves the package
        try { Assert.Single(Read(bytes)); }
        catch (MeshImportException) { }
    }

    [Fact]
    public void Components_that_multiply_geometry_hit_the_triangle_limit()
    {
        // a small file whose build item expands to 10 x 10 x 12 triangles
        var resources = Object(1, Box())
            + $"<object id=\"2\" type=\"model\"><components>{string.Concat(Enumerable.Repeat("<component objectid=\"1\"/>", 10))}</components></object>"
            + $"<object id=\"3\" type=\"model\"><components>{string.Concat(Enumerable.Repeat("<component objectid=\"2\"/>", 10))}</components></object>";

        var error = Fails(Package(Model(resources, Item(3))), new ThreeMfLimits { MaxTriangles = 500 });

        Assert.Contains("triangles", error.Message);
        Assert.Equal(1200, ReadModel(Model(resources, Item(3))).Single().Mesh.TriangleCount); // within the normal limits it works
    }

    [Fact]
    public void Too_many_vertices_in_one_file_are_refused()
    {
        var error = Fails(Package(Model(Object(1, Box()), Item(1))), new ThreeMfLimits { MaxVertices = 5 });

        Assert.Contains("vertices", error.Message);
    }

    // ---- fuzzing ---------------------------------------------------------------------------------

    [Fact]
    public void Damaged_packages_never_crash_the_reader()
    {
        using var stream = new MemoryStream();
        ThreeMfWriter.Write(stream, [new ThreeMfObject("A", Box(), 0xFF112233), new ThreeMfObject("B", Box(3, 4, 5), 0xFF445566)]);
        var original = stream.ToArray();
        var random = new Random(3141);

        for (var run = 0; run < 400; run++)
        {
            var bytes = (byte[])original.Clone();
            var changes = 1 + random.Next(8);
            for (var c = 0; c < changes; c++) bytes[random.Next(bytes.Length)] = (byte)random.Next(256);
            if (run % 7 == 0) bytes = bytes[..random.Next(bytes.Length)]; // truncated as well

            try { Read(bytes); }
            catch (MeshImportException) { }
        }
    }

    [Fact]
    public void Damaged_xml_inside_a_valid_package_never_crashes_the_reader()
    {
        var model = Encoding.UTF8.GetBytes(Model(
            Object(1, Box(), "name=\"x\"") + "<object id=\"2\" type=\"model\"><components><component objectid=\"1\" transform=\"1 0 0 0 1 0 0 0 1 5 0 0\"/></components></object>",
            Item(2, "1 0 0 0 1 0 0 0 1 0 0 0")));
        var random = new Random(2718);

        for (var run = 0; run < 600; run++)
        {
            var bytes = (byte[])model.Clone();
            var changes = 1 + random.Next(6);
            for (var c = 0; c < changes; c++)
            {
                var at = random.Next(bytes.Length);
                bytes[at] = random.Next(3) == 0 ? (byte)random.Next(256) : XmlAlphabet[random.Next(XmlAlphabet.Length)];
            }

            if (run % 5 == 0) bytes = bytes[..random.Next(bytes.Length)];
            try { Read(Package(Encoding.UTF8.GetString(bytes))); }
            catch (MeshImportException) { }
        }
    }

    // ---- speed -----------------------------------------------------------------------------------

    [Fact]
    public void A_model_with_a_million_triangles_is_read_in_reasonable_time()
    {
        const int n = 710;
        var vertices = new Vector3[n * n];
        for (var y = 0; y < n; y++)
        {
            for (var x = 0; x < n; x++) vertices[y * n + x] = new Vector3(x * 0.1f, y * 0.1f, MathF.Sin(x * 0.05f) * MathF.Cos(y * 0.05f));
        }

        var indices = new List<int>();
        for (var y = 0; y < n - 1; y++)
        {
            for (var x = 0; x < n - 1; x++)
            {
                int a = y * n + x, b = a + 1, c = a + n, d = c + 1;
                indices.AddRange([a, b, d, a, d, c]);
            }
        }

        using var stream = new MemoryStream();
        ThreeMfWriter.Write(stream, [new ThreeMfObject("Big", new MeshData(vertices, [.. indices]), 0xFFB0B8C4)]);
        var bytes = stream.ToArray();

        var best = TimeSpan.MaxValue;
        MeshData? read = null;
        for (var run = 0; run < 3; run++)
        {
            var watch = Stopwatch.StartNew();
            read = Read(bytes).Single().Mesh;
            watch.Stop();
            if (watch.Elapsed < best) best = watch.Elapsed;
        }

        Assert.True(read!.TriangleCount > 1_000_000);
        Assert.True(best < TimeSpan.FromSeconds(10), $"took {best.TotalSeconds:0.0} s");
    }
}
