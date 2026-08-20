using System.Drawing;
using System.IO;
using System.Windows.Forms;
using System.Windows.Resources;
using Application = System.Windows.Application;

namespace SpinePet.Services;

public sealed class TrayIconService : IDisposable
{
    private static readonly Uri TrayIconUri = new(
        "pack://application:,,,/Assets/Brand/SpinePet.ico",
        UriKind.Absolute);

    private readonly Action _openPanel;
    private readonly Action _showAll;
    private readonly Action _hideAll;
    private readonly Action _exit;
    private NotifyIcon? _notifyIcon;
    private ContextMenuStrip? _contextMenu;
    private Icon? _trayIconImage;

    public TrayIconService(
        Action openPanel,
        Action showAll,
        Action hideAll,
        Action exit)
    {
        _openPanel = openPanel;
        _showAll = showAll;
        _hideAll = hideAll;
        _exit = exit;
    }

    public void Initialize()
    {
        if (_notifyIcon != null)
        {
            return;
        }

        ContextMenuStrip contextMenu = new();
        Icon trayIconImage = LoadTrayIcon();
        NotifyIcon notifyIcon = new()
        {
            Text = "SpinePet - Render Mode",
            Icon = trayIconImage,
            ContextMenuStrip = contextMenu
        };

        try
        {
            contextMenu.Items.Add("Open Panel", null, (_, _) => _openPanel());
            contextMenu.Items.Add(new ToolStripSeparator());
            contextMenu.Items.Add("Show All", null, (_, _) => _showAll());
            contextMenu.Items.Add("Hide All", null, (_, _) => _hideAll());
            contextMenu.Items.Add(new ToolStripSeparator());
            contextMenu.Items.Add("Exit", null, (_, _) => _exit());

            notifyIcon.DoubleClick += (_, _) => _openPanel();
            notifyIcon.Visible = true;

            _contextMenu = contextMenu;
            _trayIconImage = trayIconImage;
            _notifyIcon = notifyIcon;
        }
        catch
        {
            notifyIcon.Dispose();
            trayIconImage.Dispose();
            contextMenu.Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        if (_notifyIcon == null)
        {
            return;
        }

        try
        {
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
        }
        finally
        {
            _notifyIcon = null;
            _trayIconImage?.Dispose();
            _trayIconImage = null;
            _contextMenu?.Dispose();
            _contextMenu = null;
        }

        GC.SuppressFinalize(this);
    }

    private static Icon LoadTrayIcon()
    {
        StreamResourceInfo? resource = Application.GetResourceStream(TrayIconUri);
        if (resource == null)
        {
            throw new InvalidOperationException(
                $"Tray icon resource was not found: {TrayIconUri}");
        }

        using Stream stream = resource.Stream;
        using Icon sourceIcon = new(stream, SystemInformation.SmallIconSize);
        return (Icon)sourceIcon.Clone();
    }
}
