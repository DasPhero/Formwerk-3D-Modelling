using System.IO.Compression;
using System.Numerics;
using Formwerk.Core.Commands;
using Formwerk.Core.Model;
using Formwerk.Features.Shapes;
using Formwerk.Geometry;
using Formwerk.IO;

namespace Formwerk.Tests.IO;

/// <summary>Imported meshes in the project file (<c>meshes/&lt;id&gt;.bin</c>) and in the autosave.</summary>
public class ProjectMeshTests
{
    private static Dictionary<string, double> P(params (string, double)[] v) =>
        v.ToDictionary(x => x.Item1, x => x.Item2, StringComparer.OrdinalIgnoreCase);

    private static MeshData Sphere() => new SphereGenerator().Generate(P(("diameter", 20), ("segments", 24), ("rings", 12)));

    private static MeshData Box() => new BoxGenerator().Generate(P());

    /// <summary>Two imported objects (one mesh each) and a third object that shares the first mesh.</summary>
    private static (FormwerkDocument Doc, MeshStore Store, Guid A, Guid B) Scene()
    {
        var doc = new FormwerkDocument();
        var store = new MeshStore();
        var history = new UndoHistory();
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        store.Add(a, Sphere());
        store.Add(b, Box());
        store.Add(Guid.NewGuid(), Box().Transformed(Matrix4x4.CreateScale(3))); // not used by any object

        history.Execute(new AddNodeCommand(doc, doc.Root, new MeshNode("Imported A", a)));
        history.Execute(new AddNodeCommand(doc, doc.Root, new MeshNode("Imported B", b) { Transform = Transform.FromPosition(new Vector3(40, 0, 0)) }));
        history.Execute(new AddNodeCommand(doc, doc.Root, new MeshNode("Copy of A", a) { Transform = Transform.FromPosition(new Vector3(-40, 0, 0)) }));
        return (doc, store, a, b);
    }

    private static byte[] Save(FormwerkDocument doc, MeshStore? store)
    {
        using var stream = new MemoryStream();
        ProjectFile.Write(stream, doc, store);
        return stream.ToArray();
    }

    private static FormwerkDocument Open(byte[] bytes, MeshStore? store)
    {
        using var stream = new MemoryStream(bytes);
        return ProjectFile.Read(stream, store);
    }

    private static void AssertSameMesh(MeshData expected, MeshData actual)
    {
        Assert.Equal(expected.Vertices.ToArray(), actual.Vertices.ToArray());
        Assert.Equal(expected.Indices.ToArray(), actual.Indices.ToArray());
    }

    // ---- the mesh codec --------------------------------------------------------------------------

    [Fact]
    public void The_mesh_format_roundtrips_exactly()
    {
        var mesh = Sphere();
        using var stream = new MemoryStream();
        MeshFormat.Write(stream, mesh);

        var back = MeshFormat.Read(stream.ToArray());

        AssertSameMesh(mesh, back);
        Assert.True(back.IsWatertight());
    }

    [Fact]
    public void The_mesh_format_has_the_documented_size()
    {
        var mesh = Box();
        using var stream = new MemoryStream();
        MeshFormat.Write(stream, mesh);

        Assert.Equal(12 + mesh.VertexCount * 12 + mesh.Indices.Length * 4, stream.Length);
        Assert.Equal("FWM1", System.Text.Encoding.ASCII.GetString(stream.ToArray(), 0, 4));
    }

    private static byte[] Encoded(MeshData mesh)
    {
        using var stream = new MemoryStream();
        MeshFormat.Write(stream, mesh);
        return stream.ToArray();
    }

    [Fact]
    public void A_damaged_mesh_is_a_format_error()
    {
        var good = Encoded(Box());

        Assert.Throws<ProjectFormatException>(() => MeshFormat.Read([]));
        Assert.Throws<ProjectFormatException>(() => MeshFormat.Read(good[..8]));
        Assert.Throws<ProjectFormatException>(() => MeshFormat.Read(good[..^4]));                       // incomplete
        Assert.Throws<ProjectFormatException>(() => MeshFormat.Read([.. good, 1, 2, 3, 4]));          // too long
        var wrongMagic = (byte[])good.Clone();
        wrongMagic[0] = (byte)'X';
        Assert.Throws<ProjectFormatException>(() => MeshFormat.Read(wrongMagic));

        var hugeCount = (byte[])good.Clone();
        BitConverter.GetBytes(int.MaxValue).CopyTo(hugeCount, 4);
        Assert.Throws<ProjectFormatException>(() => MeshFormat.Read(hugeCount));

        var negativeCount = (byte[])good.Clone();
        BitConverter.GetBytes(-5).CopyTo(negativeCount, 8);
        Assert.Throws<ProjectFormatException>(() => MeshFormat.Read(negativeCount));

        var notTriangles = (byte[])good.Clone();
        BitConverter.GetBytes(Box().Indices.Length - 1).CopyTo(notTriangles, 8);
        Assert.Throws<ProjectFormatException>(() => MeshFormat.Read(notTriangles));
    }

    [Fact]
    public void Invalid_numbers_and_indices_in_a_mesh_are_rejected()
    {
        var nan = Encoded(Box());
        BitConverter.GetBytes(float.NaN).CopyTo(nan, 12);
        Assert.Throws<ProjectFormatException>(() => MeshFormat.Read(nan));

        var outOfRange = Encoded(Box());
        BitConverter.GetBytes(9999).CopyTo(outOfRange, 12 + Box().VertexCount * 12);
        Assert.Throws<ProjectFormatException>(() => MeshFormat.Read(outOfRange));
    }

    [Fact]
    public void Random_corruption_of_a_mesh_never_causes_anything_but_a_format_error()
    {
        var valid = Encoded(Sphere());
        var random = new Random(31337);
        for (var i = 0; i < 400; i++)
        {
            var bytes = (byte[])valid.Clone();
            if (i % 2 == 0) for (var k = 0; k < 1 + random.Next(6); k++) bytes[random.Next(bytes.Length)] ^= (byte)(1 + random.Next(255));
            else bytes = bytes[..random.Next(bytes.Length)];

            try { MeshFormat.Read(bytes); }
            catch (ProjectFormatException) { }
        }
    }

    // ---- meshes in the project -------------------------------------------------------------------

    [Fact]
    public void Meshes_roundtrip_through_a_project_file()
    {
        var (doc, store, a, b) = Scene();

        var loaded = Open(Save(doc, store), out var loadedStore);

        Assert.Equal(3, loaded.Root.Children.Count);
        Assert.IsType<MeshNode>(loaded.Root.Children[0]);
        Assert.Equal(a, ((MeshNode)loaded.Root.Children[0]).MeshId);
        Assert.Equal(a, ((MeshNode)loaded.Root.Children[2]).MeshId); // the copy still shares the mesh
        AssertSameMesh(store.Get(a)!, loadedStore.Get(a)!);
        AssertSameMesh(store.Get(b)!, loadedStore.Get(b)!);
    }

    private static FormwerkDocument Open(byte[] bytes, out MeshStore loadedStore)
    {
        loadedStore = new MeshStore();
        return Open(bytes, loadedStore);
    }

    [Fact]
    public void Only_the_meshes_that_an_object_uses_are_written_and_a_shared_mesh_only_once()
    {
        var (doc, store, a, b) = Scene();
        Assert.Equal(3, store.Count); // one of them is not used

        using var zip = new ZipArchive(new MemoryStream(Save(doc, store)));

        var names = zip.Entries.Select(e => e.FullName).Order().ToList();
        Assert.Equal(3, names.Count);
        Assert.Contains("document.json", names);
        Assert.Contains($"meshes/{a:D}.bin", names);
        Assert.Contains($"meshes/{b:D}.bin", names);
    }

    [Fact]
    public void Save_load_save_gives_identical_bytes_with_meshes()
    {
        var (doc, store, _, _) = Scene();
        var first = Save(doc, store);

        var loaded = Open(first, out var loadedStore);
        var second = Save(loaded, loadedStore);

        Assert.Equal(first, second);
    }

    [Fact]
    public void A_project_without_imported_objects_stays_a_single_part_even_with_a_store()
    {
        var doc = new FormwerkDocument();
        new UndoHistory().Execute(new AddNodeCommand(doc, doc.Root, new PrimitiveNode("box", "B")));

        using var zip = new ZipArchive(new MemoryStream(Save(doc, new MeshStore())));

        Assert.Single(zip.Entries);
    }

    [Fact]
    public void Saving_an_object_whose_mesh_is_not_in_the_store_fails_instead_of_losing_it_silently()
    {
        var (doc, _, _, _) = Scene();

        var ex = Assert.Throws<InvalidOperationException>(() => Save(doc, new MeshStore()));
        Assert.Contains("Imported", ex.Message);
    }

    [Fact]
    public void A_missing_mesh_part_makes_the_file_unusable_with_a_clear_message()
    {
        var (doc, store, a, _) = Scene();
        var withoutMesh = Rezip(Save(doc, store), e => e.FullName != $"meshes/{a:D}.bin");

        var ex = Assert.Throws<ProjectFormatException>(() => Open(withoutMesh, new MeshStore()));
        Assert.Contains("mesh", ex.Message);
        Assert.Contains("Imported A", ex.Message);
    }

    [Fact]
    public void A_damaged_mesh_part_is_a_format_error()
    {
        var (doc, store, a, _) = Scene();
        var damaged = Rezip(Save(doc, store), _ => true, replace: (name, data) => name == $"meshes/{a:D}.bin" ? data[..(data.Length / 2)] : data);

        Assert.Throws<ProjectFormatException>(() => Open(damaged, new MeshStore()));
    }

    [Fact]
    public void Without_a_store_the_meshes_are_ignored_and_the_document_still_opens()
    {
        var (doc, store, _, _) = Scene();

        var loaded = Open(Save(doc, store), store: null);

        Assert.Equal(3, loaded.Root.Children.Count);
    }

    [Fact]
    public void Mesh_parts_are_subject_to_the_zip_slip_check_like_everything_else()
    {
        var (doc, store, _, _) = Scene();
        var evil = Rezip(Save(doc, store), _ => true, extra: ("meshes/../../evil.bin", [1, 2, 3]));

        Assert.Contains("invalid path", Assert.Throws<ProjectFormatException>(() => Open(evil, new MeshStore())).Message);
    }

    [Fact]
    public void Random_corruption_of_a_project_with_meshes_never_causes_anything_but_a_format_error()
    {
        var (doc, store, _, _) = Scene();
        var valid = Save(doc, store);
        var random = new Random(2026);
        for (var i = 0; i < 300; i++)
        {
            var bytes = (byte[])valid.Clone();
            if (i % 2 == 0) for (var k = 0; k < 1 + random.Next(6); k++) bytes[random.Next(bytes.Length)] ^= (byte)(1 + random.Next(255));
            else bytes = bytes[..random.Next(bytes.Length)];

            try { Open(bytes, new MeshStore()); }
            catch (ProjectFormatException) { }
        }
    }

    // ---- autosave --------------------------------------------------------------------------------

    [Fact]
    public void The_autosave_keeps_the_imported_meshes()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"formwerk-autosave-mesh-{Guid.NewGuid():N}");
        try
        {
            var (doc, store, a, _) = Scene();
            var autosave = new AutosaveStore(directory);
            autosave.Write(doc, originalPath: null, store);

            var restoredStore = new MeshStore();
            var restored = autosave.Load(restoredStore);

            Assert.Equal(3, restored.Root.Children.Count);
            AssertSameMesh(store.Get(a)!, restoredStore.Get(a)!);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    // ---- helpers ---------------------------------------------------------------------------------

    private static byte[] Rezip(
        byte[] original, Func<ZipArchiveEntry, bool> keep,
        Func<string, byte[], byte[]>? replace = null, (string Name, byte[] Data)? extra = null)
    {
        using var source = new ZipArchive(new MemoryStream(original));
        using var output = new MemoryStream();
        using (var target = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var entry in source.Entries.Where(keep))
            {
                using var read = entry.Open();
                using var buffer = new MemoryStream();
                read.CopyTo(buffer);
                var data = replace is null ? buffer.ToArray() : replace(entry.FullName, buffer.ToArray());
                using var write = target.CreateEntry(entry.FullName).Open();
                write.Write(data);
            }

            if (extra is { } e)
            {
                using var write = target.CreateEntry(e.Name).Open();
                write.Write(e.Data);
            }
        }

        return output.ToArray();
    }
}
