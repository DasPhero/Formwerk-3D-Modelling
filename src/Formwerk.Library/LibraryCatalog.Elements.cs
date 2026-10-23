using System.Numerics;
using Formwerk.Core.Model;

namespace Formwerk.Library;

public static partial class LibraryCatalog
{
    private const double Corner6 = 11.55;

    /// <summary>The second batch of ready-made elements (F-LIB-02): more fasteners, brackets, enclosures and mechanics.</summary>
    private static void AddMoreElements(List<LibraryItem> items)
    {
        // Fasteners
        items.Add(Recipe("hex-nut-m6", "Hex Nut M6", LibraryCategory.Fasteners,
            ["nut", "mutter", "hexagon", "m6", "fastener", "verbindungselement"],
            "Hexagon nut for M6 screws with a hole of 6.4 mm.", new Vector3((float)Corner6, (float)(Corner6 * Math.Sqrt(3) / 2), 5), () => HexNut("Hex Nut M6", Corner6, 5, 6.4)));
        items.Add(Recipe("hex-bolt-m6x25", "Hex Bolt M6x25", LibraryCategory.Fasteners,
            ["screw", "schraube", "bolt", "bolzen", "hexagon", "m6", "fastener", "verbindungselement"],
            "Hexagon head bolt M6x25 (a plain shaft, no thread).", new Vector3((float)Corner6, (float)(Corner6 * Math.Sqrt(3) / 2), 29), () => HexBolt("Hex Bolt M6x25", Corner6, 4, 6, 25)));
        items.Add(Recipe("washer-m6", "Washer M6", LibraryCategory.Fasteners,
            ["washer", "unterlegscheibe", "scheibe", "ring", "m6", "fastener", "verbindungselement"],
            "Flat washer for M6 screws.", new Vector3(12, 12, 1.6f), () => Tube("Washer M6", 12, 2.8, 1.6)));

        foreach (var (size, length, head, headHeight, shaft) in new[] { ("M3x10", 10.0, 5.5, 3.0, 3.0), ("M4x16", 16.0, 7.0, 4.0, 4.0), ("M5x20", 20.0, 8.5, 5.0, 5.0) })
        {
            items.Add(Recipe($"cap-screw-{size.ToLowerInvariant()}", $"Cap Screw {size}", LibraryCategory.Fasteners,
                ["screw", "schraube", "zylinderschraube", "socket", "cap", size[..2].ToLowerInvariant(), "fastener", "verbindungselement"],
                $"Round head cap screw {size} (a plain shaft, no thread and no socket).", new Vector3((float)head, (float)head, (float)(headHeight + length)),
                () => CapScrew($"Cap Screw {size}", head, headHeight, shaft, length)));
        }

        foreach (var (size, side, height, hole) in new[] { ("M3", 5.5, 1.8, 3.2), ("M4", 7.0, 3.2, 4.2) })
        {
            items.Add(Recipe($"square-nut-{size.ToLowerInvariant()}", $"Square Nut {size}", LibraryCategory.Fasteners,
                ["nut", "mutter", "square", "vierkant", "slot", "nutenstein", size.ToLowerInvariant(), "fastener", "verbindungselement"],
                $"Square nut for {size} screws, fits a slot in a profile or a printed pocket.", new Vector3((float)side, (float)side, (float)height),
                () => SquareNut($"Square Nut {size}", side, height, hole)));
        }

        items.Add(Recipe("insert-boss-m3", "Insert Boss M3", LibraryCategory.Fasteners,
            ["insert", "heat set", "einpressmutter", "boss", "standoff", "m3", "fastener", "verbindungselement"],
            "Small sleeve with a 4 mm bore for a heat-set M3 insert, 6 mm high.", new Vector3(5.6f, 5.6f, 6), () => Tube("Insert Boss M3", 5.6, 0.8, 6)));

        // Brackets
        items.Add(Recipe("u-bracket", "U-Bracket", LibraryCategory.Brackets,
            ["u", "channel", "halterung", "holder", "clamp", "buegel", "winkel"], "U-shaped holder 30 x 20 x 20 mm with 3 mm walls and floor, open at both ends.",
            new Vector3(30, 20, 20), UBracket));
        items.Add(Recipe("t-bracket", "T-Bracket", LibraryCategory.Brackets,
            ["t", "plate", "platte", "halterung", "holder", "mounting", "verbinder"], "Flat T-shaped connector, 3 mm thick, with three screw holes.",
            new Vector3(60, 40, 3), TBracket));
        items.Add(Recipe("flat-l-brace", "Flat L-Brace", LibraryCategory.Brackets,
            ["l", "angle", "winkel", "corner", "ecke", "verbinder", "mounting"], "Flat L-shaped corner brace, 3 mm thick, with two screw holes.",
            new Vector3(40, 40, 3), FlatLBrace));
        items.Add(Recipe("cable-clip", "Cable Clip", LibraryCategory.Brackets,
            ["cable", "kabel", "clip", "ring", "halterung", "holder", "guide"], "Ring for a cable (8 mm hole) on a base plate with two screw holes.",
            new Vector3(20, 14, 8), CableClip));

        // Enclosures
        items.Add(Recipe("round-box", "Round Box", LibraryCategory.Enclosures,
            ["case", "housing", "gehaeuse", "dose", "container", "tray", "round", "rund"], "Open round box 40 mm across and 20 mm high with 2 mm walls and floor.",
            new Vector3(40, 40, 20), RoundBox));
        items.Add(Recipe("round-lid", "Round Lid", LibraryCategory.Enclosures,
            ["cover", "deckel", "cap", "round", "rund", "housing", "gehaeuse"], "Round lid with a lip that fits the round box (0.2 mm play).",
            new Vector3(40, 40, 5), RoundLid));
        items.Add(Recipe("divided-tray", "Divided Tray", LibraryCategory.Enclosures,
            ["tray", "organizer", "sorting", "sortierbox", "schale", "compartment", "fach", "box"], "Tray 60 x 40 x 20 mm with two pockets and a 2 mm wall between them.",
            new Vector3(60, 40, 20), DividedTray));
        items.Add(Recipe("cable-grommet", "Cable Grommet", LibraryCategory.Enclosures,
            ["cable", "kabel", "grommet", "durchfuehrung", "tuelle", "bushing", "passthrough"], "Grommet with a flange and a 12 mm hole for a cable through a panel.",
            new Vector3(26, 26, 6), CableGrommet));
        items.Add(Recipe("round-foot", "Round Foot", LibraryCategory.Enclosures,
            ["foot", "fuss", "feet", "bumper", "gummifuss", "pad", "standfuss"], "Round foot 12 mm across and 5 mm high with a chamfer.",
            new Vector3(12, 12, 5), () => ChamferedCylinder("Round Foot", 12, 5, 1)));

        // Mechanics
        items.Add(Recipe("pulley", "Pulley", LibraryCategory.Mechanics,
            ["wheel", "rad", "rolle", "riemenscheibe", "belt", "riemen", "disc", "scheibe"], "Pulley with two flanges, 24 mm across, 10 mm high, with a 5 mm bore.",
            new Vector3(24, 24, 10), Pulley));
        items.Add(Recipe("bearing-608", "Bearing 608", LibraryCategory.Mechanics,
            ["bearing", "lager", "kugellager", "ring", "608", "skate"], "Stand-in for the 608 ball bearing: ring of 22 mm across, 8 mm bore, 7 mm high.",
            new Vector3(22, 22, 7), () => Tube("Bearing 608", 22, 7, 7)));
        items.Add(Recipe("bearing-seat-608", "Bearing Seat 608", LibraryCategory.Mechanics,
            ["bearing", "lager", "lagersitz", "seat", "holder", "608", "pocket"], "Block with a pocket of 22.2 mm for a 608 bearing and a 12 mm hole through the floor.",
            new Vector3(28, 28, 10), BearingSeat));
        items.Add(Recipe("shaft-coupler", "Shaft Coupler", LibraryCategory.Mechanics,
            ["coupling", "kupplung", "sleeve", "huelse", "connector", "verbinder", "shaft", "welle"], "Sleeve with a 5 mm bore, 20 mm long, that joins two 5 mm shafts.",
            new Vector3(12, 12, 20), () => Tube("Shaft Coupler", 12, 3.5, 20)));
        items.Add(Recipe("knob", "Knob", LibraryCategory.Mechanics,
            ["handle", "griff", "drehknopf", "button", "knopf", "grip", "nut"], "Round knob 30 mm across with a hexagon pocket for an M5 nut on top.",
            new Vector3(30, 30, 15), Knob));
        items.Add(Recipe("flanged-bushing", "Flanged Bushing", LibraryCategory.Mechanics,
            ["bushing", "buchse", "bush", "sleeve", "flange", "flansch", "bearing", "lager"], "Bushing with a collar: 14 mm sleeve, 20 mm collar, 8 mm bore, 8 mm high.",
            new Vector3(20, 20, 8), FlangedBushing));
        items.Add(Recipe("dowel-pin", "Dowel Pin", LibraryCategory.Mechanics,
            ["pin", "stift", "dowel", "passstift", "bolzen", "axle", "rod"], "Round pin 3 mm across and 10 mm long with chamfered ends.",
            new Vector3(3, 3, 10), () => ChamferedCylinder("Dowel Pin", 3, 10, 0.3)));
    }

    // ---- the recipes of the second batch -----------------------------------------------------------

    private static Node CapScrew(string name, double head, double headHeight, double shaft, double length)
    {
        var tree = new Tree();
        var top = tree.Add(tree.Root, new MergeNode(name));
        tree.Add(top, Cylinder("Head", head, headHeight, Vector3.Zero));
        // the shaft starts inside the head, so that the two overlap and merge into one body
        tree.Add(top, Cylinder("Shaft", shaft, length + headHeight - 1, new Vector3(0, 0, 1)));
        return tree.Take(top);
    }

    private static Node SquareNut(string name, double side, double height, double hole)
    {
        var tree = new Tree();
        var top = tree.Add(tree.Root, new BooleanNode(name, BooleanOperation.Difference));
        tree.Add(top, Box("Body", side, side, height, Vector3.Zero));
        tree.Add(top, Cylinder("Hole", hole, height + 2, new Vector3(0, 0, -1)));
        return tree.Take(top);
    }

    private static Node UBracket()
    {
        var tree = new Tree();
        var top = tree.Add(tree.Root, new BooleanNode("U-Bracket", BooleanOperation.Difference));
        tree.Add(top, Box("Outside", 30, 20, 20, Vector3.Zero));
        tree.Add(top, Box("Inside", 24, 22, 20, new Vector3(0, 0, 3)));
        return tree.Take(top);
    }

    private static Node TBracket()
    {
        var tree = new Tree();
        var top = tree.Add(tree.Root, new BooleanNode("T-Bracket", BooleanOperation.Difference));
        var body = tree.Add(top, new MergeNode("Body"));
        tree.Add(body, Box("Bar", 60, 10, 3, new Vector3(0, 15, 0)));
        tree.Add(body, Box("Stem", 10, 31, 3, new Vector3(0, -4.5f, 0)));
        tree.Add(top, Cylinder("Hole 1", 4, 5, new Vector3(-25, 15, -1)));
        tree.Add(top, Cylinder("Hole 2", 4, 5, new Vector3(25, 15, -1)));
        tree.Add(top, Cylinder("Hole 3", 4, 5, new Vector3(0, -15, -1)));
        return tree.Take(top);
    }

    private static Node FlatLBrace()
    {
        var tree = new Tree();
        var top = tree.Add(tree.Root, new BooleanNode("Flat L-Brace", BooleanOperation.Difference));
        var body = tree.Add(top, new MergeNode("Body"));
        tree.Add(body, Box("Arm 1", 40, 10, 3, new Vector3(0, -15, 0)));
        tree.Add(body, Box("Arm 2", 10, 40, 3, new Vector3(-15, 0, 0)));
        tree.Add(top, Cylinder("Hole 1", 4, 5, new Vector3(5, -15, -1)));
        tree.Add(top, Cylinder("Hole 2", 4, 5, new Vector3(-15, 5, -1)));
        return tree.Take(top);
    }

    private static Node CableClip()
    {
        var tree = new Tree();
        var top = tree.Add(tree.Root, new BooleanNode("Cable Clip", BooleanOperation.Difference));
        var body = tree.Add(top, new MergeNode("Body"));
        tree.Add(body, Box("Base", 20, 14, 2, Vector3.Zero));
        tree.Add(body, Cylinder("Ring", 12, 8, Vector3.Zero));
        tree.Add(top, Cylinder("Cable Hole", 8, 10, new Vector3(0, 0, -1)));
        tree.Add(top, Cylinder("Screw Hole 1", 3, 4, new Vector3(-8, 0, -1)));
        tree.Add(top, Cylinder("Screw Hole 2", 3, 4, new Vector3(8, 0, -1)));
        return tree.Take(top);
    }

    private static Node RoundBox()
    {
        var tree = new Tree();
        var top = tree.Add(tree.Root, new BooleanNode("Round Box", BooleanOperation.Difference));
        tree.Add(top, Cylinder("Outside", 40, 20, Vector3.Zero));
        tree.Add(top, Cylinder("Inside", 36, 20, new Vector3(0, 0, 2)));
        return tree.Take(top);
    }

    private static Node RoundLid()
    {
        var tree = new Tree();
        var top = tree.Add(tree.Root, new MergeNode("Round Lid"));
        tree.Add(top, Cylinder("Plate", 40, 2, Vector3.Zero));
        tree.Add(top, Cylinder("Lip", 35.6, 3, new Vector3(0, 0, 2)));
        return tree.Take(top);
    }

    private static Node DividedTray()
    {
        var tree = new Tree();
        var top = tree.Add(tree.Root, new BooleanNode("Divided Tray", BooleanOperation.Difference));
        tree.Add(top, Box("Outside", 60, 40, 20, Vector3.Zero));
        tree.Add(top, Box("Pocket 1", 27, 36, 20, new Vector3(-14.5f, 0, 2)));
        tree.Add(top, Box("Pocket 2", 27, 36, 20, new Vector3(14.5f, 0, 2)));
        return tree.Take(top);
    }

    private static Node CableGrommet()
    {
        var tree = new Tree();
        var top = tree.Add(tree.Root, new BooleanNode("Cable Grommet", BooleanOperation.Difference));
        var body = tree.Add(top, new MergeNode("Body"));
        tree.Add(body, Cylinder("Neck", 20, 6, Vector3.Zero));
        tree.Add(body, Cylinder("Flange", 26, 1.5, Vector3.Zero));
        tree.Add(top, Cylinder("Cable Hole", 12, 8, new Vector3(0, 0, -1)));
        return tree.Take(top);
    }

    private static Node Pulley()
    {
        var tree = new Tree();
        var top = tree.Add(tree.Root, new BooleanNode("Pulley", BooleanOperation.Difference));
        var body = tree.Add(top, new MergeNode("Body"));
        tree.Add(body, Cylinder("Core", 16, 10, Vector3.Zero));
        tree.Add(body, Cylinder("Flange 1", 24, 2, Vector3.Zero));
        tree.Add(body, Cylinder("Flange 2", 24, 2, new Vector3(0, 0, 8)));
        tree.Add(top, Cylinder("Bore", 5, 12, new Vector3(0, 0, -1)));
        return tree.Take(top);
    }

    private static Node BearingSeat()
    {
        var tree = new Tree();
        var top = tree.Add(tree.Root, new BooleanNode("Bearing Seat 608", BooleanOperation.Difference));
        tree.Add(top, Cylinder("Block", 28, 10, Vector3.Zero));
        tree.Add(top, Cylinder("Pocket", 22.2, 8, new Vector3(0, 0, 2)));
        tree.Add(top, Cylinder("Hole", 12, 12, new Vector3(0, 0, -1)));
        return tree.Take(top);
    }

    private static Node Knob()
    {
        var tree = new Tree();
        var top = tree.Add(tree.Root, new BooleanNode("Knob", BooleanOperation.Difference));
        tree.Add(top, Cylinder("Grip", 30, 15, Vector3.Zero));
        tree.Add(top, Make("prism", "Nut Pocket", new Vector3(0, 0, 8), ("diameter", Corner5), ("height", 9), ("sides", 6)));
        return tree.Take(top);
    }

    private static Node FlangedBushing()
    {
        var tree = new Tree();
        var top = tree.Add(tree.Root, new BooleanNode("Flanged Bushing", BooleanOperation.Difference));
        var body = tree.Add(top, new MergeNode("Body"));
        tree.Add(body, Cylinder("Sleeve", 14, 8, Vector3.Zero));
        tree.Add(body, Cylinder("Collar", 20, 2, Vector3.Zero));
        tree.Add(top, Cylinder("Bore", 8, 10, new Vector3(0, 0, -1)));
        return tree.Take(top);
    }

    private static Node ChamferedCylinder(string name, double diameter, double height, double chamfer)
    {
        var node = Cylinder(name, diameter, height, Vector3.Zero);
        node.Parameters["edgeSize"] = Fmt(chamfer);
        node.Parameters["edgeType"] = "0"; // chamfer
        node.Parameters["edgeWhere"] = "0"; // all edges
        return node;
    }
}
