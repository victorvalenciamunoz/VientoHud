using Mapsui;
using Mapsui.Layers;
using Mapsui.Manipulations;
using Mapsui.Nts;
using Mapsui.Projections;
using Mapsui.Styles;
using NetTopologySuite.Geometries;
using VientoHud.App.Hud;
using VientoHud.App.Maps;
using VientoHud.App.Services;
using VientoHud.Core.Flyover;
using VientoHud.Core.Gpx;
using VientoHud.Core.Location;
using VientoHud.Core.Net;
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

    /// <summary>Whether the rider has put a point in this kind of ride, so its hint below the map is no longer needed.</summary>
    private string HintSeenKey => _loopMode ? "hint_seen_loop" : "hint_seen_out_and_back";

    private TimeSpan _duration = TimeSpan.FromMinutes(Preferences.Default.Get(DurationKey, 120));
    private double _speedKmh = Preferences.Default.Get(SpeedKey, RideDistance.DefaultAverageSpeedKmh);
    private TimeSpan _pendingDuration;
    private double _pendingSpeed;

    private static readonly TimeZoneInfo Zone = TimeZoneInfo.Local;

    private readonly TileSources _tiles;
    /// <summary>The 3D view's tiles on the device (phase 8): terrain ahead of time, the base map as it is seen.</summary>
    private readonly TileStore _tileStore;
    /// <summary>What the app took from each service (phase 9), for the panel.</summary>
    private readonly UsageMeter _usage;
    /// <summary>The time: the real one, or a recorded day's (phase 9, step 4).</summary>
    private readonly TimeProvider _clock;
    private readonly ILocationProvider _location;
    private readonly DirectionAdvisor _direction;
    private readonly TracedRideAdvisor _rides;
    private readonly MemoryLayer _fanLayer = new("Fan") { Style = FanStyle() };
    private readonly MemoryLayer _routeLayer = new("Route") { Style = RouteStyle() };
    /// <summary>Which way the ride goes: small triangles on the track, each turned to its own bearing (a style per mark).</summary>
    private readonly MemoryLayer _markLayer = new("Marks") { Style = null };
    private readonly MemoryLayer _viaLayer = new("Via") { Style = ViaStyle() };
    private readonly MemoryLayer _reachedLayer = new("Reached") { Style = ReachedStyle() };
    private readonly MemoryLayer _snapLayer = new("Snap") { Style = SnapStyle() };
    // The wind and the rain along the ride (phase 10, DECISIONS 065): how it blows, a white arrow on the road with a dark
    // halo; how it feels on each pass, the small arrows of the way round in color, each on its rider's right; and a
    // blue drop by the pass that meets likely rain.
    private readonly MemoryLayer _windHaloLayer = new("WindHalo") { Style = ArrowStyle(HudTheme.Background, 7) };
    private readonly MemoryLayer _windLayer = new("Wind") { Style = ArrowStyle(HudTheme.Text, 3.5) };
    private readonly MemoryLayer _passLayer = new("Passes") { Style = null };
    private readonly MemoryLayer _dropLayer = new("Drops") { Style = null };
    private readonly MemoryLayer _homeLayer = new("Home") { Style = HomeStyle() };
    private readonly List<GeoCoordinate> _via = [];
    /// <summary>VUELTA (points accumulate) or IDA Y VUELTA (one point, replaced by the next).</summary>
    private bool _loopMode;
    /// <summary>The last direction advice: PROPONER heads that way.</summary>
    private DirectionAdvice? _lastDirection;
    private GeoCoordinate? _home;
    /// <summary>The ride as last judged (wind and rain in): what COMPARTIR GPX and ABRIR CON hand over.</summary>
    private TracedRide? _ride;
    /// <summary>The ride as last shown, even with the rain still on its way: the profile marks its wind and warnings.</summary>
    private TracedRide? _shown;
    /// <summary>The altitude profile of the route on screen (phase 8), from Core; null without elevations.</summary>
    private RouteProfile? _profile;
    /// <summary>The route on screen, as drawn (its better way round once judged): what the 3D view shows.</summary>
    private Route? _shownRoute;
    private readonly ProfileDrawable _profileDrawable = new();
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
            var now = _clock.GetUtcNow();
            if (_departureAt is not { } at)
            {
                return now;
            }
            var next = DepartureTime.Next(at, now, Zone);
            return next - now > TimeSpan.FromHours(23) ? now : next;
        }
    }
    private CancellationTokenSource? _cancel;

    public MapPage(TileSources tiles, TileStore tileStore, UsageMeter usage, TimeProvider clock, ILocationProvider location, DirectionAdvisor direction, TracedRideAdvisor rides)
    {
        InitializeComponent();
        _usage = usage;
        _clock = clock;
        _tiles = tiles;
        _tileStore = tileStore;
        _location = location;
        _direction = direction;
        _rides = rides;
        // A recorded day says so on the card: nothing on screen is today.
        if (AppSettings.Replay is { } replay)
        {
            ReplayTag.Text = $"DÍA GRABADO · {TimeZoneInfo.ConvertTime(replay.Start, Zone):d MMM HH:mm}".ToUpperInvariant();
            ReplayTag.IsVisible = true;
        }
        Attribution.Text = tiles.Attribution;
        MapView.Map.Layers.Add(tiles.CreateLayer());
        MapView.Map.Layers.Add(_fanLayer);
        MapView.Map.Layers.Add(_routeLayer);
        MapView.Map.Layers.Add(_markLayer);
        MapView.Map.Layers.Add(_snapLayer);
        MapView.Map.Layers.Add(_windHaloLayer);
        MapView.Map.Layers.Add(_windLayer);
        MapView.Map.Layers.Add(_passLayer);
        MapView.Map.Layers.Add(_dropLayer);
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
        // Until the device answers: a view of Europe, which says nothing about where the author lives. The navigator keeps the call until the map has its size.
        MapView.Map.Navigator.CenterOnAndZoomTo(ToMercator(new GeoCoordinate(48.0, 9.0)), ResolutionAt(4));
        ShowVia();
        ShowOptionsChip();
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
            if (DetailPanel.IsVisible || OptionsPanel.IsVisible || GpxPanel.IsVisible || ViewsPanel.IsVisible || ProfilePanel.IsVisible
                || LegendPanel.IsVisible)
            {
                e.Handled = true;
                DetailPanel.IsVisible = GpxPanel.IsVisible = ViewsPanel.IsVisible = ProfilePanel.IsVisible = LegendPanel.IsVisible = false;
                if (OptionsPanel.IsVisible)
                {
                    OnCloseOptionsClicked(this, EventArgs.Empty);
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
        // The hint has done its job for this kind of ride.
        Preferences.Default.Set(HintSeenKey, true);
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
                [("VIENTO", [RideText.Wind(advice.Wind)])]);
            ShowFan(advice.Fan);
            _lastDirection = advice;
            ShowRideWind(RideMarks.None);
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
            ShowProfile(traced.Route, null);
            summary = RideText.TracedSummary(traced) + ". ";
            Status.Text = $"{summary}Pidiendo el viento…";

            // The wind first (one request): the arrows, the way round and most of the panel; then the rain.
            var windOnly = await Task.Run(() => _rides.JudgeWindAsync(traced, cancellationToken), cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            ShowRide(windOnly, "Pidiendo la lluvia…");

            var ride = await Task.Run(() => _rides.JudgeRainAsync(windOnly, _clock.GetUtcNow(), cancellationToken), cancellationToken);
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
        // The GPX waits for the rain: its waypoints warn of it.
        _ride = ride.RainPending ? null : ride;
        _shown = ride;
        RefreshGpxPanel();
        ShowRoute(option.Route);
        ShowProfile(option.Route, ride);
        ShowRideWind(RideMarking.Along(option));
        // One idea per line: the way round and the advantage, each wind stretch, the gusts, the rain, the town, and
        // the route's figures (the summary's comma-separated parts, each on its own line).
        // Why to wait opens the detail: the card keeps the headline and one line (phase 10, 2026-10-27).
        var why = RideText.WaitReason(ride.Departure, fromNow: _departureAt is null, Zone);
        var sections = new List<(string, IReadOnlyList<string>)>
        {
            ("SALIDA", why is null ? [] : [why + "."]),
            ("SENTIDO", [SpanishText.Capitalized(RideText.Turn(option, ride.Shape)), SpanishText.Capitalized(RideText.Advantage(option.Score))]),
            ("VIENTO POR TRAMOS", WindStretches.Describe(option.Winds).Select(RideText.Stretch).ToList()),
            ("RACHAS DE LADO", RideText.Gusts(option.Score) is { } gusts ? [gusts] : []),
            ("LLUVIA", RideText.RainLines(option, ride.RainFailure, Zone, ride.RainPending).Select(l => SpanishText.Capitalized(l.StartsWith("Lluvia: ") ? l["Lluvia: ".Length..] : l)).ToList()),
            ("CIUDAD", [RideText.Urban(option.Urban)["Ciudad: ".Length..]]),
            ("RUTA", RideText.TracedSummary(ride).Split(", ").Select(SpanishText.Capitalized).ToList()),
        };
        // A short headline (the whole sentence covered the map, 2026-10-26), and what to watch for under it.
        CardWarning.Text = RideText.Warnings(option);
        CardWarning.IsVisible = CardWarning.Text is not null;
        Show(RideText.Headline(ride.Departure, fromNow: _departureAt is null, Zone),
            $"{SpanishText.Km(option.Route.DistanceKm)} · {RideText.Arrival(option, Zone)}{(doing is null ? "" : $" {doing}")}",
            sections);
    }

    /// <summary>Cancels what was running and clears the panel for the next job.</summary>
    private CancellationToken Restart()
    {
        _cancel?.Cancel();
        _cancel = new CancellationTokenSource();
        Retry.IsVisible = false;
        Headline.IsVisible = CardWarning.IsVisible = DetailChevron.IsVisible = DetailPanel.IsVisible = false;
        _ride = _shown = null;
        _profile = null;
        _shownRoute = null;
        GpxPanel.IsVisible = ViewsPanel.IsVisible = ProfilePanel.IsVisible = LegendPanel.IsVisible = false;
        RefreshActions();
        return _cancel.Token;
    }

    /// <summary>The button row below the map: PROPONER until there is a route, then GPX and VER; QUITAR PUNTO with points.</summary>
    private void RefreshActions()
    {
        var hasRoute = _shownRoute is not null;
        Propose.IsVisible = !hasRoute;
        ShowGpx.IsVisible = ShowViews.IsVisible = hasRoute;
        RemoveLast.IsVisible = _via.Count > 0;
    }

    /// <summary>
    /// The profile of the route on screen (phase 8): Core computes it from the engine's elevations; the panel draws it
    /// with the wind and the warnings of <paramref name="ride"/> when it is judged. VER appears with the route.
    /// </summary>
    private void ShowProfile(Route route, TracedRide? ride)
    {
        _shownRoute = route;
        _profile = RouteProfile.From(route);
        _profileDrawable.Profile = _profile;
        _profileDrawable.Winds = ride is null ? [] : WindStretches.Describe(ride.Ride.Winds);
        ProfileWindLegend.IsVisible = ride is not null;
        _profileDrawable.Alerts = ride is null ? [] : RideAlerts.For(ride.Ride);
        ProfileView.Drawable = _profileDrawable;
        ProfileView.Invalidate();
        ProfileSummary.Text = _profile is null ? "" : RideText.Profile(_profile);
        if (ProfileLegend.Children.Count == 0)
        {
            foreach (var band in GradeBands.All)
            {
                var item = new HorizontalStackLayout { Spacing = 4, Margin = new Thickness(0, 0, 10, 0) };
                item.Children.Add(new BoxView
                {
                    WidthRequest = 10, HeightRequest = 10, CornerRadius = 2, VerticalOptions = LayoutOptions.Center,
                    Color = Microsoft.Maui.Graphics.Color.FromArgb(GradeBands.Color(band)),
                });
                item.Children.Add(new Label { Text = RideText.Grade(band), FontFamily = "OpenSansRegular", FontSize = 11, TextColor = HudTheme.TextMuted });
                ProfileLegend.Children.Add(item);
            }
        }
        RefreshActions();
        RefreshViewsPanel();
    }

    private void OnShowViewsClicked(object? sender, EventArgs e)
    {
        if (OptionsPanel.IsVisible)
        {
            OnCloseOptionsClicked(this, EventArgs.Empty);
        }
        DetailPanel.IsVisible = GpxPanel.IsVisible = ProfilePanel.IsVisible = LegendPanel.IsVisible = false;
        RefreshViewsPanel();
        ViewsPanel.IsVisible = true;
    }

    private void OnCloseViewsClicked(object? sender, EventArgs e) => ViewsPanel.IsVisible = false;

    /// <summary>The VER panel: PERFIL when the route has elevations; 3D whenever there is a route (it needs the network).</summary>
    private void RefreshViewsPanel()
    {
        var hasProfile = _profile is not null;
        OpenProfile.IsEnabled = hasProfile;
        OpenProfile.Opacity = hasProfile ? 1 : 0.4;
        OpenFlyover.IsEnabled = _shownRoute is not null;
        OpenFlyover.Opacity = _shownRoute is not null ? 1 : 0.4;
        ViewsNote.Text = hasProfile
            ? (_shown is null ? "PERFIL: las subidas por color (el viento y los avisos llegan con el juicio). 3D: la ruta sobre el relieve, con conexión." : "PERFIL: las subidas por color, el viento por tramos y los avisos del GPX. 3D: la ruta sobre el relieve, con conexión.")
            : "BRouter no dio alturas para esta ruta: sin perfil. 3D: la ruta sobre el relieve, con conexión.";
    }

    /// <summary>The 3D view (phase 8): Core composes the scene from the route and its profile; the page draws it.</summary>
    private async void OnOpenFlyoverClicked(object? sender, EventArgs e)
    {
        if (_shownRoute is not { } route)
        {
            return;
        }
        ViewsPanel.IsVisible = false;
        await Navigation.PushModalAsync(new FlyoverPage(
            FlyoverScene.From(route, _profile),
            FlyoverPath.Along(route, _profile),
            _shown is { } ride ? FlyoverWeather.From(ride.Ride, withAlerts: !ride.RainPending) : FlyoverWeather.None,
            route, _tiles, _tileStore));
    }

    private void OnOpenProfileClicked(object? sender, EventArgs e)
    {
        ViewsPanel.IsVisible = false;
        ProfileView.Invalidate();
        ProfilePanel.IsVisible = true;
    }

    private void OnCloseProfileClicked(object? sender, EventArgs e) => ProfilePanel.IsVisible = false;

    /// <summary>The bar over the map (headline and status) and the detail panel behind DETALLE.</summary>
    private void Show(string headline, string status, IReadOnlyList<(string Title, IReadOnlyList<string> Lines)> sections)
    {
        Headline.Text = headline;
        Status.Text = status;
        DetailSections.Children.Clear();
        foreach (var (title, lines) in sections.Where(s => s.Lines.Count > 0))
        {
            var block = new VerticalStackLayout { Spacing = 2 };
            block.Children.Add(new Label
            {
                Text = title, FontFamily = "ChakraPetchBold", FontSize = 11, CharacterSpacing = 1, TextColor = HudTheme.TextMuted,
            });
            foreach (var line in lines)
            {
                block.Children.Add(new Label { Text = line, FontFamily = "OpenSansRegular", FontSize = 14, TextColor = HudTheme.TextSoft });
            }
            DetailSections.Children.Add(block);
        }
        Headline.IsVisible = true;
        DetailChevron.IsVisible = DetailSections.Children.Count > 0 && !Retry.IsVisible;
    }

    /// <summary>The card over the map opens the detail, when there is one.</summary>
    private void OnCardTapped(object? sender, TappedEventArgs e)
    {
        if (!DetailChevron.IsVisible)
        {
            return;
        }
        if (OptionsPanel.IsVisible)
        {
            OnCloseOptionsClicked(this, EventArgs.Empty);
        }
        GpxPanel.IsVisible = ViewsPanel.IsVisible = ProfilePanel.IsVisible = LegendPanel.IsVisible = false;
        DetailPanel.IsVisible = true;
    }

    private void OnShowLegendClicked(object? sender, EventArgs e)
    {
        if (OptionsPanel.IsVisible)
        {
            OnCloseOptionsClicked(this, EventArgs.Empty);
        }
        DetailPanel.IsVisible = GpxPanel.IsVisible = ViewsPanel.IsVisible = ProfilePanel.IsVisible = false;
        LegendPanel.IsVisible = true;
    }

    private void OnCloseLegendClicked(object? sender, EventArgs e) => LegendPanel.IsVisible = false;

    /// <summary>The phone's back closes an open panel first; it left the app with the detail open (2026-10-27).</summary>
    protected override bool OnBackButtonPressed()
    {
        if (OptionsPanel.IsVisible)
        {
            OnCloseOptionsClicked(this, EventArgs.Empty);
            return true;
        }
        if (DetailPanel.IsVisible || GpxPanel.IsVisible || ViewsPanel.IsVisible || ProfilePanel.IsVisible || LegendPanel.IsVisible)
        {
            DetailPanel.IsVisible = GpxPanel.IsVisible = ViewsPanel.IsVisible = ProfilePanel.IsVisible = LegendPanel.IsVisible = false;
            return true;
        }
        return base.OnBackButtonPressed();
    }

    private void OnCloseDetailClicked(object? sender, EventArgs e) => DetailPanel.IsVisible = false;

    private void OnShowGpxClicked(object? sender, EventArgs e)
    {
        if (OptionsPanel.IsVisible)
        {
            OnCloseOptionsClicked(this, EventArgs.Empty);
        }
        DetailPanel.IsVisible = ViewsPanel.IsVisible = ProfilePanel.IsVisible = LegendPanel.IsVisible = false;
        RefreshGpxPanel();
        GpxPanel.IsVisible = true;
    }

    private void OnCloseGpxClicked(object? sender, EventArgs e) => GpxPanel.IsVisible = false;

    /// <summary>The GPX panel: ready once the ride is judged (its waypoints warn of gusts and rain); waiting until then.</summary>
    private void RefreshGpxPanel()
    {
        var ready = _ride is not null;
        ShareGpx.IsEnabled = OpenGpx.IsEnabled = ready;
        ShareGpx.Opacity = OpenGpx.Opacity = ready ? 1 : 0.4;
        GpxNote.Text = ready
            ? "Al Karoo (app Hammerhead): COMPARTIR. A Garmin Connect: ABRIR CON…"
            : "Esperando el viento y la lluvia: el GPX lleva sus avisos.";
    }

    /// <summary>The GPX to the bike device (phase 7f): written to the app's cache and handed to the system.</summary>
    private const string GpxMediaType = "application/gpx+xml";

    /// <summary>Hammerhead and most apps take a share.</summary>
    private async void OnShareGpxClicked(object? sender, EventArgs e)
    {
        if (WriteGpx() is not { } gpx)
        {
            return;
        }
        try
        {
            await Share.Default.RequestAsync(new ShareFileRequest { Title = gpx.Track.Name, File = new ShareFile(gpx.Path, GpxMediaType) });
        }
        catch (Exception ex)
        {
            Status.Text = $"No se pudo compartir el GPX: {ex.Message}";
        }
    }

    /// <summary>Garmin Connect only registers "open with" for GPX files, not share.</summary>
    private async void OnOpenGpxClicked(object? sender, EventArgs e)
    {
        if (WriteGpx() is not { } gpx)
        {
            return;
        }
        try
        {
            await Launcher.Default.OpenAsync(new OpenFileRequest(gpx.Track.Name, new ReadOnlyFile(gpx.Path, GpxMediaType)));
        }
        catch (Exception ex)
        {
            Status.Text = $"No se pudo abrir el GPX con otra app: {ex.Message}";
        }
    }

    /// <summary>The GPX of the ride judged, as a file in the cache (the same one the console writes).</summary>
    private (string Path, GpxTrack Track)? WriteGpx()
    {
        if (_ride is null)
        {
            return null;
        }
        try
        {
            var gpx = RideGpx.ForTraced(_ride, fromNow: _departureAt is null, Zone);
            var directory = Path.Combine(FileSystem.CacheDirectory, "gpx");
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, gpx.FileName);
            using (var stream = File.Create(path))
            {
                GpxWriter.Write(gpx.Track, stream);
            }
            return (path, gpx.Track);
        }
        catch (Exception ex)
        {
            Status.Text = $"No se pudo escribir el GPX: {ex.Message}";
            return null;
        }
    }

    /// <summary>"2 h", "1½ h": short, so it fits a chip at any font size.</summary>
    private static string DurationText(TimeSpan duration) =>
        duration.Minutes == 0 ? $"{(int)duration.TotalHours} h" : $"{(int)duration.TotalHours}½ h";

    private void OnOptionsClicked(object? sender, EventArgs e)
    {
        _pendingAt = _departureAt;
        _pendingDuration = _duration;
        _pendingSpeed = _speedKmh;
        BuildDepartureChips();
        BuildRideChips();
        ShowUsage();
        DetailPanel.IsVisible = GpxPanel.IsVisible = ViewsPanel.IsVisible = ProfilePanel.IsVisible = LegendPanel.IsVisible = false;
        OptionsPanel.IsVisible = true;
    }

    /// <summary>The services used this month, busiest first, each with its line from Core.</summary>
    private void ShowUsage()
    {
        UsageLines.Children.Clear();
        var used = UsageText.Used(_usage.Report());
        if (used.Count == 0)
        {
            UsageLines.Children.Add(new Label { Text = "Nada todavía este mes.", FontFamily = "OpenSansRegular", FontSize = 12, TextColor = HudTheme.TextMuted });
        }
        foreach (var row in used)
        {
            var block = new VerticalStackLayout { Spacing = 1 };
            block.Children.Add(new Label { Text = UsageText.Name(row.Service), FontFamily = "OpenSansSemibold", FontSize = 12, TextColor = HudTheme.TextSoft });
            block.Children.Add(new Label
            {
                Text = UsageText.Line(row), FontFamily = "OpenSansRegular", FontSize = 12,
                TextColor = row.Busiest is > 0.8 ? HudTheme.Alert : HudTheme.TextMuted,
            });
            UsageLines.Children.Add(block);
        }
    }

    /// <summary>Closes the panel and, for whatever changed, remembers it and judges everything again.</summary>
    private async void OnCloseOptionsClicked(object? sender, EventArgs e)
    {
        OptionsPanel.IsVisible = false;
        var departureChanged = _pendingAt != _departureAt;
        var rideChanged = _pendingDuration != _duration || _pendingSpeed != _speedKmh;
        if (!departureChanged && !rideChanged)
        {
            return;
        }
        _departureAt = _pendingAt;
        _duration = _pendingDuration;
        _speedKmh = _pendingSpeed;
        Preferences.Default.Set(DurationKey, (int)_duration.TotalMinutes);
        Preferences.Default.Set(SpeedKey, _speedKmh);
        ShowOptionsChip();
        ShowVia();
        if (_via.Count > 0)
        {
            if (rideChanged)
            {
                // The fan for the new duration, then the ride at the new pace.
                await AdviseDirectionAsync();
            }
            await AdviseRideAsync();
        }
        else
        {
            await AdviseDirectionAsync();
        }
    }

    /// <summary>"AHORA · 2 H · 25 KM/H" or "14:00 · 2 H · 25 KM/H": the three choices at a glance.</summary>
    private void ShowOptionsChip() =>
        OptionsChip.Text = $"{(_departureAt is { } at ? at.ToString("HH\\:mm") : "AHORA")} · {DurationText(_duration).ToUpperInvariant()} · {_speedKmh:0} KM/H";

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

    /// <summary>AHORA and the hours left today; with an hour chosen, its quarters.</summary>
    private void BuildDepartureChips()
    {
        HourChips.Children.Clear();
        MinuteChips.Children.Clear();
        var nowLocal = TimeZoneInfo.ConvertTime(_clock.GetUtcNow(), Zone);
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
        DetailChevron.IsVisible = false;
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
        RefreshActions();
        // Only until the rider has put a point in this kind of ride once; then "?" explains the aim.
        Hint.IsVisible = !Preferences.Default.Get(HintSeenKey, false);
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
        _markLayer.Features = route is null
            ? []
            : RouteMarks.Along(route).Select(m =>
            {
                var mark = new PointFeature(ToMercator(m.Position));
                mark.Styles.Add(MarkStyle(m.BearingDegrees));
                return mark;
            }).ToList();
        _markLayer.DataHasChanged();
        if (route is null)
        {
            _reachedLayer.Features = [];
            _snapLayer.Features = [];
            _reachedLayer.DataHasChanged();
            _snapLayer.DataHasChanged();
            ShowRideWind(RideMarks.None);
        }
        MapView.Map.RefreshGraphics();
    }

    private void ShowRideWind(RideMarks marks)
    {
        _windHaloLayer.Features = marks.Arrows.SelectMany(ArrowFeatures).ToList();
        _windHaloLayer.DataHasChanged();
        _windLayer.Features = _windHaloLayer.Features;
        _windLayer.DataHasChanged();
        _passLayer.Features = marks.Passes.Select(p =>
        {
            var mark = new PointFeature(ToMercator(p.Road));
            // The offset turns with the symbol: +x is to the right of where the arrow points, the rider's right.
            mark.Styles.Add(PassStyle(p, new Offset(PassAsidePixels, 0)));
            return (IFeature)mark;
        }).ToList();
        _passLayer.DataHasChanged();
        // The colored marks take the place of the white ones of the way round.
        if (marks.Passes.Count > 0)
        {
            _markLayer.Features = [];
            _markLayer.DataHasChanged();
        }
        _dropLayer.Features = marks.Drops.Select(d =>
        {
            var drop = new PointFeature(ToMercator(d.Road));
            drop.Styles.Add(DropStyle(Aside(d.SideDegrees, DropBesidePassPixels)));
            return (IFeature)drop;
        }).ToList();
        _dropLayer.DataHasChanged();
        // No km labels any more: they covered the marks (2026-10-27); the km are in the detail.
        MapView.Map.RefreshGraphics();
        // The marks' and drops' images are only loaded when the map fetches its layers' data, which a repaint does not
        // do: they stayed out until the map moved (2026-10-27). Fetch now, and again once the images are in.
        MapView.Map.Refresh(ChangeType.Discrete);
        foreach (var ms in new[] { 500, 2000 })
        {
            Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(ms), () => MapView.Map.Refresh(ChangeType.Discrete));
        }
    }

    /// <summary>How far off the road, on screen, a pass's mark stands; its drop goes beyond it.</summary>
    private const double PassAsidePixels = 15;

    private const double DropBesidePassPixels = 36;

    /// <summary>A screen offset of some pixels towards a bearing (the map is north up).</summary>
    private static Offset Aside(double bearingDegrees, double pixels) =>
        new(Math.Sin(bearingDegrees * Math.PI / 180) * pixels, -Math.Cos(bearingDegrees * Math.PI / 180) * pixels);

    /// <summary>A mark of the way round: a small arrow pointing where the rider goes, colored by how the wind feels, dark-rimmed.</summary>
    private static ImageStyle PassStyle(PassMark pass, Offset offset)
    {
        var color = pass.Feel switch
        {
            WindFeel.Headwind => HudTheme.Alert,
            WindFeel.Tailwind => HudTheme.Good,
            WindFeel.Light => HudTheme.TextMuted,
            _ => HudTheme.Marker,
        };
        // A small arrow pointing up, turned to where the rider goes (triangles read as a jumble, 2026-10-27).
        return new ImageStyle
        {
            Offset = offset,
            SymbolRotation = pass.BearingDegrees,
            RotateWithMap = true,
            Image = new Mapsui.Styles.Image
            {
                Source = "svg-content://<svg xmlns='http://www.w3.org/2000/svg' width='14' height='20' viewBox='0 0 14 20'>"
                    + $"<path d='M7 1 L13 9 L9.2 9 L9.2 19 L4.8 19 L4.8 9 L1 9 Z' fill='{color.ToHex()}' stroke='#0B1218' stroke-width='1.5' stroke-linejoin='round'/></svg>",
            },
        };
    }

    /// <summary>Likely rain when passing there: a small blue drop (a red one looked like blood, 2026-10-27) with a dark rim.</summary>
    private static ImageStyle DropStyle(Offset offset) => new()
    {
        Offset = offset,
        Image = new Mapsui.Styles.Image
        {
            Source = "svg-content://<svg xmlns='http://www.w3.org/2000/svg' width='18' height='24' viewBox='0 0 26 34'>"
                + $"<path d='M13 2 C13 2 2.5 15 2.5 22 a10.5 10.5 0 0 0 21 0 C23.5 15 13 2 13 2 Z' fill='#2F6BFF' stroke='#0B1218' stroke-width='2.5'/>"
                + "<path d='M8 22 a5 5 0 0 0 4 5' fill='none' stroke='#E8EEF4' stroke-width='2' stroke-linecap='round'/></svg>",
        },
    };

    /// <summary>The shaft and the head of an arrow, as two lines.</summary>
    private static IEnumerable<IFeature> ArrowFeatures(WindArrow arrow) =>
    [
        new GeometryFeature(new LineString([ToNts(arrow.Tail), ToNts(arrow.Tip)])),
        new GeometryFeature(new LineString([ToNts(arrow.HeadLeft), ToNts(arrow.Tip), ToNts(arrow.HeadRight)])),
    ];

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

    /// <summary>A mark of the way round: a light triangle with a dark rim, pointing where the rider goes.</summary>
    private static SymbolStyle MarkStyle(double bearingDegrees) => new()
    {
        SymbolType = SymbolType.Triangle,
        SymbolScale = 0.45,
        SymbolRotation = bearingDegrees,
        RotateWithMap = true,
        Fill = new Mapsui.Styles.Brush(ToMapsui(HudTheme.Text, 255)),
        Outline = new Pen(ToMapsui(HudTheme.Background, 255), 1.5),
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
