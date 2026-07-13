using System.Windows.Forms;
using System.Windows.Threading;
using CodexBarWin.Core;

namespace CodexBarWin.App;

/// <summary>
/// Owns the notification-area icon, its context menu, the flyout window, and the
/// 45-second auto-refresh timer. This is the composition root for the tray app.
/// </summary>
public sealed class TrayIconManager : IDisposable
{
    private readonly ClaudeWebViewService _claudeWebViewService;
    private readonly MainViewModel _viewModel;
    private readonly FlyoutWindow _flyoutWindow;
    private readonly NotifyIcon _notifyIcon;
    private readonly DispatcherTimer _timer;

    public TrayIconManager()
    {
        _claudeWebViewService = new ClaudeWebViewService();
        _viewModel = new MainViewModel(new UsageService(), _claudeWebViewService);
        _viewModel.TooltipChanged += (_, _) => UpdateTooltip();

        _flyoutWindow = new FlyoutWindow(_viewModel);

        _notifyIcon = new NotifyIcon
        {
            Icon = TrayIconFactory.CreateIcon(),
            Text = "CodexBarWin - 読み込み中...",
            Visible = true,
            ContextMenuStrip = BuildContextMenu(),
        };
        _notifyIcon.MouseClick += OnTrayIconMouseClick;

        _timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(45),
        };
        _timer.Tick += (_, _) => _viewModel.Refresh();
    }

    /// <summary>Kicks off the first refresh and starts the auto-refresh timer.</summary>
    public void Start()
    {
        _viewModel.Refresh();
        _timer.Start();
    }

    private void OnTrayIconMouseClick(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left)
        {
            return;
        }

        if (_flyoutWindow.IsVisible)
        {
            _flyoutWindow.Hide();
            return;
        }

        // Refresh immediately whenever the flyout is opened, per spec.
        _viewModel.Refresh();
        _flyoutWindow.ShowNearTray();
    }

    private ContextMenuStrip BuildContextMenu()
    {
        var menu = new ContextMenuStrip();

        var refreshItem = new ToolStripMenuItem("今すぐ更新");
        refreshItem.Click += (_, _) => _viewModel.Refresh();

        var claudeLoginItem = new ToolStripMenuItem("Claude連携（ログイン）");
        claudeLoginItem.Click += (_, _) => _claudeWebViewService.ShowLoginWindow();

        var startupItem = new ToolStripMenuItem("Windowsと同時に起動")
        {
            CheckOnClick = true,
            Checked = StartupManager.IsEnabled(),
        };
        startupItem.CheckedChanged += (_, _) => StartupManager.SetEnabled(startupItem.Checked);

        var exitItem = new ToolStripMenuItem("終了");
        exitItem.Click += (_, _) => System.Windows.Application.Current.Shutdown();

        menu.Items.Add(refreshItem);
        menu.Items.Add(claudeLoginItem);
        menu.Items.Add(startupItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(exitItem);

        return menu;
    }

    private void UpdateTooltip()
    {
        // NotifyIcon.Text has a historical 63-char limit on older Windows; keep it safely short.
        var text = _viewModel.TooltipText;
        _notifyIcon.Text = text.Length <= 63 ? text : text[..63];
    }

    public void Dispose()
    {
        _timer.Stop();
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _flyoutWindow.Close();
        _claudeWebViewService.Dispose();
    }
}
