using System.IO.Compression;
using System.Numerics;
using System.Text;
using Formwerk.Core.Commands;
using Formwerk.Core.Model;
using Formwerk.IO;

namespace Formwerk.Tests.IO;

public class ProjectFileTests
{
    // ---- helpers -------------------------------------------------------------------------------

    private static FormwerkDocument RichDocument()
    {
        var doc = new FormwerkDocument();
        var history = new UndoHistory();
        doc.Settings.BuildVolume = new Vector3(180, 200, 220);
        doc.Settings.NozzleDiameter = 0.6;
        doc.Settings.Tolerance = 0.25;
        doc.Variables["Wandstaerke"] = "2";
        doc.Variables["Doppelt"] = "Wandstaerke*2";

        var box = new PrimitiveNode("box", "Größe ä ö ü") { Color = 0xFF112233 };
        box.Parameters["width"] = "Doppelt*10";
        box.Parameters["depth"] = "20";
        box.Parameters["height"] = "5.5";
        box.Transform = new Transform(new Vector3(1.5f, -2.25f, 3), new EulerAngles(10, 20, 30).ToQuaternion(), new Vector3(1, 2, 3));
        history.Execute(new AddNodeCommand(doc, doc.Root, box));

        var hole = new PrimitiveNode("cylinder", "Hole") { Role = NodeRole.Hole, Visible = false, Locked = true };
        hole.Parameters["diameter"] = "4";
        history.Execute(new AddNodeCommand(doc, doc.Root, hole));

        var group = new GroupNode("Group");
        history.Execute(new WrapNodesCommand(doc, group, [box, hole]));

        var a = new PrimitiveNode("sphere", "A");
        var b = new PrimitiveNode("sphere", "B") { Transform = Transform.FromPosition(new Vector3(5, 0, 0)) };
        history.Execute(new AddNodeCommand(doc, doc.Root, a));
        history.Execute(new AddNodeCommand(doc, doc.Root, b));
        history.Execute(new WrapNodesCommand(doc, new BooleanNode("Diff", BooleanOperation.Difference), [a, b]));

        var m1 = new PrimitiveNode("box", "M1");
        var m2 = new PrimitiveNode("box", "M2");
        history.Execute(new AddNodeCommand(doc, doc.Root, m1));
        history.Execute(new AddNodeCommand(doc, doc.Root, m2));
        history.Execute(new WrapNodesCommand(doc, new MergeNode("Merged"), [m1, m2]));

        history.Execute(new AddNodeCommand(doc, doc.Root, new MeshNode("Imported", Guid.Parse("11111111-2222-3333-4444-555555555555"))));
        return doc;
    }

    private static byte[] Save(FormwerkDocument doc)
    {
        using var stream = new MemoryStream();
        ProjectFile.Write(stream, doc);
        return stream.ToArray();
    }

    private static FormwerkDocument Open(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        return ProjectFile.Read(stream);
    }

    private static byte[] Zip(params (string Name, byte[] Data)[] entries)
    {
        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, data) in entries)
            {
                using var entry = zip.CreateEntry(name).Open();
                entry.Write(data);
            }
        }

        return stream.ToArray();
    }

    private static byte[] ZipJson(string json) => Zip((ProjectFile.DocumentEntryName, Encoding.UTF8.GetBytes(json)));

    private static void AssertSameNode(Node expected, Node actual)
    {
        Assert.Equal(expected.GetType(), actual.GetType());
        Assert.Equal(expected.Id, actual.Id);
        Assert.Equal(expected.Name, actual.Name);
        Assert.Equal(expected.Role, actual.Role);
        Assert.Equal(expected.Color, actual.Color);
        Assert.Equal(expected.Visible, actual.Visible);
        Assert.Equal(expected.Locked, actual.Locked);
        Assert.Equal(expected.Transform, actual.Transform);
        switch (expected)
        {
            case PrimitiveNode p:
                var q = (PrimitiveNode)actual;
                Assert.Equal(p.GeneratorId, q.GeneratorId);
                Assert.Equal(p.Parameters.OrderBy(x => x.Key), q.Parameters.OrderBy(x => x.Key));
                break;
            case BooleanNode bo:
                Assert.Equal(bo.Operation, ((BooleanNode)actual).Operation);
                break;
            case MeshNode me:
                Assert.Equal(me.MeshId, ((MeshNode)actual).MeshId);
                break;
        }

        Assert.Equal(expected.Children.Count, actual.Children.Count);
        for (var i = 0; i < expected.Children.Count; i++)
        {
            Assert.Same(actual, actual.Children[i].Parent);
            AssertSameNode(expected.Children[i], actual.Children[i]);
        }
    }

    // ---- roundtrip -----------------------------------------------------------------------------

    [Fact]
    public void Roundtrip_keeps_every_property_of_every_node_type()
    {
        var original = RichDocument();

        var loaded = Open(Save(original));

        Assert.Equal(original.Root.Children.Count, loaded.Root.Children.Count);
        for (var i = 0; i < original.Root.Children.Count; i++) AssertSameNode(original.Root.Children[i], loaded.Root.Children[i]);
        Assert.Equal(new Vector3(180, 200, 220), loaded.Settings.BuildVolume);
        Assert.Equal(0.6, loaded.Settings.NozzleDiameter);
        Assert.Equal(0.25, loaded.Settings.Tolerance);
        Assert.Equal(["Wandstaerke", "Doppelt"], loaded.Variables.Keys); // order matters
        Assert.Equal("Wandstaerke*2", loaded.Variables["Doppelt"]);
    }

    // Spec section 10: "Projekt speichern, laden, speichern: Dateien identisch".
    [Fact]
    public void Save_load_save_gives_identical_bytes()
    {
        var first = Save(RichDocument());
        var second = Save(Open(first));
        Assert.Equal(first, second);
    }

    [Fact]
    public void Saving_the_same_document_twice_is_deterministic()
    {
        var doc = RichDocument();
        Assert.Equal(Save(doc), Save(doc));
    }

    [Fact]
    public void Loaded_document_has_the_same_geometry_hashes()
    {
        var original = RichDocument();
        var loaded = Open(Save(original));
        var v1 = original.ResolveVariables(out _);
        var v2 = loaded.ResolveVariables(out _);

        for (var i = 0; i < original.Root.Children.Count; i++)
            Assert.Equal(original.Root.Children[i].ComputeHash(v1), loaded.Root.Children[i].ComputeHash(v2));
    }

    [Fact]
    public void Empty_document_roundtrips()
    {
        var loaded = Open(Save(new FormwerkDocument()));
        Assert.Empty(loaded.Root.Children);
        Assert.Empty(loaded.Variables);
    }

    [Fact]
    public void Saved_file_is_a_zip_with_readable_json()
    {
        var bytes = Save(RichDocument());
        using var zip = new ZipArchive(new MemoryStream(bytes));
        var entry = Assert.Single(zip.Entries);
        Assert.Equal("document.json", entry.FullName);
        using var reader = new StreamReader(entry.Open(), Encoding.UTF8);
        var text = reader.ReadToEnd();
        Assert.Contains("\"schemaVersion\": 1", text);
        Assert.Contains("Größe ä ö ü", text); // not escaped to \u00F6
        Assert.DoesNotContain("\r", text);
    }

    // ---- schema version 1, pinned --------------------------------------------------------------

    // If this test breaks, the file format changed: add a migration step and keep this file loading.
    private const string V1Json = """
        {
          "schemaVersion": 1,
          "application": "Formwerk",
          "settings": { "buildVolume": [220, 220, 250], "nozzleDiameter": 0.4, "tolerance": 0.2 },
          "variables": [ { "name": "W", "expression": "5" } ],
          "nodes": [
            {
              "type": "group", "id": "aaaaaaaa-0000-0000-0000-000000000001", "name": "G", "role": "solid",
              "color": "#FFB0B8C4", "visible": true, "locked": false,
              "position": [1, 2, 3], "rotation": [0, 0, 0, 1], "scale": [1, 1, 1],
              "children": [
                {
                  "type": "primitive", "id": "aaaaaaaa-0000-0000-0000-000000000002", "name": "Cyl", "role": "hole",
                  "color": "#FF336699", "visible": true, "locked": false,
                  "position": [0, 0, 0], "rotation": [0, 0, 0, 1], "scale": [1, 1, 1],
                  "generator": "cylinder", "parameters": { "diameter": "W*2", "height": "30" }
                }
              ]
            }
          ]
        }
        """;

    [Fact]
    public void Version_1_file_still_loads()
    {
        var doc = Open(ZipJson(V1Json));

        Assert.Equal(new Vector3(220, 220, 250), doc.Settings.BuildVolume);
        Assert.Equal("5", doc.Variables["W"]);
        var group = Assert.IsType<GroupNode>(Assert.Single(doc.Root.Children));
        Assert.Equal(Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001"), group.Id);
        Assert.Equal(new Vector3(1, 2, 3), group.Transform.Position);
        var cylinder = Assert.IsType<PrimitiveNode>(Assert.Single(group.Children));
        Assert.Equal(NodeRole.Hole, cylinder.Role);
        Assert.Equal(0xFF336699u, cylinder.Color);
        Assert.Equal("W*2", cylinder.Parameters["diameter"]);
    }

    [Fact]
    public void Unknown_extra_properties_are_ignored_for_forward_compatibility()
    {
        var json = V1Json.Replace("\"application\": \"Formwerk\",", "\"application\": \"Formwerk\", \"somethingNew\": {\"a\": 1},");
        Assert.Single(Open(ZipJson(json)).Root.Children);
    }

    // ---- damaged and hostile files -------------------------------------------------------------

    private static ProjectFormatException Fails(byte[] bytes) => Assert.Throws<ProjectFormatException>(() => Open(bytes));

    [Fact]
    public void Newer_file_version_gives_a_clear_message()
    {
        var ex = Fails(ZipJson(V1Json.Replace("\"schemaVersion\": 1", "\"schemaVersion\": 7")));
        Assert.Contains("newer version", ex.Message);
        Assert.Contains("7", ex.Message);
    }

    [Theory]
    [InlineData("\"schemaVersion\": 0")]
    [InlineData("\"schemaVersion\": \"one\"")]
    public void Invalid_version_is_rejected(string replacement)
    {
        Fails(ZipJson(V1Json.Replace("\"schemaVersion\": 1", replacement)));
    }

    [Fact]
    public void Missing_version_is_rejected()
    {
        Fails(ZipJson(V1Json.Replace("\"schemaVersion\": 1,", "")));
    }

    [Fact]
    public void Not_a_zip_is_rejected()
    {
        Assert.Contains("Formwerk project", Fails(Encoding.UTF8.GetBytes("hello world")).Message);
        Fails([]);
    }

    [Fact]
    public void Zip_without_document_json_is_rejected()
    {
        Assert.Contains("document.json", Fails(Zip(("other.txt", [1, 2, 3]))).Message);
    }

    [Theory]
    [InlineData("not json at all")]
    [InlineData("[1,2,3]")]
    [InlineData("{ \"schemaVersion\": 1 }")]
    [InlineData("{ \"schemaVersion\": 1, \"nodes\": {} }")]
    [InlineData("{ \"schemaVersion\": 1, \"nodes\": [ 5 ] }")]
    [InlineData("{ \"schemaVersion\": 1, \"nodes\": [ { \"type\": \"teapot\", \"id\": \"aaaaaaaa-0000-0000-0000-000000000001\" } ] }")]
    [InlineData("{ \"schemaVersion\": 1, \"nodes\": [ { \"type\": \"group\", \"id\": \"nope\" } ] }")]
    [InlineData("{ \"schemaVersion\": 1, \"nodes\": [ { \"type\": \"group\" } ] }")]
    public void Malformed_documents_are_rejected_with_a_message(string json)
    {
        Assert.False(string.IsNullOrWhiteSpace(Fails(ZipJson(json)).Message));
    }

    [Fact]
    public void Duplicate_ids_are_rejected()
    {
        var json = V1Json.Replace("aaaaaaaa-0000-0000-0000-000000000002", "aaaaaaaa-0000-0000-0000-000000000001");
        Assert.Contains("same id", Fails(ZipJson(json)).Message);
    }

    [Theory]
    [InlineData("\"color\": \"#FF336699\"", "\"color\": \"red\"")]
    [InlineData("\"buildVolume\": [220, 220, 250]", "\"buildVolume\": [220, -1, 250]")]
    [InlineData("\"buildVolume\": [220, 220, 250]", "\"buildVolume\": [220, 220]")]
    [InlineData("\"position\": [1, 2, 3]", "\"position\": [1, 2, 1e400]")]
    [InlineData("\"position\": [1, 2, 3]", "\"position\": [1, 2, \"x\"]")]
    [InlineData("\"rotation\": [0, 0, 0, 1]", "\"rotation\": [0, 0, 0, 0]")]
    [InlineData("\"role\": \"hole\"", "\"role\": \"ghost\"")]
    [InlineData("\"visible\": true", "\"visible\": \"yes\"")]
    public void Invalid_values_are_rejected(string find, string replace)
    {
        Assert.Contains(find, V1Json);
        Fails(ZipJson(V1Json.Replace(find, replace)));
    }

    [Fact]
    public void Children_in_a_leaf_node_are_rejected()
    {
        var json = V1Json.Replace("\"generator\": \"cylinder\",", "\"generator\": \"cylinder\", \"children\": [ { \"type\": \"group\", \"id\": \"aaaaaaaa-0000-0000-0000-000000000009\" } ],");
        Assert.Contains("cannot contain", Fails(ZipJson(json)).Message);
    }

    [Fact]
    public void Excessive_nesting_is_rejected_without_overflowing_the_stack()
    {
        var sb = new StringBuilder("{\"schemaVersion\":1,\"nodes\":[");
        const int depth = ProjectFile.Limits.MaxDepth + 10;
        for (var i = 0; i < depth; i++) sb.Append($"{{\"type\":\"group\",\"id\":\"{Guid.NewGuid()}\",\"children\":[");
        for (var i = 0; i < depth; i++) sb.Append("]}");
        sb.Append("]}");

        Fails(ZipJson(sb.ToString()));
    }

    [Fact]
    public void Absurdly_deep_json_is_rejected_by_the_parser()
    {
        var json = "{\"schemaVersion\":1,\"nodes\":" + new string('[', 5000) + new string(']', 5000) + "}";
        Fails(ZipJson(json));
    }

    [Fact]
    public void Too_many_nodes_are_rejected()
    {
        var sb = new StringBuilder("{\"schemaVersion\":1,\"nodes\":[");
        for (var i = 0; i <= ProjectFile.Limits.MaxNodes; i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append($"{{\"type\":\"group\",\"id\":\"{Guid.NewGuid()}\"}}");
        }

        sb.Append("]}");
        Assert.Contains("too many objects", Fails(ZipJson(sb.ToString())).Message);
    }

    [Fact]
    public void Zip_slip_paths_are_rejected()
    {
        var json = Encoding.UTF8.GetBytes(V1Json);
        foreach (var evil in new[] { "../evil.txt", "..\\evil.txt", "/etc/passwd", "C:\\Windows\\evil.dll", "a/../../b.txt" })
        {
            var bytes = Zip((ProjectFile.DocumentEntryName, json), (evil, [1]));
            Assert.Contains("invalid path", Fails(bytes).Message);
        }
    }

    [Fact]
    public void Zip_bomb_is_rejected_before_it_is_expanded()
    {
        var huge = new byte[(int)ProjectFile.Limits.MaxDocumentBytes + 1024]; // compresses to a few kilobytes
        var bytes = Zip((ProjectFile.DocumentEntryName, huge));
        Assert.True(bytes.Length < 1_000_000);

        Assert.Contains("unsafe", Fails(bytes).Message);
    }

    [Fact]
    public void Many_small_entries_are_rejected()
    {
        var entries = Enumerable.Range(0, ProjectFile.Limits.MaxEntries + 1).Select(i => ($"x/{i}.txt", new byte[] { 1 })).ToArray();
        Fails(Zip(entries));
    }

    // N-SEC-01: fuzzing must never crash or throw anything but the documented exception.
    [Fact]
    public void Random_corruption_never_causes_anything_but_a_format_error()
    {
        var valid = Save(RichDocument());
        var random = new Random(12345);

        for (var i = 0; i < 400; i++)
        {
            var bytes = (byte[])valid.Clone();
            switch (i % 3)
            {
                case 0: // flip a few bytes
                    for (var k = 0; k < 1 + random.Next(6); k++) bytes[random.Next(bytes.Length)] ^= (byte)(1 + random.Next(255));
                    break;
                case 1: // truncate
                    bytes = bytes[..random.Next(bytes.Length)];
                    break;
                default: // overwrite a block
                    var start = random.Next(bytes.Length - 8);
                    random.NextBytes(bytes.AsSpan(start, 8));
                    break;
            }

            try
            {
                Open(bytes);
            }
            catch (ProjectFormatException)
            {
                // expected
            }
        }
    }

    [Fact]
    public void Random_corruption_of_the_json_never_causes_anything_but_a_format_error()
    {
        var random = new Random(777);
        var chars = V1Json.ToCharArray();
        for (var i = 0; i < 400; i++)
        {
            var mutated = (char[])chars.Clone();
            const string alphabet = "{}[]\",:0-.xe9 \n";
            for (var k = 0; k < 1 + random.Next(4); k++) mutated[random.Next(mutated.Length)] = alphabet[random.Next(alphabet.Length)];
            try
            {
                Open(ZipJson(new string(mutated)));
            }
            catch (ProjectFormatException)
            {
            }
        }
    }

    // ---- files on disk -------------------------------------------------------------------------

    private static string TempPath() => Path.Combine(Path.GetTempPath(), $"formwerk-test-{Guid.NewGuid():N}.fwp");

    [Fact]
    public void Save_and_load_through_the_file_system()
    {
        var path = TempPath();
        try
        {
            ProjectFile.SaveAtomic(path, RichDocument());

            Assert.False(File.Exists(path + ".tmp"));
            var loaded = ProjectFile.Load(path);
            Assert.Equal(RichDocument().Root.Children.Count, loaded.Root.Children.Count);
        }
        finally
        {
            File.Delete(path);
        }
    }

    // N-ROB-02: a failure while saving must leave the old file untouched and readable.
    [Fact]
    public void Failed_save_keeps_the_existing_file_and_removes_the_temp_file()
    {
        var path = TempPath();
        try
        {
            ProjectFile.SaveAtomic(path, RichDocument());
            var before = File.ReadAllBytes(path);

            Assert.Throws<InvalidOperationException>(() => ProjectFile.SaveAtomic(path, stream =>
            {
                stream.Write(new byte[1000]);
                throw new InvalidOperationException("disk full");
            }));

            Assert.Equal(before, File.ReadAllBytes(path));
            Assert.False(File.Exists(path + ".tmp"));
            Assert.NotNull(ProjectFile.Load(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Saving_over_an_existing_file_replaces_it()
    {
        var path = TempPath();
        try
        {
            ProjectFile.SaveAtomic(path, new FormwerkDocument());
            ProjectFile.SaveAtomic(path, RichDocument());
            Assert.Equal(RichDocument().Root.Children.Count, ProjectFile.Load(path).Root.Children.Count);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Loading_a_missing_file_is_a_format_error_with_a_message()
    {
        var ex = Assert.Throws<ProjectFormatException>(() => ProjectFile.Load(TempPath()));
        Assert.Contains("does not exist", ex.Message);
    }

    [Fact]
    public void Loading_a_text_file_is_a_format_error()
    {
        var path = TempPath();
        try
        {
            File.WriteAllText(path, "just text");
            Assert.Throws<ProjectFormatException>(() => ProjectFile.Load(path));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
