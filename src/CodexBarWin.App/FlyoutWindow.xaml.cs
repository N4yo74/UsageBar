using System.Windows;

namespace CodexBarWin.App;

/// <summary>
/// Borderless flyout panel anchored near the tray icon (bottom-right of the work area).
/// Hides itself automatically when it loses focus, like a typical tray flyout.
/// </summary>
public partial class FlyoutWindow : Window
{
    public FlyoutWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;

        Deactivated += (_, _) => Hide();
    }

    /// <summary>Positions the window at the bottom-right of the work area and shows it.</summary>
    public void ShowNearTray()
    {
        var workArea = SystemParameters.WorkArea;
        const double margin = 12;

        // Measure first so Height reflects current content (SizeToContent="Height").
        Show();
        UpdateLayout();

        Left = workArea.Right - Width - margin;
        Top = workArea.Bottom - ActualHeight - margin;

        Activate();
    }

    public void ToggleVisibility()
    {
        if (IsVisible)
        {
            Hide();
        }
        else
        {
            ShowNearTray();
        }
    }
}
