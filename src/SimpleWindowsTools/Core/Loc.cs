using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;

namespace SimpleWindowsTools.Core;

public enum UiLanguage { Uk, En }

/// <summary>
/// Interface language. Every UI string lives in <see cref="Strings.Table"/> with two translations (Ukrainian, English). Russian was dropped in 0.6.0.
/// XAML uses them as <c>{DynamicResource Key}</c>: <see cref="Set"/> copies the current language into the
/// application resources, so open windows switch on the fly. Code uses <see cref="T"/> / <see cref="F"/> and
/// re-reads them on <see cref="Changed"/>.
/// </summary>
public static class Loc
{
    public static UiLanguage Current { get; private set; } = UiLanguage.Uk;

    /// <summary>Raised after the language changes (code-set texts must be re-read).</summary>
    public static event Action? Changed;

    public static readonly UiLanguage[] All = { UiLanguage.Uk, UiLanguage.En };

    /// <summary>Settings value ("uk" / "en"; empty = follow Windows).</summary>
    public static string Code(UiLanguage lang) => lang == UiLanguage.En ? "en" : "uk";

    /// <summary>Name of the language in itself, for the picker.</summary>
    public static string NativeName(UiLanguage lang) => lang == UiLanguage.En ? "English" : "Українська";

    public static CultureInfo Culture => CultureInfo.GetCultureInfo(Current == UiLanguage.En ? "en-US" : "uk-UA");

    public static UiLanguage FromCode(string? code, CultureInfo? system = null) => (code ?? "").Trim().ToLowerInvariant() switch
    {
        "uk" or "ua" or "ru" => UiLanguage.Uk, // "ru": the Russian UI was dropped
        "en" => UiLanguage.En,
        _ => FromSystem(system ?? CultureInfo.CurrentUICulture),
    };

    /// <summary>Windows in Ukrainian → Ukrainian, Russian (or Belarusian/Kazakh) → Ukrainian too, anything else → English.</summary>
    public static UiLanguage FromSystem(CultureInfo culture) => culture.TwoLetterISOLanguageName switch
    {
        "uk" or "ru" or "be" or "kk" => UiLanguage.Uk,
        _ => UiLanguage.En,
    };

    public static void Set(UiLanguage lang)
    {
        Current = lang;
        var res = Application.Current?.Resources;
        if (res is not null)
            foreach (var (key, values) in Strings.Table) res[key] = values[(int)lang];
        Changed?.Invoke();
    }

    public static string T(string key) =>
        Strings.Table.TryGetValue(key, out var v) ? v[(int)Current] : key;

    public static string F(string key, params object?[] args) => string.Format(Culture, T(key), args);

    /// <summary>Picks the current language from an inline (uk, en) pair.</summary>
    public static string Pick(string[] pair) => pair[(int)Current];
}

/// <summary>All UI strings: key → { Ukrainian, English }. Keys are also XAML resource keys.</summary>
public static class Strings
{
    public static readonly Dictionary<string, string[]> Table = new()
    {
        // ===== Common =====
        ["Common.Close"] = new[] { "Закрити", "Close" },

        // ===== Settings window =====
        ["S.Minimize"] = new[] { "Згорнути", "Minimize" },
        ["S.CloseWindow"] = new[] { "Закрити вікно (програма залишиться в треї)", "Close the window (the app stays in the tray)" },
        ["S.AhkWarning"] = new[]
        {
            "Запущено AutoHotkey. Закрий старі скрипти в треї — інакше вони перехоплюють ті самі клавіші.",
            "AutoHotkey is running. Close the old scripts in the tray, or they will catch the same keys.",
        },
        ["S.Modules"] = new[] { "МОДУЛІ", "MODULES" },
        ["S.Overlay.Title"] = new[] { "Плашка розкладки", "Layout pill" },
        ["S.Overlay.Text"] = new[]
        {
            "Під час зміни мови показує EN / RU / UA посередині екрана, де відкрите активне вікно.",
            "Shows EN / RU / UA in the middle of the active window's screen when the language changes.",
        },
        ["S.Overlay.Duration"] = new[] { "Час показу", "Show for" },
        ["S.Overlay.FontSize"] = new[] { "Розмір тексту", "Text size" },
        ["S.Overlay.Preview"] = new[] { "Показати приклад", "Show preview" },
        ["S.Overlay.Seconds"] = new[] { "{0:0.0} с", "{0:0.0} s" },
        ["S.Skip.Title"] = new[] { "Пропуск мов", "Skip languages" },
        ["S.Skip.Text"] = new[]
        {
            "Позначені мови не потрапляють у перемикання розкладки — лишаються тільки інші. Гаряча клавіша тимчасово повертає їх і прибирає знову.",
            "Checked languages are left out when switching layouts, so only the rest remain. The hotkey brings them back for a while and removes them again.",
        },
        ["S.Win.Title"] = new[] { "Блокування клавіші Win", "Win key blocker" },
        ["S.Win.Text"] = new[]
        {
            "Win не відкриває «Пуск», Win + Space і Alt + Space нічого не роблять. Поки Win затиснута, інші клавіші теж не натискаються.",
            "Win doesn't open Start, Win + Space and Alt + Space do nothing. While Win is held, other keys don't work either.",
        },
        ["S.Dash.Title"] = new[] { "Довге тире", "Em dash" },
        ["S.Dash.Text"] = new[]
        {
            "Друкує «—» у будь-якій розкладці. Типово Alt + «−» (мінус в основному ряду).",
            "Types “—” in any layout. Default: Alt + “−” (the minus in the main row).",
        },
        ["S.Shot.Title"] = new[] { "Скриншот", "Screenshot" },
        ["S.Shot.Text"] = new[]
        {
            "Виділи область і познач на ній потрібне: стрілка, прямокутник, лінія, текст, розмиття, олівець. Результат — у буфер обміну або в PNG.",
            "Select an area and mark it up: arrow, rectangle, line, text, blur, pencil. The result goes to the clipboard or to a PNG file.",
        },
        ["S.Shot.Tools"] = new[] { "Інструменти", "Tools" },
        ["S.Shot.ToolsText"] = new[]
        {
            "Перетягни, щоб змінити порядок на панелі. Цифра — клавіша інструмента в редакторі.",
            "Drag to change the toolbar order. The number is the tool's key in the editor.",
        },
        ["S.Shot.AllCommands"] = new[] { "Усі команди та сполучення", "All commands and shortcuts" },
        ["S.System"] = new[] { "СИСТЕМА", "SYSTEM" },
        ["S.Language.Title"] = new[] { "Мова інтерфейсу", "Interface language" },
        ["S.Language.Text"] = new[]
        {
            "Меню в треї, налаштування, редактор скриншотів і підказки.",
            "Tray menu, settings, screenshot editor and hints.",
        },
        ["S.Autostart.Title"] = new[] { "Запускати разом з Windows", "Start with Windows" },
        ["S.Autostart.Text"] = new[]
        {
            "Видно в Диспетчері завдань → «Автозавантаження», там теж можна вимкнути. Права адміністратора беруться через Планувальник завдань — без вікна UAC. Програма одразу йде в трей.",
            "Listed in Task Manager → Startup apps, where it can be turned off too. Admin rights come through Task Scheduler, with no UAC prompt. The app goes straight to the tray.",
        },
        ["S.Autostart.Failed"] = new[]
        {
            "Не вдалося змінити автозапуск. Подробиці — у log.txt у теці налаштувань.",
            "Couldn't change autostart. Details are in log.txt in the settings folder.",
        },
        ["S.Exit.Title"] = new[] { "Закрити програму", "Quit the app" },
        ["S.Exit.Text"] = new[]
        {
            "Хрестик вікна лише ховає його — програма продовжує працювати в треї.",
            "The window's × only hides it; the app keeps running in the tray.",
        },
        ["S.Exit.Button"] = new[] { "Вийти", "Quit" },
        ["S.Version"] = new[] { "Версія {0} · налаштування: {1}", "Version {0} · settings: {1}" },
        ["S.Tab.Features"] = new[] { "Функції", "Features" },
        ["S.Tab.App"] = new[] { "Програма", "App" },
        ["S.Interface"] = new[] { "ІНТЕРФЕЙС", "INTERFACE" },
        ["S.UpdatesSection"] = new[] { "ОНОВЛЕННЯ", "UPDATES" },
        ["S.Updates.Auto"] = new[] { "Перевіряти оновлення", "Check for updates" },
        ["S.Updates.AutoText"] = new[]
        {
            "Раз на кілька годин програма дивиться, чи вийшла нова версія на GitHub, і показує сповіщення. Сама нічого не встановлює.",
            "Every few hours the app looks for a new version on GitHub and shows a notification. It never installs anything by itself.",
        },
        ["S.Updates.Check"] = new[] { "Перевірити зараз", "Check now" },
        ["S.Updates.Checking"] = new[] { "Перевіряю…", "Checking…" },
        ["S.Updates.UpToDate"] = new[] { "Встановлено останню версію · перевірено {0}", "You have the latest version · checked {0}" },
        ["S.Updates.Never"] = new[] { "Ще не перевірялося", "Not checked yet" },
        ["S.Updates.Failed"] = new[] { "Не вдалося перевірити: {0}", "Couldn't check: {0}" },
        ["S.Updates.Available"] = new[] { "Доступна версія {0}", "Version {0} is available" },
        ["S.Updates.OpenPage"] = new[] { "Що нового — на GitHub", "What's new, on GitHub" },
        ["S.Updates.Install"] = new[] { "Завантажити й встановити", "Download and install" },
        ["S.Updates.Downloading"] = new[] { "Завантажую… {0:0}%", "Downloading… {0:0}%" },
        ["S.Updates.DownloadFailed"] = new[] { "Не вдалося завантажити: {0}", "Couldn't download: {0}" },
        ["S.Updates.Starting"] = new[] { "Запускаю інсталятор…", "Starting the installer…" },
        ["S.History"] = new[] { "Історія оновлень", "Update history" },
        ["N.UpdateText"] = new[] { "Натисни, щоб відкрити й оновити.", "Click to open and update." },
        ["T.Update"] = new[] { "Оновити до {0}", "Update to {0}" },

        // ===== Screenshot help (settings accordion) =====
        ["H.Area"] = new[] { "Область", "Area" },
        ["H.Drawing"] = new[] { "Малювання", "Drawing" },
        ["H.Drawn"] = new[] { "Намальовані елементи", "Drawn elements" },
        ["H.Result"] = new[] { "Результат", "Result" },
        ["H.Drag"] = new[] { "ЛКМ + тягнути", "Drag" },
        ["H.SelectArea"] = new[] { "Виділити область", "Select an area" },
        ["H.Corner"] = new[] { "Кут + тягнути", "Drag a corner" },
        ["H.Resize"] = new[] { "Змінити розмір області", "Resize the area" },
        ["H.Side"] = new[] { "Сторона + тягнути", "Drag a side" },
        ["H.Move"] = new[] { "Перемістити всю область", "Move the whole area" },
        ["H.Outside"] = new[] { "ЛКМ повз область", "Click outside" },
        ["H.Reselect"] = new[] { "Виділити наново — поки нічого не намальовано", "Select again, while nothing is drawn yet" },
        ["H.Toolbar"] = new[] { "Панель", "Toolbar" },
        ["H.ToolbarText"] = new[]
        {
            "Інструменти (перший обраний одразу; порядок змінюється перетягуванням), колір, товщина",
            "Tools (the first one is selected at start; drag them to reorder), color, thickness",
        },
        ["H.PickTool"] = new[]
        {
            "Обрати інструмент за його місцем на панелі",
            "Pick the tool at that place on the toolbar",
        },
        ["H.Pencil"] = new[] { "Олівець", "Pencil" },
        ["H.PencilText"] = new[]
        {
            "Малювання від руки. Лінія трохи згладжується — без гострих кутів і ривків",
            "Freehand drawing. The line is smoothed a little: no sharp corners or jerks",
        },
        ["H.Blur"] = new[] { "Розмиття", "Blur" },
        ["H.BlurText"] = new[]
        {
            "Виділи область, яку треба сховати. Коліщатко — сила розмиття (останнього або взятого через Shift)",
            "Select the area to hide. Wheel: blur strength (of the last one or the one picked with Shift)",
        },
        ["H.Draw"] = new[] { "Намалювати обраний елемент", "Draw the selected element" },
        ["H.ShiftText"] = new[]
        {
            "Під час малювання лінії чи стрілки — строго по горизонталі або вертикалі",
            "Keeps a line or arrow strictly horizontal or vertical",
        },
        ["H.Text"] = new[] { "Текст", "Text" },
        ["H.TextText"] = new[]
        {
            "Виділи рамку під текст і друкуй. Enter — новий рядок",
            "Drag a box for the text and type. Enter: new line",
        },
        ["H.ClickOut"] = new[] { "Клік повз / Esc", "Click outside / Esc" },
        ["H.FinishText"] = new[] { "Завершити введення тексту", "Finish typing" },
        ["H.ClickText"] = new[] { "Клік по тексту", "Click on text" },
        ["H.EditText"] = new[]
        {
            "Редагувати текст, коли обрано інструмент «Текст»",
            "Edit the text while the Text tool is selected",
        },
        ["H.ShiftDrag"] = new[] { "Shift + тягнути", "Shift + drag" },
        ["H.MoveElement"] = new[]
        {
            "Перемістити елемент. Поки Shift затиснутий, обраний елемент обведено пунктиром",
            "Move an element. While Shift is held, the picked element has a dashed outline",
        },
        ["H.Wheel"] = new[] { "Коліщатко", "Wheel" },
        ["H.WheelText"] = new[]
        {
            "Товщина лінії, розмір тексту або сила розмиття — в останнього намальованого або взятого через Shift",
            "Line thickness, text size or blur strength of the last drawn element or the one picked with Shift",
        },
        ["H.Color"] = new[] { "Колір на панелі", "Toolbar color" },
        ["H.ColorText"] = new[]
        {
            "Для нових елементів. Елемент, взятий через Shift, і текст під час введення теж перефарбовуються",
            "For new elements. The element picked with Shift and text being typed are recolored too",
        },
        ["H.Undo"] = new[] { "Крок назад — прибрати останній елемент", "Undo: remove the last element" },
        ["H.Copy"] = new[] { "Скопіювати в буфер обміну й закрити", "Copy to the clipboard and close" },
        ["H.Save"] = new[] { "Зберегти в PNG", "Save as PNG" },
        ["H.Close"] = new[] { "Закрити без збереження", "Close without saving" },

        // ===== Hotkey editor =====
        ["HK.IsDefault"] = new[] { "Стоїть стандартне сполучення", "The default shortcut is set" },
        ["HK.Reset"] = new[] { "Повернути стандартне: {0}", "Reset to default: {0}" },
        ["HK.Taken"] = new[] { "Зайнято іншою програмою — обери інше сполучення", "Taken by another app, pick another shortcut" },
        ["HK.Click"] = new[] { "Натисни, щоб задати інше сполучення", "Click to set another shortcut" },
        ["HK.AddMod"] = new[] { "Додай Ctrl, Alt або Win", "Add Ctrl, Alt or Win" },
        ["HK.Press"] = new[] { "Натисни сполучення…", "Press a shortcut…" },
        ["HK.EscCancel"] = new[] { "Esc — скасувати", "Esc to cancel" },

        // ===== Controller: hotkey titles (as in "Already used for: …"), toasts =====
        ["C.Title.Overlay"] = new[] { "плашку розкладки", "the layout pill" },
        ["C.Title.Skip"] = new[] { "пропуск мов", "language skipping" },
        ["C.Title.Win"] = new[] { "блокування Win", "the Win blocker" },
        ["C.Title.Dash"] = new[] { "довге тире", "the em dash" },
        ["C.Title.Shot"] = new[] { "скриншот", "screenshots" },
        ["C.Title.Exit"] = new[] { "закриття програми", "quitting the app" },
        ["C.InUse"] = new[] { "Уже використовується: {0}", "Already used for {0}" },
        ["C.TakenByOther"] = new[] { "Це сполучення вже зайняте іншою програмою", "Another app already uses this shortcut" },
        ["C.LayoutOn"] = new[] { "Розкладка: увімк", "Layout pill: on" },
        ["C.LayoutOff"] = new[] { "Розкладка: вимк", "Layout pill: off" },
        ["C.NoSkipLangs"] = new[] { "Мови для пропуску не обрано", "No languages to skip are selected" },
        ["C.Skipped"] = new[] { "{0}: пропускається", "{0}: skipped" },
        ["C.InSwitch"] = new[] { "{0}: у перемиканні", "{0}: back in switching" },
        ["C.WinBlocked"] = new[] { "Win: заблокована", "Win: blocked" },
        ["C.WinWorks"] = new[] { "Win: працює", "Win: works" },
        ["C.ShotFailed"] = new[] { "Скриншот не вдався", "Screenshot failed" },

        // ===== Tray menu =====
        ["T.Settings"] = new[] { "Налаштування", "Settings" },
        ["T.Shot"] = new[] { "Зробити скриншот", "Take a screenshot" },
        ["T.Win"] = new[] { "Блокування Win", "Win blocker" },
        ["T.Exit"] = new[] { "Вихід", "Quit" },

        // ===== Screenshot editor =====
        ["Sh.Hint"] = new[] { "Виділи область мишкою  ·  Esc — вийти", "Select an area with the mouse  ·  Esc to exit" },
        ["Sh.Arrow"] = new[] { "Стрілка ({0})", "Arrow ({0})" },
        ["Sh.Rect"] = new[] { "Прямокутник ({0})", "Rectangle ({0})" },
        ["Sh.Line"] = new[] { "Лінія ({0}). Shift — рівно по горизонталі або вертикалі", "Line ({0}). Shift: strictly horizontal or vertical" },
        ["Sh.Text"] = new[] { "Текст ({0}) — виділи місце під текст", "Text ({0}): drag a box for the text" },
        ["Sh.Blur"] = new[] { "Розмиття ({0}) — виділи область, коліщатко змінює силу", "Blur ({0}): select an area, the wheel changes strength" },
        ["Sh.Pencil"] = new[] { "Олівець ({0}) — малюй від руки, лінія згладжується", "Pencil ({0}): draw freehand, the line is smoothed" },
        ["Sh.DragHint"] = new[] { "Перетягни, щоб змінити порядок", "Drag to reorder" },
        ["Sh.Size"] = new[] { "Коліщатко миші — товщина лінії / розмір тексту", "Mouse wheel: line thickness / text size" },
        ["Sh.Copy"] = new[] { "Копіювати (Enter, Ctrl+C)", "Copy (Enter, Ctrl+C)" },
        ["Sh.Save"] = new[] { "Зберегти (Ctrl+S)", "Save (Ctrl+S)" },
        ["Sh.Close"] = new[] { "Закрити (Esc)", "Close (Esc)" },
        ["Sh.Color"] = new[] { "Колір", "Color" },
        ["Sh.Copied"] = new[] { "Скопійовано", "Copied" },
        ["Sh.CopyFailed"] = new[] { "Не вдалося скопіювати", "Couldn't copy" },
        ["Sh.SaveTitle"] = new[] { "Зберегти скриншот", "Save screenshot" },
        ["Sh.Saved"] = new[] { "Збережено", "Saved" },
        ["Sh.SaveFailed"] = new[] { "Не вдалося зберегти", "Couldn't save" },
        ["Sh.Caption"] = new[] { "Підпис до скриншоту", "Screenshot caption" },

        // ===== Installer =====
        ["I.WindowTitle"] = new[] { "Встановлення Simple Windows Tools", "Simple Windows Tools Setup" },
        ["I.Install"] = new[] { "Встановлення {0}", "Install {0}" },
        ["I.Reinstall"] = new[] { "Перевстановлення {0}", "Reinstall {0}" },
        ["I.Update"] = new[] { "Оновлення {0} → {1}", "Update {0} → {1}" },
        ["I.Intro"] = new[] { "Маленька програма в треї для Windows 10 і 11:", "A small tray app for Windows 10 and 11:" },
        ["I.F1"] = new[] { "•  плашка EN / RU / UA і пропуск непотрібних мов", "•  EN / RU / UA layout pill and skipping unneeded languages" },
        ["I.F2"] = new[] { "•  блокування клавіші Win, Win + Space і Alt + Space", "•  blocks the Win key, Win + Space and Alt + Space" },
        ["I.F3"] = new[] { "•  довге тире «—» на Alt + «−»", "•  em dash “—” on Alt + “−”" },
        ["I.F4"] = new[] { "•  скриншоти зі стрілками, рамками, лініями, текстом, олівцем і розмиттям", "•  screenshots with arrows, boxes, lines, text, pencil and blur" },
        ["I.F5"] = new[] { "•  інтерфейс українською та англійською", "•  interface in Ukrainian and English" },
        ["I.Autostart.Text"] = new[] { "Потім можна вимкнути в налаштуваннях або в Диспетчері завдань.", "Can be turned off later in settings or in Task Manager." },
        ["I.Desktop"] = new[] { "Ярлик на робочому столі", "Desktop shortcut" },
        ["I.Folder"] = new[] { "Тека", "Folder" },
        ["I.DefaultFolder"] = new[] { "Повернути типову теку", "Back to the default folder" },
        ["I.Change"] = new[] { "Змінити…", "Change…" },
        ["I.FolderWarning"] = new[]
        {
            "Тека поза Program Files: будь-яка програма на цьому комп'ютері зможе підмінити файл, а він запускається з правами адміністратора без запиту. Краще залишити типову теку.",
            "This folder is outside Program Files: any program on this computer could replace the file, and it runs with admin rights without asking. Better keep the default folder.",
        },
        ["I.Installing"] = new[] { "Встановлюю…", "Installing…" },
        ["I.Cancel"] = new[] { "Скасувати", "Cancel" },
        ["I.InstallButton"] = new[] { "Встановити", "Install" },
        ["I.UpdateButton"] = new[] { "Оновити", "Update" },
        ["I.Size"] = new[] { "Займе {0}", "Needs {0}" },
        ["I.SizeAbout"] = new[] { "Займе близько 75 МБ", "Needs about 75 MB" },
        ["I.Free"] = new[] { " · вільно {0} на диску {1}", " · {0} free on drive {1}" },
        ["I.GB"] = new[] { "ГБ", "GB" },
        ["I.MB"] = new[] { "МБ", "MB" },
        ["I.KB"] = new[] { "КБ", "KB" },
        ["I.FolderDialog"] = new[]
        {
            "Куди встановити Simple Windows Tools (усередині буде створено теку «Simple Windows Tools»)",
            "Where to install Simple Windows Tools (a “Simple Windows Tools” folder will be created inside)",
        },
        ["N.WelcomeTitle"] = new[] { "Simple Windows Tools працює", "Simple Windows Tools is running" },
        ["N.WelcomeText"] = new[]
        {
            "Програма живе в треї. Клікни по іконці, щоб відкрити налаштування.",
            "The app lives in the tray. Click its icon to open the settings.",
        },
        ["I.Failed"] = new[] { "Не вдалося встановити", "Installation failed" },
        ["I.P.Stop"] = new[] { "Закриваю запущену копію…", "Closing the running copy…" },
        ["I.P.Copy"] = new[] { "Копіюю файли…", "Copying files…" },
        ["I.P.Shortcuts"] = new[] { "Створюю ярлики…", "Creating shortcuts…" },
        ["I.P.Register"] = new[] { "Реєструю в «Програмах»…", "Registering in Apps…" },
        ["I.P.Task"] = new[] { "Налаштовую запуск із правами адміністратора…", "Setting up launch with admin rights…" },
        ["I.TaskFailed"] = new[] { "Не вдалося створити завдання в Планувальнику.", "Couldn't create the Task Scheduler task." },

        // ===== Uninstall =====
        ["U.Ask"] = new[] { "Видалити Simple Windows Tools разом із налаштуваннями?", "Remove Simple Windows Tools along with its settings?" },
        ["U.Done"] = new[] { "Simple Windows Tools видалено.", "Simple Windows Tools has been removed." },
        ["U.Failed"] = new[] { "Не вдалося видалити: {0}", "Couldn't remove it: {0}" },
    };

    /// <summary>Placeholders like {0} or {1:0.0} in a format string, for the self-test.</summary>
    public static string[] Placeholders(string s) =>
        System.Text.RegularExpressions.Regex.Matches(s, @"\{(\d+)").Select(m => m.Groups[1].Value).Distinct().OrderBy(x => x).ToArray();
}
