using System;
using System.Collections.Generic;
using System.Linq;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Media;
using SimpleWindowsTools.Core;

namespace SimpleWindowsTools.Modules;

/// <summary>
/// Opens a program, shortcut, file or folder for a launcher hotkey.
///
/// Our app runs elevated (for the keyboard hook), and a process it starts directly would be elevated too —
/// wrong for the user's own apps (drag and drop from Explorer stops working, files get admin-owned).
/// So it asks the desktop shell (Explorer, which runs as the normal user) to open the file, the way a
/// double-click would: the shell's IShellDispatch2.ShellExecute, reached through the desktop's folder view
/// (Raymond Chen, "How can I launch an unelevated process from my elevated process").
/// </summary>
public static class AppLauncher
{
    /// <summary>Opens the target. Throws if it can't (missing file, shell unavailable).</summary>
    public static void Start(string path, string? arguments = null, int show = 1)
    {
        if (!File.Exists(path) && !Directory.Exists(path))
            throw new FileNotFoundException("Not found", path);
        var dir = Directory.Exists(path) ? path : Path.GetDirectoryName(path) ?? "";

        if (!IsElevated() && !ForceShellRoute)
        {
            Process.Start(new ProcessStartInfo(path, arguments ?? "") { UseShellExecute = true, WorkingDirectory = dir });
            return;
        }
        ShellExecuteAsUser(path, arguments ?? "", dir, show);
    }

    /// <summary>
    /// Built-in Windows programs offered with one click (Calculator is a Store app with no shortcut to
    /// browse to, so people can't find it otherwise). Name key in Loc + path; only those present are shown.
    /// </summary>
    public static readonly (string NameKey, string[] Candidates)[] StandardCandidates =
    {
        ("S.Std.Calc", new[] { Sys("calc.exe") }),
        ("S.Std.Notepad", new[] { Sys("notepad.exe"), Alias("notepad.exe") }),
        // Windows 11 moved these to the Store: no System32 file there, only the app execution alias.
        ("S.Std.Paint", new[] { Sys("mspaint.exe"), Alias("mspaint.exe") }),
        ("S.Std.Snip", new[] { Sys("SnippingTool.exe"), Alias("SnippingTool.exe") }),
        ("S.Std.Terminal", new[] { Alias("wt.exe") }),
        ("S.Std.Explorer", new[] { System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe") }),
        ("S.Std.TaskMgr", new[] { Sys("Taskmgr.exe") }),
        ("S.Std.Cmd", new[] { Sys("cmd.exe") }),
        ("S.Std.PowerShell", new[] { Sys(@"WindowsPowerShell\v1.0\powershell.exe") }),
        ("S.Std.Control", new[] { Sys("control.exe") }),
        ("S.Std.CharMap", new[] { Sys("charmap.exe") }),
    };

    /// <summary>The built-in programs present on this PC: name key + the first path that exists.</summary>
    public static IEnumerable<(string NameKey, string Path)> Standard =>
        StandardCandidates
            .Select(s => (s.NameKey, Path: s.Candidates.FirstOrDefault(File.Exists)))
            .Where(s => s.Path is not null)
            .Select(s => (s.NameKey, s.Path!));

    private static string Alias(string file) =>
        System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "WindowsApps", file);

    private static string Sys(string file) => System.IO.Path.Combine(Environment.SystemDirectory, file);

    /// <summary>Name for the list: the program's own description ("Windows Calculator"), else the file name.</summary>
    public static string DisplayName(string path)
    {
        try
        {
            if (Directory.Exists(path)) return new DirectoryInfo(path).Name;
            if (path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                var description = FileVersionInfo.GetVersionInfo(path).FileDescription?.Trim();
                if (!string.IsNullOrEmpty(description)) return description;
            }
        }
        catch { /* fall back to the file name */ }
        return Path.GetFileNameWithoutExtension(path.TrimEnd('\\'));
    }

    /// <summary>The icon Explorer shows for the file, or null.</summary>
    public static ImageSource? Icon(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            using var icon = System.Drawing.Icon.ExtractAssociatedIcon(path);
            if (icon is null) return null;
            var image = System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(icon.Handle, System.Windows.Int32Rect.Empty,
                System.Windows.Media.Imaging.BitmapSizeOptions.FromEmptyOptions());
            image.Freeze();
            return image;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Self-test: go through Explorer even when not elevated, to check that route works.</summary>
    internal static bool ForceShellRoute;

    public static bool IsElevated()
    {
        using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
        return new System.Security.Principal.WindowsPrincipal(identity).IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
    }

    // ===== Explorer's ShellExecute =====

    private static readonly Guid CLSID_ShellWindows = new("9BA05972-F6A8-11CF-A442-00A0C90A8F39");
    private static readonly Guid SID_STopLevelBrowser = new("4C96BE40-915C-11CF-99D3-00AA004AE837");
    private static readonly Guid IID_IShellBrowser = new("000214E2-0000-0000-C000-000000000046");
    private static readonly Guid IID_IDispatch = new("00020400-0000-0000-C000-000000000046");
    private const int SWC_DESKTOP = 8, SWFO_NEEDDISPATCH = 1, SVGIO_BACKGROUND = 0;

    private static void ShellExecuteAsUser(string file, string arguments, string dir, int show)
    {
        var shellWindows = (IShellWindows)Activator.CreateInstance(Type.GetTypeFromCLSID(CLSID_ShellWindows, throwOnError: true)!)!;
        object? folderView = null, desktop = null, browser = null, view = null;
        try
        {
            object loc = 0; // CSIDL_DESKTOP
            object root = Type.Missing;
            desktop = shellWindows.FindWindowSW(ref loc, ref root, SWC_DESKTOP, out _, SWFO_NEEDDISPATCH)
                      ?? throw new InvalidOperationException("No desktop window (Explorer not running?)");

            var sid = SID_STopLevelBrowser;
            var iidBrowser = IID_IShellBrowser;
            ((IServiceProvider)desktop).QueryService(ref sid, ref iidBrowser, out browser);
            ((IShellBrowser)browser).QueryActiveShellView(out var shellView);
            view = shellView;
            var iidDispatch = IID_IDispatch;
            ((IShellView)view).GetItemObject(SVGIO_BACKGROUND, ref iidDispatch, out folderView);

            dynamic shell = ((dynamic)folderView).Application; // IShellDispatch2
            shell.ShellExecute(file, arguments, dir, "open", show);
        }
        finally
        {
            foreach (var o in new[] { folderView, view, browser, desktop, shellWindows })
                if (o is not null && Marshal.IsComObject(o)) Marshal.ReleaseComObject(o);
        }
    }

    [ComImport, Guid("85CB6900-4D95-11CF-960C-0080C7F4EE85"), InterfaceType(ComInterfaceType.InterfaceIsDual)]
    private interface IShellWindows
    {
        int Count { get; }
        [return: MarshalAs(UnmanagedType.IDispatch)] object Item([MarshalAs(UnmanagedType.Struct)] object index);
        [return: MarshalAs(UnmanagedType.IUnknown)] object _NewEnum();
        void Register_(); // vtable placeholders: never called
        void RegisterPending_();
        void Revoke_();
        void OnNavigate_();
        void OnActivated_();
        [return: MarshalAs(UnmanagedType.IDispatch)]
        object FindWindowSW([MarshalAs(UnmanagedType.Struct)] ref object loc, [MarshalAs(UnmanagedType.Struct)] ref object locRoot,
            int swClass, out int hwnd, int swfwOptions);
    }

    [ComImport, Guid("6D5140C1-7436-11CE-8034-00AA006009FA"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IServiceProvider
    {
        void QueryService(ref Guid service, ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object obj);
    }

    [ComImport, Guid("000214E2-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellBrowser
    {
        void GetWindow_(); void ContextSensitiveHelp_(); // IOleWindow
        void InsertMenusSB_(); void SetMenuSB_(); void RemoveMenusSB_(); void SetStatusTextSB_();
        void EnableModelessSB_(); void TranslateAcceleratorSB_(); void BrowseObject_(); void GetViewStateStream_();
        void GetControlWindow_(); void SendControlMsg_();
        void QueryActiveShellView([MarshalAs(UnmanagedType.IUnknown)] out object view);
    }

    [ComImport, Guid("000214E3-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellView
    {
        void GetWindow_(); void ContextSensitiveHelp_(); // IOleWindow
        void TranslateAccelerator_(); void EnableModeless_(); void UIActivate_(); void Refresh_();
        void CreateViewWindow_(); void DestroyViewWindow_(); void GetCurrentInfo_(); void AddPropertySheetPages_();
        void SaveViewState_(); void SelectItem_();
        void GetItemObject(uint item, ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object obj);
    }
}
