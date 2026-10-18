using System;
using System.Windows;

namespace ColiseuDesk
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            DispatcherUnhandledException += (s, args) =>
            {
                args.Handled = true;
            };

            base.OnStartup(e);
            var win = new MainWindow();
            MainWindow = win;
            win.Show();
            win.Activate();
        }
    }
}
