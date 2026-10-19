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

    public static IReadOnlyCollection<IShapeGenerator> All => Generators.Values;

    public static IShapeGenerator? Find(string id) => Generators.GetValueOrDefault(id);
}
