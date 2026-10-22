using Formwerk.Library;

namespace Formwerk.Tests.Library;

/// <summary>Searching and filtering the library (F-LIB-02): a search for "Schraube" finds every element with that keyword.</summary>
public class LibrarySearchTests
{
    private static IReadOnlyList<LibraryItem> Items => LibraryCatalog.Items;

    private static string[] Names(IEnumerable<LibraryItem> items) => [.. items.Select(i => i.Name)];

    [Fact]
    public void Schraube_finds_the_screws_as_well_as_the_english_word()
    {
        var german = LibrarySearch.Filter(Items, "Schraube");
        var english = LibrarySearch.Filter(Items, "screw");

        Assert.NotEmpty(german);
        Assert.Equal(Names(english), Names(german));
        Assert.All(german, i => Assert.StartsWith("Hex Bolt", i.Name));
        Assert.Equal(Items.Count(i => i.Name.StartsWith("Hex Bolt")), german.Count);
    }

    [Fact]
    public void The_search_ignores_case_and_surrounding_spaces()
    {
        Assert.Equal(Names(LibrarySearch.Filter(Items, "nut")), Names(LibrarySearch.Filter(Items, "  NuT ")));
        Assert.NotEmpty(LibrarySearch.Filter(Items, "nut"));
    }

    [Fact]
    public void All_words_must_match()
    {
        var hexNuts = LibrarySearch.Filter(Items, "hex nut");

        Assert.NotEmpty(hexNuts);
        Assert.All(hexNuts, i => Assert.StartsWith("Hex Nut", i.Name));
        Assert.True(LibrarySearch.Filter(Items, "hex").Count > hexNuts.Count);
        Assert.Empty(LibrarySearch.Filter(Items, "nut sphere"));
    }

    [Fact]
    public void The_name_the_category_and_the_keywords_are_searched()
    {
        Assert.Contains("Open Box", Names(LibrarySearch.Filter(Items, "enclosures")));
        Assert.Contains("Cylinder", Names(LibrarySearch.Filter(Items, "cylinder")));
        Assert.Contains("Washer M3", Names(LibrarySearch.Filter(Items, "unterlegscheibe")));
    }

    [Fact]
    public void An_empty_search_gives_everything_in_the_order_of_the_catalog()
    {
        Assert.Equal(Names(Items), Names(LibrarySearch.Filter(Items, "")));
        Assert.Equal(Names(Items), Names(LibrarySearch.Filter(Items, null)));
        Assert.Equal(Names(Items), Names(LibrarySearch.Filter(Items, "   ")));
    }

    [Fact]
    public void Nothing_found_gives_an_empty_list()
    {
        Assert.Empty(LibrarySearch.Filter(Items, "zzzz"));
    }

    [Fact]
    public void A_category_narrows_the_list()
    {
        var fasteners = LibrarySearch.Filter(Items, "", LibraryCategory.Fasteners);

        Assert.NotEmpty(fasteners);
        Assert.All(fasteners, i => Assert.Equal(LibraryCategory.Fasteners, i.Category));
        Assert.Equal(Items.Count(i => i.Category == LibraryCategory.Fasteners), fasteners.Count);
        Assert.Equal(Names(LibrarySearch.Filter(fasteners, "nut")), Names(LibrarySearch.Filter(Items, "nut", LibraryCategory.Fasteners)));
    }

    [Fact]
    public void Only_the_given_ids_are_kept_for_the_favorites()
    {
        var favorites = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { Items[3].Id, Items[7].Id };

        var found = LibrarySearch.Filter(Items, "", null, favorites);

        Assert.Equal([Items[3].Name, Items[7].Name], Names(found));
        Assert.Empty(LibrarySearch.Filter(Items, "", null, new HashSet<string>()));
    }
}
