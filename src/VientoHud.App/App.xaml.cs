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

    protected override Window CreateWindow(IActivationState? activationState) => new(new AppShell());
}
