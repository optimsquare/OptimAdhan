using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using OptimAdhan.App.Services;
using OptimAdhan.App.ViewModels;
using OptimAdhan.Core.Scheduling;

namespace OptimAdhan.App.Views;

/// <summary>
/// Compact frosted-glass notification beside the notification area. While an
/// Adhan plays it is a "now playing" card that lives exactly as long as that
/// playback: it closes when the audio ends, is stopped or fails. Other alerts
/// close on their own after a short while. It never takes focus, and unlike
/// Windows notifications it is not silenced by Focus Assist.
/// </summary>
public partial class AlertPopup : Window
{
    private const int GwlExStyle = -20;
    private const int WsExToolWindow = 0x00000080;
    private const int WsExNoActivate = 0x08000000;
    private const int EdgeGap = 12;

    private static readonly TimeSpan ErrorLinger = TimeSpan.FromSeconds(6);

    /// <summary>Alerts already shown this session, so the same prayer event never pops up twice.</summary>
    private static readonly HashSet<string> Shown = [];
    private static readonly Queue<string> ShownOrder = new();

    private static AlertPopup? _current;

    private readonly AppController _app;
    private readonly DispatcherTimer _autoClose = new();
    private readonly DispatcherTimer _progressTimer = new(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(200) };
    private string _key = "";
    private int _session = -1; // the playback this card follows; -1 when not a now-playing card
    private bool _closed;
    private Taskbar.Edge _edge = Taskbar.Edge.Bottom;

    private AlertPopup(AppController app)
    {
        InitializeComponent();
        _app = app;
        _app.AudioStateChanged += OnAudioStateChanged;
        _autoClose.Tick += (_, _) => Dismiss();
        _progressTimer.Tick += (_, _) => UpdateProgress();
        SourceInitialized += (_, _) => ApplyMaterial();
        Loaded += (_, _) =>
        {
            PlaceNearNotificationArea();
            AnimateIn();
        };
        // Re-anchor when the card grows or shrinks (the window rect updates after this event).
        SizeChanged += (_, _) => Dispatcher.BeginInvoke(PlaceNearNotificationArea, DispatcherPriority.Loaded);
        Closed += (_, _) =>
        {
            _closed = true;
            _autoClose.Stop();
            _progressTimer.Stop();
            _app.AudioStateChanged -= OnAudioStateChanged;
            if (_current == this)
            {
                _current = null;
            }
        };
    }

    public static void ShowFor(AlertOccurrence occurrence, AppController app)
    {
        var alert = occurrence.Alert;
        if (!alert.IsTest && !Remember(alert.Key))
        {
            Log.Info($"Popup for {alert.Key} already shown; not reopening");
            return;
        }

        _current?.Dismiss();
        var popup = new AlertPopup(app) { _key = alert.Key };
        var (title, body) = AlertText.For(occurrence, app);
        popup.TitleText.Text = title;
        popup.KindText.Text = alert.IsTest ? "OptimAdhan · Test" : "OptimAdhan";
        popup.ArabicText.Text = alert.Kind is AlertKind.Adhan or AlertKind.PrayerReminder && alert.Prayer is { } p
            ? PrayerNames.Arabic(p)
            : "";
        popup.ArabicText.Visibility = popup.ArabicText.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;

        // Bind to the playback that Deliver just started, if it really is playing.
        var nowPlaying = alert.Kind == AlertKind.Adhan && occurrence.Delivery == AlertDelivery.Play && app.Audio.IsPlaying;
        if (nowPlaying)
        {
            popup._session = app.Audio.Session;
            popup.StatusText.Text = "Adhan is playing";
            popup.StartEqualizer();
            popup._progressTimer.Start();
        }
        else
        {
            popup.StatusText.Text = alert.Kind switch
            {
                AlertKind.Adhan when occurrence.Delivery == AlertDelivery.Silent => "Adhan muted",
                AlertKind.Adhan when occurrence.Delivery == AlertDelivery.Play && alert.SoundId is not null => "Adhan sound unavailable",
                AlertKind.Adhan when occurrence.Delivery == AlertDelivery.Play => "Prayer time",
                _ => body,
            };
            popup.ShowAsNotice();
            popup._autoClose.Interval = TimeSpan.FromSeconds(occurrence.Delivery == AlertDelivery.Missed ? 60 : 20);
            popup._autoClose.Start();
        }

        popup.Edge.BorderBrush = new SolidColorBrush(alert.Kind == AlertKind.Adhan && occurrence.Delivery != AlertDelivery.Missed
            ? Color.FromArgb(0x66, 0xFF, 0xD6, 0x5A)
            : Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF));

        // Never cover a full-screen game, video or presentation.
        popup.Topmost = !FullScreenDetector.IsFullScreenActive();

        _current = popup;
        popup.Left = -32000; // off-screen until measured and placed
        popup.Top = -32000;
        popup.Show();
    }

    /// <summary>Closes any open popup (used on exit).</summary>
    public static void CloseCurrent() => _current?.Dismiss();

    private static bool Remember(string key)
    {
        if (!Shown.Add(key))
        {
            return false;
        }

        ShownOrder.Enqueue(key);
        if (ShownOrder.Count > 64)
        {
            Shown.Remove(ShownOrder.Dequeue());
        }

        return true;
    }

    private void Dismiss()
    {
        if (!_closed)
        {
            _closed = true;
            Close();
        }
    }

    /// <summary>No audio to control: a single Hide button and a timed close.</summary>
    private void ShowAsNotice()
    {
        Equalizer.Visibility = Visibility.Collapsed;
        StatusText.Visibility = string.IsNullOrEmpty(StatusText.Text) ? Visibility.Collapsed : Visibility.Visible;
        StopButton.Visibility = Visibility.Collapsed;
        System.Windows.Controls.Grid.SetColumnSpan(HideButton, 3);
    }

    private void OnAudioStateChanged(object? sender, EventArgs e)
    {
        if (_session < 0 || _closed)
        {
            return;
        }

        var audio = _app.Audio;
        if (audio.IsPlaying && audio.Session == _session)
        {
            return;
        }

        // Our Adhan is over: finished, stopped (here, from the tray or elsewhere) or replaced.
        var failed = audio.Session == _session && audio.LastEnd == AudioPlayer.PlaybackEnd.Failed;
        _session = -1;
        _progressTimer.Stop();
        if (failed)
        {
            ShowError();
        }
        else
        {
            Dismiss();
        }
    }

    private void ShowError()
    {
        Log.Info($"Adhan for {_key} failed to play; showing error briefly");
        StatusText.Text = "The Adhan could not be played";
        StatusText.Foreground = (Brush)FindResource("CoralBrush");
        Equalizer.Visibility = Visibility.Collapsed;
        Progress.Visibility = Visibility.Collapsed;
        Actions.Visibility = Visibility.Collapsed;
        Edge.BorderBrush = new SolidColorBrush(Color.FromArgb(0x66, 0xFF, 0x9C, 0x8A));
        _autoClose.Interval = ErrorLinger;
        _autoClose.Start();
    }

    private void UpdateProgress()
    {
        var audio = _app.Audio;
        if (!audio.IsPlaying || audio.Session != _session)
        {
            OnAudioStateChanged(this, EventArgs.Empty);
            return;
        }

        if (audio.Duration is not { } duration || duration <= TimeSpan.Zero)
        {
            return;
        }

        Progress.Visibility = Visibility.Visible;
        var fraction = Math.Clamp(audio.Position / duration, 0, 1);
        ProgressFill.Width = Progress.ActualWidth * fraction;
    }

    private void StartEqualizer()
    {
        if (!WindowEffects.AnimationsEnabled)
        {
            return;
        }

        (Rectangle Bar, double From, double To, int Ms)[] bars =
        [
            (Bar1, 0.35, 0.9, 520),
            (Bar2, 1.0, 0.45, 430),
            (Bar3, 0.5, 1.0, 610),
        ];
        foreach (var (bar, from, to, ms) in bars)
        {
            ((ScaleTransform)bar.RenderTransform).BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(ms))
            {
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
            });
        }
    }

    private void AnimateIn()
    {
        if (!WindowEffects.AnimationsEnabled)
        {
            return;
        }

        // Rise gently out of the taskbar.
        var (property, offset) = _edge switch
        {
            Taskbar.Edge.Top => (TranslateTransform.YProperty, -10.0),
            Taskbar.Edge.Left => (TranslateTransform.XProperty, -10.0),
            Taskbar.Edge.Right => (TranslateTransform.XProperty, 10.0),
            _ => (TranslateTransform.YProperty, 10.0),
        };
        var duration = TimeSpan.FromMilliseconds(220);
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        Slide.BeginAnimation(property, new DoubleAnimation(offset, 0, duration) { EasingFunction = ease });
        Root.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, duration));
    }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        Dispatcher.BeginInvoke(PlaceNearNotificationArea, DispatcherPriority.Loaded);
    }

    /// <summary>
    /// Puts the card in the corner of the work area next to the notification area,
    /// on whichever monitor and edge the main taskbar is. Works in physical pixels,
    /// so mixed display scaling across monitors places it correctly.
    /// </summary>
    private void PlaceNearNotificationArea()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero || _closed || !GetWindowRect(hwnd, out var bounds))
        {
            return;
        }

        var (edge, work) = Taskbar.Locate();
        _edge = edge;
        var margin = (int)Math.Round(EdgeGap * VisualTreeHelper.GetDpi(this).DpiScaleX);
        var width = bounds.Right - bounds.Left;
        var height = bounds.Bottom - bounds.Top;

        // The clock and notification area sit at the right of a horizontal taskbar
        // and at the bottom of a vertical one.
        var x = edge == Taskbar.Edge.Left ? work.Left + margin : work.Right - width - margin;
        var y = edge == Taskbar.Edge.Top ? work.Top + margin : work.Bottom - height - margin;
        if (x != bounds.Left || y != bounds.Top)
        {
            SetWindowPos(hwnd, IntPtr.Zero, x, y, 0, 0, SwpNoSize | SwpNoZOrder | SwpNoActivate);
        }
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

    /// <summary>Hide: the card goes away, the Adhan keeps playing (Stop Adhan stays in the tray menu).</summary>
    private void OnHide(object sender, RoutedEventArgs e) => Dismiss();

    private void OnStop(object sender, RoutedEventArgs e)
    {
        Dismiss();
        _app.StopAudio();
    }

    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    /// <summary>Where the primary taskbar (which holds the notification area) is.</summary>
    private static class Taskbar
    {
        private const uint AbmGetTaskbarPos = 5;
        private const uint MonitorDefaultToPrimary = 1;

        public enum Edge
        {
            Left = 0,
            Top = 1,
            Right = 2,
            Bottom = 3,
        }

        /// <summary>The taskbar's edge and the work area of its monitor, in physical pixels.</summary>
        public static (Edge Edge, Rect Work) Locate()
        {
            var data = new AppBarData { Size = Marshal.SizeOf<AppBarData>() };
            var found = SHAppBarMessage(AbmGetTaskbarPos, ref data) != 0;
            var edge = found && Enum.IsDefined((Edge)data.Edge) ? (Edge)data.Edge : Edge.Bottom;

            var monitor = found
                ? MonitorFromRect(ref data.Bounds, MonitorDefaultToPrimary)
                : MonitorFromPoint(default, MonitorDefaultToPrimary);
            var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
            if (!GetMonitorInfo(monitor, ref info))
            {
                var area = SystemParameters.WorkArea; // primary monitor, DIPs; last resort
                info.Work = new Rect { Left = (int)area.Left, Top = (int)area.Top, Right = (int)area.Right, Bottom = (int)area.Bottom };
            }

            return (edge, info.Work);
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct AppBarData
        {
            public int Size;
            public IntPtr Hwnd;
            public uint CallbackMessage;
            public uint Edge;
            public Rect Bounds;
            public IntPtr LParam;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MonitorInfo
        {
            public int Size;
            public Rect Monitor;
            public Rect Work;
            public uint Flags;
        }

        [DllImport("shell32.dll")]
        private static extern UIntPtr SHAppBarMessage(uint message, ref AppBarData data);

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromRect(ref Rect rect, uint flags);

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromPoint(System.Drawing.Point point, uint flags);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern int GetWindowLong(IntPtr hwnd, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")]
    private static extern int SetWindowLong(IntPtr hwnd, int index, int value);
}
