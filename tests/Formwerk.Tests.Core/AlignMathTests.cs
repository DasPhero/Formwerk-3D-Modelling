using Formwerk.Core.Model;

namespace Formwerk.Tests.Core;

/// <summary>Aligning and distributing along one axis (F-SNP-04): the shift of every object.</summary>
public class AlignMathTests
{
    // three objects along one axis: [0,10], [20,50] (30 wide), [70,80]
    private static readonly (float Min, float Max)[] Three = [(0, 10), (20, 50), (70, 80)];

    [Fact]
    public void Aligning_the_minimum_to_the_selection_box_moves_everything_to_the_left_edge()
    {
        var shifts = AlignMath.Align(Three, AlignMode.Min, reference: -1);

        Assert.Equal([0f, -20f, -70f], shifts);
    }

    [Fact]
    public void Aligning_the_maximum_to_the_selection_box_moves_everything_to_the_right_edge()
    {
        var shifts = AlignMath.Align(Three, AlignMode.Max, reference: -1);

        Assert.Equal([70f, 30f, 0f], shifts);
    }

    [Fact]
    public void Aligning_the_centers_to_the_selection_box_uses_the_middle_of_the_box()
    {
        var shifts = AlignMath.Align(Three, AlignMode.Center, reference: -1);

        // the box is 0..80, its middle is 40: the centers are 5, 35 and 75
        Assert.Equal([35f, 5f, -35f], shifts);
    }

    [Theory]
    [InlineData(AlignMode.Min, new[] { 20f, 0f, -50f })]
    [InlineData(AlignMode.Center, new[] { 30f, 0f, -40f })]
    [InlineData(AlignMode.Max, new[] { 40f, 0f, -30f })]
    public void Aligning_to_the_first_object_leaves_that_object_where_it_is(AlignMode mode, float[] expected)
    {
        var shifts = AlignMath.Align(Three, mode, reference: 1); // the middle one, [20, 50], is the reference

        Assert.Equal(expected, shifts);
    }

    [Fact]
    public void After_aligning_the_chosen_edges_coincide()
    {
        foreach (var mode in Enum.GetValues<AlignMode>())
        {
            var shifts = AlignMath.Align(Three, mode, reference: 2);
            var edges = Three.Select((e, i) => mode switch
            {
                AlignMode.Min => e.Min + shifts[i],
                AlignMode.Max => e.Max + shifts[i],
                _ => (e.Min + e.Max) / 2 + shifts[i],
            }).Distinct().ToList();

            Assert.Single(edges);
        }
    }

    [Fact]
    public void One_object_or_none_does_not_move()
    {
        Assert.Equal([0f], AlignMath.Align([(5, 9)], AlignMode.Max, -1));
        Assert.Empty(AlignMath.Align([], AlignMode.Min, -1));
    }

    [Fact]
    public void A_reference_that_does_not_exist_falls_back_to_the_selection_box()
    {
        Assert.Equal(AlignMath.Align(Three, AlignMode.Min, -1), AlignMath.Align(Three, AlignMode.Min, 9));
    }

    // ---- distribute ----

    [Fact]
    public void Three_objects_get_equal_gaps_and_the_outer_ones_stay()
    {
        // widths 10, 30, 10 between 0 and 80: 30 mm of gap for two gaps, 15 mm each
        var shifts = AlignMath.Distribute(Three);

        Assert.Equal(0f, shifts[0]);
        Assert.Equal(0f, shifts[2]);
        var boxes = Three.Select((e, i) => (e.Min + shifts[i], e.Max + shifts[i])).ToList();
        Assert.Equal(15f, boxes[1].Item1 - boxes[0].Item2, 3);
        Assert.Equal(15f, boxes[2].Item1 - boxes[1].Item2, 3);
    }

    [Fact]
    public void The_order_in_the_selection_does_not_matter_only_the_place_on_the_axis()
    {
        (float Min, float Max)[] shuffled = [(70, 80), (0, 10), (20, 50)];

        var shifts = AlignMath.Distribute(shuffled);

        Assert.Equal(0f, shifts[0]); // 70..80 is the last one on the axis
        Assert.Equal(0f, shifts[1]); // 0..10 is the first
        Assert.Equal(5f, shifts[2], 3); // the middle one (20..50) moves 5 mm to the right: gaps of 15 mm on both sides
    }

    [Fact]
    public void Four_objects_are_spread_evenly()
    {
        (float Min, float Max)[] four = [(0, 10), (12, 22), (14, 24), (90, 100)];

        var shifts = AlignMath.Distribute(four);

        var boxes = four.Select((e, i) => (Min: e.Min + shifts[i], Max: e.Max + shifts[i])).OrderBy(b => b.Min).ToList();
        var gaps = new[] { boxes[1].Min - boxes[0].Max, boxes[2].Min - boxes[1].Max, boxes[3].Min - boxes[2].Max };
        Assert.All(gaps, g => Assert.Equal(gaps[0], g, 3));
        Assert.Equal(0f, shifts[0]);
        Assert.Equal(0f, shifts[3]);
    }

    [Fact]
    public void Objects_that_overlap_too_much_get_a_negative_gap()
    {
        (float Min, float Max)[] crowded = [(0, 40), (5, 45), (50, 60)]; // 90 wide in total, but only 60 mm of room

        var shifts = AlignMath.Distribute(crowded);

        var boxes = crowded.Select((e, i) => (Min: e.Min + shifts[i], Max: e.Max + shifts[i])).OrderBy(b => b.Min).ToList();
        Assert.True(boxes[1].Min - boxes[0].Max < 0);
        Assert.Equal(boxes[1].Min - boxes[0].Max, boxes[2].Min - boxes[1].Max, 3);
    }

    [Fact]
    public void Fewer_than_three_objects_are_not_distributed()
    {
        Assert.Equal([0f, 0f], AlignMath.Distribute([(0, 10), (30, 40)]));
        Assert.Equal([0f], AlignMath.Distribute([(0, 10)]));
        Assert.Empty(AlignMath.Distribute([]));
    }

    [Fact]
    public void Objects_that_are_spread_evenly_already_do_not_move()
    {
        (float Min, float Max)[] even = [(0, 10), (20, 30), (40, 50)];

        Assert.All(AlignMath.Distribute(even), s => Assert.Equal(0f, s, 4));
    }
}
