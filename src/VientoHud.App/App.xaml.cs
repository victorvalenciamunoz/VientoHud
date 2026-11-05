using VientoHud.App.Hud;

namespace VientoHud.App;

public partial class App : Application
{
    public App()
    {
        InitializeComponent();
        // Before any page is built, so their DynamicResource colors are there from the first frame.
        HudTheme.Apply(this);
        // A HUD of the family: always dark, whatever the system theme.
        UserAppTheme = AppTheme.Dark;
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        var window = new Window(new AppShell());
        // The counts of what the app took from each service, on disk when it leaves the screen (phase 9); and the map
        // caches back under their cap then and at start.
        window.Stopped += (_, _) =>
        {
            Handler?.MauiContext?.Services.GetService<VientoHud.Core.Net.UsageMeter>()?.Save();
            TrimMapCaches();
        };
        window.Created += (_, _) => TrimMapCaches();
        return window;
    }

    /// <summary>
    /// The 3D view's tiles and the map's, each under 50 MB, deleting what was used longest ago (phase 9, step 3), off
    /// the UI thread. The radar keeps its own 3 h; the GPX files are tiny.
    /// </summary>
    private static void TrimMapCaches() => _ = Task.Run(() =>
    {
        foreach (var folder in new[] { "flyover", "tiles" })
        {
            VientoHud.Core.Net.CacheLimit.Trim(Path.Combine(FileSystem.CacheDirectory, folder));
        }
    });
}
