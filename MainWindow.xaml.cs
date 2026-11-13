using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WindowSizer.Models;
using WindowSizer.Services;

namespace WindowSizer;

public partial class MainWindow : Window
{
    private const decimal MaximumPercentage = 1000m;

    private readonly WindowService _windowService = new();
    private WindowInfo? _selectedWindow;
    private int? _pendingWidth;
    private int? _pendingHeight;
    private bool _suppressPercentageChanged;

    public MainWindow()
    {
        InitializeComponent();
        RefreshWindows();
    }

    private void Refresh_Click(object sender, RoutedEventArgs e)
    {
        RefreshWindows();
    }

    private void RefreshWindows()
    {
        var previousHandle = _selectedWindow?.Handle ?? IntPtr.Zero;

        try
        {
            var windows = _windowService.GetOpenWindows();
            WindowList.ItemsSource = windows;
            WindowCountText.Text = windows.Count == 1 ? "1 window" : $"{windows.Count} windows";
            EmptyWindowsText.Visibility = windows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

            var previousSelection = windows.FirstOrDefault(window => window.Handle == previousHandle);
            if (previousSelection is not null)
            {
                WindowList.SelectedItem = previousSelection;
            }
            else
            {
                UpdateSelection(null);
            }

            SetStatus($"Found {windows.Count} visible windows. Select one to set its client-area size.");
        }
        catch (Exception exception)
        {
            WindowList.ItemsSource = Array.Empty<WindowInfo>();
            WindowCountText.Text = "Unavailable";
            EmptyWindowsText.Visibility = Visibility.Visible;
            UpdateSelection(null);
            SetStatus($"Could not list windows: {exception.Message}", isError: true);
        }
    }

    private void WindowList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateSelection(WindowList.SelectedItem as WindowInfo);
    }

    private void UpdateSelection(WindowInfo? window)
    {
        _selectedWindow = window;
        _pendingWidth = null;
        _pendingHeight = null;

        _suppressPercentageChanged = true;
        PercentageBox.Text = string.Empty;
        _suppressPercentageChanged = false;

        if (window is null)
        {
            SelectedTitleText.Text = "Select a window";
            SelectedProcessText.Text = "Window details will appear here.";
            CurrentSizeText.Text = "Current client area: —";
            DisplayResolutionText.Text = "Display resolution: —";
            PreviewText.Text = "Choose a preset or enter a percentage";
            PreviewBasisText.Text = "The preview is shown in pixels.";
            PresetPanel.IsEnabled = false;
            PercentagePanel.IsEnabled = false;
            ResizeButton.IsEnabled = false;
            return;
        }

        SelectedTitleText.Text = window.Title;
        SelectedProcessText.Text = window.ProcessName;
        CurrentSizeText.Text = $"Current client area: {window.ClientWidth:N0} × {window.ClientHeight:N0} px";

        if (window.DisplayWidth > 0 && window.DisplayHeight > 0)
        {
            DisplayResolutionText.Text =
                $"Display resolution: {window.DisplayWidth:N0} × {window.DisplayHeight:N0} px  ·  {window.DisplayName}";
            PercentagePanel.IsEnabled = true;
        }
        else
        {
            DisplayResolutionText.Text = "Display resolution could not be detected; presets are still available.";
            PercentagePanel.IsEnabled = false;
        }

        PreviewText.Text = "Choose a preset or enter a percentage";
        PreviewBasisText.Text = "The preview is shown in client-area pixels.";
        PresetPanel.IsEnabled = true;
        ResizeButton.IsEnabled = false;
    }

    private void Preset_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedWindow is null || sender is not Button { Tag: string tag })
        {
            return;
        }

        var parts = tag.Split(',');
        if (parts.Length != 2
            || !int.TryParse(parts[0], out var width)
            || !int.TryParse(parts[1], out var height))
        {
            return;
        }

        _suppressPercentageChanged = true;
        PercentageBox.Text = string.Empty;
        _suppressPercentageChanged = false;

        SetPendingSize(width, height, "Preset size · client area");
    }

    private void PercentageBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppressPercentageChanged || _selectedWindow is null)
        {
            return;
        }

        var text = PercentageBox.Text.Trim();
        if (text.Length == 0)
        {
            ClearPendingSize();
            return;
        }

        if (!decimal.TryParse(text, NumberStyles.AllowDecimalPoint, CultureInfo.CurrentCulture, out var percentage)
            && !decimal.TryParse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out percentage))
        {
            ShowInvalidPercentage("Enter a number, for example 75.");
            return;
        }

        if (percentage <= 0 || percentage > MaximumPercentage)
        {
            ShowInvalidPercentage("Enter a percentage greater than 0 and no more than 1000.");
            return;
        }

        var displayWidth = _selectedWindow.DisplayWidth;
        var displayHeight = _selectedWindow.DisplayHeight;
        if (displayWidth <= 0 || displayHeight <= 0)
        {
            ShowInvalidPercentage("The selected window's display resolution is unavailable.");
            return;
        }

        try
        {
            var width = decimal.ToInt32(decimal.Round(
                displayWidth * percentage / 100m,
                0,
                MidpointRounding.AwayFromZero));
            var height = decimal.ToInt32(decimal.Round(
                displayHeight * percentage / 100m,
                0,
                MidpointRounding.AwayFromZero));

            if (width <= 0 || height <= 0)
            {
                ShowInvalidPercentage("That percentage rounds to less than one pixel.");
                return;
            }

            SetPendingSize(
                width,
                height,
                $"{percentage:0.##}% of {_selectedWindow.DisplayWidth:N0} × {_selectedWindow.DisplayHeight:N0} on {_selectedWindow.DisplayName}");
        }
        catch (OverflowException)
        {
            ShowInvalidPercentage("The resulting pixel dimensions are too large.");
        }
    }

    private void SetPendingSize(int width, int height, string basis)
    {
        _pendingWidth = width;
        _pendingHeight = height;
        PreviewText.Text = $"{width:N0} × {height:N0} px";
        PreviewBasisText.Text = basis;
        ResizeButton.IsEnabled = _selectedWindow is not null;
    }

    private void ClearPendingSize()
    {
        _pendingWidth = null;
        _pendingHeight = null;
        PreviewText.Text = "Choose a preset or enter a percentage";
        PreviewBasisText.Text = "The preview is shown in client-area pixels.";
        ResizeButton.IsEnabled = false;
    }

    private void ShowInvalidPercentage(string message)
    {
        _pendingWidth = null;
        _pendingHeight = null;
        PreviewText.Text = "Preview unavailable";
        PreviewBasisText.Text = message;
        ResizeButton.IsEnabled = false;
    }

    private void Resize_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedWindow is null || _pendingWidth is not int width || _pendingHeight is not int height)
        {
            return;
        }

        var selectedHandle = _selectedWindow.Handle;
        try
        {
            var result = _windowService.ResizeClientArea(selectedHandle, width, height);
            RefreshWindows();

            if (result.MatchesRequest)
            {
                SetStatus($"Resized the selected window to {result.ActualWidth:N0} × {result.ActualHeight:N0} client pixels.");
            }
            else
            {
                SetStatus(
                    $"The window accepted the resize but reports {result.ActualWidth:N0} × {result.ActualHeight:N0} client pixels instead of {result.RequestedWidth:N0} × {result.RequestedHeight:N0}. The app may enforce its own size limits.",
                    isError: true);
            }
        }
        catch (Exception exception)
        {
            SetStatus(exception.Message, isError: true);
        }
    }

    private void SetStatus(string message, bool isError = false)
    {
        StatusText.Text = message;
        StatusText.Foreground = isError
            ? new SolidColorBrush(Color.FromRgb(176, 56, 56))
            : (Brush)FindResource("MutedBrush");
    }
}
