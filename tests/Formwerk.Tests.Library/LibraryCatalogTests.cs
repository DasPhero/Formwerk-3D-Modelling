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
        // the templates of the user are not part of the catalog
        foreach (var category in Enum.GetValues<LibraryCategory>().Where(c => c != LibraryCategory.MyTemplates)) Assert.Contains(Items, i => i.Category == category);
        Assert.DoesNotContain(Items, i => i.Category == LibraryCategory.MyTemplates);
        Assert.Equal("My Templates", LibraryCategory.MyTemplates.DisplayName());

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
    public void The_catalog_has_grown_to_at_least_55_elements_and_the_screw_search_finds_the_cap_screws()
    {
        Assert.True(Items.Count >= 55, Items.Count.ToString());
        foreach (var id in new[] { "hex-nut-m6", "hex-bolt-m6x25", "washer-m6", "cap-screw-m3x10", "cap-screw-m4x16", "cap-screw-m5x20", "square-nut-m3", "square-nut-m4", "insert-boss-m3",
                     "u-bracket", "t-bracket", "flat-l-brace", "cable-clip", "round-box", "round-lid", "divided-tray", "cable-grommet", "round-foot",
                     "pulley", "bearing-608", "bearing-seat-608", "shaft-coupler", "knob", "flanged-bushing", "dowel-pin" })
            Assert.NotNull(LibraryCatalog.Find(id));

        Assert.Contains(LibrarySearch.Filter(Items, "Schraube"), i => i.Id == "cap-screw-m4x16");
    }

    [Fact]
    public void A_cap_screw_is_one_body_of_a_round_head_and_a_shaft()
    {
        var (solids, bounds, volume) = Evaluate(LibraryCatalog.Find("cap-screw-m3x10")!);

        Assert.Single(solids);
        var expected = Math.PI * 2.75 * 2.75 * 3 + Math.PI * 1.5 * 1.5 * 10;
        Assert.Equal(expected, volume, expected * 0.02);
        Assert.Equal(13, bounds.Size.Z, 2);
    }

    [Fact]
    public void A_square_nut_is_a_block_with_a_round_hole()
    {
        var (_, _, volume) = Evaluate(LibraryCatalog.Find("square-nut-m3")!);

        var expected = (5.5 * 5.5 - Math.PI * 1.6 * 1.6) * 1.8;
        Assert.Equal(expected, volume, expected * 0.02);
    }

    [Fact]
    public void A_flanged_bushing_is_a_sleeve_with_a_collar()
    {
        var (_, _, volume) = Evaluate(LibraryCatalog.Find("flanged-bushing")!);

        var expected = Math.PI * (7 * 7 * 6 + 10 * 10 * 2 - 4 * 4 * 8);
        Assert.Equal(expected, volume, expected * 0.02);
    }

    [Fact]
    public void The_divided_tray_has_two_pockets_with_a_wall_between_them()
    {
        var (_, _, volume) = Evaluate(LibraryCatalog.Find("divided-tray")!);

        Assert.Equal(60 * 40 * 20 - 2 * 27 * 36 * 18.0, volume, 1);
    }

    [Fact]
    public void The_second_batch_adds_21_elements_and_the_catalog_has_at_least_75()
    {
        Assert.True(Items.Count >= 75, Items.Count.ToString());
        foreach (var id in new[] { "screw-boss-m3", "standoff-m3-20", "hex-standoff-m3-15", "rod-m5x40", "fender-washer-m5",
                     "wall-hook", "rod-holder-8", "rod-holder-12", "mounting-strip", "hanging-tab",
                     "rect-plug", "round-plug", "flat-cover", "battery-holder-2aa", "vent-plate",
                     "wheel-40", "link-arm-40", "bearing-housing-608", "spacer-block", "shaft-collar", "plain-bushing" })
            Assert.NotNull(LibraryCatalog.Find(id));
    }

    [Fact]
    public void The_wall_hook_is_a_plate_an_arm_and_a_tip_in_one_body()
    {
        var (solids, _, volume) = Evaluate(LibraryCatalog.Find("wall-hook")!);

        Assert.Single(solids);
        Assert.Equal(20 * 3 * 40 + 20 * 28 * 3 + 20 * 3 * 12 - 2 * 20 * 3 * 3.0, volume, 1);
    }

    [Fact]
    public void The_battery_holder_has_two_pockets_for_AA_cells()
    {
        var (_, _, volume) = Evaluate(LibraryCatalog.Find("battery-holder-2aa")!);

        Assert.Equal(36 * 56 * 16 - 2 * 15 * 52 * 14.0, volume, 1);
    }

    [Fact]
    public void The_wheel_is_a_dished_disc_with_a_hub_and_a_bore()
    {
        var (solids, _, volume) = Evaluate(LibraryCatalog.Find("wheel-40")!);

        Assert.Single(solids);
        var expected = Math.PI * ((400 - 256) * 10 + 256 * 2 + 36 * 8 - 6.25 * 10);
        Assert.Equal(expected, volume, expected * 0.02);
    }

    [Fact]
    public void The_open_box_is_hollow()
    {
        var (_, _, volume) = Evaluate(LibraryCatalog.Find("open-box")!);

        Assert.Equal(60 * 40 * 25 - 56 * 36 * 23.0, volume, 1);
    }
}
