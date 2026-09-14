using System.Windows;
using Forms = System.Windows.Forms;

namespace TopMediaBar;

public partial class App : System.Windows.Application
{
    private Forms.NotifyIcon? _trayIcon;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _trayIcon = new Forms.NotifyIcon
        {
            Icon = System.Drawing.SystemIcons.Application,
            Visible = true,
            Text = "Top Media Bar"
        };

        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("ออกจากโปรแกรม", null, (_, _) => Shutdown());
        _trayIcon.ContextMenuStrip = menu;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _trayIcon?.Dispose();
        base.OnExit(e);
    }
}
