using System.Windows;

namespace ArcTrellis.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += (_, args) =>
        {
            MessageBox.Show(args.Exception.Message, "ArcTrellis", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };
    }
}
