namespace VientoHud.Core.Routing;

/// <summary>Whether a stretch of road suits a road bike under DECISIONS 003.</summary>
public enum RoadCategory
{
    /// <summary>A paved road or cycleway that is not forbidden.</summary>
    Road,

    /// <summary>
    /// A track, path, footway or road of unknown type that OSM declares paved. Allowed, as the strict profile does,
    /// but counted apart: these can be in poor condition (DECISIONS 014).
    /// </summary>
    PavedTrack,

    /// <summary>
    /// A trunk road ("trunk", "trunk_link"). Allowed but counted apart: in Spain many are quiet national roads, others are
    /// busy (DECISIONS 016).
    /// </summary>
    Trunk,

    /// <summary>Motorway, unpaved surface, unpaved or undeclared track, works, steps, ferry…</summary>
    Forbidden,

    /// <summary>The engine reported no road type, so the stretch cannot be checked.</summary>
    Unknown,
}

/// <summary>
/// Classifies stretches by their OpenStreetMap tags. Checks the routes independently of the routing profile: it follows
/// the same rules as road-strict.brf but with a wider list of unpaved surfaces, so it can catch gaps in the profile.
/// </summary>
public static class RoadRules
{
    private static readonly HashSet<string> ForbiddenHighways =
    [
        "motorway", "motorway_link", "construction", "proposed", "abandoned", "steps", "bridleway",
    ];

    private static readonly HashSet<string> TrunkHighways = ["trunk", "trunk_link"];

    // Ways that are unpaved unless tagged otherwise. "road" is a way whose type the mapper did not know.
    private static readonly HashSet<string> TrackLikeHighways = ["track", "path", "footway", "road"];

    private static readonly HashSet<string> UnpavedSurfaces =
    [
        "unpaved", "gravel", "fine_gravel", "dirt", "ground", "compacted", "grass", "sand", "mud", "earth",
        "pebblestone", "rock", "woodchips", "grass_paver", "shells", "snow", "ice", "salt",
    ];

    private static readonly HashSet<string> PavedSurfaces =
    [
        "paved", "asphalt", "chipseal", "concrete", "concrete:plates", "concrete:lanes", "paving_stones", "sett",
    ];

    // As in the profile: a track with no surface but a smooth surface quality counts as paved.
    private static readonly HashSet<string> SmoothSmoothness = ["excellent", "good"];

    public static RoadCategory Classify(RouteSegment segment)
    {
        ArgumentNullException.ThrowIfNull(segment);
        var tags = segment.WayTags;
        if (tags.GetValueOrDefault("route") == "ferry")
        {
            return RoadCategory.Forbidden;
        }
        if (segment.Highway is not { } highway)
        {
            return RoadCategory.Unknown;
        }
        if (ForbiddenHighways.Contains(highway) || segment.Surface is { } surface && UnpavedSurfaces.Contains(surface))
        {
            return RoadCategory.Forbidden;
        }
        if (TrackLikeHighways.Contains(highway))
        {
            var paved = segment.Surface is { } trackSurface
                ? PavedSurfaces.Contains(trackSurface)
                : tags.GetValueOrDefault("smoothness") is { } smoothness && SmoothSmoothness.Contains(smoothness);
            return paved ? RoadCategory.PavedTrack : RoadCategory.Forbidden;
        }
        return TrunkHighways.Contains(highway) ? RoadCategory.Trunk : RoadCategory.Road;
    }
}
