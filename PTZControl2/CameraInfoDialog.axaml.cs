using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;

namespace PTZControl2;

public sealed partial class CameraInfoDialog : Window
{
    private readonly DispatcherTimer _refreshTimer;
    private Func<string>? _infoProvider;
    private TextBox _infoText = null!;
    private TextBlock _refreshStatusText = null!;
    private int _consecutiveRefreshErrors;

    public CameraInfoDialog()
    {
        InitializeComponent();
        _refreshTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _refreshTimer.Tick += (_, _) => RefreshInfo();
        Opened += (_, _) =>
        {
            RefreshInfo();
            _refreshTimer.Start();
        };
        Closed += (_, _) => _refreshTimer.Stop();
    }

    public Func<string>? InfoProvider
    {
        get => _infoProvider;
        set
        {
            _infoProvider = value;
            RefreshInfo();
        }
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
        _infoText = this.FindControl<TextBox>("InfoText")
            ?? throw new System.InvalidOperationException("InfoText control not found.");
        _refreshStatusText = this.FindControl<TextBlock>("RefreshStatusText")
            ?? throw new System.InvalidOperationException("RefreshStatusText control not found.");
    }

    private void RefreshNowButton_Click(object? sender, RoutedEventArgs e) => RefreshInfo();

    private void CloseButton_Click(object? sender, RoutedEventArgs e) => Close();

    private void RefreshInfo()
    {
        if (_infoProvider is null)
            return;

        try
        {
            _infoText.Text = _infoProvider();
            _consecutiveRefreshErrors = 0;
            _refreshStatusText.Text = $"Updated {DateTime.Now:HH:mm:ss}";
        }
        catch (Exception ex)
        {
            _consecutiveRefreshErrors++;
            _refreshStatusText.Text = $"Refresh failed ({_consecutiveRefreshErrors})";
            _infoText.Text = $"Camera monitor refresh failed.{Environment.NewLine}{ex.Message}";
        }
    }
}
