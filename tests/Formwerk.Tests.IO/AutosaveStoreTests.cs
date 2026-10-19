using Formwerk.Core.Commands;
using Formwerk.Core.Model;
using Formwerk.IO;

namespace Formwerk.Tests.IO;

public sealed class AutosaveStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"formwerk-autosave-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }

    private static FormwerkDocument Document(string name)
    {
        var doc = new FormwerkDocument();
        new UndoHistory().Execute(new AddNodeCommand(doc, doc.Root, new PrimitiveNode("box", name)));
        return doc;
    }

    [Fact]
    public void Nothing_to_recover_without_an_autosave()
    {
        var store = new AutosaveStore(_directory);
        Assert.False(store.TryGetRecovery(out _));
    }

    [Fact]
    public void Written_autosave_can_be_found_and_loaded()
    {
        var store = new AutosaveStore(_directory);
        var before = DateTime.UtcNow.AddSeconds(-5);

        store.Write(Document("Box"), @"C:\work\bracket.fwp");

        Assert.True(store.TryGetRecovery(out var info));
        Assert.Equal(@"C:\work\bracket.fwp", info.OriginalPath);
        Assert.True(info.SavedUtc > before);
        Assert.Equal("Box", store.Load().Root.Children.Single().Name);
    }

    [Fact]
    public void Untitled_projects_have_no_original_path()
    {
        var store = new AutosaveStore(_directory);
        store.Write(Document("Box"), originalPath: null);

        Assert.True(store.TryGetRecovery(out var info));
        Assert.Null(info.OriginalPath);
    }

    [Fact]
    public void A_new_autosave_replaces_the_old_one()
    {
        var store = new AutosaveStore(_directory);
        store.Write(Document("First"), null);
        store.Write(Document("Second"), null);

        Assert.Equal("Second", store.Load().Root.Children.Single().Name);
        Assert.Equal(2, Directory.GetFiles(_directory).Length); // project + metadata, no leftovers
    }

    [Fact]
    public void Clear_removes_everything()
    {
        var store = new AutosaveStore(_directory);
        store.Write(Document("Box"), null);

        store.Clear();

        Assert.False(store.TryGetRecovery(out _));
        Assert.Empty(Directory.GetFiles(_directory));
        store.Clear(); // clearing twice is fine
    }

    [Fact]
    public void Recovery_still_works_when_the_metadata_is_damaged()
    {
        var store = new AutosaveStore(_directory);
        store.Write(Document("Box"), "x.fwp");
        File.WriteAllText(Path.Combine(_directory, "autosave.json"), "{ broken");

        Assert.True(store.TryGetRecovery(out var info));
        Assert.Null(info.OriginalPath);
        Assert.NotNull(store.Load());
    }

    [Fact]
    public void Damaged_autosave_gives_a_format_error()
    {
        var store = new AutosaveStore(_directory);
        store.Write(Document("Box"), null);
        File.WriteAllText(Path.Combine(_directory, "autosave.fwp"), "garbage");

        Assert.True(store.TryGetRecovery(out _));
        Assert.Throws<ProjectFormatException>(() => store.Load());
    }
}
