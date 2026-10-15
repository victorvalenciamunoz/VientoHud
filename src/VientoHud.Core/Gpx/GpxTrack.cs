namespace VientoHud.Core.Gpx;

/// <summary>One point of a GPX track.</summary>
/// <param name="Position">WGS84 position.</param>
/// <param name="ElevationMeters">Elevation above sea level in metres, if known.</param>
public readonly record struct GpxTrackPoint(Coordinate Position, double? ElevationMeters = null)
{
    public double? ElevationMeters { get; } = ElevationMeters is null || double.IsFinite(ElevationMeters.Value)
        ? ElevationMeters
        : throw new ArgumentOutOfRangeException(nameof(ElevationMeters), ElevationMeters, "Elevation must be finite.");
}

/// <summary>Who holds the rights to the data in a GPX file and under which licence (GPX <c>copyright</c> element).</summary>
/// <param name="Author">Rights holder, e.g. "OpenStreetMap contributors".</param>
/// <param name="License">Link to the licence text.</param>
public sealed record GpxCopyright(string Author, Uri? License = null)
{
    public string Author { get; } = !string.IsNullOrWhiteSpace(Author)
        ? Author
        : throw new ArgumentException("Author is required.", nameof(Author));

    /// <summary>OpenStreetMap data (ODbL): attribution required in anything produced from it.</summary>
    public static GpxCopyright OpenStreetMap { get; } =
        new("OpenStreetMap contributors", new Uri("https://opendatacommons.org/licenses/odbl/"));
}

/// <summary>A single-segment GPX track: the exact line a bike GPS follows as a course.</summary>
/// <param name="Name">Track name shown by the GPS.</param>
/// <param name="Points">Points in riding order. At least two.</param>
/// <param name="Time">When the file was created, in UTC.</param>
/// <param name="Description">Longer text (summary of the ride), if any.</param>
/// <param name="Copyright">Data rights and licence, if any.</param>
public sealed record GpxTrack(
    string Name,
    IReadOnlyList<GpxTrackPoint> Points,
    DateTimeOffset Time,
    string? Description = null,
    GpxCopyright? Copyright = null)
{
    public string Name { get; } = !string.IsNullOrWhiteSpace(Name)
        ? Name
        : throw new ArgumentException("Name is required.", nameof(Name));

    public IReadOnlyList<GpxTrackPoint> Points { get; } = Points is { Count: >= 2 }
        ? Points
        : throw new ArgumentException("A track needs at least two points.", nameof(Points));

    public DateTimeOffset Time { get; } = Time.Offset == TimeSpan.Zero
        ? Time
        : throw new ArgumentException("Time must be in UTC.", nameof(Time));

    /// <summary>The track of a route, with elevations, under OpenStreetMap's licence (routes are built from its data).</summary>
    public static GpxTrack FromRoute(Routing.Route route, string name, DateTimeOffset time, string? description = null)
    {
        ArgumentNullException.ThrowIfNull(route);
        return new GpxTrack(
            name,
            route.Points.Select(p => new GpxTrackPoint(p.Position, p.ElevationMeters)).ToList(),
            time,
            description,
            GpxCopyright.OpenStreetMap);
    }
}
