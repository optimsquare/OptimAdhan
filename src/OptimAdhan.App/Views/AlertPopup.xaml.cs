using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using OptimAdhan.App.Services;
using OptimAdhan.App.ViewModels;
using OptimAdhan.Core.Scheduling;

namespace OptimAdhan.App.Views;

/// <summary>
/// Frosted-glass panel in the corner of the screen for Adhan and Ramadan
/// alerts. Does not take focus, so it never interrupts typing. Unlike Windows
/// notifications it is not silenced by Focus Assist, which matters for the Adhan.
/// </summary>
public partial class AlertPopup : Window
{
    private const int GwlExStyle = -20;
    private const int WsExToolWindow = 0x00000080;
    private const int WsExNoActivate = 0x08000000;

    private static AlertPopup? _current;
    private readonly AppController _app;
    private readonly DispatcherTimer _autoClose = new();

    private AlertPopup(AppController app)
    {
        InitializeComponent();
        _app = app;
        _app.AudioStateChanged += OnAudioStateChanged;
        _autoClose.Tick += (_, _) => Close();
        SourceInitialized += (_, _) => ApplyMaterial();
        Closed += (_, _) =>
        {
            _autoClose.Stop();
            _app.AudioStateChanged -= OnAudioStateChanged;
            if (_current == this)
            {
                _current = null;
            }
        };
    }

    public static void ShowFor(AlertOccurrence occurrence, AppController app)
    {
        _current?.Close();
        var popup = new AlertPopup(app);
        var alert = occurrence.Alert;
        var (title, body) = AlertText.For(occurrence, app);
        popup.TitleText.Text = title;
        popup.BodyText.Text = body;
        popup.BodyText.Visibility = string.IsNullOrEmpty(body) ? Visibility.Collapsed : Visibility.Visible;
        popup.KindText.Text = alert.Kind switch
        {
            AlertKind.Adhan when occurrence.Delivery == AlertDelivery.Missed => "MISSED ADHAN",
            AlertKind.Adhan => $"ADHAN · {app.Schedule?.Location.DisplayName.Split(',')[0].ToUpperInvariant()}",
            AlertKind.SehriReminder => "SEHRI",
            AlertKind.IftarReminder => "IFTAR",
            _ => "REMINDER",
        };
        popup.ArabicText.Text = alert.Kind == AlertKind.Adhan && alert.Prayer is { } p ? PrayerNames.Arabic(p) : "";
        var playing = occurrence.Delivery == AlertDelivery.Play && app.Audio.IsPlaying;
        popup.NowPlaying.Visibility = playing && alert.Kind == AlertKind.Adhan ? Visibility.Visible : Visibility.Collapsed;
        popup.SoundText.Text = $"{app.Sounds.Adhans.FirstOrDefault(s => s.Id == alert.SoundId)?.Name ?? "Adhan"} Adhan is playing";
        popup.StopButton.Content = alert.Kind == AlertKind.Adhan ? "Stop Adhan" : "Stop";
        popup.Edge.BorderBrush = new SolidColorBrush(alert.Kind == AlertKind.Adhan && occurrence.Delivery != AlertDelivery.Missed
            ? Color.FromArgb(0x73, 0xFF, 0xD6, 0x5A)
            : Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF));
        popup.UpdateStopButton();

        // Adhan stays while it plays; other alerts close on their own.
        popup._autoClose.Interval = alert.Kind == AlertKind.Adhan && occurrence.Delivery == AlertDelivery.Play
            ? TimeSpan.FromMinutes(10)
            : TimeSpan.FromSeconds(occurrence.Delivery == AlertDelivery.Missed ? 60 : 20);
        popup._autoClose.Start();

        _current = popup;
        popup.Show();
        popup.PositionInCorner();
    }

    private void ApplyMaterial()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        SetWindowLong(hwnd, GwlExStyle, GetWindowLong(hwnd, GwlExStyle) | WsExToolWindow | WsExNoActivate);
        WindowEffects.UseDarkFrame(hwnd);
        WindowEffects.RoundCorners(hwnd);
        if (WindowEffects.TrySetBackdrop(this, WindowEffects.Backdrop.Acrylic))
        {
            WindowEffects.KeepVisuallyActive(this);
        }
        else
        {
            HwndSource.FromHwnd(hwnd)!.CompositionTarget.BackgroundColor = Color.FromRgb(0x0B, 0x1C, 0x3C);
        }
    }

    private void PositionInCorner()
    {
        var area = SystemParameters.WorkArea;
        Left = area.Right - ActualWidth - 12;
        var finalTop = area.Bottom - ActualHeight - 12;
        if (WindowEffects.AnimationsEnabled)
        {
            // Rise gently into place.
            BeginAnimation(TopProperty, new DoubleAnimation(finalTop + 24, finalTop, TimeSpan.FromMilliseconds(260))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
                FillBehavior = FillBehavior.Stop,
            });
        }

        Top = finalTop;
    }

    private void UpdateStopButton() =>
        StopButton.Visibility = _app.Audio.IsPlaying ? Visibility.Visible : Visibility.Collapsed;

    private void OnAudioStateChanged(object? sender, EventArgs e)
    {
        UpdateStopButton();
        if (!_app.Audio.IsPlaying)
        {
            NowPlaying.Visibility = Visibility.Collapsed;
            if (_autoClose.Interval > TimeSpan.FromSeconds(30))
            {
                // Adhan finished: linger briefly, then close.
                _autoClose.Stop();
                _autoClose.Interval = TimeSpan.FromSeconds(30);
                _autoClose.Start();
            }
        }
    }

    private void OnStop(object sender, RoutedEventArgs e) => _app.StopAudio();

    private void OnClose(object sender, RoutedEventArgs e)
    {
        _app.StopAudio();
        Close();
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern int GetWindowLong(IntPtr hwnd, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")]
    private static extern int SetWindowLong(IntPtr hwnd, int index, int value);
}
