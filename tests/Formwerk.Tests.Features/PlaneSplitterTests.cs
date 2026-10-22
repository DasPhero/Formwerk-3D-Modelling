using System.Numerics;
using Formwerk.Features.Evaluation;
using Formwerk.Features.Shapes;
using Formwerk.Geometry;
using Formwerk.Kernel;

namespace Formwerk.Tests.Features;

/// <summary>Splitting a mesh at a plane into two closed parts (F-SPL-01).</summary>
public class PlaneSplitterTests
{
    private static readonly ManifoldKernel Kernel = new();

    private static Dictionary<string, double> P(params (string, double)[] values) =>
        values.ToDictionary(v => v.Item1, v => v.Item2, StringComparer.OrdinalIgnoreCase);

    private static MeshData Box() => new BoxGenerator().Generate(P()); // 20 x 20 x 20, z from 0 to 20

    private static PlaneSplit Split(MeshData mesh, SectionPlane plane) =>
        PlaneSplitter.Split(mesh, plane, Kernel, CancellationToken.None)!;

    [Fact]
    public void A_box_split_across_the_middle_gives_two_closed_halves()
    {
        var split = Split(Box(), SectionPlane.Axis(2, 10));

        Assert.True(split.Behind.IsWatertight());
        Assert.True(split.Front.IsWatertight());
        Assert.Equal(4000, split.Behind.ComputeVolume(), 3);
        Assert.Equal(4000, split.Front.ComputeVolume(), 3);
        Assert.Equal(10, split.Behind.ComputeBounds().Max.Z, 4); // behind the plane = lower z for a normal pointing up
        Assert.Equal(10, split.Front.ComputeBounds().Min.Z, 4);
    }

    [Fact]
    public void Off_center_planes_split_the_volume_accordingly()
    {
        var split = Split(Box(), SectionPlane.Axis(0, 5)); // x runs from -10 to 10: 15 behind, 5 in front

        Assert.Equal(15 * 20 * 20, split.Behind.ComputeVolume(), 3);
        Assert.Equal(5 * 20 * 20, split.Front.ComputeVolume(), 3);
    }

    [Fact]
    public void A_slanted_plane_keeps_the_whole_volume()
    {
        var plane = SectionPlane.FromNormal(Vector3.Normalize(new Vector3(1, 1, 1)), new Vector3(0, 0, 8));

        var split = Split(Box(), plane);

        Assert.True(split.Behind.IsWatertight());
        Assert.True(split.Front.IsWatertight());
        Assert.Equal(8000, split.Behind.ComputeVolume() + split.Front.ComputeVolume(), 2);
        Assert.True(split.Behind.ComputeVolume() > 100);
        Assert.True(split.Front.ComputeVolume() > 100);
    }

    [Fact]
    public void The_flipped_plane_swaps_the_parts()
    {
        var plane = SectionPlane.Axis(0, 5);

        var split = Split(Box(), plane);
        var flipped = Split(Box(), plane.Flipped);

        Assert.Equal(split.Behind.ComputeVolume(), flipped.Front.ComputeVolume(), 3);
        Assert.Equal(split.Front.ComputeVolume(), flipped.Behind.ComputeVolume(), 3);
    }

    [Fact]
    public void A_body_with_a_hole_is_split_with_the_hole_in_both_parts()
    {
        var hole = new CylinderGenerator().Generate(P(("diameter", 10), ("height", 40), ("segments", 64))).Transformed(Matrix4x4.CreateTranslation(0, 0, -10));
        var body = Kernel.Boolean(Formwerk.Core.Model.BooleanOperation.Difference, [Box(), hole], CancellationToken.None);

        var split = Split(body, SectionPlane.Axis(2, 10));

        Assert.True(split.Behind.IsWatertight());
        Assert.True(split.Front.IsWatertight());
        Assert.Equal(body.ComputeVolume(), split.Behind.ComputeVolume() + split.Front.ComputeVolume(), 2);
        Assert.Equal(split.Behind.ComputeVolume(), split.Front.ComputeVolume(), 2);
    }

    [Fact]
    public void A_plane_that_misses_the_mesh_gives_nothing()
    {
        Assert.Null(PlaneSplitter.Split(Box(), SectionPlane.Axis(2, 25), Kernel, CancellationToken.None));
        Assert.Null(PlaneSplitter.Split(Box(), SectionPlane.Axis(2, -5), Kernel, CancellationToken.None));
        Assert.Null(PlaneSplitter.Split(Box(), SectionPlane.Axis(2, 20), Kernel, CancellationToken.None)); // only touches the top
    }

    [Fact]
    public void An_empty_mesh_gives_nothing()
    {
        Assert.Null(PlaneSplitter.Split(MeshData.Empty, SectionPlane.Axis(2, 0), Kernel, CancellationToken.None));
    }

    [Fact]
    public void A_cancelled_split_throws()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.Throws<OperationCanceledException>(() => PlaneSplitter.Split(Box(), SectionPlane.Axis(2, 10), Kernel, cts.Token));
    }
}
