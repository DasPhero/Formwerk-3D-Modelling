using System.Numerics;
using Formwerk.Features.Shapes;
using Formwerk.Geometry;
using Formwerk.IO;

namespace Formwerk.Tests.IO;

/// <summary>3MF files through the common import entry point (F-IMP-01).</summary>
public sealed class MeshImportThreeMfTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"formwerk-import3mf-{Guid.NewGuid():N}");

    public MeshImportThreeMfTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        try { Directory.Delete(_folder, recursive: true); } catch (IOException) { }
    }

    private static MeshData Box(double w, double d, double h) =>
        new BoxGenerator().Generate(new Dictionary<string, double> { ["width"] = w, ["depth"] = d, ["height"] = h });

    private string Write(string name, params ThreeMfObject[] objects)
    {
        var path = Path.Combine(_folder, name);
        ThreeMfWriter.SaveAtomic(path, objects);
        return path;
    }

    [Fact]
    public void A_3mf_file_gives_one_mesh_per_object_with_name_color_and_known_unit()
    {
        var path = Write("parts.3mf", new ThreeMfObject("Base", Box(10, 10, 10), 0xFF112233), new ThreeMfObject("Lid", Box(5, 5, 5), 0xFF445566));

        var all = MeshImport.LoadAll(path);

        Assert.Equal(["Base", "Lid"], all.Select(m => m.Name).ToArray());
        Assert.Equal([0xFF112233u, 0xFF445566u], all.Select(m => m.ColorArgb!.Value).ToArray());
        Assert.All(all, m => Assert.True(m.UnitKnown));
    }

    [Fact]
    public void Stl_and_obj_files_still_need_the_unit_question_and_have_no_color()
    {
        var path = Path.Combine(_folder, "box.stl");
        using (var stream = File.Create(path)) StlWriter.WriteBinary(stream, [Box(10, 10, 10)]);

        var imported = Assert.Single(MeshImport.LoadAll(path));

        Assert.False(imported.UnitKnown);
        Assert.Null(imported.ColorArgb);
        Assert.Equal("box", imported.Name);
    }

    [Fact]
    public void Objects_without_a_name_are_named_after_the_file()
    {
        var path = Path.Combine(_folder, "my part.3mf");
        // the writer names empty names "Object n"; build a file whose object has no name at all
        File.WriteAllBytes(path, NamelessPackage());

        var imported = Assert.Single(MeshImport.LoadAll(path));

        Assert.Equal("my part", imported.Name);
    }

    [Fact]
    public void Load_merges_several_objects_into_one_mesh()
    {
        var path = Write("two.3mf", new ThreeMfObject("A", Box(10, 10, 10), 0xFF000000), new ThreeMfObject("B", Box(10, 10, 10), 0xFF000000));

        var merged = MeshImport.Load(path);

        Assert.Equal("two", merged.Name);
        Assert.Equal(24, merged.Mesh.TriangleCount);
        Assert.Equal(2000, merged.Mesh.ComputeVolume(), 3);
        Assert.True(merged.UnitKnown);
    }

    [Fact]
    public void A_damaged_3mf_file_gives_a_message_and_not_an_exception_of_another_kind()
    {
        var path = Path.Combine(_folder, "broken.3mf");
        File.WriteAllText(path, "not a package");

        var error = Assert.Throws<MeshImportException>(() => MeshImport.LoadAll(path));

        Assert.Contains("3MF", error.Message);
    }

    [Fact]
    public void Three_mf_is_a_supported_extension_in_the_dialog_filter()
    {
        Assert.True(MeshImport.IsSupported("x.3MF"));
        Assert.Contains("*.3mf", MeshImport.DialogFilter);
    }

    private static byte[] NamelessPackage()
    {
        const string model = "<model unit=\"millimeter\" xmlns=\"http://schemas.microsoft.com/3dmanufacturing/core/2015/02\"><resources><object id=\"1\" type=\"model\"><mesh><vertices>"
            + "<vertex x=\"0\" y=\"0\" z=\"0\"/><vertex x=\"10\" y=\"0\" z=\"0\"/><vertex x=\"0\" y=\"10\" z=\"0\"/><vertex x=\"0\" y=\"0\" z=\"10\"/></vertices>"
            + "<triangles><triangle v1=\"0\" v2=\"2\" v3=\"1\"/><triangle v1=\"0\" v2=\"1\" v3=\"3\"/><triangle v1=\"1\" v2=\"2\" v3=\"3\"/><triangle v1=\"2\" v2=\"0\" v3=\"3\"/></triangles>"
            + "</mesh></object></resources><build><item objectid=\"1\"/></build></model>";
        using var stream = new MemoryStream();
        using (var zip = new System.IO.Compression.ZipArchive(stream, System.IO.Compression.ZipArchiveMode.Create, leaveOpen: true))
        {
            using var writer = new StreamWriter(zip.CreateEntry("3D/3dmodel.model").Open());
            writer.Write(model);
        }

        return stream.ToArray();
    }
}
