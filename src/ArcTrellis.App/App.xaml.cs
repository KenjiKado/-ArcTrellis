using System.Windows;

namespace ArcTrellis.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        Loc.Initialize();
        string? requestedLanguage = e.Args.FirstOrDefault(x => x.StartsWith("--language=", StringComparison.OrdinalIgnoreCase))?.Split('=', 2).LastOrDefault();
        if (!string.IsNullOrWhiteSpace(requestedLanguage)) Loc.SetLanguage(requestedLanguage);
        base.OnStartup(e);
        DispatcherUnhandledException += (_, args) =>
        {
            MessageBox.Show(args.Exception.Message, "ArcTrellis", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };
    }
}
