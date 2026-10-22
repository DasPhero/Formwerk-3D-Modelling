using System.Globalization;
using System.Numerics;
using Formwerk.Core.Model;
using Formwerk.Features.Shapes;

namespace Formwerk.Library;

/// <summary>The categories of the library (F-LIB-02); "My Templates" are the user's own and are managed separately.</summary>
public enum LibraryCategory
{
    BasicShapes,
    Fasteners,
    Brackets,
    Enclosures,
    Mechanics,

    /// <summary>The templates the user saved (F-LIB-03); they are not part of the catalog but of the list in the program.</summary>
    MyTemplates,
}

public static class LibraryCategoryNames
{
    /// <summary>The English name of a category, also what the search matches.</summary>
    public static string DisplayName(this LibraryCategory category) => category switch
    {
        LibraryCategory.BasicShapes => "Basic Shapes",
        LibraryCategory.Fasteners => "Fasteners",
        LibraryCategory.Brackets => "Brackets",
        LibraryCategory.Enclosures => "Enclosures",
        LibraryCategory.Mechanics => "Mechanics",
        LibraryCategory.MyTemplates => "My Templates",
        _ => category.ToString(),
    };
}

/// <summary>
/// One element of the library: a basic shape, or a ready-made parametric element (a tree of shapes and booleans that is inserted as
/// normal nodes, so every dimension stays editable). <see cref="Keywords"/> hold the other names of the element (also German ones), so
/// that a search for "Schraube" finds the screws.
/// </summary>
/// <param name="Id">Stable id (saved for the favorites).</param>
/// <param name="Footprint">The size in mm of the element as it is inserted (it rests on the plate, centered); used to find a free place for it.</param>
/// <param name="ShapeId">The generator of a basic shape; null for a recipe.</param>
/// <param name="Recipe">Builds a new, loose node tree of a ready-made element; null for a basic shape.</param>
public sealed record LibraryItem(
    string Id,
    string Name,
    LibraryCategory Category,
    IReadOnlyList<string> Keywords,
    string Description,
    Vector3 Footprint,
    string? ShapeId = null,
    Func<Node>? Recipe = null)
{
    public bool IsShape => ShapeId is not null;

    /// <summary>A new loose node (tree) of this element with new ids, named like the element; the caller makes the name unique and adds it.</summary>
    public Node CreateNode()
    {
        if (Recipe is not null) return Recipe();

        var generator = ShapeLibrary.Find(ShapeId!) ?? throw new InvalidOperationException($"Unknown shape '{ShapeId}'.");
        var node = new PrimitiveNode(generator.Id, Name);
        foreach (var definition in generator.Parameters)
            node.Parameters[definition.Name] = definition.Default.ToString("R", CultureInfo.InvariantCulture);
        return node;
    }
}
