using System.Globalization;
using System.Numerics;
using Formwerk.Core.Commands;
using Formwerk.Core.Model;
using Formwerk.Features.Shapes;

namespace Formwerk.Library;

/// <summary>
/// The built-in elements of the library (F-LIB-02): the basic shapes and ready-made parametric elements. The ready-made ones are built from
/// the basic shapes, merges and differences, so after inserting every dimension can be changed in the properties of its part.
/// All of them stand on the plate (Z = 0) and are centered on X and Y.
/// </summary>
public static class LibraryCatalog
{
    // The corner circle of a hexagon with the given width across the flats.
    private const double Corner3 = 6.35, Corner4 = 8.08, Corner5 = 9.24;

    /// <summary>Part of the name of the saved pictures: raise it when an element changes, so that its picture is made again.</summary>
    public const int ThumbnailVersion = 2;

    public static IReadOnlyList<LibraryItem> Items { get; } = Build();

    /// <summary>The item with this id (any case), or null.</summary>
    public static LibraryItem? Find(string id) => Items.FirstOrDefault(i => string.Equals(i.Id, id, StringComparison.OrdinalIgnoreCase));

    private static List<LibraryItem> Build()
    {
        var items = new List<LibraryItem>();

        var shapeWords = new Dictionary<string, string[]>
        {
            ["box"] = ["cube", "block", "quader", "wuerfel"],
            ["cylinder"] = ["rod", "zylinder", "round"],
            ["sphere"] = ["ball", "kugel"],
            ["hemisphere"] = ["dome", "halbkugel"],
            ["cone"] = ["kegel"],
            ["frustum"] = ["truncated cone", "kegelstumpf"],
            ["pyramid"] = ["pyramide"],
            ["prism"] = ["polygon", "hexagon", "n-gon", "prisma"],
            ["wedge"] = ["ramp", "keil"],
            ["torus"] = ["ring", "donut"],
            ["tube"] = ["pipe", "hollow", "rohr"],
            ["capsule"] = ["pill", "kapsel"],
        };
        foreach (var generator in ShapeLibrary.All)
        {
            var size = generator.Generate(new Dictionary<string, double>()).ComputeBounds().Size;
            items.Add(new LibraryItem(generator.Id, generator.DisplayName, LibraryCategory.BasicShapes,
                shapeWords.GetValueOrDefault(generator.Id, [generator.Id]), $"A {generator.DisplayName.ToLowerInvariant()} with its own dimensions.",
                size, ShapeId: generator.Id));
        }

        // Fasteners: nuts, bolts and washers for M3, M4 and M5 (the sizes of the usual printer hardware).
        foreach (var (size, corner, height, hole) in new[] { ("M3", Corner3, 2.4, 3.2), ("M4", Corner4, 3.2, 4.2), ("M5", Corner5, 4.0, 5.2) })
        {
            var across = corner * Math.Sqrt(3) / 2;
            items.Add(Recipe($"hex-nut-{size.ToLowerInvariant()}", $"Hex Nut {size}", LibraryCategory.Fasteners,
                ["nut", "mutter", "hexagon", size.ToLowerInvariant(), "fastener", "verbindungselement"],
                $"Hexagon nut for {size} screws with a hole of {Fmt(hole)} mm.", new Vector3((float)corner, (float)across, (float)height),
                () => HexNut($"Hex Nut {size}", corner, height, hole)));
        }

        foreach (var (size, length, corner, head, shaft) in new[] { ("M3x10", 10.0, Corner3, 2.0, 3.0), ("M4x16", 16.0, Corner4, 2.8, 4.0), ("M5x20", 20.0, Corner5, 3.5, 5.0) })
        {
            var across = corner * Math.Sqrt(3) / 2;
            items.Add(Recipe($"hex-bolt-{size.ToLowerInvariant()}", $"Hex Bolt {size}", LibraryCategory.Fasteners,
                ["screw", "schraube", "bolt", "bolzen", "hexagon", size[..2].ToLowerInvariant(), "fastener", "verbindungselement"],
                $"Hexagon head bolt {size} (a plain shaft, no thread).", new Vector3((float)corner, (float)across, (float)(head + length)),
                () => HexBolt($"Hex Bolt {size}", corner, head, shaft, length)));
        }

        foreach (var (size, outer, hole, height) in new[] { ("M3", 7.0, 3.2, 0.5), ("M4", 9.0, 4.3, 0.8), ("M5", 10.0, 5.3, 1.0) })
        {
            items.Add(Recipe($"washer-{size.ToLowerInvariant()}", $"Washer {size}", LibraryCategory.Fasteners,
                ["washer", "unterlegscheibe", "scheibe", "ring", size.ToLowerInvariant(), "fastener", "verbindungselement"],
                $"Flat washer for {size} screws.", new Vector3((float)outer, (float)outer, (float)height),
                () => Tube($"Washer {size}", outer, (outer - hole) / 2, height)));
        }

        items.Add(Recipe("standoff-m3-10", "Standoff M3 10 mm", LibraryCategory.Fasteners,
            ["standoff", "spacer", "abstandshalter", "abstandsbolzen", "m3", "fastener", "verbindungselement"],
            "Round spacer sleeve, 10 mm long, for M3 screws.", new Vector3(6, 6, 10), () => Tube("Standoff M3 10 mm", 6, 1.4, 10)));

        // Brackets
        items.Add(Recipe("l-bracket", "L-Bracket", LibraryCategory.Brackets,
            ["angle", "winkel", "halterung", "corner", "holder"], "Angle bracket 40 mm wide with two screw holes in its foot.",
            new Vector3(40, 25, 30), LBracket));
        items.Add(Recipe("wall-plate", "Wall Plate", LibraryCategory.Brackets,
            ["plate", "platte", "mounting", "halterung", "holder"], "Flat mounting plate 60 x 40 mm with four screw holes.",
            new Vector3(60, 40, 3), WallPlate));

        // Enclosures
        items.Add(Recipe("open-box", "Open Box", LibraryCategory.Enclosures,
            ["case", "housing", "gehaeuse", "schale", "container", "tray"], "Open box 60 x 40 x 25 mm with 2 mm walls and floor.",
            new Vector3(60, 40, 25), OpenBox));
        items.Add(Recipe("lid", "Lid", LibraryCategory.Enclosures,
            ["cover", "deckel", "cap", "housing", "gehaeuse"], "Lid with a lip that fits the open box (0.2 mm play on each side).",
            new Vector3(60, 40, 5), Lid));

        // Mechanics
        items.Add(Recipe("hub-disc", "Hub Disc", LibraryCategory.Mechanics,
            ["disc", "wheel", "rad", "scheibe", "pulley", "hub", "nabe"], "Round disc 30 mm across with a 5 mm bore.",
            new Vector3(30, 30, 10), HubDisc));
        items.Add(Recipe("spacer-ring", "Spacer Ring", LibraryCategory.Mechanics,
            ["ring", "spacer", "bushing", "buchse", "sleeve", "abstandshalter"], "Ring 20 mm across with a 12 mm bore, 5 mm high.",
            new Vector3(20, 20, 5), () => Tube("Spacer Ring", 20, 4, 5)));
        items.Add(Recipe("axle-pin", "Axle Pin", LibraryCategory.Mechanics,
            ["axle", "achse", "pin", "stift", "shaft", "welle", "rod"], "Round pin 4 mm across and 30 mm long with chamfered ends.",
            new Vector3(4, 4, 30), AxlePin));

        return items;
    }

    private static LibraryItem Recipe(string id, string name, LibraryCategory category, string[] keywords, string description, Vector3 footprint, Func<Node> build) =>
        new(id, name, category, keywords, description, footprint, Recipe: build);

    // ---- the recipes -----------------------------------------------------------------------------

    private static Node HexNut(string name, double corner, double height, double hole)
    {
        var tree = new Tree();
        var top = tree.Add(tree.Root, new BooleanNode(name, BooleanOperation.Difference));
        tree.Add(top, Prism("Body", corner, height, 6));
        tree.Add(top, Cylinder("Hole", hole, height + 2, new Vector3(0, 0, -1)));
        return tree.Take(top);
    }

    private static Node HexBolt(string name, double corner, double head, double shaft, double length)
    {
        var tree = new Tree();
        var top = tree.Add(tree.Root, new MergeNode(name));
        tree.Add(top, Prism("Head", corner, head, 6));
        // the shaft starts inside the head, so that the two overlap and merge into one body
        tree.Add(top, Cylinder("Shaft", shaft, length + head - 1, new Vector3(0, 0, 1)));
        return tree.Take(top);
    }

    private static Node LBracket()
    {
        var tree = new Tree();
        var top = tree.Add(tree.Root, new BooleanNode("L-Bracket", BooleanOperation.Difference));
        var body = tree.Add(top, new MergeNode("Body"));
        tree.Add(body, Box("Foot", 40, 25, 3, Vector3.Zero));
        tree.Add(body, Box("Wall", 40, 3, 30, new Vector3(0, -11, 0)));
        tree.Add(top, Cylinder("Hole 1", 4, 5, new Vector3(-12, 4, -1)));
        tree.Add(top, Cylinder("Hole 2", 4, 5, new Vector3(12, 4, -1)));
        return tree.Take(top);
    }

    private static Node WallPlate()
    {
        var tree = new Tree();
        var top = tree.Add(tree.Root, new BooleanNode("Wall Plate", BooleanOperation.Difference));
        tree.Add(top, Box("Plate", 60, 40, 3, Vector3.Zero));
        var number = 1;
        foreach (var x in new[] { -25f, 25f })
        foreach (var y in new[] { -15f, 15f })
            tree.Add(top, Cylinder($"Hole {number++}", 4, 5, new Vector3(x, y, -1)));
        return tree.Take(top);
    }

    private static Node OpenBox()
    {
        var tree = new Tree();
        var top = tree.Add(tree.Root, new BooleanNode("Open Box", BooleanOperation.Difference));
        tree.Add(top, Box("Outside", 60, 40, 25, Vector3.Zero));
        tree.Add(top, Box("Inside", 56, 36, 25, new Vector3(0, 0, 2)));
        return tree.Take(top);
    }

    private static Node Lid()
    {
        var tree = new Tree();
        var top = tree.Add(tree.Root, new MergeNode("Lid"));
        tree.Add(top, Box("Plate", 60, 40, 2, Vector3.Zero));
        tree.Add(top, Box("Lip", 55.6, 35.6, 3, new Vector3(0, 0, 2)));
        return tree.Take(top);
    }

    private static Node HubDisc()
    {
        var tree = new Tree();
        var top = tree.Add(tree.Root, new BooleanNode("Hub Disc", BooleanOperation.Difference));
        tree.Add(top, Cylinder("Disc", 30, 10, Vector3.Zero));
        tree.Add(top, Cylinder("Bore", 5, 12, new Vector3(0, 0, -1)));
        return tree.Take(top);
    }

    private static Node AxlePin()
    {
        var pin = Cylinder("Axle Pin", 4, 30, Vector3.Zero);
        pin.Parameters["edgeSize"] = "0.5";
        pin.Parameters["edgeType"] = "0"; // chamfer
        pin.Parameters["edgeWhere"] = "0"; // all edges
        return pin;
    }

    // ---- building blocks -------------------------------------------------------------------------

    private static string Fmt(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);

    private static PrimitiveNode Make(string generator, string name, Vector3 position, params (string Key, double Value)[] values)
    {
        var node = new PrimitiveNode(generator, name) { Transform = Transform.FromPosition(position) };
        foreach (var (key, value) in values) node.Parameters[key] = value.ToString("R", CultureInfo.InvariantCulture);
        return node;
    }

    private static PrimitiveNode Box(string name, double width, double depth, double height, Vector3 position) =>
        Make("box", name, position, ("width", width), ("depth", depth), ("height", height));

    private static PrimitiveNode Cylinder(string name, double diameter, double height, Vector3 position) =>
        Make("cylinder", name, position, ("diameter", diameter), ("height", height), ("segments", 64));

    private static PrimitiveNode Prism(string name, double cornerDiameter, double height, int sides) =>
        Make("prism", name, Vector3.Zero, ("diameter", cornerDiameter), ("height", height), ("sides", sides));

    private static PrimitiveNode Tube(string name, double outerDiameter, double wall, double height) =>
        Make("tube", name, Vector3.Zero, ("outerDiameter", outerDiameter), ("wall", wall), ("height", height), ("segments", 64));

    /// <summary>Builds a tree in a scratch document with the normal commands and hands it out as a loose copy with new ids.</summary>
    private sealed class Tree
    {
        private readonly FormwerkDocument _document = new();
        private readonly UndoHistory _history = new();

        public Node Root => _document.Root;

        public T Add<T>(Node parent, T node) where T : Node
        {
            _history.Execute(new AddNodeCommand(_document, parent, node));
            return node;
        }

        public Node Take(Node node) => node.Clone(newIds: true);
    }
}
