using System.Text.RegularExpressions;

namespace Formwerk.Library;

/// <summary>
/// The sub folders of the built-in categories and the order of the library (F-LIB-02): a category (Fasteners) has sub folders (Nuts,
/// Bolts and Screws, ...), an element belongs to one of them, and everything is sorted by category, sub folder and name, with numbers
/// in natural order (M3 before M10). The folders of the user are not here; they live in the program settings.
/// </summary>
public static partial class LibraryFolders
{
    private static readonly Dictionary<LibraryCategory, string[]> Folders = new()
    {
        [LibraryCategory.Fasteners] = ["Nuts", "Bolts and Screws", "Washers", "Spacers and Posts", "Pins and Rods"],
        [LibraryCategory.Brackets] = ["Plates and Strips", "Holders", "Hooks and Pegs", "Wedges"],
        [LibraryCategory.Enclosures] = ["Boxes", "Lids and Covers", "Plugs and Feet"],
        [LibraryCategory.Mechanics] = ["Wheels and Rollers", "Shafts and Pins", "Bushings and Bearings", "Knobs and Arms"],
    };

    /// <summary>The names of the sub folders of a category in the order they are shown; empty for a category without any.</summary>
    public static IReadOnlyList<string> SubFolders(LibraryCategory category) => Folders.GetValueOrDefault(category) ?? [];

    /// <summary>The sub folder of an element, or an empty text when its category has none (basic shapes, templates).</summary>
    public static string SubFolderOf(LibraryItem item) => item.Category switch
    {
        LibraryCategory.Fasteners => item.Id switch
        {
            var id when id.StartsWith("hex-nut") || id.StartsWith("square-nut") => "Nuts",
            var id when id.StartsWith("hex-bolt") || id.StartsWith("cap-screw") || id.StartsWith("countersunk") || id.StartsWith("thumb-screw") => "Bolts and Screws",
            var id when id.StartsWith("washer") || id.StartsWith("fender-washer") => "Washers",
            var id when id.StartsWith("standoff") || id.StartsWith("hex-standoff") || id.StartsWith("spacer-sleeve") || id.StartsWith("insert-boss") || id.StartsWith("screw-boss") => "Spacers and Posts",
            _ => "Pins and Rods",
        },
        LibraryCategory.Brackets => item.Id switch
        {
            "l-bracket" or "wall-plate" or "mounting-plate" or "mounting-strip" or "hanging-tab" or "t-bracket" or "flat-l-brace" => "Plates and Strips",
            "wall-hook" or "peg-rail" or "hook-peg" => "Hooks and Pegs",
            "cable-ramp" or "door-stop" => "Wedges",
            _ => "Holders",
        },
        LibraryCategory.Enclosures => item.Id switch
        {
            "lid" or "round-lid" or "hex-lid" or "flat-cover" or "vent-plate" => "Lids and Covers",
            "rect-plug" or "round-plug" or "cable-grommet" or "round-foot" => "Plugs and Feet",
            _ => "Boxes", // open box, box with lid, round box, divided tray, hex box, battery holder, plant pot, cup, dome
        },
        LibraryCategory.Mechanics => item.Id switch
        {
            "hub-disc" or "pulley" or "wheel-40" or "cone-pulley" or "roller" => "Wheels and Rollers",
            "axle-pin" or "dowel-pin" or "taper-pin" or "stepped-shaft" or "hex-shaft-8" => "Shafts and Pins",
            "knob" or "dome-knob" or "link-arm-40" => "Knobs and Arms",
            _ => "Bushings and Bearings",
        },
        _ => "",
    };

    /// <summary>The items in the order of the library: by category, then sub folder, then name (numbers compared as numbers).</summary>
    public static IReadOnlyList<LibraryItem> Order(IEnumerable<LibraryItem> items) =>
    [
        .. items
            .Select((item, position) => (Item: item, Position: position, Folder: SubFolderIndex(item)))
            .OrderBy(x => (int)x.Item.Category)
            .ThenBy(x => x.Folder)
            .ThenBy(x => x.Item.Name, NaturalComparer.Instance)
            .ThenBy(x => x.Position)
            .Select(x => x.Item),
    ];

    private static int SubFolderIndex(LibraryItem item)
    {
        var index = SubFolders(item.Category).ToList().IndexOf(SubFolderOf(item));
        return index < 0 ? int.MaxValue : index;
    }

    /// <summary>Compares names so that "M3" comes before "M10" and "Nut 2" before "Nut 10"; case does not matter.</summary>
    public sealed class NaturalComparer : IComparer<string>
    {
        public static NaturalComparer Instance { get; } = new();

        public int Compare(string? x, string? y)
        {
            if (ReferenceEquals(x, y)) return 0;
            if (x is null) return -1;
            if (y is null) return 1;

            var a = Pieces().Split(x);
            var b = Pieces().Split(y);
            for (var i = 0; i < Math.Min(a.Length, b.Length); i++)
            {
                int result;
                if (double.TryParse(a[i], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var na)
                    && double.TryParse(b[i], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var nb))
                    result = na.CompareTo(nb);
                else
                    result = string.Compare(a[i], b[i], StringComparison.OrdinalIgnoreCase);
                if (result != 0) return result;
            }

            return a.Length.CompareTo(b.Length);
        }
    }

    [GeneratedRegex(@"(?<=\d)(?=\D)|(?<=\D)(?=\d)")]
    private static partial Regex Pieces();
}
