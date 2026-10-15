using VientoHud.Core.Routing;

namespace VientoHud.Core.Tests.Routing;

public class RoadRulesTests
{
    private static RouteSegment Segment(string wayTags) =>
        new(0, 1, wayTags.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(t => t.Split('='))
            .ToDictionary(kv => kv[0], kv => kv[1]));

    [Theory]
    [InlineData("highway=tertiary surface=asphalt")]
    [InlineData("highway=tertiary")] // Roads with no surface tag are paved almost always.
    [InlineData("highway=secondary surface=concrete reversedirection=yes")]
    [InlineData("highway=cycleway")]
    [InlineData("highway=residential surface=paving_stones")]
    public void Paved_roads_and_cycleways_are_roads(string tags) =>
        Assert.Equal(RoadCategory.Road, RoadRules.Classify(Segment(tags)));

    [Theory]
    [InlineData("highway=motorway")]
    [InlineData("highway=motorway_link")]
    [InlineData("highway=trunk surface=asphalt")]
    [InlineData("highway=trunk_link")]
    [InlineData("highway=construction")]
    [InlineData("highway=steps")]
    [InlineData("highway=bridleway surface=asphalt")]
    [InlineData("route=ferry")]
    public void Motorways_trunk_roads_works_steps_and_ferries_are_forbidden(string tags) =>
        Assert.Equal(RoadCategory.Forbidden, RoadRules.Classify(Segment(tags)));

    [Theory]
    [InlineData("highway=unclassified surface=gravel")]
    [InlineData("highway=tertiary surface=dirt")]
    [InlineData("highway=residential surface=earth")] // Not in the profile's list: the check is stricter.
    [InlineData("highway=service surface=compacted")]
    public void Any_road_with_an_unpaved_surface_is_forbidden(string tags) =>
        Assert.Equal(RoadCategory.Forbidden, RoadRules.Classify(Segment(tags)));

    [Theory]
    [InlineData("highway=track")]
    [InlineData("highway=track surface=ground")]
    [InlineData("highway=track surface=cobblestone")]
    [InlineData("highway=path")]
    [InlineData("highway=footway smoothness=intermediate")]
    [InlineData("highway=road")]
    public void Tracks_and_paths_not_declared_paved_are_forbidden(string tags) =>
        Assert.Equal(RoadCategory.Forbidden, RoadRules.Classify(Segment(tags)));

    [Theory]
    [InlineData("highway=track surface=asphalt")]
    [InlineData("highway=track surface=concrete:plates")]
    [InlineData("highway=track smoothness=good")]
    [InlineData("highway=path surface=paved")]
    [InlineData("highway=footway surface=paving_stones")]
    public void Tracks_and_paths_declared_paved_are_counted_apart(string tags) =>
        Assert.Equal(RoadCategory.PavedTrack, RoadRules.Classify(Segment(tags)));

    [Fact]
    public void A_stretch_without_a_road_type_cannot_be_checked() =>
        Assert.Equal(RoadCategory.Unknown, RoadRules.Classify(Segment("surface=asphalt")));
}
