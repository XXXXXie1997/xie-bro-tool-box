using System.Windows;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace XieBox.Services;

public sealed class TrayIcon : IDisposable
{
    private readonly Drawing.Icon _image;
    private readonly Forms.ContextMenuStrip _menu;
    private readonly Forms.NotifyIcon _icon;
    private bool _hintShown;
    public bool IsVisible => _icon.Visible;

    public TrayIcon(Action restore, Action exit)
    {
        using var resource = Application.GetResourceStream(new Uri("/XieBroToolBox;component/Resources/app.ico", UriKind.Relative))!.Stream;
        _image = new Drawing.Icon(resource, Forms.SystemInformation.SmallIconSize);
        _menu = new Forms.ContextMenuStrip();
        _menu.Items.Add("打开蟹小箱", null, (_, _) => restore());
        _menu.Items.Add(new Forms.ToolStripSeparator());
        _menu.Items.Add("退出", null, (_, _) => exit());
        _icon = new Forms.NotifyIcon { Icon = _image, Text = "蟹小箱", ContextMenuStrip = _menu, Visible = true };
        _icon.DoubleClick += (_, _) => restore();
        _icon.BalloonTipClicked += (_, _) => restore();
    }

    public void ShowBackgroundHint()
    {
        if (_hintShown) return;
        _hintShown = true;
        _icon.ShowBalloonTip(3000, "蟹小箱仍在后台运行", "双击托盘图标重新打开，右键菜单可退出。", Forms.ToolTipIcon.Info);
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
        _menu.Dispose();
        _image.Dispose();
    }
}
