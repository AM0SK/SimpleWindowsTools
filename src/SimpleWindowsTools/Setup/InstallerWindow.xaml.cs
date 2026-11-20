using System;
using System.Threading.Tasks;
using System.Windows;
using SimpleWindowsTools.Core;

namespace SimpleWindowsTools.Setup;

/// <summary>Setup window: options → progress → done (launch). Runs elevated (App starts it that way).</summary>
public partial class InstallerWindow : Window
{
    private enum Stage { Options, Working, Failed }
    private Stage _stage = Stage.Options;

    public InstallerWindow()
    {
        InitializeComponent();
        WindowFrame.Apply(this);
        if (Installer.IsInstalled)
            AutostartSwitch.IsChecked = Autostart.IsEnabled() || Installer.InstalledVersion is null; // keep the current choice when updating
        BuildLanguagePicker();
        ApplyLanguage();
        Loc.Changed += ApplyLanguage;
        Closed += (_, _) => Loc.Changed -= ApplyLanguage;
    }

    /// <summary>UA / EN in the title bar. The choice is also saved for the app itself.</summary>
    private void BuildLanguagePicker()
    {
        foreach (var lang in Loc.All)
        {
            var chip = new System.Windows.Controls.Primitives.ToggleButton
            {
                Content = lang == UiLanguage.En ? "EN" : "UA",
                Tag = lang,
                ToolTip = Loc.NativeName(lang),
                Style = (Style)FindResource("ChoiceChip"),
                Margin = new Thickness(0, 0, 6, 0),
            };
            System.Windows.Shell.WindowChrome.SetIsHitTestVisibleInChrome(chip, true); // it sits in the caption area
            chip.Click += (_, _) => Loc.Set(lang);
            LanguagePicker.Children.Add(chip);
        }
    }

    /// <summary>Texts set from code (XAML ones follow the resources by themselves).</summary>
    private void ApplyLanguage()
    {
        foreach (System.Windows.Controls.Primitives.ToggleButton chip in LanguagePicker.Children)
            chip.IsChecked = (UiLanguage)chip.Tag == Loc.Current;
        ShowFolder();
        switch (_stage)
        {
            case Stage.Options:
                if (Installer.IsInstalled)
                {
                    var old = Installer.InstalledVersion;
                    Heading.Text = old is null || old == Installer.CurrentVersion
                        ? Loc.F("I.Reinstall", Installer.CurrentVersion)
                        : Loc.F("I.Update", old, Installer.CurrentVersion);
                    MainButton.Content = Loc.T("I.UpdateButton");
                }
                else
                {
                    Heading.Text = Loc.F("I.Install", Installer.CurrentVersion);
                    MainButton.Content = Loc.T("I.InstallButton");
                }
                break;
            case Stage.Failed:
                ProgressTitle.Text = Loc.T("I.Failed");
                MainButton.Content = Loc.T("Common.Close");
                break;
        }
    }

    private void ShowFolder()
    {
        FolderText.Text = Installer.InstallDir;
        FolderWarning.Visibility = Installer.IsProtectedLocation(Installer.InstallDir) ? Visibility.Collapsed : Visibility.Visible;
        bool isDefault = string.Equals(Installer.InstallDir, Installer.DefaultInstallDir, StringComparison.OrdinalIgnoreCase);
        DefaultFolderButton.Visibility = isDefault ? Visibility.Collapsed : Visibility.Visible;
        SizeText.Text = SizeLine();
    }

    /// <summary>"Займёт 72 МБ · свободно 120 ГБ на диске D:" — the app is this one file.</summary>
    private static string SizeLine()
    {
        string line;
        try
        {
            long size = new System.IO.FileInfo(Environment.ProcessPath!).Length;
            line = Loc.F("I.Size", FormatSize(size));
        }
        catch
        {
            line = Loc.T("I.SizeAbout");
        }
        try
        {
            var drive = new System.IO.DriveInfo(System.IO.Path.GetPathRoot(Installer.InstallDir)!);
            line += Loc.F("I.Free", FormatSize(drive.AvailableFreeSpace), drive.Name.TrimEnd('\\'));
        }
        catch
        {
            // drive not ready / not a local drive: show just the size
        }
        return line;
    }

    private static string FormatSize(long bytes) => bytes switch
    {
        >= 1L << 30 => (bytes / (double)(1L << 30)).ToString("0.#", Loc.Culture) + " " + Loc.T("I.GB"),
        >= 1L << 20 => (bytes / (double)(1L << 20)).ToString("0", Loc.Culture) + " " + Loc.T("I.MB"),
        _ => $"{Math.Max(1, bytes / 1024)} " + Loc.T("I.KB"),
    };

    private void ChangeFolder_Click(object sender, RoutedEventArgs e)
    {
        var parent = System.IO.Path.GetDirectoryName(Installer.InstallDir);
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = Loc.T("I.FolderDialog"),
            InitialDirectory = parent is not null && System.IO.Directory.Exists(parent) ? parent : null,
        };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            Installer.InstallDir = Installer.NormalizeChosenFolder(dialog.FolderName);
        }
        catch (Exception ex)
        {
            Log.Error("Bad install folder", ex);
        }
        ShowFolder();
    }

    private void DefaultFolder_Click(object sender, RoutedEventArgs e)
    {
        Installer.InstallDir = Installer.DefaultInstallDir;
        ShowFolder();
    }

    private async void Main_Click(object sender, RoutedEventArgs e)
    {
        switch (_stage)
        {
            case Stage.Options:
                await RunInstall();
                break;
            default:
                Close();
                break;
        }
    }

    private async Task RunInstall()
    {
        _stage = Stage.Working;
        bool autostart = AutostartSwitch.IsChecked == true, desktop = DesktopSwitch.IsChecked == true;
        OptionsPanel.Visibility = Visibility.Collapsed;
        LanguagePicker.Visibility = Visibility.Collapsed;
        ProgressPanel.Visibility = Visibility.Visible;
        MainButton.IsEnabled = CancelButton.IsEnabled = CloseButton.IsEnabled = false;

        bool firstInstall = !Installer.IsInstalled;
        try
        {
            await Task.Run(() => Installer.Install(autostart, desktop,
                text => Dispatcher.Invoke(() => ProgressText.Text = text)));

            SaveLanguageChoice();
            // No "Done" screen: start the app and get out of the way. First install → it opens its
            // settings and says it lives in the tray; update → straight to the tray, like before.
            try { Installer.LaunchInstalled(firstInstall); }
            catch (Exception ex) { Log.Error("Launch after install failed", ex); }
            Close();
            return;
        }
        catch (Exception ex)
        {
            Log.Error("Install failed", ex);
            _stage = Stage.Failed;
            ApplyLanguage();
            ProgressText.Text = ex.Message;
            CancelButton.Visibility = Visibility.Collapsed;
        }
        MainButton.IsEnabled = CloseButton.IsEnabled = true;
    }

    /// <summary>The language picked here becomes the app's language (the running copy is already stopped).</summary>
    private static void SaveLanguageChoice()
    {
        try
        {
            var settings = SettingsStore.Load();
            settings.Language = Loc.Code(Loc.Current);
            SettingsStore.Save(settings);
        }
        catch (Exception ex)
        {
            Log.Error("Saving the language failed", ex);
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        if (_stage == Stage.Working) return;
        Close();
    }
}
