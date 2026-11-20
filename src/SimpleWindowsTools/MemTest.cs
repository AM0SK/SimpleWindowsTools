#if DEBUG
using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using SimpleWindowsTools.UI;

namespace SimpleWindowsTools;

/// <summary>
/// Debug only: `--memtest report.txt` opens and closes the screenshot editor and the settings window
/// many times (off screen) and logs memory after each round — to find what makes the app grow.
/// </summary>
internal static class MemTest
{
    public static async Task RunAsync(string file)
    {
        var sb = new StringBuilder();
        var temp = Path.Combine(Path.GetTempPath(), "swt-mem-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        Core.SettingsStore.Folder = temp;
        try
        {
            using var app = new AppController();
            Screenshot.ScreenshotWindow.DebugOffscreen = true;
            await Idle(1500);
            Line(sb, "start");

            for (int round = 1; round <= 3; round++)
            {
                for (int i = 0; i < 10; i++)
                {
                    var shot = Screenshot.ScreenCapture.CaptureMonitorUnderCursor();
                    var w = new Screenshot.ScreenshotWindow(shot, app.Settings, _ => { });
                    w.Show();
                    await Idle(150);
                    w.Close();
                    await Idle(50);
                }
                await Idle(1000);
                Line(sb, $"screenshot x10 #{round}");
                Collect();
                await Idle(1000);
                Line(sb, $"  after GC");
            }

            for (int round = 1; round <= 3; round++)
            {
                for (int i = 0; i < 10; i++)
                {
                    var s = new SettingsWindow(app) { Left = -30000, Top = 0, ShowActivated = false, WindowStartupLocation = WindowStartupLocation.Manual };
                    s.Show();
                    await Idle(150);
                    s.Close();
                    await Idle(50);
                }
                await Idle(1000);
                Line(sb, $"settings x10 #{round}");
                Collect();
                await Idle(1000);
                Line(sb, $"  after GC");
            }

            await Idle(20000);
            Line(sb, "idle 20 s");
            Core.Native.EmptyWorkingSetSelf();
            await Idle(500);
            Line(sb, "after trim");
        }
        catch (Exception ex) { sb.AppendLine(ex.ToString()); }
        File.WriteAllText(file, sb.ToString());
        try { Directory.Delete(temp, true); } catch { }
    }

    private static void Collect()
    {
        System.Runtime.GCSettings.LargeObjectHeapCompactionMode = System.Runtime.GCLargeObjectHeapCompactionMode.CompactOnce;
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
    }

    private static async Task Idle(int ms)
    {
        await Task.Delay(ms);
        await Dispatcher.CurrentDispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
    }

    private static void Line(StringBuilder sb, string label)
    {
        var p = Process.GetCurrentProcess();
        p.Refresh();
        sb.AppendLine($"{label,-22} private {p.PrivateMemorySize64 / 1048576.0,7:F1} MB  ws {p.WorkingSet64 / 1048576.0,7:F1} MB  managed {GC.GetTotalMemory(false) / 1048576.0,6:F1} MB");
    }
}
#endif
