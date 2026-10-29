using System.IO;
using System.Windows;
using System.Windows.Threading;
using OptimAdhan.App.Services;
using OptimAdhan.App.Views;
using OptimAdhan.Core.Scheduling;
using OptimAdhan.Core.Settings;
using MainPage = OptimAdhan.App.Views.MainWindow.Page;
using MainView = OptimAdhan.App.Views.MainWindow;

namespace OptimAdhan.App;

public partial class App : Application
{
    private SingleInstance? _instance;
    private AppController? _controller;
    private TrayIcon? _tray;
    private MainView? _mainWindow;
    private WidgetWindow? _widget;

    public static AppController Controller => ((App)Current)._controller!;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _instance = new SingleInstance();
        if (!_instance.IsFirstInstance)
        {
            _instance.SignalFirstInstance();
            Shutdown();
            return;
        }

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) => Log.Error("Unhandled exception", args.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Log.Error("Unobserved task exception", args.Exception);
            args.SetObserved();
        };

        Log.Info($"Starting {AppInfo.ProductName} {AppInfo.Version}{(DevMode.IsEnabled ? " (developer mode)" : "")}");

        var sounds = SoundLibrary.Load(Path.Combine(AppContext.BaseDirectory, "Assets", "Audio"));
        var settingsPath = DevMode.IsEnabled ? DevMode.SettingsPath : SettingsStore.DefaultPath;
        _controller = new AppController(new SettingsStore(settingsPath), sounds);
        _controller.AlertRaised += OnAlertRaised;
        _controller.SettingsChanged += (_, _) => SyncWidget();

        // Re-apply on every start so the Run entry follows the app if it is moved or updated.
        StartupRegistration.Apply(_controller.Settings.StartWithWindows);

        _tray = new TrayIcon(_controller, this);
        _instance.ListenForActivation(() => Dispatcher.BeginInvoke(() => ShowMainWindow()));

        SyncWidget();

        var background = e.Args.Contains(StartupRegistration.BackgroundArgument, StringComparer.OrdinalIgnoreCase);
        if (_controller.Settings.Location is null)
        {
            ShowMainWindow(MainPage.Settings);
        }
        else if (!background)
        {
            ShowMainWindow();
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        AlertPopup.CloseCurrent();
        _tray?.Dispose();
        _controller?.Dispose();
        _instance?.Dispose();
        Log.Info("Exited");
        base.OnExit(e);
    }

    public void ShowMainWindow(MainPage? page = null)
    {
        if (_mainWindow is null)
        {
            _mainWindow = new MainView(_controller!);
            // Closing the window frees it; the app keeps running in the tray.
            _mainWindow.Closed += (_, _) => _mainWindow = null;
            _mainWindow.Show();
        }

        if (page is { } p)
        {
            _mainWindow.Navigate(p);
        }

        if (_mainWindow.WindowState == WindowState.Minimized)
        {
            _mainWindow.WindowState = WindowState.Normal;
        }

        _mainWindow.Activate();
    }

    public void SetWidgetVisible(bool visible) =>
        _controller!.Update(s => s with { Widget = s.Widget with { Visible = visible } });

    public void ExitApplication()
    {
        if (_widget is not null)
        {
            _widget.ClosingByApp = true;
            _widget.Close();
        }

        _mainWindow?.Close();
        Shutdown();
    }

    private void SyncWidget()
    {
        var shouldShow = _controller!.Settings.Widget.Visible && _controller.Schedule is not null;
        if (shouldShow && _widget is null)
        {
            _widget = new WidgetWindow(_controller);
            _widget.Closed += (_, _) => _widget = null;
            _widget.Show();
        }
        else if (!shouldShow && _widget is not null)
        {
            _widget.ClosingByApp = true;
            _widget.Close();
        }
    }

    private void OnAlertRaised(object? sender, AlertOccurrence occurrence)
    {
        var settings = _controller!.Settings;
        if (occurrence.Alert.Kind == AlertKind.PrayerReminder && !settings.Adhan.ShowNotifications)
        {
            return;
        }

        AlertPopup.ShowFor(occurrence, _controller);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        // Keep the background Adhan service alive: log and continue rather than crash.
        Log.Error("Unhandled UI exception", e.Exception);
        e.Handled = true;
    }
}
