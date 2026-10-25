using Formwerk.Library;

namespace Formwerk.Tests.Library;

/// <summary>The sub folders of the built-in categories and the order of the library (F-LIB-02).</summary>
public class LibraryFoldersTests
{
    private static IReadOnlyList<LibraryItem> Items => LibraryCatalog.Items;

    [Fact]
    public void Every_element_of_a_category_with_sub_folders_is_in_one_of_them()
    {
        foreach (var item in Items)
        {
            var folders = LibraryFolders.SubFolders(item.Category);
            var folder = LibraryFolders.SubFolderOf(item);

            if (folders.Count == 0) Assert.Equal("", folder);
            else Assert.Contains(folder, folders);
        }
    }

    [Fact]
    public void No_sub_folder_is_empty_and_the_basic_shapes_have_none()
    {
        foreach (var category in Enum.GetValues<LibraryCategory>())
        foreach (var folder in LibraryFolders.SubFolders(category))
            Assert.Contains(Items, i => i.Category == category && LibraryFolders.SubFolderOf(i) == folder);

        Assert.Empty(LibraryFolders.SubFolders(LibraryCategory.BasicShapes));
        Assert.Empty(LibraryFolders.SubFolders(LibraryCategory.MyTemplates));
    }

    [Theory]
    [InlineData("hex-nut-m3", "Nuts")]
    [InlineData("square-nut-m4", "Nuts")]
    [InlineData("hex-bolt-m6x25", "Bolts and Screws")]
    [InlineData("cap-screw-m3x10", "Bolts and Screws")]
    [InlineData("washer-m5", "Washers")]
    [InlineData("fender-washer-m5", "Washers")]
    [InlineData("hex-standoff-m3-15", "Spacers and Posts")]
    [InlineData("rod-m5x40", "Pins and Rods")]
    [InlineData("cable-clip", "Holders")]
    [InlineData("wall-hook", "Hooks and Pegs")]
    [InlineData("box-with-lid", "Boxes")]
    [InlineData("round-lid", "Lids and Covers")]
    [InlineData("cable-grommet", "Plugs and Feet")]
    [InlineData("bearing-608", "Bushings and Bearings")]
    [InlineData("wheel-40", "Wheels and Rollers")]
    public void Elements_are_in_the_sub_folder_one_would_look_in(string id, string folder) =>
        Assert.Equal(folder, LibraryFolders.SubFolderOf(LibraryCatalog.Find(id)!));

    [Fact]
    public void Numbers_in_names_are_sorted_as_numbers_and_case_does_not_matter()
    {
        string[] names = ["Nut M10", "nut m3", "Nut M4", "Nut M3 long", "Washer M3"];

        var sorted = names.Order(LibraryFolders.NaturalComparer.Instance).ToArray();

        Assert.Equal(["nut m3", "Nut M3 long", "Nut M4", "Nut M10", "Washer M3"], sorted);
    }

    [Fact]
    public void The_library_is_ordered_by_category_then_sub_folder_then_name()
    {
        var ordered = LibraryFolders.Order(Items.Reverse());

        Assert.Equal(Items.Count, ordered.Count);
        Assert.True(ordered.Select(i => (int)i.Category).SequenceEqual(ordered.Select(i => (int)i.Category).Order()));
        var nuts = ordered.Where(i => LibraryFolders.SubFolderOf(i) == "Nuts").Select(i => i.Name).ToArray();
        Assert.Equal(["Hex Nut M3", "Hex Nut M4", "Hex Nut M5", "Hex Nut M6"], nuts.Take(4).ToArray());
        var first = ordered.Where(i => i.Category == LibraryCategory.Fasteners).Select(LibraryFolders.SubFolderOf).Distinct().ToArray();
        Assert.Equal(LibraryFolders.SubFolders(LibraryCategory.Fasteners), first);
    }
}
