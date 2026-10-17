using System;
using System.IO;
using System.Windows;

namespace ColiseuDesk
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            try
            {
                var win = new MainWindow();
                MainWindow = win;
                win.Show();
                win.Activate();
            }
            catch (Exception ex)
            {
                var logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "startup-error.log");
                File.WriteAllText(logPath, ex.ToString());
                MessageBox.Show(ex.ToString(), "ColiseuDesk Startup Error");
            }
        }
    }
}
