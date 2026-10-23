using System.Numerics;
using Formwerk.Core.Model;

namespace Formwerk.Library;

public static partial class LibraryCatalog
{
    /// <summary>The fourth group of ready-made elements (F-LIB-02): the last 22 to reach 100, using cones, domes, hexagons and wedges.</summary>
    private static void AddMoreElements3(List<LibraryItem> items)
    {
        // Fasteners
        foreach (var (size, bottom, top, headHeight, shaft, length) in new[] { ("M3x12", 6.0, 3.0, 1.7, 3.0, 12.0), ("M4x16", 8.0, 4.0, 2.2, 4.0, 16.0) })
        {
            items.Add(Recipe($"countersunk-screw-{size.ToLowerInvariant()}", $"Countersunk Screw {size}", LibraryCategory.Fasteners,
                ["screw", "schraube", "senkkopf", "senkschraube", "flat head", size[..2].ToLowerInvariant(), "fastener", "verbindungselement"],
                $"Countersunk screw {size} (a plain shaft, no thread and no socket).", new Vector3((float)bottom, (float)bottom, (float)(headHeight + length)),
                () => CountersunkScrew($"Countersunk Screw {size}", bottom, top, headHeight, shaft, length)));
        }

        items.Add(Recipe("headed-pin-4x20", "Headed Pin 4x20", LibraryCategory.Fasteners,
            ["pin", "stift", "bolzen", "clevis", "bolt", "kopfbolzen", "fastener", "verbindungselement"],
            "Pin 4 mm across and 20 mm long with a flat head of 7 mm.", new Vector3(7, 7, 21.5f), HeadedPin));
        items.Add(Recipe("thumb-screw-m4", "Thumb Screw M4", LibraryCategory.Fasteners,
            ["screw", "schraube", "raendel", "knob", "fluegel", "hand", "m4", "fastener", "verbindungselement"],
            "Screw M4 with a round head of 16 mm that is turned by hand (a plain shaft, no thread).", new Vector3(16, 16, 17), ThumbScrew));

        // Brackets
        items.Add(Recipe("cable-ramp", "Cable Ramp", LibraryCategory.Brackets,
            ["ramp", "rampe", "cable", "kabel", "protector", "kabelbruecke", "wedge", "keil"], "Low ramp 80 mm long that lets a cable or a wheel run over it.",
            new Vector3(80, 20, 8), () => Make("wedge", "Cable Ramp", Vector3.Zero, ("width", 80), ("depth", 20), ("height", 8))));
        items.Add(Recipe("door-stop", "Door Stop", LibraryCategory.Brackets,
            ["door", "tuer", "tuerstopper", "stopper", "wedge", "keil", "doorstop"], "Wedge 70 x 35 x 20 mm with a 5 mm screw hole through the middle.",
            new Vector3(70, 35, 20), DoorStop));
        items.Add(Recipe("spool-holder-pin", "Spool Holder Pin", LibraryCategory.Brackets,
            ["spool", "spule", "rolle", "filament", "holder", "halter", "axle", "achse"], "Round base of 40 mm with a 10 mm pin, 52 mm high, that holds a spool.",
            new Vector3(40, 40, 52), SpoolHolderPin));
        items.Add(Recipe("peg-rail", "Peg Rail", LibraryCategory.Brackets,
            ["peg", "hook", "haken", "garderobe", "rail", "leiste", "hanger", "holder"], "Strip 80 x 10 mm with three upright pegs of 6 mm.",
            new Vector3(80, 10, 18), PegRail));
        items.Add(Recipe("hook-peg", "Hook Peg", LibraryCategory.Brackets,
            ["peg", "hook", "haken", "pin", "zapfen", "hanger", "holder"], "Peg with a round base and a pointed tip, 22 mm high.",
            new Vector3(16, 16, 22), HookPeg));

        // Enclosures
        items.Add(Recipe("dome-cover", "Dome Cover", LibraryCategory.Enclosures,
            ["dome", "kuppel", "cover", "haube", "cap", "round", "rund", "hemisphere", "halbkugel"], "Hollow dome 40 mm across and 20 mm high with 2 mm walls, open at the bottom.",
            new Vector3(40, 40, 20), DomeCover));
        items.Add(Recipe("conical-cup", "Conical Cup", LibraryCategory.Enclosures,
            ["cup", "becher", "kegel", "cone", "container", "behaelter", "round", "rund"], "Cup that gets wider towards the top, 40 mm across and 30 mm high, 2 mm walls.",
            new Vector3(40, 40, 30), ConicalCup));
        items.Add(Recipe("hex-box", "Hex Box", LibraryCategory.Enclosures,
            ["hexagon", "sechseck", "sechskant", "case", "housing", "gehaeuse", "container", "box"], "Open box in the shape of a hexagon, 50 mm across the corners and 20 mm high, 2 mm walls.",
            new Vector3(50, 43.3f, 20), HexBox));
        items.Add(Recipe("hex-lid", "Hex Lid", LibraryCategory.Enclosures,
            ["hexagon", "sechseck", "sechskant", "cover", "deckel", "cap", "lid"], "Lid with a lip that fits the hex box (0.2 mm play).",
            new Vector3(50, 43.3f, 5), HexLid));
        items.Add(Recipe("plant-pot", "Plant Pot", LibraryCategory.Enclosures,
            ["pot", "topf", "planter", "blumentopf", "cup", "becher", "container", "vase"], "Pot that gets wider towards the top, 60 mm across and 50 mm high, with a drain hole.",
            new Vector3(60, 60, 50), PlantPot));

        // Mechanics
        items.Add(Recipe("cone-pulley", "Cone Pulley", LibraryCategory.Mechanics,
            ["pulley", "riemenscheibe", "cone", "kegel", "wheel", "rad", "scheibe", "conical"], "Conical pulley, 40 mm at the bottom and 20 mm at the top, 15 mm high, with a 5 mm bore.",
            new Vector3(40, 40, 15), ConePulley));
        items.Add(Recipe("stepped-shaft", "Stepped Shaft", LibraryCategory.Mechanics,
            ["shaft", "welle", "axle", "achse", "step", "absatz", "stufe", "rod"], "Shaft with a step: 12 mm across for 10 mm, then 8 mm across up to 29 mm.",
            new Vector3(12, 12, 29), SteppedShaft));
        items.Add(Recipe("hex-shaft-8", "Hex Shaft 8", LibraryCategory.Mechanics,
            ["shaft", "welle", "hexagon", "sechskant", "axle", "achse", "rod", "stange"], "Shaft with a hexagon section, 8 mm across the flats, 30 mm long.",
            new Vector3(9.24f, 8, 30), () => Prism("Hex Shaft 8", 9.24, 30, 6)));
        items.Add(Recipe("hex-coupler", "Hex Coupler", LibraryCategory.Mechanics,
            ["coupling", "kupplung", "hexagon", "sechskant", "sleeve", "huelse", "connector", "verbinder"], "Hexagon sleeve 14 mm across the corners with a 5 mm bore, 18 mm long.",
            new Vector3(14, 12.12f, 18), HexCoupler));
        items.Add(Recipe("centering-cone", "Centering Cone", LibraryCategory.Mechanics,
            ["cone", "kegel", "center", "zentrierung", "zentrierkegel", "guide", "fuehrung", "adapter"], "Cone-shaped ring, 20 mm at the bottom and 8 mm at the top, 10 mm high, with a 5 mm bore.",
            new Vector3(20, 20, 10), CenteringCone));
        items.Add(Recipe("dome-knob", "Dome Knob", LibraryCategory.Mechanics,
            ["knob", "knopf", "dome", "kuppel", "handle", "griff", "button", "drehknopf"], "Round knob 30 mm across and 15 mm high with a blind hole of 5 mm for a shaft.",
            new Vector3(30, 30, 15), DomeKnob));
        items.Add(Recipe("roller", "Roller", LibraryCategory.Mechanics,
            ["roller", "rolle", "wheel", "rad", "cylinder", "walze", "bearing", "lager"], "Roller 20 mm across and 30 mm long with an 8.2 mm bore.",
            new Vector3(20, 20, 30), Roller));
        items.Add(Recipe("taper-pin", "Taper Pin 4-6x20", LibraryCategory.Mechanics,
            ["pin", "stift", "kegelstift", "taper", "cone", "kegel", "dowel", "passstift"], "Tapered pin, 6 mm at the bottom and 4 mm at the top, 20 mm long.",
            new Vector3(6, 6, 20), () => Frustum("Taper Pin 4-6x20", 6, 4, 20, Vector3.Zero)));
    }

    // ---- the recipes of the fourth group -----------------------------------------------------------

    private static PrimitiveNode Frustum(string name, double bottom, double top, double height, Vector3 position) =>
        Make("frustum", name, position, ("bottomDiameter", bottom), ("topDiameter", top), ("height", height), ("segments", 64));

    private static PrimitiveNode Cone(string name, double diameter, double height, Vector3 position) =>
        Make("cone", name, position, ("diameter", diameter), ("height", height), ("segments", 64));

    private static PrimitiveNode Hemisphere(string name, double diameter, Vector3 position) =>
        Make("hemisphere", name, position, ("diameter", diameter));

    private static Node CountersunkScrew(string name, double bottom, double top, double headHeight, double shaft, double length)
    {
        var tree = new Tree();
        var node = tree.Add(tree.Root, new MergeNode(name));
        tree.Add(node, Frustum("Head", bottom, top, headHeight, Vector3.Zero));
        // the shaft starts inside the head, so that the two overlap and merge into one body
        tree.Add(node, Cylinder("Shaft", shaft, length + 1, new Vector3(0, 0, (float)(headHeight - 1))));
        return tree.Take(node);
    }

    private static Node HeadedPin()
    {
        var tree = new Tree();
        var top = tree.Add(tree.Root, new MergeNode("Headed Pin 4x20"));
        tree.Add(top, Cylinder("Head", 7, 1.5, Vector3.Zero));
        tree.Add(top, Cylinder("Pin", 4, 20.5, new Vector3(0, 0, 1)));
        return tree.Take(top);
    }

    private static Node ThumbScrew()
    {
        var tree = new Tree();
        var top = tree.Add(tree.Root, new MergeNode("Thumb Screw M4"));
        tree.Add(top, Cylinder("Head", 16, 5, Vector3.Zero));
        tree.Add(top, Cylinder("Shaft", 4, 13, new Vector3(0, 0, 4)));
        return tree.Take(top);
    }

    private static Node DoorStop()
    {
        var tree = new Tree();
        var top = tree.Add(tree.Root, new BooleanNode("Door Stop", BooleanOperation.Difference));
        tree.Add(top, Make("wedge", "Wedge", Vector3.Zero, ("width", 70), ("depth", 35), ("height", 20)));
        tree.Add(top, Cylinder("Screw Hole", 5, 24, new Vector3(0, 0, -2)));
        return tree.Take(top);
    }

    private static Node SpoolHolderPin()
    {
        var tree = new Tree();
        var top = tree.Add(tree.Root, new MergeNode("Spool Holder Pin"));
        tree.Add(top, Cylinder("Base", 40, 3, Vector3.Zero));
        tree.Add(top, Cylinder("Pin", 10, 52, Vector3.Zero));
        return tree.Take(top);
    }

    private static Node PegRail()
    {
        var tree = new Tree();
        var top = tree.Add(tree.Root, new MergeNode("Peg Rail"));
        tree.Add(top, Box("Strip", 80, 10, 3, Vector3.Zero));
        var number = 1;
        foreach (var x in new[] { -25f, 0f, 25f })
            tree.Add(top, Cylinder($"Peg {number++}", 6, 16, new Vector3(x, 0, 2)));
        return tree.Take(top);
    }

    private static Node HookPeg()
    {
        var tree = new Tree();
        var top = tree.Add(tree.Root, new MergeNode("Hook Peg"));
        tree.Add(top, Cylinder("Base", 16, 2, Vector3.Zero));
        tree.Add(top, Cylinder("Stem", 6, 19, new Vector3(0, 0, 1)));
        tree.Add(top, Cone("Tip", 8, 4, new Vector3(0, 0, 18)));
        return tree.Take(top);
    }

    private static Node DomeCover()
    {
        var tree = new Tree();
        var top = tree.Add(tree.Root, new BooleanNode("Dome Cover", BooleanOperation.Difference));
        tree.Add(top, Hemisphere("Outside", 40, Vector3.Zero));
        tree.Add(top, Hemisphere("Inside", 36, Vector3.Zero));
        return tree.Take(top);
    }

    private static Node ConicalCup()
    {
        var tree = new Tree();
        var top = tree.Add(tree.Root, new BooleanNode("Conical Cup", BooleanOperation.Difference));
        tree.Add(top, Frustum("Outside", 30, 40, 30, Vector3.Zero));
        tree.Add(top, Frustum("Inside", 26, 36, 30, new Vector3(0, 0, 2)));
        return tree.Take(top);
    }

    private static Node HexBox()
    {
        var tree = new Tree();
        var top = tree.Add(tree.Root, new BooleanNode("Hex Box", BooleanOperation.Difference));
        tree.Add(top, Prism("Outside", 50, 20, 6));
        tree.Add(top, Make("prism", "Inside", new Vector3(0, 0, 2), ("diameter", 46), ("height", 20), ("sides", 6)));
        return tree.Take(top);
    }

    private static Node HexLid()
    {
        var tree = new Tree();
        var top = tree.Add(tree.Root, new MergeNode("Hex Lid"));
        tree.Add(top, Prism("Plate", 50, 2, 6));
        tree.Add(top, Make("prism", "Lip", new Vector3(0, 0, 2), ("diameter", 45.6), ("height", 3), ("sides", 6)));
        return tree.Take(top);
    }

    private static Node PlantPot()
    {
        var tree = new Tree();
        var top = tree.Add(tree.Root, new BooleanNode("Plant Pot", BooleanOperation.Difference));
        tree.Add(top, Frustum("Outside", 40, 60, 50, Vector3.Zero));
        tree.Add(top, Frustum("Inside", 36, 56, 50, new Vector3(0, 0, 3)));
        tree.Add(top, Cylinder("Drain Hole", 6, 6, new Vector3(0, 0, -1)));
        return tree.Take(top);
    }

    private static Node ConePulley()
    {
        var tree = new Tree();
        var top = tree.Add(tree.Root, new BooleanNode("Cone Pulley", BooleanOperation.Difference));
        tree.Add(top, Frustum("Body", 40, 20, 15, Vector3.Zero));
        tree.Add(top, Cylinder("Bore", 5, 17, new Vector3(0, 0, -1)));
        return tree.Take(top);
    }

    private static Node SteppedShaft()
    {
        var tree = new Tree();
        var top = tree.Add(tree.Root, new MergeNode("Stepped Shaft"));
        tree.Add(top, Cylinder("Collar", 12, 10, Vector3.Zero));
        tree.Add(top, Cylinder("Shaft", 8, 20, new Vector3(0, 0, 9)));
        return tree.Take(top);
    }

    private static Node HexCoupler()
    {
        var tree = new Tree();
        var top = tree.Add(tree.Root, new BooleanNode("Hex Coupler", BooleanOperation.Difference));
        tree.Add(top, Prism("Body", 14, 18, 6));
        tree.Add(top, Cylinder("Bore", 5, 20, new Vector3(0, 0, -1)));
        return tree.Take(top);
    }

    private static Node CenteringCone()
    {
        var tree = new Tree();
        var top = tree.Add(tree.Root, new BooleanNode("Centering Cone", BooleanOperation.Difference));
        tree.Add(top, Frustum("Body", 20, 8, 10, Vector3.Zero));
        tree.Add(top, Cylinder("Bore", 5, 12, new Vector3(0, 0, -1)));
        return tree.Take(top);
    }

    private static Node DomeKnob()
    {
        var tree = new Tree();
        var top = tree.Add(tree.Root, new BooleanNode("Dome Knob", BooleanOperation.Difference));
        tree.Add(top, Hemisphere("Dome", 30, Vector3.Zero));
        tree.Add(top, Cylinder("Shaft Hole", 5, 10, new Vector3(0, 0, -1)));
        return tree.Take(top);
    }

    private static Node Roller()
    {
        var tree = new Tree();
        var top = tree.Add(tree.Root, new BooleanNode("Roller", BooleanOperation.Difference));
        tree.Add(top, Cylinder("Body", 20, 30, Vector3.Zero));
        tree.Add(top, Cylinder("Bore", 8.2, 32, new Vector3(0, 0, -1)));
        return tree.Take(top);
    }
}
