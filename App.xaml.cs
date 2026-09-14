using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using Forms = System.Windows.Forms;

namespace TopMediaBar;

public partial class App : System.Windows.Application
{
    private const string StartupShortcutName = "TopMediaBar.lnk";

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
        var startupItem = new Forms.ToolStripMenuItem("เปิดโปรแกรมอัตโนมัติตอนเปิดเครื่อง")
        {
            CheckOnClick = true,
            Checked = IsStartupEnabled
        };
        startupItem.Click += (_, _) =>
        {
            try
            {
                SetStartupEnabled(startupItem.Checked);
            }
            catch
            {
                startupItem.Checked = IsStartupEnabled;
                Forms.MessageBox.Show(
                    "ตั้งค่าเปิดอัตโนมัติตอนเปิดเครื่องไม่สำเร็จ",
                    "Top Media Bar",
                    Forms.MessageBoxButtons.OK,
                    Forms.MessageBoxIcon.Warning);
            }
        };
        menu.Items.Add(startupItem);
        menu.Items.Add("ออกจากโปรแกรม", null, (_, _) => Shutdown());
        _trayIcon.ContextMenuStrip = menu;

        if (!File.Exists(FirstRunMarkerPath))
        {
            var result = Forms.MessageBox.Show(
                "ต้องการให้ Top Media Bar เปิดอัตโนมัติทุกครั้งที่เปิดเครื่องไหม?",
                "Top Media Bar",
                Forms.MessageBoxButtons.YesNo,
                Forms.MessageBoxIcon.Question);
            try
            {
                SetStartupEnabled(result == Forms.DialogResult.Yes);
                startupItem.Checked = result == Forms.DialogResult.Yes;
            }
            catch
            {
                // Leave unchecked/unset; user can still toggle it from the tray menu later.
            }

            Directory.CreateDirectory(Path.GetDirectoryName(FirstRunMarkerPath)!);
            File.WriteAllText(FirstRunMarkerPath, "");
        }
    }

    private static string StartupShortcutPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.Startup), StartupShortcutName);

    private static string FirstRunMarkerPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "TopMediaBar", "startup-prompted.flag");

    private static bool IsStartupEnabled => File.Exists(StartupShortcutPath);

    /// <summary>
    /// Creates/removes a .lnk in the user's Startup folder via the built-in WScript.Shell COM
    /// object (late-bound, so no extra package/reference is needed just to author a shortcut).
    /// </summary>
    private static void SetStartupEnabled(bool enabled)
    {
        if (!enabled)
        {
            if (File.Exists(StartupShortcutPath)) File.Delete(StartupShortcutPath);
            return;
        }

        string exePath = Environment.ProcessPath
            ?? throw new InvalidOperationException("ไม่พบพาธของโปรแกรมปัจจุบัน");

        Type shellType = Type.GetTypeFromProgID("WScript.Shell")
            ?? throw new InvalidOperationException("ไม่พบ WScript.Shell");
        object shell = Activator.CreateInstance(shellType)!;
        try
        {
            object shortcut = shellType.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod,
                null, shell, [StartupShortcutPath])!;
            Type shortcutType = shortcut.GetType();
            try
            {
                shortcutType.InvokeMember("TargetPath", BindingFlags.SetProperty, null, shortcut, [exePath]);
                shortcutType.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, shortcut,
                    [Path.GetDirectoryName(exePath) ?? ""]);
                shortcutType.InvokeMember("Save", BindingFlags.InvokeMethod, null, shortcut, null);
            }
            finally
            {
                Marshal.ReleaseComObject(shortcut);
            }
        }
        finally
        {
            Marshal.ReleaseComObject(shell);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _trayIcon?.Dispose();
        base.OnExit(e);
    }
}
