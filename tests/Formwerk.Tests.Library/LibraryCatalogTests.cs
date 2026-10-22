using System.Numerics;
using Formwerk.Core.Commands;
using Formwerk.Core.Model;
using Formwerk.Features.Evaluation;
using Formwerk.Features.Shapes;
using Formwerk.Geometry;
using Formwerk.Kernel;
using Formwerk.Library;

namespace Formwerk.Tests.Library;

/// <summary>The built-in catalog of the library (F-LIB-02): shapes and ready-made parametric elements.</summary>
public class LibraryCatalogTests
{
    private static readonly SceneEvaluator Evaluator = new(new ManifoldKernel());

    private static IReadOnlyList<LibraryItem> Items => LibraryCatalog.Items;

    private static (List<ScenePart> Solids, Bounds Bounds, double Volume) Evaluate(LibraryItem item)
    {
        var doc = new FormwerkDocument();
        new UndoHistory().Execute(new AddNodeCommand(doc, doc.Root, item.CreateNode()));
        var result = Evaluator.Evaluate(doc);
        Assert.False(result.HasErrors, item.Name + ": " + string.Join("; ", result.Errors.Select(e => e.Message)));
        var solids = result.Parts.Where(p => p.Kind == PartKind.Solid && !p.Mesh.IsEmpty).ToList();
        Assert.NotEmpty(solids);
        var bounds = solids.Select(p => p.Mesh.ComputeBounds()).ToList();
        return (solids, new Bounds(bounds.Select(b => b.Min).Aggregate(Vector3.Min), bounds.Select(b => b.Max).Aggregate(Vector3.Max)),
            solids.Sum(p => p.Mesh.ComputeVolume()));
    }

    [Fact]
    public void The_ids_are_unique_and_every_item_is_described()
    {
        Assert.Equal(Items.Count, Items.Select(i => i.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(Items, i =>
        {
            Assert.False(string.IsNullOrWhiteSpace(i.Name));
            Assert.False(string.IsNullOrWhiteSpace(i.Description));
            Assert.NotEmpty(i.Keywords);
            Assert.True(i.Footprint.X > 0 && i.Footprint.Y > 0 && i.Footprint.Z > 0, i.Name);
            Assert.True(i.IsShape ^ i.Recipe is not null, i.Name);
        });
    }

    [Fact]
    public void Every_category_has_elements_and_the_basic_shapes_are_all_twelve()
    {
        foreach (var category in Enum.GetValues<LibraryCategory>()) Assert.Contains(Items, i => i.Category == category);

        var basic = Items.Where(i => i.Category == LibraryCategory.BasicShapes).ToList();
        Assert.Equal(ShapeLibrary.All.Select(g => g.Id).Order().ToArray(), basic.Select(i => i.ShapeId!).Order().ToArray());
        Assert.True(Items.Count >= 25);
    }

    [Fact]
    public void Items_are_found_by_their_id()
    {
        Assert.All(Items, i => Assert.Same(i, LibraryCatalog.Find(i.Id)));
        Assert.Null(LibraryCatalog.Find("no-such-item"));
        Assert.Same(Items[0], LibraryCatalog.Find(Items[0].Id.ToUpperInvariant()));
    }

    [Fact]
    public void Creating_an_item_gives_a_new_loose_tree_every_time()
    {
        foreach (var item in Items)
        {
            var a = item.CreateNode();
            var b = item.CreateNode();

            Assert.Null(a.Parent);
            Assert.NotEqual(a.Id, b.Id);
            Assert.Equal(a.Descendants().Count(), b.Descendants().Count());
            var other = b.Descendants().Select(x => x.Id).ToHashSet();
            Assert.All(a.Descendants(), d => Assert.DoesNotContain(d.Id, other));
        }
    }

    [Fact]
    public void Every_element_is_a_closed_solid_on_the_plate_with_the_declared_size()
    {
        foreach (var item in Items)
        {
            var (solids, bounds, volume) = Evaluate(item);

            Assert.All(solids, p => Assert.True(p.Mesh.IsWatertight(), item.Name + " is not closed"));
            Assert.True(volume > 0, item.Name);
            Assert.Equal(0, bounds.Min.Z, 2);
            Assert.Equal(0, (bounds.Min.X + bounds.Max.X) / 2, 1);
            Assert.Equal(0, (bounds.Min.Y + bounds.Max.Y) / 2, 1);
            Assert.Equal(item.Footprint.X, bounds.Size.X, 1);
            Assert.Equal(item.Footprint.Y, bounds.Size.Y, 1);
            Assert.Equal(item.Footprint.Z, bounds.Size.Z, 1);
        }
    }

    [Fact]
    public void A_nut_is_a_hexagon_with_a_hole_through_it()
    {
        var (_, _, volume) = Evaluate(LibraryCatalog.Find("hex-nut-m3")!);

        var hexagon = 3 * Math.Sqrt(3) / 2 * 3.175 * 3.175; // corner radius 3.175: 5.5 across the flats
        var hole = Math.PI * 1.6 * 1.6;
        Assert.Equal((hexagon - hole) * 2.4, volume, (hexagon - hole) * 2.4 * 0.02);
    }

    [Fact]
    public void A_washer_is_a_flat_ring()
    {
        var (_, _, volume) = Evaluate(LibraryCatalog.Find("washer-m3")!);

        Assert.Equal(Math.PI * (3.5 * 3.5 - 1.6 * 1.6) * 0.5, volume, 0.3);
    }

    [Fact]
    public void A_bolt_is_one_body_of_a_head_and_a_shaft()
    {
        var (solids, bounds, volume) = Evaluate(LibraryCatalog.Find("hex-bolt-m3x10")!);

        Assert.Single(solids);
        var head = 3 * Math.Sqrt(3) / 2 * 3.175 * 3.175 * 2;
        var shaft = Math.PI * 1.5 * 1.5 * 10;
        Assert.Equal(head + shaft, volume, (head + shaft) * 0.02);
        Assert.Equal(12, bounds.Size.Z, 2);
    }

    [Fact]
    public void The_bracket_has_two_holes_in_its_foot()
    {
        var (_, _, volume) = Evaluate(LibraryCatalog.Find("l-bracket")!);

        var full = 40 * 25 * 3 + 40 * 3 * 27.0; // the foot and the wall above it
        Assert.Equal(full - 2 * Math.PI * 2 * 2 * 3, volume, full * 0.01);
    }

    [Fact]
    public void The_open_box_is_hollow()
    {
        var (_, _, volume) = Evaluate(LibraryCatalog.Find("open-box")!);

        Assert.Equal(60 * 40 * 25 - 56 * 36 * 23.0, volume, 1);
    }
}
