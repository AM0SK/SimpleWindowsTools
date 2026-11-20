using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using SimpleWindowsTools.Core;
using SimpleWindowsTools.UI;

namespace SimpleWindowsTools;

/// <summary>
/// Startup, in order:
/// 1. A copy is already running → ask it to show settings (unless this is the logon autostart), quit.
/// 2. Not elevated (the exe is asInvoker so it can sit in the Run key) → start the elevated copy through
///    the scheduled task, or with a UAC prompt the very first time when there is no task yet; quit.
/// 3. Elevated → the real app: hook, tray, modules. Makes sure the task exists for next time.
/// </summary>
public partial class App : Application
{
    private const string MutexName = @"Local\SimpleWindowsTools.SingleInstance";

    private Mutex? _mutex;
    private IpcWindow? _ipc;
    private AppController? _controller;
    private TrayIcon? _tray;
    private SettingsWindow? _settingsWindow;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var startSettings = SettingsStore.Load();
        Loc.Set(Loc.FromCode(startSettings.Language));

#if DEBUG
        if (e.Args.Length == 2 && e.Args[0] == "--render")
        {
            DebugRender.Run(e.Args[1]);
            Shutdown();
            return;
        }
        if (e.Args.Length == 2 && e.Args[0] == "--render-docs")
        {
            DebugRender.RunDocs(e.Args[1]);
            Shutdown();
            return;
        }
        if (e.Args.Length == 2 && e.Args[0] == "--memtest")
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            _ = MemTest.RunAsync(e.Args[1]).ContinueWith(_ => Dispatcher.Invoke(() => Shutdown()));
            return;
        }
        if (e.Args.Length == 2 && e.Args[0] == "--selftest")
        {
            Shutdown(SelfTest.Run(e.Args[1]));
            return;
        }
#endif

        if (e.Args.Contains(Setup.Installer.UninstallArgument))
        {
            RunUninstall();
            return;
        }
#if SETUP
        // Setup build started from anywhere but Program Files = the installer.
        if (!Setup.Installer.IsRunningInstalled)
        {
            RunSetup();
            return;
        }
#endif

        bool atLogon = e.Args.Contains(Autostart.Argument);
        bool fromTask = e.Args.Contains(Autostart.ElevatedArgument);

        if (IpcWindow.NotifyRunning(showSettings: !atLogon && !fromTask))
        {
            Shutdown();
            return;
        }

#if !DEBUG
        // Debug builds run as they are, without admin, so the UI can be checked without UAC.
        if (!IsElevated())
        {
            StartElevated(e.Args, showSettings: !atLogon);
            Shutdown();
            return;
        }
#endif

        _mutex = new Mutex(true, MutexName, out bool first);
        if (!first)
        {
            // Another elevated copy is starting right now; it will create its window in a moment.
            Shutdown();
            return;
        }

        DispatcherUnhandledException += (_, args) =>
        {
            Log.Error("Unhandled UI exception", args.Exception);
            args.Handled = true; // stay alive in the tray; the log has the details
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) => Log.Error("Unhandled exception", args.ExceptionObject as Exception);

        _controller = new AppController();
        _controller.ExitRequested += ExitApp;

        _tray = new TrayIcon(_controller);
        _tray.OpenSettingsRequested += ShowSettings;
        _tray.OpenUpdatesRequested += () => ShowSettings(appTab: true);
        _controller.StartUpdateChecks();
        _controller.StartMemoryTrim();

        _ipc = new IpcWindow();
        _ipc.ShowRequested += ShowSettings;
        _ipc.ExitRequested += ExitApp;

        Task.Run(PrepareElevationTask);

        // By hand → open the window so it's clear the app is running. At logon / after an update → tray only.
        // First start after a fresh install → the window plus a note that the app lives in the tray.
        bool welcome = e.Args.Contains(Setup.Installer.WelcomeArgument);
        bool show = welcome || (fromTask ? IpcWindow.TakeShowFlag() : !atLogon);
        if (show) ShowSettings();
        if (welcome) _tray.ShowWelcome();
    }

    private void RunSetup()
    {
        if (!IsElevated())
        {
            RelaunchElevated(Setup.Installer.SetupArgument);
            Shutdown();
            return;
        }
        var window = new Setup.InstallerWindow();
        window.Closed += (_, _) => Shutdown();
        window.Show();
    }

    private void RunUninstall()
    {
        if (!IsElevated())
        {
            RelaunchElevated(Setup.Installer.UninstallArgument);
            Shutdown();
            return;
        }
        var answer = MessageBox.Show(Loc.T("U.Ask"), "Simple Windows Tools",
            MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (answer == MessageBoxResult.Yes)
        {
            try
            {
                Setup.Installer.Uninstall();
                MessageBox.Show(Loc.T("U.Done"), "Simple Windows Tools", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                Log.Error("Uninstall failed", ex);
                MessageBox.Show(Loc.F("U.Failed", ex.Message), "Simple Windows Tools", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        Shutdown();
    }

    private static void RelaunchElevated(string arguments)
    {
        try
        {
            Process.Start(new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = true, Verb = "runas", Arguments = arguments });
        }
        catch (Win32Exception)
        {
            // UAC prompt cancelled.
        }
    }

    private static bool IsElevated()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    /// <summary>Non-elevated launch: hand over to an elevated copy and let this one exit.</summary>
    private static void StartElevated(string[] args, bool showSettings)
    {
        if (Autostart.TaskExists())
        {
            if (showSettings) IpcWindow.LeaveShowFlag();
            if (Autostart.RunTask()) return;
        }

        // No task yet (first launch) or it failed: ask for admin once. The elevated copy creates the task,
        // and from then on launches go through it without a prompt.
        try
        {
            Process.Start(new ProcessStartInfo(Environment.ProcessPath!)
            {
                UseShellExecute = true,
                Verb = "runas",
                Arguments = string.Join(" ", args.Select(a => $"\"{a}\"")),
            });
        }
        catch (Win32Exception)
        {
            // UAC prompt cancelled — nothing to do.
        }
    }

    /// <summary>
    /// The task must point at this exe. Versions before the Run entry started at logon from the task
    /// itself (LogonTrigger); if that was on, move autostart to the Run entry so it stays on.
    /// </summary>
    private static void PrepareElevationTask()
    {
        try
        {
            bool oldStyleAutostart = Autostart.TaskHasLogonTrigger();
            Autostart.EnsureTask();
            if (oldStyleAutostart) Autostart.Enable();
            else Autostart.RefreshPath();
        }
        catch (Exception ex)
        {
            Log.Error("Preparing the elevation task failed", ex);
        }
    }

    private void ShowSettings() => ShowSettings(appTab: false);

    private void ShowSettings(bool appTab)
    {
        if (_controller is null) return;
        if (_settingsWindow is null)
        {
            _settingsWindow = new SettingsWindow(_controller);
            _settingsWindow.Closed += (_, _) => { _settingsWindow = null; _controller?.TrimSoon(); };
            _settingsWindow.Show();
        }
        if (appTab) _settingsWindow.ShowAppTab();
        if (_settingsWindow.WindowState == WindowState.Minimized) _settingsWindow.WindowState = WindowState.Normal;
        _settingsWindow.Activate();
    }

    private void ExitApp()
    {
        _settingsWindow?.Close();
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _ipc?.Dispose();
        _tray?.Dispose();
        _controller?.Dispose();
        if (_controller is not null) _mutex?.ReleaseMutex();
        _mutex?.Dispose();
        base.OnExit(e);
    }
}
