using System.Numerics;
using Formwerk.Core.Model;

namespace Formwerk.Library;

public static partial class LibraryCatalog
{
    /// <summary>The third group of ready-made elements (F-LIB-02): spacers and rods, holders, plugs and covers, wheels and bushings.</summary>
    private static void AddMoreElements2(List<LibraryItem> items)
    {
        // Fasteners
        items.Add(Recipe("screw-boss-m3", "Mounting Post M3", LibraryCategory.Fasteners,
            ["boss", "post", "saeule", "schraubdom", "m3", "fastener", "verbindungselement"],
            "Round post 7 mm across with a 2.5 mm hole for a self-tapping M3 screw, 8 mm high.", new Vector3(7, 7, 8), () => Tube("Mounting Post M3", 7, 2.25, 8)));
        items.Add(Recipe("standoff-m3-20", "Standoff M3 20 mm", LibraryCategory.Fasteners,
            ["standoff", "spacer", "abstandshalter", "abstandsbolzen", "m3", "fastener", "verbindungselement"],
            "Round spacer sleeve, 20 mm long, for M3 screws.", new Vector3(6, 6, 20), () => Tube("Standoff M3 20 mm", 6, 1.4, 20)));
        items.Add(Recipe("hex-standoff-m3-15", "Hex Standoff M3 15 mm", LibraryCategory.Fasteners,
            ["standoff", "spacer", "abstandshalter", "hexagon", "sechskant", "m3", "fastener", "verbindungselement"],
            "Hexagon spacer for M3 screws, 15 mm long.", new Vector3((float)Corner3, (float)(Corner3 * Math.Sqrt(3) / 2), 15), () => HexNut("Hex Standoff M3 15 mm", Corner3, 15, 3.2)));
        items.Add(Recipe("rod-m5x40", "Rod M5x40", LibraryCategory.Fasteners,
            ["rod", "stange", "gewindestange", "threaded", "bolzen", "m5", "fastener", "verbindungselement"],
            "Plain round rod 5 mm across and 40 mm long (no thread).", new Vector3(5, 5, 40), () => Cylinder("Rod M5x40", 5, 40, Vector3.Zero)));
        items.Add(Recipe("fender-washer-m5", "Fender Washer M5", LibraryCategory.Fasteners,
            ["washer", "unterlegscheibe", "scheibe", "karosseriescheibe", "ring", "m5", "fastener", "verbindungselement"],
            "Large flat washer for M5 screws, 20 mm across.", new Vector3(20, 20, 1.2f), () => Tube("Fender Washer M5", 20, 7.25, 1.2)));

        // Brackets
        items.Add(Recipe("wall-hook", "Wall Hook", LibraryCategory.Brackets,
            ["hook", "haken", "hanger", "wandhaken", "holder", "halterung"], "Hook 20 mm wide: a back plate 40 mm high, an arm of 28 mm and a tip.",
            new Vector3(20, 28, 40), WallHook));
        items.Add(Recipe("rod-holder-8", "Rod Holder 8 mm", LibraryCategory.Brackets,
            ["rod", "stange", "holder", "halter", "halterung", "clamp", "tube", "rohr"], "Block with an 8.2 mm hole through it, holds an 8 mm rod.",
            new Vector3(22, 22, 15), () => RodHolder("Rod Holder 8 mm", 22, 8.2)));
        items.Add(Recipe("rod-holder-12", "Rod Holder 12 mm", LibraryCategory.Brackets,
            ["rod", "stange", "holder", "halter", "halterung", "clamp", "tube", "rohr"], "Block with a 12.2 mm hole through it, holds a 12 mm rod.",
            new Vector3(26, 26, 15), () => RodHolder("Rod Holder 12 mm", 26, 12.2)));
        items.Add(Recipe("mounting-strip", "Mounting Strip", LibraryCategory.Brackets,
            ["strip", "leiste", "bar", "streifen", "mounting", "halterung", "holder", "plate"], "Flat strip 60 x 10 mm, 3 mm thick, with three screw holes.",
            new Vector3(60, 10, 3), MountingStrip));
        items.Add(Recipe("hanging-tab", "Hanging Tab", LibraryCategory.Brackets,
            ["tab", "lasche", "hanger", "oese", "eyelet", "hole", "loch"], "Small tab 30 x 12 mm, 2 mm thick, with a 4 mm hole near one end.",
            new Vector3(30, 12, 2), HangingTab));

        // Enclosures
        items.Add(Recipe("rect-plug", "Rectangular Plug", LibraryCategory.Enclosures,
            ["plug", "stopfen", "cap", "kappe", "end cap", "endkappe", "square", "quadrat"], "Plug for a square tube: a 25 mm head with a 20 mm stem, 10 mm high.",
            new Vector3(25, 25, 10), RectPlug));
        items.Add(Recipe("round-plug", "Round Plug", LibraryCategory.Enclosures,
            ["plug", "stopfen", "cap", "kappe", "end cap", "endkappe", "round", "rund"], "Plug for a round hole: a 24 mm head with a 20 mm stem, 9 mm high.",
            new Vector3(24, 24, 9), RoundPlug));
        items.Add(Recipe("flat-cover", "Flat Cover", LibraryCategory.Enclosures,
            ["cover", "deckel", "plate", "platte", "panel", "blende", "lid"], "Flat cover 80 x 50 mm, 2 mm thick, with four screw holes in the corners.",
            new Vector3(80, 50, 2), FlatCover));
        items.Add(Recipe("battery-holder-2aa", "Battery Holder 2xAA", LibraryCategory.Enclosures,
            ["battery", "batterie", "akku", "aa", "holder", "halter", "tray", "box"], "Open holder for two AA cells side by side (no contacts), 36 x 56 x 16 mm.",
            new Vector3(36, 56, 16), BatteryHolder));
        items.Add(Recipe("vent-plate", "Vent Plate", LibraryCategory.Enclosures,
            ["vent", "luftung", "lueftung", "grill", "gitter", "slots", "schlitze", "cover", "panel"], "Square plate 50 mm with four long slots for air.",
            new Vector3(50, 50, 2), VentPlate));

        // Mechanics
        items.Add(Recipe("wheel-40", "Wheel 40 mm", LibraryCategory.Mechanics,
            ["wheel", "rad", "roller", "rolle", "disc", "scheibe", "hub", "nabe"], "Wheel 40 mm across and 10 mm wide, dished, with a hub and a 5 mm bore.",
            new Vector3(40, 40, 10), Wheel));
        items.Add(Recipe("link-arm-40", "Link Arm 40 mm", LibraryCategory.Mechanics,
            ["arm", "lever", "hebel", "link", "lenker", "crank", "kurbel", "rod"], "Flat arm with a round end and a 5 mm hole at each side, 40 mm between the holes.",
            new Vector3(54, 14, 4), LinkArm));
        items.Add(Recipe("bearing-housing-608", "Bearing Housing 608", LibraryCategory.Mechanics,
            ["bearing", "lager", "lagergehaeuse", "housing", "pillow", "block", "608", "holder"], "Block 40 x 30 x 20 mm with a 22.2 mm hole for a 608 bearing and two screw holes.",
            new Vector3(40, 30, 20), BearingHousing));
        items.Add(Recipe("spacer-block", "Spacer Block", LibraryCategory.Mechanics,
            ["spacer", "block", "klotz", "abstandshalter", "distanzstueck", "shim"], "Block 20 x 20 x 10 mm with a 5.2 mm hole through it.",
            new Vector3(20, 20, 10), SpacerBlock));
        items.Add(Recipe("shaft-collar", "Shaft Collar", LibraryCategory.Mechanics,
            ["collar", "stellring", "ring", "shaft", "welle", "stop", "anschlag"], "Ring 16 mm across with a 5 mm bore, 8 mm high, as a stop on a shaft.",
            new Vector3(16, 16, 8), () => Tube("Shaft Collar", 16, 5.5, 8)));
        items.Add(Recipe("plain-bushing", "Plain Bushing 8x12", LibraryCategory.Mechanics,
            ["bushing", "buchse", "bush", "sleeve", "huelse", "bearing", "lager", "gleitlager"], "Sleeve 12 mm across with an 8 mm bore, 10 mm long.",
            new Vector3(12, 12, 10), () => Tube("Plain Bushing 8x12", 12, 2, 10)));
    }

    // ---- the recipes of the third group ------------------------------------------------------------

    private static Node WallHook()
    {
        var tree = new Tree();
        var top = tree.Add(tree.Root, new MergeNode("Wall Hook"));
        tree.Add(top, Box("Back Plate", 20, 3, 40, new Vector3(0, -12.5f, 0)));
        tree.Add(top, Box("Arm", 20, 28, 3, Vector3.Zero));
        tree.Add(top, Box("Tip", 20, 3, 12, new Vector3(0, 12.5f, 0)));
        return tree.Take(top);
    }

    private static Node RodHolder(string name, double side, double hole)
    {
        var tree = new Tree();
        var top = tree.Add(tree.Root, new BooleanNode(name, BooleanOperation.Difference));
        tree.Add(top, Box("Block", side, side, 15, Vector3.Zero));
        tree.Add(top, Cylinder("Hole", hole, 17, new Vector3(0, 0, -1)));
        return tree.Take(top);
    }

    private static Node MountingStrip()
    {
        var tree = new Tree();
        var top = tree.Add(tree.Root, new BooleanNode("Mounting Strip", BooleanOperation.Difference));
        tree.Add(top, Box("Strip", 60, 10, 3, Vector3.Zero));
        var number = 1;
        foreach (var x in new[] { -22f, 0f, 22f })
            tree.Add(top, Cylinder($"Hole {number++}", 4, 5, new Vector3(x, 0, -1)));
        return tree.Take(top);
    }

    private static Node HangingTab()
    {
        var tree = new Tree();
        var top = tree.Add(tree.Root, new BooleanNode("Hanging Tab", BooleanOperation.Difference));
        tree.Add(top, Box("Tab", 30, 12, 2, Vector3.Zero));
        tree.Add(top, Cylinder("Hole", 4, 4, new Vector3(10, 0, -1)));
        return tree.Take(top);
    }

    private static Node RectPlug()
    {
        var tree = new Tree();
        var top = tree.Add(tree.Root, new MergeNode("Rectangular Plug"));
        tree.Add(top, Box("Head", 25, 25, 3, Vector3.Zero));
        tree.Add(top, Box("Stem", 20, 20, 8, new Vector3(0, 0, 2)));
        return tree.Take(top);
    }

    private static Node RoundPlug()
    {
        var tree = new Tree();
        var top = tree.Add(tree.Root, new MergeNode("Round Plug"));
        tree.Add(top, Cylinder("Head", 24, 2, Vector3.Zero));
        tree.Add(top, Cylinder("Stem", 20, 8, new Vector3(0, 0, 1)));
        return tree.Take(top);
    }

    private static Node FlatCover()
    {
        var tree = new Tree();
        var top = tree.Add(tree.Root, new BooleanNode("Flat Cover", BooleanOperation.Difference));
        tree.Add(top, Box("Plate", 80, 50, 2, Vector3.Zero));
        var number = 1;
        foreach (var x in new[] { -35f, 35f })
        foreach (var y in new[] { -20f, 20f })
            tree.Add(top, Cylinder($"Hole {number++}", 3.5, 4, new Vector3(x, y, -1)));
        return tree.Take(top);
    }

    private static Node BatteryHolder()
    {
        var tree = new Tree();
        var top = tree.Add(tree.Root, new BooleanNode("Battery Holder 2xAA", BooleanOperation.Difference));
        tree.Add(top, Box("Outside", 36, 56, 16, Vector3.Zero));
        tree.Add(top, Box("Cell 1", 15, 52, 16, new Vector3(-8, 0, 2)));
        tree.Add(top, Box("Cell 2", 15, 52, 16, new Vector3(8, 0, 2)));
        return tree.Take(top);
    }

    private static Node VentPlate()
    {
        var tree = new Tree();
        var top = tree.Add(tree.Root, new BooleanNode("Vent Plate", BooleanOperation.Difference));
        tree.Add(top, Box("Plate", 50, 50, 2, Vector3.Zero));
        var number = 1;
        foreach (var y in new[] { -12f, -4f, 4f, 12f })
            tree.Add(top, Box($"Slot {number++}", 40, 4, 4, new Vector3(0, y, -1)));
        return tree.Take(top);
    }

    private static Node Wheel()
    {
        var tree = new Tree();
        var top = tree.Add(tree.Root, new BooleanNode("Wheel 40 mm", BooleanOperation.Difference));
        var body = tree.Add(top, new MergeNode("Body"));
        var rim = tree.Add(body, new BooleanNode("Rim", BooleanOperation.Difference));
        tree.Add(rim, Cylinder("Outside", 40, 10, Vector3.Zero));
        tree.Add(rim, Cylinder("Recess", 32, 10, new Vector3(0, 0, 2)));
        tree.Add(body, Cylinder("Hub", 12, 10, Vector3.Zero));
        tree.Add(top, Cylinder("Bore", 5, 12, new Vector3(0, 0, -1)));
        return tree.Take(top);
    }

    private static Node LinkArm()
    {
        var tree = new Tree();
        var top = tree.Add(tree.Root, new BooleanNode("Link Arm 40 mm", BooleanOperation.Difference));
        var body = tree.Add(top, new MergeNode("Body"));
        tree.Add(body, Box("Bar", 40, 8, 4, Vector3.Zero));
        tree.Add(body, Cylinder("End 1", 14, 4, new Vector3(-20, 0, 0)));
        tree.Add(body, Cylinder("End 2", 14, 4, new Vector3(20, 0, 0)));
        tree.Add(top, Cylinder("Hole 1", 5, 6, new Vector3(-20, 0, -1)));
        tree.Add(top, Cylinder("Hole 2", 5, 6, new Vector3(20, 0, -1)));
        return tree.Take(top);
    }

    private static Node BearingHousing()
    {
        var tree = new Tree();
        var top = tree.Add(tree.Root, new BooleanNode("Bearing Housing 608", BooleanOperation.Difference));
        tree.Add(top, Box("Block", 40, 30, 20, Vector3.Zero));
        tree.Add(top, Cylinder("Bearing Hole", 22.2, 22, new Vector3(0, 0, -1)));
        tree.Add(top, Cylinder("Screw Hole 1", 4, 22, new Vector3(-16, 0, -1)));
        tree.Add(top, Cylinder("Screw Hole 2", 4, 22, new Vector3(16, 0, -1)));
        return tree.Take(top);
    }

    private static Node SpacerBlock()
    {
        var tree = new Tree();
        var top = tree.Add(tree.Root, new BooleanNode("Spacer Block", BooleanOperation.Difference));
        tree.Add(top, Box("Block", 20, 20, 10, Vector3.Zero));
        tree.Add(top, Cylinder("Hole", 5.2, 12, new Vector3(0, 0, -1)));
        return tree.Take(top);
    }
}
