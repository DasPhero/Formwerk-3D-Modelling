using System.Numerics;
using Formwerk.Core.Model;

namespace Formwerk.Library;

public static partial class LibraryCatalog
{
    /// <summary>
    /// Ready-made elements with parameters (F-PAR-05) that were plain recipes before: the defaults give the same shape as then. An
    /// object made from one remembers its values, so they can be changed in the properties later.
    /// </summary>
    private static void AddParametricElements(List<LibraryItem> items)
    {
        ElementParameter[] openBox =
        [
            new("outerWidth", "Width", 60, 10, 1000), new("outerDepth", "Depth", 40, 10, 1000), new("height", "Height", 25, 4, 1000), new("wall", "Wall", 2, 0.4, 20),
        ];
        items.Add(Parametric("open-box", "Open Box", LibraryCategory.Enclosures,
            ["case", "housing", "gehaeuse", "schale", "container", "tray", "variant", "variante"], "Open box with walls and floor of the same thickness; asks for the outer size and the wall.",
            openBox, OpenBoxFrom, v => new Vector3((float)v.N("outerWidth"), (float)v.N("outerDepth"), (float)v.N("height"))));

        ElementParameter[] roundBox =
        [
            new("diameter", "Diameter", 40, 10, 1000), new("height", "Height", 20, 4, 1000), new("wall", "Wall", 2, 0.4, 20),
        ];
        items.Add(Parametric("round-box", "Round Box", LibraryCategory.Enclosures,
            ["case", "housing", "gehaeuse", "dose", "container", "tray", "round", "rund", "variant", "variante"], "Open round box; asks for the diameter, the height and the wall.",
            roundBox, RoundBoxFrom, v => new Vector3((float)v.N("diameter"), (float)v.N("diameter"), (float)v.N("height"))));

        ElementParameter[] lBracket =
        [
            new("width", "Width", 40, 10, 1000), new("depth", "Foot depth", 25, 10, 1000), new("height", "Height", 30, 6, 1000),
            new("thickness", "Thickness", 3, 0.8, 30), new("holeDiameter", "Hole diameter", 4, 1, 40),
        ];
        items.Add(Parametric("l-bracket", "L-Bracket", LibraryCategory.Brackets,
            ["angle", "winkel", "halterung", "corner", "holder", "variant", "variante"], "Angle bracket with two screw holes in its foot; asks for the size, the thickness and the holes.",
            lBracket, LBracketFrom, v => new Vector3((float)v.N("width"), (float)v.N("depth"), (float)v.N("height"))));

        ElementParameter[] uBracket =
        [
            new("width", "Width", 30, 8, 1000), new("depth", "Depth", 20, 4, 1000), new("height", "Height", 20, 4, 1000), new("wall", "Wall", 3, 0.8, 30),
        ];
        items.Add(Parametric("u-bracket", "U-Bracket", LibraryCategory.Brackets,
            ["u", "channel", "halterung", "holder", "clamp", "buegel", "winkel", "variant", "variante"], "U-shaped holder, open at both ends; asks for the size and the wall.",
            uBracket, UBracketFrom, v => new Vector3((float)v.N("width"), (float)v.N("depth"), (float)v.N("height"))));

        ElementParameter[] clip =
        [
            new("cable", "Cable diameter", 8, 2, 200), new("wall", "Ring wall", 2, 0.8, 50), new("height", "Height", 8, 3, 200),
            new("base", "Base thickness", 2, 1, 20), new("screw", "Screw hole", 3.4, 1, 30), new("gap", "Gap to the screws", 1.5, 0, 50),
        ];
        items.Add(Parametric("cable-clip", "Cable Clip", LibraryCategory.Brackets,
            ["cable", "kabel", "clip", "ring", "halterung", "holder", "guide", "variant", "variante"],
            "Ring for a cable on a base plate with two screw holes; the screw holes keep their size and move out when the cable gets thicker.",
            clip, CableClipFrom, v => new Vector3((float)(v.N("cable") + 2 * v.N("wall") + 2 * v.N("gap") + 2 * v.N("screw") + 4),
                (float)Math.Max(v.N("cable") + 2 * v.N("wall"), v.N("screw") + 4), (float)Math.Max(v.N("height"), v.N("base")))));
    }

    private static Node OpenBoxFrom(ElementValues v)
    {
        var wall = (float)v.N("wall");
        var tree = new Tree();
        var top = tree.Add(tree.Root, new BooleanNode("Open Box", BooleanOperation.Difference));
        tree.Add(top, Shape("box", "Outside", Vector3.Zero, ("width", v.Calc("outerWidth")), ("depth", v.Calc("outerDepth")), ("height", v.Calc("height"))));
        tree.Add(top, Shape("box", "Inside", new Vector3(0, 0, wall), ("width", v.Calc("outerWidth - 2*wall")), ("depth", v.Calc("outerDepth - 2*wall")), ("height", v.Calc("height"))));
        return tree.Take(top);
    }

    private static Node RoundBoxFrom(ElementValues v)
    {
        var tree = new Tree();
        var top = tree.Add(tree.Root, new BooleanNode("Round Box", BooleanOperation.Difference));
        tree.Add(top, Shape("cylinder", "Outside", Vector3.Zero, ("diameter", v.Calc("diameter")), ("height", v.Calc("height")), ("segments", "64")));
        tree.Add(top, Shape("cylinder", "Inside", new Vector3(0, 0, (float)v.N("wall")), ("diameter", v.Calc("diameter - 2*wall")), ("height", v.Calc("height")), ("segments", "64")));
        return tree.Take(top);
    }

    private static Node LBracketFrom(ElementValues v)
    {
        var thickness = v.N("thickness");
        var tree = new Tree();
        var top = tree.Add(tree.Root, new BooleanNode("L-Bracket", BooleanOperation.Difference));
        var body = tree.Add(top, new MergeNode("Body"));
        tree.Add(body, Shape("box", "Foot", Vector3.Zero, ("width", v.Calc("width")), ("depth", v.Calc("depth")), ("height", v.Calc("thickness"))));
        tree.Add(body, Shape("box", "Wall", new Vector3(0, (float)(-(v.N("depth") - thickness) / 2), 0), ("width", v.Calc("width")), ("depth", v.Calc("thickness")), ("height", v.Calc("height"))));
        var x = (float)(0.3 * v.N("width"));
        var y = (float)(thickness / 2); // the middle of the free part of the foot
        tree.Add(top, Shape("cylinder", "Hole 1", new Vector3(-x, y, -1), ("diameter", v.Calc("holeDiameter")), ("height", v.Calc("thickness + 2")), ("segments", "64")));
        tree.Add(top, Shape("cylinder", "Hole 2", new Vector3(x, y, -1), ("diameter", v.Calc("holeDiameter")), ("height", v.Calc("thickness + 2")), ("segments", "64")));
        return tree.Take(top);
    }

    private static Node UBracketFrom(ElementValues v)
    {
        var tree = new Tree();
        var top = tree.Add(tree.Root, new BooleanNode("U-Bracket", BooleanOperation.Difference));
        tree.Add(top, Shape("box", "Outside", Vector3.Zero, ("width", v.Calc("width")), ("depth", v.Calc("depth")), ("height", v.Calc("height"))));
        tree.Add(top, Shape("box", "Inside", new Vector3(0, 0, (float)v.N("wall")), ("width", v.Calc("width - 2*wall")), ("depth", v.Calc("depth + 2")), ("height", v.Calc("height"))));
        return tree.Take(top);
    }

    private static Node CableClipFrom(ElementValues v)
    {
        var ring = v.N("cable") + 2 * v.N("wall");
        var screwX = (float)(ring / 2 + v.N("gap") + v.N("screw") / 2); // the screw holes keep their size and sit just outside the ring
        var tree = new Tree();
        var top = tree.Add(tree.Root, new BooleanNode("Cable Clip", BooleanOperation.Difference));
        var body = tree.Add(top, new MergeNode("Body"));
        tree.Add(body, Shape("box", "Base", Vector3.Zero,
            ("width", v.Calc("cable + 2*wall + 2*gap + 2*screw + 4")), ("depth", v.Calc("max(cable + 2*wall, screw + 4)")), ("height", v.Calc("base"))));
        tree.Add(body, Shape("cylinder", "Ring", Vector3.Zero, ("diameter", v.Calc("cable + 2*wall")), ("height", v.Calc("max(height, base)")), ("segments", "64")));
        tree.Add(top, Shape("cylinder", "Cable Hole", new Vector3(0, 0, -1), ("diameter", v.Calc("cable")), ("height", v.Calc("max(height, base) + 2")), ("segments", "64")));
        tree.Add(top, Shape("cylinder", "Screw Hole 1", new Vector3(-screwX, 0, -1), ("diameter", v.Calc("screw")), ("height", v.Calc("base + 2")), ("segments", "64")));
        tree.Add(top, Shape("cylinder", "Screw Hole 2", new Vector3(screwX, 0, -1), ("diameter", v.Calc("screw")), ("height", v.Calc("base + 2")), ("segments", "64")));
        return tree.Take(top);
    }
}
