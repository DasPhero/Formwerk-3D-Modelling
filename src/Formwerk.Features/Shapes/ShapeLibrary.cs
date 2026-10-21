namespace Formwerk.Features.Shapes;

/// <summary>Registry of the built-in parametric shapes, looked up by <c>PrimitiveNode.GeneratorId</c>.</summary>
public static class ShapeLibrary
{
    private static readonly Dictionary<string, IShapeGenerator> Generators =
        new IShapeGenerator[]
        {
            new BoxGenerator(), new CylinderGenerator(), new SphereGenerator(),
            new HemisphereGenerator(), new ConeGenerator(), new FrustumGenerator(), new PyramidGenerator(), new PrismGenerator(),
            new WedgeGenerator(), new TorusGenerator(), new TubeGenerator(), new CapsuleGenerator(),
        }
            .ToDictionary(g => g.Id, StringComparer.OrdinalIgnoreCase);

    /// <summary>Generators that are found by id but are not offered in the shape menu: the hole and the edge cutters are made by their own tools.</summary>
    private static readonly Dictionary<string, IShapeGenerator> Tools =
        new IShapeGenerator[] { new HoleGenerator(), new EdgeCutGenerator(), new EdgeRingGenerator() }.ToDictionary(g => g.Id, StringComparer.OrdinalIgnoreCase);

    /// <summary>The shapes of the shape menu.</summary>
    public static IReadOnlyCollection<IShapeGenerator> All => Generators.Values;

    /// <summary>The generator of the hole tool.</summary>
    public static HoleGenerator Hole { get; } = (HoleGenerator)Tools["hole"];

    public static IShapeGenerator? Find(string id) => Generators.GetValueOrDefault(id) ?? Tools.GetValueOrDefault(id);
}
