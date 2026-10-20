using Mapsui;
using Mapsui.Layers;
using Mapsui.Manipulations;
using Mapsui.Nts;
using Mapsui.Projections;
using Mapsui.Styles;
using NetTopologySuite.Geometries;
using VientoHud.App.Hud;
using VientoHud.App.Maps;
using VientoHud.Core.Location;
using VientoHud.Core.Routing;
using VientoHud.Core.Text;
using VientoHud.Core.Wind;
using GeoCoordinate = VientoHud.Core.Coordinate;

namespace VientoHud.App;

/// <summary>
/// The one screen: where the rider is, which way to head (phase 7d) and the ride traced by touching the map (7e):
/// one point is an out and back, two or more a loop. Layout only: the position comes from the device and every
/// route, judgement and sentence from Core. Core's work runs on a background thread (<see cref="Task.Run(Func{Task})"/>):
/// reading radar frames and ranking a ride take seconds on a phone, and on the UI thread that froze the map until
/// Android killed the app (ANR, 2026-10-19).
/// </summary>
public partial class MapPage : ContentPage
{
    /// <summary>How long the rider wants to ride and at what average (phase 7g); remembered between sessions.</summary>
    private static readonly TimeSpan[] DurationChoices =
        [TimeSpan.FromHours(1), TimeSpan.FromMinutes(90), TimeSpan.FromHours(2), TimeSpan.FromMinutes(150), TimeSpan.FromHours(3), TimeSpan.FromHours(4), TimeSpan.FromHours(5)];

    private static readonly double[] SpeedChoices = [18, 20, 22, 25, 28, 30, 33];

    private const string DurationKey = "ride_duration_minutes";
    private const string SpeedKey = "ride_speed_kmh";

    private TimeSpan _duration = TimeSpan.FromMinutes(Preferences.Default.Get(DurationKey, 120));
    private double _speedKmh = Preferences.Default.Get(SpeedKey, RideDistance.DefaultAverageSpeedKmh);
    private TimeSpan _pendingDuration;
    private double _pendingSpeed;

    private static readonly TimeZoneInfo Zone = TimeZoneInfo.Local;

    private readonly TileSources _tiles;
    private readonly ILocationProvider _location;
    private readonly DirectionAdvisor _direction;
    private readonly TracedRideAdvisor _rides;
    private readonly MemoryLayer _fanLayer = new("Fan") { Style = FanStyle() };
    private readonly MemoryLayer _routeLayer = new("Route") { Style = RouteStyle() };
    private readonly MemoryLayer _viaLayer = new("Via") { Style = ViaStyle() };
    private readonly MemoryLayer _reachedLayer = new("Reached") { Style = ReachedStyle() };
    private readonly MemoryLayer _snapLayer = new("Snap") { Style = SnapStyle() };
    // The wind: the mean one at home, and along the ride one layer per feel (the color is the layer's).
    // Every arrow has a white halo underneath, so it stands out on the pale map whatever its color.
    private readonly MemoryLayer _windHaloLayer = new("WindHalo") { Style = ArrowStyle(HudTheme.Text, 7) };
    private readonly MemoryLayer _windLabelLayer = new("WindLabels") { Style = KmLabelStyle() };
    private readonly Dictionary<WindFeel, MemoryLayer> _windLayers = new()
    {
        [WindFeel.Headwind] = new MemoryLayer("WindHead") { Style = ArrowStyle(HudTheme.Alert, 3.5) },
        [WindFeel.Tailwind] = new MemoryLayer("WindTail") { Style = ArrowStyle(HudTheme.Good, 3.5) },
        [WindFeel.FromLeft] = new MemoryLayer("WindLeft") { Style = ArrowStyle(HudTheme.Marker, 3.5) },
        [WindFeel.FromRight] = new MemoryLayer("WindRight") { Style = ArrowStyle(HudTheme.Marker, 3.5) },
    };
    private readonly MemoryLayer _homeLayer = new("Home") { Style = HomeStyle() };
    private readonly List<GeoCoordinate> _via = [];
    /// <summary>VUELTA (points accumulate) or IDA Y VUELTA (one point, replaced by the next).</summary>
    private bool _loopMode;
    /// <summary>The last direction advice: PROPONER heads that way.</summary>
    private DirectionAdvice? _lastDirection;
    private GeoCoordinate? _home;
    private bool _started;
    /// <summary>When to leave, as a time of day today; null for now.</summary>
    private TimeOnly? _departureAt;
    /// <summary>The choice being made in the departure panel, applied when it closes.</summary>
    private TimeOnly? _pendingAt;

    /// <summary>The departure in UTC: now, or the time chosen today (now again if it has already passed).</summary>
    private DateTimeOffset Departure
    {
        get
        {
            var now = DateTimeOffset.UtcNow;
            if (_departureAt is not { } at)
            {
                return now;
            }
            var next = DepartureTime.Next(at, now, Zone);
            return next - now > TimeSpan.FromHours(23) ? now : next;
        }
    }
    private CancellationTokenSource? _cancel;

    public MapPage(TileSources tiles, ILocationProvider location, DirectionAdvisor direction, TracedRideAdvisor rides)
    {
        InitializeComponent();
        _tiles = tiles;
        _location = location;
        _direction = direction;
        _rides = rides;
        MapName.Text = tiles.Name;
        Attribution.Text = tiles.Attribution;
        MapView.Map.Layers.Add(tiles.CreateLayer());
        MapView.Map.Layers.Add(_fanLayer);
        MapView.Map.Layers.Add(_routeLayer);
        MapView.Map.Layers.Add(_snapLayer);
        MapView.Map.Layers.Add(_windHaloLayer);
        foreach (var layer in _windLayers.Values)
        {
            MapView.Map.Layers.Add(layer);
        }
        MapView.Map.Layers.Add(_windLabelLayer);
        MapView.Map.Layers.Add(_viaLayer);
        MapView.Map.Layers.Add(_reachedLayer);
        MapView.Map.Layers.Add(_homeLayer);
        MapView.Map.Tapped += OnMapTapped;
        // + and − on the map: one-handed on the phone, and the only way on the emulator without a pinch.
        MapView.Map.Widgets.Enqueue(new Mapsui.Widgets.ButtonWidgets.ZoomInOutWidget
        {
            HorizontalAlignment = Mapsui.Widgets.HorizontalAlignment.Right,
            VerticalAlignment = Mapsui.Widgets.VerticalAlignment.Bottom,
            Margin = new MRect(12, 12),
            Size = 40,
            BackColor = ToMapsui(HudTheme.Background, 230),
            StrokeColor = ToMapsui(HudTheme.Guide, 255),
            TextColor = ToMapsui(HudTheme.Guide, 255),
        });
        // Until the device answers: León, where the author rides. The navigator keeps the call until the map has its size.
        MapView.Map.Navigator.CenterOnAndZoomTo(ToMercator(new GeoCoordinate(42.5987, -5.5671)), ResolutionAt(12));
        ShowVia();
        ShowRideChip();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        if (!_started)
        {
            _started = true;
            _ = AdviseDirectionAsync();
        }
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _cancel?.Cancel();
    }

    private async void OnRetryClicked(object? sender, EventArgs e)
    {
        if (_via.Count > 0)
        {
            await AdviseRideAsync();
        }
        else
        {
            await AdviseDirectionAsync();
        }
    }

    /// <summary>
    /// A tap on the map only closes an open panel (taps on its gaps reach the map). Points are added with the aim
    /// alone (<see cref="OnAimTapped"/>): taps, long presses, drags and pinches on the map never add one, which they
    /// did by mistake before.
    /// </summary>
    private void OnMapTapped(object? sender, MapEventArgs e)
    {
        if (_home is null)
        {
            return;
        }
        if (e.GestureType == GestureType.SingleTap)
        {
            if (DetailPanel.IsVisible || DeparturePanel.IsVisible || RidePanel.IsVisible)
            {
                e.Handled = true;
                DetailPanel.IsVisible = false;
                if (DeparturePanel.IsVisible)
                {
                    OnCloseDepartureClicked(this, EventArgs.Empty);
                }
                if (RidePanel.IsVisible)
                {
                    OnCloseRideClicked(this, EventArgs.Empty);
                }
            }
            return;
        }
    }

    /// <summary>A point for the ride: the only one of an out and back, one more of a loop; then the ride through them.</summary>
    private void AddPoint(GeoCoordinate point)
    {
        if (!_loopMode)
        {
            _via.Clear();
        }
        _via.Add(point);
        ShowVia();
        _ = AdviseRideAsync();
    }

    /// <summary>The aim tapped: the point goes where it is, at the centre of the map. The only way to add one.</summary>
    private void OnAimTapped(object? sender, TappedEventArgs e)
    {
        if (_home is null)
        {
            return;
        }
        var viewport = MapView.Map.Navigator.Viewport;
        AddPoint(FromMercator(viewport.CenterX, viewport.CenterY));
    }

    private void OnOutAndBackClicked(object? sender, EventArgs e) => SetMode(loop: false);

    private void OnLoopClicked(object? sender, EventArgs e) => SetMode(loop: true);

    /// <summary>Switches the kind of ride; back to an out and back, only the first point stays.</summary>
    private void SetMode(bool loop)
    {
        if (_loopMode == loop)
        {
            return;
        }
        _loopMode = loop;
        OutAndBackChip.Style = (Microsoft.Maui.Controls.Style)Resources[loop ? "Chip" : "ChipOn"];
        LoopChip.Style = (Microsoft.Maui.Controls.Style)Resources[loop ? "ChipOn" : "Chip"];
        if (!loop && _via.Count > 1)
        {
            _via.RemoveRange(1, _via.Count - 1);
            ShowVia();
            ShowRoute(null);
            _ = AdviseRideAsync();
        }
        else
        {
            ShowVia();
        }
    }

    /// <summary>
    /// An out and back of the default duration towards the recommended direction, found by Core's generator: a ride
    /// that fits without choosing a point. The rider can then move the point or add one for a loop.
    /// </summary>
    private async void OnProposeClicked(object? sender, EventArgs e)
    {
        if (_home is null || _lastDirection is not { } direction)
        {
            Status.Text = "Primero hace falta la posición y el viento.";
            return;
        }
        var cancellationToken = Restart();
        var bearing = direction.Wind.StartBearingDegrees ?? RideBearings.For(direction.Wind, 1)[0];
        try
        {
            Status.Text = $"Buscando una ida y vuelta de {DurationText(_duration)} {SpanishText.Heading(bearing)}…";
            var home = _home.Value;
            var targetKm = RideDistance.FromDuration(_duration, _speedKmh);
            var proposal = await Task.Run(() => _rides.ProposeFarPointAsync(home, bearing, targetKm, cancellationToken), cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (proposal is not { } found)
            {
                Fail($"BRouter no encontró carretera {SpanishText.Heading(bearing)}. Pon tú el punto.");
                return;
            }
            SetMode(loop: false);
            AddPoint(found.FarPoint);
        }
        catch (OperationCanceledException)
        {
        }
        catch (RouteException ex)
        {
            Fail($"BRouter no responde: {ex.Message}");
        }
    }

    private async void OnRemoveLastClicked(object? sender, EventArgs e)
    {
        if (_via.Count == 0)
        {
            return;
        }
        _via.RemoveAt(_via.Count - 1);
        ShowVia();
        // The old route goes at once; the one through the points left comes when the engine answers.
        ShowRoute(null);
        if (_via.Count > 0)
        {
            await AdviseRideAsync();
        }
        else
        {
            await AdviseDirectionAsync();
        }
    }

    /// <summary>The position, then the wind and where to head; what failed stays on screen with a way to try again.</summary>
    private async Task AdviseDirectionAsync()
    {
        var cancellationToken = Restart();
        try
        {
            if (_home is null)
            {
                Status.Text = "Buscando tu posición…";
                var fix = await _location.GetCurrentAsync(cancellationToken);
                _home = fix.Position;
                ShowHome(fix.Position);
            }
            Status.Text = "Pidiendo el viento…";
            var targetKm = RideDistance.FromDuration(_duration, _speedKmh);
            var departure = Departure;
            var speed = _speedKmh;
            var advice = await Task.Run(() => _direction.AdviseAsync(_home.Value, targetKm, departure, speed, cancellationToken), cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            Show(RideText.DirectionHeadline(advice.Wind),
                $"Salida de {DurationText(_duration)} a {SpanishText.Kmh(_speedKmh)}, viento de las próximas {advice.Hours} h.",
                RideText.Wind(advice.Wind), "");
            ShowFan(advice.Fan);
            _lastDirection = advice;
            ShowRideWind([]);
        }
        catch (OperationCanceledException)
        {
        }
        catch (LocationUnavailableException ex)
        {
            Fail(ex.Message);
        }
        catch (WindForecastException ex)
        {
            Fail($"Sin previsión de viento: {ex.Message}");
        }
    }

    /// <summary>The route through the points (drawn as soon as it comes), then the wind, the rain and when to leave.</summary>
    private async Task AdviseRideAsync()
    {
        var cancellationToken = Restart();
        // The route's own line stays on screen even when the weather after it fails.
        var summary = "";
        try
        {
            var request = new TracedRequest(_home!.Value, _via.ToList(), Departure, _speedKmh,
                _loopMode ? RouteShape.Loop : RouteShape.OutAndBack);
            Status.Text = request.Shape == RouteShape.OutAndBack ? "Buscando la ida y vuelta…" : "Buscando la vuelta…";
            var traced = await Task.Run(() => _rides.RouteAsync(request, cancellationToken), cancellationToken);
            // A point added or taken away meanwhile: this answer is stale, the next job draws its own.
            cancellationToken.ThrowIfCancellationRequested();
            ShowRoute(traced.Route);
            ShowReached(traced);
            summary = RideText.TracedSummary(traced) + ". ";
            Status.Text = $"{summary}Pidiendo el viento…";

            // The wind first (one request): the arrows, the way round and most of the panel; then the rain.
            var windOnly = await Task.Run(() => _rides.JudgeWindAsync(traced, cancellationToken), cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            ShowRide(windOnly, "Pidiendo la lluvia…");

            var ride = await Task.Run(() => _rides.JudgeRainAsync(windOnly, DateTimeOffset.UtcNow, cancellationToken), cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            ShowRide(ride, null);
        }
        catch (OperationCanceledException)
        {
        }
        catch (RouteException ex) when (ex.Failure == RouteFailure.Unreachable)
        {
            Fail("BRouter no pudo unir los puntos: mueve el último a una carretera (QUITAR PUNTO y toca otra vez).");
        }
        catch (RouteException ex)
        {
            Fail($"BRouter no responde: {ex.Message}");
        }
        catch (WindForecastException ex)
        {
            Fail($"{summary}Sin previsión de viento: {ex.Message}");
        }
    }

    /// <summary>The ride as judged so far: the route in its better way round, the wind along it and the panel.</summary>
    /// <param name="doing">What is still on its way, for the status; null when nothing is.</param>
    private void ShowRide(TracedRide ride, string? doing)
    {
        var option = ride.Ride;
        ShowRoute(option.Route);
        ShowRideWind(WindArrows.AlongRide(option.Winds));
        var lines = new List<string> { $"{SpanishText.Capitalized(RideText.Turn(option, ride.Shape))}: {RideText.Advantage(option.Score)}" };
        lines.AddRange(RideText.Lines(option, ride.RainFailure, Zone, ride.RainPending));
        Show(RideText.Recommendation(ride.Departure, fromNow: _departureAt is null, Zone),
            $"{SpanishText.Km(option.Route.DistanceKm)} · {RideText.Arrival(option, Zone)}{(doing is null ? "" : $" {doing}")}",
            string.Join("\n", lines), RideText.TracedSummary(ride) + ".");
    }

    /// <summary>Cancels what was running and clears the panel for the next job.</summary>
    private CancellationToken Restart()
    {
        _cancel?.Cancel();
        _cancel = new CancellationTokenSource();
        Retry.IsVisible = false;
        Headline.IsVisible = ShowDetail.IsVisible = DetailPanel.IsVisible = false;
        return _cancel.Token;
    }

    /// <summary>The bar over the map (headline and status) and the detail panel behind DETALLE.</summary>
    private void Show(string headline, string status, string detail, string footer)
    {
        Headline.Text = headline;
        Status.Text = status;
        Detail.Text = detail;
        DetailFooter.Text = footer;
        DetailFooter.IsVisible = footer.Length > 0;
        Headline.IsVisible = true;
        ShowDetail.IsVisible = detail.Length > 0;
    }

    private void OnShowDetailClicked(object? sender, EventArgs e) => DetailPanel.IsVisible = true;

    private void OnCloseDetailClicked(object? sender, EventArgs e) => DetailPanel.IsVisible = false;

    /// <summary>"2 h", "1½ h": short, so it fits a chip at any font size.</summary>
    private static string DurationText(TimeSpan duration) =>
        duration.Minutes == 0 ? $"{(int)duration.TotalHours} h" : $"{(int)duration.TotalHours}½ h";

    private void OnRideClicked(object? sender, EventArgs e)
    {
        _pendingDuration = _duration;
        _pendingSpeed = _speedKmh;
        BuildRideChips();
        DetailPanel.IsVisible = DeparturePanel.IsVisible = false;
        RidePanel.IsVisible = true;
    }

    /// <summary>Closes the panel and, if the duration or the speed changed, remembers them and judges everything again.</summary>
    private async void OnCloseRideClicked(object? sender, EventArgs e)
    {
        RidePanel.IsVisible = false;
        if (_pendingDuration == _duration && _pendingSpeed == _speedKmh)
        {
            return;
        }
        _duration = _pendingDuration;
        _speedKmh = _pendingSpeed;
        Preferences.Default.Set(DurationKey, (int)_duration.TotalMinutes);
        Preferences.Default.Set(SpeedKey, _speedKmh);
        ShowRideChip();
        ShowVia();
        if (_via.Count > 0)
        {
            // The fan for the new duration, then the ride at the new pace.
            await AdviseDirectionAsync();
            await AdviseRideAsync();
        }
        else
        {
            await AdviseDirectionAsync();
        }
    }

    private void ShowRideChip() => RideChip.Text = $"{DurationText(_duration).ToUpperInvariant()} · {_speedKmh:0} KM/H";

    private void BuildRideChips()
    {
        DurationChips.Children.Clear();
        SpeedChips.Children.Clear();
        foreach (var choice in DurationChoices)
        {
            var duration = choice;
            DurationChips.Children.Add(Chip(DurationText(duration), _pendingDuration == duration, () => _pendingDuration = duration, BuildRideChips));
        }
        foreach (var choice in SpeedChoices)
        {
            var speed = choice;
            SpeedChips.Children.Add(Chip($"{speed:0}", _pendingSpeed == speed, () => _pendingSpeed = speed, BuildRideChips));
        }
        RideNote.Text = $"Unos {SpanishText.Km(RideDistance.FromDuration(_pendingDuration, _pendingSpeed))}: el abanico y PROPONER van a esa distancia; " +
                        "la media reparte las horas de paso (más despacio cuesta arriba).";
    }

    private void OnDepartureClicked(object? sender, EventArgs e)
    {
        _pendingAt = _departureAt;
        BuildDepartureChips();
        DetailPanel.IsVisible = false;
        DeparturePanel.IsVisible = true;
    }

    /// <summary>Closes the panel and, if the departure changed, judges everything again for it.</summary>
    private async void OnCloseDepartureClicked(object? sender, EventArgs e)
    {
        DeparturePanel.IsVisible = false;
        if (_pendingAt == _departureAt)
        {
            return;
        }
        _departureAt = _pendingAt;
        DepartureChip.Text = _departureAt is { } at ? $"SALIDA · {at:HH\\:mm}" : "SALIDA · AHORA";
        if (_via.Count > 0)
        {
            await AdviseRideAsync();
        }
        else
        {
            await AdviseDirectionAsync();
        }
    }

    /// <summary>AHORA and the hours left today; with an hour chosen, its quarters.</summary>
    private void BuildDepartureChips()
    {
        HourChips.Children.Clear();
        MinuteChips.Children.Clear();
        var nowLocal = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, Zone);
        HourChips.Children.Add(Chip("AHORA", _pendingAt is null, () => _pendingAt = null, BuildDepartureChips));
        for (var h = nowLocal.Hour + 1; h <= 23; h++)
        {
            var hour = h;
            HourChips.Children.Add(Chip($"{hour:00}", _pendingAt?.Hour == hour, () => _pendingAt = new TimeOnly(hour, _pendingAt?.Minute ?? 0), BuildDepartureChips));
        }
        if (_pendingAt is { } at)
        {
            foreach (var m in new[] { 0, 15, 30, 45 })
            {
                var minute = m;
                MinuteChips.Children.Add(Chip($":{minute:00}", at.Minute == minute, () => _pendingAt = new TimeOnly(at.Hour, minute), BuildDepartureChips));
            }
            var hoursAhead = (at.ToTimeSpan() - nowLocal.TimeOfDay).TotalHours;
            DepartureNote.Text = $"Salida a las {at:HH\\:mm}." +
                                 (hoursAhead > 3 ? " A más de 3 h vista la lluvia es solo un aviso para la zona." : "");
        }
        else
        {
            DepartureNote.Text = "Salida ahora.";
        }
    }

    private Button Chip(string text, bool selected, Action choose, Action rebuild)
    {
        var chip = new Button { Text = text, Style = (Microsoft.Maui.Controls.Style)Resources["Chip"], Margin = new Thickness(0, 0, 8, 8) };
        if (selected)
        {
            chip.BackgroundColor = HudTheme.Guide;
            chip.TextColor = HudTheme.Background;
        }
        chip.Clicked += (_, _) =>
        {
            choose();
            rebuild();
        };
        return chip;
    }

    private void Fail(string why)
    {
        Status.Text = why;
        Retry.IsVisible = true;
    }

    private void ShowHome(GeoCoordinate position)
    {
        _homeLayer.Features = [new PointFeature(ToMercator(position))];
        _homeLayer.DataHasChanged();
        MapView.Map.Navigator.CenterOnAndZoomTo(ToMercator(position), ResolutionAt(12));
    }

    private void ShowFan(IReadOnlyList<GeoCoordinate>? ring)
    {
        _fanLayer.Features = ring is null
            ? []
            : [new GeometryFeature(new Polygon(new LinearRing(ring.Select(p => ToNts(p)).ToArray())))];
        _fanLayer.DataHasChanged();
        MapView.Map.RefreshGraphics();
    }

    private void ShowVia()
    {
        _viaLayer.Features = _via.Select(p => new PointFeature(ToMercator(p))).ToList();
        _viaLayer.DataHasChanged();
        RemoveLast.IsVisible = _via.Count > 0;
        Hint.Text = (_loopMode, _via.Count) switch
        {
            (false, 0) => $"Hasta dónde: mueve el mapa y toca la mira (el anillo del centro), o PROPONER ({DurationText(_duration)}).",
            (false, _) => "Ida y vuelta. Para cambiar el punto, toca la mira en otro sitio.",
            (true, 0) => "Por dónde pasar: mueve el mapa y toca la mira; puedes añadir varios.",
            (true, 1) => "Vuelta por 1 punto: BRouter elige por dónde volver. Toca la mira en más sitios para mandar tú.",
            (true, var n) => $"Vuelta por {n} puntos. Añade más con la mira o QUITAR PUNTO.",
        };
        MapView.Map.RefreshGraphics();
    }

    private void ShowRoute(Route? route)
    {
        _routeLayer.Features = route is null
            ? []
            : [new GeometryFeature(new LineString(route.Points.Select(p => ToNts(p.Position)).ToArray()))];
        _routeLayer.DataHasChanged();
        if (route is null)
        {
            _reachedLayer.Features = [];
            _snapLayer.Features = [];
            _reachedLayer.DataHasChanged();
            _snapLayer.DataHasChanged();
            ShowRideWind([]);
        }
        MapView.Map.RefreshGraphics();
    }

    private void ShowRideWind(IReadOnlyList<WindArrow> arrows)
    {
        Legend.IsVisible = arrows.Count > 0;
        _windHaloLayer.Features = arrows.SelectMany(ArrowFeatures).ToList();
        _windHaloLayer.DataHasChanged();
        _windLabelLayer.Features = arrows.Select(a =>
        {
            var label = new PointFeature(ToMercator(a.Tip));
            label["km"] = SpanishText.KmAt(a.AtKm);
            return label;
        }).ToList();
        _windLabelLayer.DataHasChanged();
        foreach (var (feel, layer) in _windLayers)
        {
            layer.Features = arrows.Where(a => a.Feel == feel).SelectMany(ArrowFeatures).ToList();
            layer.DataHasChanged();
        }
        MapView.Map.RefreshGraphics();
    }

    /// <summary>The shaft and the head of an arrow, as two lines.</summary>
    private static IEnumerable<IFeature> ArrowFeatures(WindArrow arrow) =>
    [
        new GeometryFeature(new LineString([ToNts(arrow.Tail), ToNts(arrow.Tip)])),
        new GeometryFeature(new LineString([ToNts(arrow.HeadLeft), ToNts(arrow.Tip), ToNts(arrow.HeadRight)])),
    ];

    /// <summary>"km 7,5" by the arrow: dark text with a light halo, above its tip.</summary>
    private static LabelStyle KmLabelStyle() => new()
    {
        LabelColumn = "km",
        ForeColor = ToMapsui(HudTheme.Background, 255),
        Halo = new Pen(ToMapsui(HudTheme.Text, 220), 2),
        BackColor = null,
        Font = new Mapsui.Styles.Font { Size = 12, Bold = true },
        Offset = new Offset(0, -16),
    };

    private static VectorStyle ArrowStyle(Microsoft.Maui.Graphics.Color color, double width) => new()
    {
        Line = new Pen(ToMapsui(color, 230), width),
        Fill = null,
        Outline = null,
    };

    /// <summary>
    /// Where the route really passes for each point touched: a solid dot there and, when it is not where the finger
    /// was (no asphalt nearer), a thin line from the touched point to it.
    /// </summary>
    private void ShowReached(TracedRoute traced)
    {
        var reached = traced.Reached;
        _reachedLayer.Features = reached.Select(p => new PointFeature(ToMercator(p))).ToList();
        _snapLayer.Features = traced.Request.Via
            .Select((via, i) => (Via: via, Reached: reached[i]))
            .Where(pair => pair.Via.DistanceKmTo(pair.Reached) >= 0.3)
            .Select(pair => (IFeature)new GeometryFeature(new LineString([ToNts(pair.Via), ToNts(pair.Reached)])))
            .ToList();
        _reachedLayer.DataHasChanged();
        _snapLayer.DataHasChanged();
        MapView.Map.RefreshGraphics();
    }

    /// <summary>The sector, translucent, in the guide color.</summary>
    private static VectorStyle FanStyle() => new()
    {
        Fill = new Mapsui.Styles.Brush(ToMapsui(HudTheme.Guide, 48)),
        Outline = new Pen(ToMapsui(HudTheme.Guide, 180), 2),
        Line = null,
    };

    /// <summary>The route: a firm line in the guide color.</summary>
    private static VectorStyle RouteStyle() => new()
    {
        Line = new Pen(ToMapsui(HudTheme.Guide, 230), 5),
        Fill = null,
        Outline = null,
    };

    /// <summary>Home: a marker-colored dot with a light rim.</summary>
    private static SymbolStyle HomeStyle() => new()
    {
        SymbolType = SymbolType.Ellipse,
        SymbolScale = 0.5,
        Fill = new Mapsui.Styles.Brush(ToMapsui(HudTheme.Marker, 255)),
        Outline = new Pen(ToMapsui(HudTheme.Text, 255), 2),
    };

    /// <summary>The points touched: smaller dots in the marker color.</summary>
    private static SymbolStyle ViaStyle() => new()
    {
        SymbolType = SymbolType.Ellipse,
        SymbolScale = 0.4,
        Fill = new Mapsui.Styles.Brush(ToMapsui(HudTheme.Background, 255)),
        Outline = new Pen(ToMapsui(HudTheme.Marker, 255), 3),
    };

    /// <summary>Where the route really passes: a small solid dot in the marker color.</summary>
    private static SymbolStyle ReachedStyle() => new()
    {
        SymbolType = SymbolType.Ellipse,
        SymbolScale = 0.3,
        Fill = new Mapsui.Styles.Brush(ToMapsui(HudTheme.Marker, 255)),
        Outline = new Pen(ToMapsui(HudTheme.Background, 255), 1),
    };

    /// <summary>From the touched point to where the route passes: thin, in the marker color.</summary>
    private static VectorStyle SnapStyle() => new()
    {
        Line = new Pen(ToMapsui(HudTheme.Marker, 200), 2) { PenStyle = PenStyle.Dash },
        Fill = null,
        Outline = null,
    };

    private static Mapsui.Styles.Color ToMapsui(Microsoft.Maui.Graphics.Color color, int alpha) =>
        new((int)(color.Red * 255), (int)(color.Green * 255), (int)(color.Blue * 255), alpha);

    /// <summary>Metres per pixel of Web Mercator at a zoom level (at the equator): the tile size over the world.</summary>
    private static double ResolutionAt(int zoom) => 2 * Math.PI * 6378137 / 256 / Math.Pow(2, zoom);

    private static MPoint ToMercator(GeoCoordinate position)
    {
        var (x, y) = SphericalMercator.FromLonLat(position.Longitude, position.Latitude);
        return new MPoint(x, y);
    }

    private static GeoCoordinate FromMercator(double x, double y)
    {
        var (longitude, latitude) = SphericalMercator.ToLonLat(x, y);
        return new GeoCoordinate(latitude, longitude);
    }

    private static Coordinate ToNts(GeoCoordinate position)
    {
        var m = ToMercator(position);
        return new Coordinate(m.X, m.Y);
    }

    private async void OnAttributionTapped(object? sender, TappedEventArgs e) =>
        await Launcher.OpenAsync(_tiles.AttributionUrl);
}
