namespace VientoHud.Core.Routing;

/// <summary>Settings for <see cref="RouteUrban"/>. Provisional until the author's review (phase 6, step 6c).</summary>
public sealed record UrbanOptions
{
    /// <summary>A km with at least this many urban nodes (crossings, traffic signals, stops, give ways) is in town.</summary>
    public int MinUrbanNodesPerKm { get; init; } = 2;

    /// <summary>A km with at least this share of its length on streets (residential, service…) is in town.</summary>
    public double MinStreetShare { get; init; } = 0.5;

    /// <summary>Around the start: being this close to it in the middle of the ride is coming back through town.</summary>
    public double HomeRadiusKm { get; init; } = 3;

    /// <summary>Km at the start and at the end where being near home is just leaving and arriving.</summary>
    public double HomeEdgeKm { get; init; } = 5;
}

/// <summary>How much of a route is in town.</summary>
/// <param name="UrbanKm">Km in town (whole km of the route, the last one shorter).</param>
/// <param name="UrbanStretches">Consecutive km in town, as (start, end) from the start of the route.</param>
/// <param name="BackHomeKm">
/// Km of the route within <see cref="UrbanOptions.HomeRadiusKm"/> of the start outside its first and last
/// <see cref="UrbanOptions.HomeEdgeKm"/>: a loop that crosses the home town again in the middle.
/// </param>
public sealed record UrbanMeasure(
    double DistanceKm,
    double UrbanKm,
    IReadOnlyList<(double StartKm, double EndKm)> UrbanStretches,
    double BackHomeKm)
{
    public double UrbanFraction => DistanceKm > 0 ? UrbanKm / DistanceKm : 0;
}

/// <summary>
/// Measures how much of a route goes through towns (phase 6, step 6c; DECISIONS 042), from what the route engine
/// reports: the kind of way of each stretch and the tags of the nodes on it. The map data at hand have no "town around"
/// estimate, so a km counts as in town when it has several crossings or traffic signals, or runs mostly on streets.
/// </summary>
public static class RouteUrban
{
    private static readonly HashSet<string> UrbanNodes = ["crossing", "traffic_signals", "stop", "give_way"];

    private static readonly HashSet<string> Streets = ["residential", "living_street", "service", "pedestrian"];

    public static UrbanMeasure Measure(Route route, UrbanOptions? options = null)
    {
        options ??= new UrbanOptions();
        var distance = route.DistanceKm;
        var bins = Math.Max(1, (int)Math.Ceiling(distance - 1e-9));

        var nodes = new int[bins];
        foreach (var node in route.Nodes)
        {
            if (IsUrban(node.Tags))
            {
                nodes[Math.Clamp((int)Math.Floor(node.AtKm), 0, bins - 1)]++;
            }
        }

        var streets = new double[bins];
        foreach (var segment in route.Segments.Where(s => s.Highway is { } h && Streets.Contains(h)))
        {
            var from = segment.StartKm;
            var to = segment.StartKm + segment.LengthKm;
            for (var bin = Math.Max(0, (int)Math.Floor(from)); bin < bins && bin < to; bin++)
            {
                streets[bin] += Math.Max(0, Math.Min(to, bin + 1) - Math.Max(from, bin));
            }
        }

        var stretches = new List<(double StartKm, double EndKm)>();
        var urbanKm = 0.0;
        for (var bin = 0; bin < bins; bin++)
        {
            var length = Math.Min(distance, bin + 1) - bin;
            var urban = nodes[bin] >= options.MinUrbanNodesPerKm
                        || (length > 0 && streets[bin] / length >= options.MinStreetShare);
            if (!urban)
            {
                continue;
            }
            urbanKm += length;
            if (stretches.Count > 0 && Math.Abs(stretches[^1].EndKm - bin) < 1e-9)
            {
                stretches[^1] = (stretches[^1].StartKm, bin + length);
            }
            else
            {
                stretches.Add((bin, bin + length));
            }
        }

        return new UrbanMeasure(distance, urbanKm, stretches, BackHomeKm(route, options));
    }

    private static bool IsUrban(IReadOnlyDictionary<string, string> tags) =>
        (tags.TryGetValue("highway", out var highway) && UrbanNodes.Contains(highway)) || tags.ContainsKey("crossing");

    // Km along the track, point by point, within the home radius between the edges.
    private static double BackHomeKm(Route route, UrbanOptions options)
    {
        var home = route.Points[0].Position;
        var along = 0.0;
        var total = route.Points.Zip(route.Points.Skip(1), (a, b) => a.Position.DistanceKmTo(b.Position)).Sum();
        var back = 0.0;
        for (var i = 1; i < route.Points.Count; i++)
        {
            var step = route.Points[i - 1].Position.DistanceKmTo(route.Points[i].Position);
            var middle = along + step / 2;
            along += step;
            if (middle > options.HomeEdgeKm && middle < total - options.HomeEdgeKm
                && route.Points[i].Position.DistanceKmTo(home) <= options.HomeRadiusKm)
            {
                back += step;
            }
        }
        return back;
    }
}
