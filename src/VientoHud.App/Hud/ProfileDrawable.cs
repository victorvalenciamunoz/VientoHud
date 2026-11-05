using VientoHud.Core.Routing;

namespace VientoHud.App.Hud;

/// <summary>
/// Draws the altitude profile Core computed (<see cref="RouteProfile"/>): the ground under the line filled stretch by
/// stretch in the colour of its grade band, km ticks below, heights at the left, the GPX's warnings where they begin
/// and, under the ground, a band with the wind stretch by stretch in the arrows' colours. Only drawing: every number
/// comes from Core.
/// </summary>
public sealed class ProfileDrawable : IDrawable
{
    private const float Left = 44;
    private const float Right = 10;
    private const float Top = 18;
    private const float WindBand = 10;
    private const float Axis = 22;

    /// <summary>How many times steeper than reality the drawing may be, at most (route sites use 10-30).</summary>
    public const double MaxExaggeration = 30;

    public RouteProfile? Profile { get; set; }

    public IReadOnlyList<RideAlert> Alerts { get; set; } = [];

    public IReadOnlyList<WindStretch> Winds { get; set; } = [];

    public void Draw(ICanvas canvas, RectF rect)
    {
        if (Profile is not { Points.Count: > 1 } profile)
        {
            return;
        }
        var plotLeft = rect.Left + Left;
        var plotRight = rect.Right - Right;
        var plotTop = rect.Top + Top;
        var plotBottom = rect.Bottom - Axis - WindBand - 4;
        if (plotRight - plotLeft < 40 || plotBottom - plotTop < 40)
        {
            return;
        }

        // Heights: round the range out to a 50 m grid so the line never touches the edges, and never stretch it more
        // than MaxExaggeration times the horizontal scale: filling the height made 150 m of rolling road over 80 km
        // look like a mountain pass (the author, 2026-10-23).
        var lowest = Math.Floor(profile.MinElevationMeters / 50) * 50;
        var highest = Math.Ceiling(profile.MaxElevationMeters / 50) * 50;
        var leastRange = profile.LengthKm * 1000 * (plotBottom - plotTop) / (plotRight - plotLeft) / MaxExaggeration;
        highest = Math.Max(highest, lowest + Math.Ceiling(Math.Max(100, leastRange) / 50) * 50);
        float X(double km) => (float)(plotLeft + km / profile.LengthKm * (plotRight - plotLeft));
        float Y(double metres) => (float)(plotBottom - (metres - lowest) / (highest - lowest) * (plotBottom - plotTop));

        // The GPX's warnings as bands behind the ground, from where each begins to where it ends (a triangle alone at
        // km 0 hid against the axis, 2026-10-26): rain red, gusts yellow, the area warning grey to the end.
        foreach (var alert in Alerts)
        {
            var endKm = alert.Kind == RideAlertKind.AreaShowers ? profile.LengthKm : Math.Max(alert.EndKm, alert.StartKm + 0.3);
            canvas.FillColor = AlertColor(alert.Kind).WithAlpha(alert.Kind == RideAlertKind.AreaShowers ? 0.12f : 0.28f);
            canvas.FillRectangle(X(alert.StartKm), plotTop, Math.Max(2, X(endKm) - X(alert.StartKm)), plotBottom - plotTop);
        }

        // The ground, stretch by stretch in the colour of its band.
        foreach (var stretch in profile.Stretches)
        {
            var path = new PathF();
            path.MoveTo(X(stretch.StartKm), plotBottom);
            foreach (var p in profile.Points.Where(p => p.Km >= stretch.StartKm && p.Km <= stretch.EndKm))
            {
                path.LineTo(X(p.Km), Y(p.ElevationMeters));
            }
            path.LineTo(X(stretch.EndKm), Y(stretch.EndElevationMeters));
            path.LineTo(X(stretch.EndKm), plotBottom);
            path.Close();
            canvas.FillColor = Color.FromArgb(GradeBands.Color(stretch.Band));
            canvas.FillPath(path);
        }

        // The line on top, in the text colour.
        var line = new PathF();
        line.MoveTo(X(profile.Points[0].Km), Y(profile.Points[0].ElevationMeters));
        foreach (var p in profile.Points.Skip(1))
        {
            line.LineTo(X(p.Km), Y(p.ElevationMeters));
        }
        canvas.StrokeColor = HudTheme.Text;
        canvas.StrokeSize = 1.5f;
        canvas.DrawPath(line);

        // Heights at the left: the two ends of the range and the middle.
        canvas.FontColor = HudTheme.TextMuted;
        canvas.FontSize = 10;
        canvas.StrokeColor = HudTheme.TextDim;
        canvas.StrokeSize = 1;
        foreach (var metres in new[] { lowest, (lowest + highest) / 2, highest })
        {
            var y = Y(metres);
            canvas.DrawLine(plotLeft - 4, y, plotLeft, y);
            canvas.DrawString($"{metres:0}", rect.Left, y - 7, Left - 6, 14, HorizontalAlignment.Right, VerticalAlignment.Center);
        }
        canvas.DrawLine(plotLeft, plotTop, plotLeft, plotBottom);
        canvas.DrawLine(plotLeft, plotBottom, plotRight, plotBottom);

        // Km ticks: every 5 km under 60 km, every 10 beyond.
        var everyKm = profile.LengthKm > 60 ? 10 : 5;
        for (var km = 0; km <= profile.LengthKm; km += everyKm)
        {
            var x = X(km);
            canvas.DrawLine(x, plotBottom, x, plotBottom + 3);
            canvas.DrawString($"{km}", x - 15, rect.Bottom - Axis + WindBand + 4, 30, 12, HorizontalAlignment.Center, VerticalAlignment.Top);
        }

        // The wind stretch by stretch, just under the ground, in the colours of the arrows on the map.
        var windTop = plotBottom + 3;
        foreach (var wind in Winds)
        {
            canvas.FillColor = wind.Feel switch
            {
                WindFeel.Headwind => HudTheme.Alert,
                WindFeel.Tailwind => HudTheme.Good,
                WindFeel.FromLeft or WindFeel.FromRight => HudTheme.Marker,
                _ => HudTheme.TextDim,
            };
            canvas.FillRectangle(X(wind.StartKm), windTop, Math.Max(1, X(wind.EndKm) - X(wind.StartKm)), WindBand - 2);
        }

        // The GPX's warnings where they begin: a small mark above the ground, in the alert colour.
        foreach (var alert in Alerts)
        {
            // Clear of the axis, so that one at km 0 shows.
            var x = Math.Max(plotLeft + 7, X(alert.StartKm));
            var y = Y(profile.ElevationAt(alert.StartKm)) - 6;
            var mark = new PathF();
            mark.MoveTo(x, y);
            mark.LineTo(x - 7, y - 12);
            mark.LineTo(x + 7, y - 12);
            mark.Close();
            canvas.FillColor = AlertColor(alert.Kind);
            canvas.FillPath(mark);
            canvas.StrokeColor = HudTheme.Background;
            canvas.StrokeSize = 1.5f;
            canvas.DrawPath(mark);
        }
    }

    private static Color AlertColor(RideAlertKind kind) => kind switch
    {
        RideAlertKind.Gusts => HudTheme.Marker,
        RideAlertKind.Rain => HudTheme.Alert,
        _ => HudTheme.TextMuted,
    };
}
