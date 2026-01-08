using System.Windows;

namespace PicView;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var mainWindow = new MainWindow();
        
        // If an image path was passed as argument, open it
        if (e.Args.Length > 0 && !string.IsNullOrEmpty(e.Args[0]))
        {
            mainWindow.LoadImage(e.Args[0]);
        }
        
        mainWindow.Show();
    }
}
