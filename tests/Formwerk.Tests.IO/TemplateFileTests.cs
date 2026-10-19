using System.IO.Compression;
using System.Numerics;
using System.Text;
using Formwerk.Core.Commands;
using Formwerk.Core.Model;
using Formwerk.Geometry;
using Formwerk.IO;

namespace Formwerk.Tests.IO;

/// <summary>The template file (F-TPL-01..03): a zip with the description, a preview and the whole node structure.</summary>
public class TemplateFileTests
{
    private static readonly TemplateInfo Info = new("Hinge plate", "A plate with two holes", "Mechanics", ["plate", "hinge"], new Vector3(40, 20, 5), new DateTime(2026, 10, 19, 12, 0, 0, DateTimeKind.Utc));

    private static FormwerkDocument Structure()
    {
        var doc = new FormwerkDocument();
        var history = new UndoHistory();
        doc.Variables["Wall"] = "2";

        var plate = new PrimitiveNode("box", "Plate") { Color = 0xFF334455 };
        plate.Parameters["width"] = "40";
        plate.Parameters["depth"] = "Wall*10";
        plate.Parameters["height"] = "5";
        var hole = new PrimitiveNode("cylinder", "Hole") { Role = NodeRole.Hole };
        hole.Parameters["diameter"] = "4.5";
        hole.Transform = Transform.FromPosition(new Vector3(10, 0, -1));
        history.Execute(new AddNodeCommand(doc, doc.Root, plate));
        history.Execute(new AddNodeCommand(doc, doc.Root, hole));
        history.Execute(new WrapNodesCommand(doc, new GroupNode("Group"), [plate, hole]));

        var a = new PrimitiveNode("sphere", "A");
        var b = new PrimitiveNode("sphere", "B");
        history.Execute(new AddNodeCommand(doc, doc.Root, a));
        history.Execute(new AddNodeCommand(doc, doc.Root, b));
        history.Execute(new WrapNodesCommand(doc, new BooleanNode("Cut", BooleanOperation.Difference), [a, b]));
        return doc;
    }

    private static byte[] Write(FormwerkDocument doc, TemplateInfo? info = null, byte[]? preview = null, MeshStore? meshes = null)
    {
        using var stream = new MemoryStream();
        TemplateFile.Write(stream, doc, meshes, info ?? Info, preview);
        return stream.ToArray();
    }

    private static string Describe(Node node) =>
        $"{node.GetType().Name}:{node.Name}:{node.Role}:{string.Join(",", (node as PrimitiveNode)?.Parameters.OrderBy(p => p.Key).Select(p => p.Key + "=" + p.Value) ?? [])}"
        + "(" + string.Join(";", node.Children.Select(Describe)) + ")";

    // ---- round trip --------------------------------------------------------------------------------

    [Fact]
    public void The_description_survives()
    {
        var bytes = Write(Structure(), preview: [1, 2, 3]);

        var (info, preview) = TemplateFile.ReadInfo(new MemoryStream(bytes));

        Assert.Equal(Info.Name, info.Name);
        Assert.Equal(Info.Description, info.Description);
        Assert.Equal(Info.Category, info.Category);
        Assert.Equal(Info.Tags, info.Tags);
        Assert.Equal(Info.Size, info.Size);
        Assert.Equal(Info.CreatedUtc, info.CreatedUtc);
        Assert.Equal([1, 2, 3], preview);
    }

    [Fact]
    public void The_whole_structure_comes_back_editable_parameters_holes_groups_and_booleans()
    {
        var doc = Structure();

        var back = TemplateFile.ReadDocument(new MemoryStream(Write(doc)));

        Assert.Equal(Describe(doc.Root), Describe(back.Root));
        Assert.Equal("2", back.Variables["Wall"]);
        Assert.Equal(NodeRole.Hole, back.Root.Descendants().OfType<PrimitiveNode>().Single(n => n.Name == "Hole").Role);
        Assert.IsType<BooleanNode>(back.Root.Children[1]);
    }

    [Fact]
    public void Transforms_and_colors_are_kept()
    {
        var back = TemplateFile.ReadDocument(new MemoryStream(Write(Structure())));

        var hole = back.Root.Descendants().OfType<PrimitiveNode>().Single(n => n.Name == "Hole");
        Assert.Equal(new Vector3(10, 0, -1), hole.Transform.Position);
        Assert.Equal(0xFF334455u, back.Root.Descendants().OfType<PrimitiveNode>().Single(n => n.Name == "Plate").Color);
    }

    [Fact]
    public void Imported_meshes_travel_with_the_template()
    {
        var doc = new FormwerkDocument();
        var id = Guid.NewGuid();
        new AddNodeCommand(doc, doc.Root, new MeshNode("Imported", id)).Do();
        var store = new MeshStore();
        store.Add(id, new MeshData([Vector3.Zero, Vector3.UnitX, Vector3.UnitY], [0, 1, 2]));

        var target = new MeshStore();
        var back = TemplateFile.ReadDocument(new MemoryStream(Write(doc, meshes: store)), target);

        Assert.Equal(id, ((MeshNode)back.Root.Children.Single()).MeshId);
        Assert.Equal(1, target.Get(id)!.TriangleCount);
    }

    [Fact]
    public void A_template_may_have_no_preview()
    {
        var (_, preview) = TemplateFile.ReadInfo(new MemoryStream(Write(Structure())));

        Assert.Null(preview);
    }

    [Fact]
    public void Several_roots_stay_several_roots()
    {
        var doc = Structure();

        var back = TemplateFile.ReadDocument(new MemoryStream(Write(doc)));

        Assert.Equal(doc.Root.Children.Count, back.Root.Children.Count);
    }

    // ---- damaged and hostile files -------------------------------------------------------------------

    [Theory]
    [InlineData("")]
    [InlineData("not a zip at all")]
    public void A_file_that_is_not_a_template_is_refused(string content)
    {
        Assert.Throws<TemplateFormatException>(() => TemplateFile.ReadInfo(new MemoryStream(Encoding.UTF8.GetBytes(content))));
    }

    private static byte[] Zip(params (string Name, byte[] Content)[] entries)
    {
        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, content) in entries)
            {
                using var entry = zip.CreateEntry(name).Open();
                entry.Write(content);
            }
        }

        return stream.ToArray();
    }

    private static byte[] Json(string text) => Encoding.UTF8.GetBytes(text);

    private static byte[] Project()
    {
        using var stream = new MemoryStream();
        ProjectFile.Write(stream, Structure());
        return stream.ToArray();
    }

    private const string GoodInfo = "{ \"schema\": 1, \"name\": \"X\", \"size\": [1,1,1] }";

    [Fact]
    public void A_zip_without_the_description_is_refused()
    {
        Assert.Throws<TemplateFormatException>(() => TemplateFile.ReadInfo(new MemoryStream(Zip(("project.fwp", Project())))));
    }

    [Fact]
    public void A_zip_without_the_project_is_refused_when_the_structure_is_read()
    {
        var bytes = Zip(("template.json", Json(GoodInfo)));

        Assert.Throws<TemplateFormatException>(() => TemplateFile.ReadDocument(new MemoryStream(bytes)));
    }

    [Theory]
    [InlineData("{ \"schema\": 1, \"name\": \"\", \"size\": [1,1,1] }")]
    [InlineData("{ \"schema\": 1, \"name\": 5, \"size\": [1,1,1] }")]
    [InlineData("{ \"schema\": 1, \"size\": [1,1,1] }")]
    [InlineData("{ \"schema\": 1, \"name\": \"X\", \"size\": [1,1] }")]
    [InlineData("{ \"schema\": 1, \"name\": \"X\", \"size\": [1,\"a\",1] }")]
    [InlineData("{ \"schema\": 1, \"name\": \"X\", \"size\": [1,1,1], \"tags\": \"single\" }")]
    [InlineData("{ \"schema\": 99, \"name\": \"X\", \"size\": [1,1,1] }")]
    [InlineData("[1,2,3]")]
    [InlineData("not json")]
    public void A_damaged_description_is_refused(string json)
    {
        var bytes = Zip(("template.json", Json(json)), ("project.fwp", Project()));

        Assert.Throws<TemplateFormatException>(() => TemplateFile.ReadInfo(new MemoryStream(bytes)));
    }

    [Fact]
    public void A_huge_name_or_too_many_tags_are_refused()
    {
        var longName = new string('x', TemplateLimits.MaxNameLength + 1);
        var tags = string.Join(",", Enumerable.Range(0, TemplateLimits.MaxTags + 1).Select(i => "\"t" + i + "\""));

        Assert.Throws<TemplateFormatException>(() => TemplateFile.ReadInfo(new MemoryStream(
            Zip(("template.json", Json("{ \"schema\": 1, \"name\": \"" + longName + "\", \"size\": [1,1,1] }")), ("project.fwp", Project())))));
        Assert.Throws<TemplateFormatException>(() => TemplateFile.ReadInfo(new MemoryStream(
            Zip(("template.json", Json("{ \"schema\": 1, \"name\": \"X\", \"size\": [1,1,1], \"tags\": [" + tags + "] }")), ("project.fwp", Project())))));
    }

    [Fact]
    public void A_preview_that_is_far_too_large_is_refused()
    {
        var bytes = Zip(("template.json", Json(GoodInfo)), ("preview.png", new byte[TemplateLimits.MaxPreviewBytes + 1]), ("project.fwp", Project()));

        Assert.Throws<TemplateFormatException>(() => TemplateFile.ReadInfo(new MemoryStream(bytes)));
    }

    [Fact]
    public void A_zip_with_too_many_entries_is_refused()
    {
        var entries = Enumerable.Range(0, TemplateLimits.MaxEntries + 1).Select(i => ("extra" + i + ".txt", new byte[] { 1 })).ToList();
        entries.Add(("template.json", Json(GoodInfo)));

        Assert.Throws<TemplateFormatException>(() => TemplateFile.ReadInfo(new MemoryStream(Zip([.. entries]))));
    }

    [Fact]
    public void A_damaged_project_inside_gives_a_template_error_not_a_crash()
    {
        var bytes = Zip(("template.json", Json(GoodInfo)), ("project.fwp", [1, 2, 3, 4]));

        Assert.Throws<TemplateFormatException>(() => TemplateFile.ReadDocument(new MemoryStream(bytes)));
    }

    [Fact]
    public void Writing_refuses_an_invalid_description()
    {
        Assert.Throws<ArgumentException>(() => Write(Structure(), Info with { Name = "  " }));
        Assert.Throws<ArgumentException>(() => Write(Structure(), Info with { Size = new Vector3(float.NaN, 1, 1) }));
    }
}
