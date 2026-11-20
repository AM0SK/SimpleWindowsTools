using System;

namespace SimpleWindowsTools.Core;

/// <summary>
/// "What's new" in the settings window, newest first. Add an entry for every version that ships —
/// the self-test fails if the newest one doesn't match the assembly version.
/// Each item is { Ukrainian, English }.
/// </summary>
public static class Changelog
{
    public sealed record Release(string Version, DateOnly Date, string[][] Items);

    public static readonly Release[] Releases =
    {
        new("0.6.3", new DateOnly(2026, 11, 20), new[]
        {
            new[] { "Світлу тему прибрано: програма знову лише темна",
                    "The light theme was removed: the app is dark only again" },
        }),
        new("0.6.2", new DateOnly(2026, 11, 20), new[]
        {
            new[] { "Інсталятор без зайвого кроку «Готово»: після оновлення програма одразу запускається, після першого встановлення відкриває налаштування",
                    "No extra “Done” step in the installer: after an update the app just starts, after a first install it opens its settings" },
        }),
        new("0.6.1", new DateOnly(2026, 11, 20), new[]
        {
            new[] { "Завантаження оновлення більше не зависає, якщо зв'язок завмер, і не запускає недокачаний файл",
                    "Downloading an update no longer hangs when the connection freezes, and a cut-off file is never started" },
        }),
        new("0.6.0", new DateOnly(2026, 11, 20), new[]
        {
            new[] { "Олівець у редакторі скриншотів (клавіша 6): малювання від руки, лінія згладжується",
                    "Pencil in the screenshot editor (key 6): freehand drawing with a smoothed line" },
            new[] { "Інструменти на панелі можна перетягувати мишкою — тут або в налаштуваннях. Клавіші 1–6 ідуть за порядком",
                    "Drag the toolbar tools to reorder them, in the editor or in settings. Keys 1–6 follow the order" },
            new[] { "Інтерфейс тепер українською та англійською, російську прибрано",
                    "The interface is now in Ukrainian and English; Russian was removed" },
            new[] { "Оновлення — внизу вкладки «Програма», історія оновлень згорнута",
                    "Updates moved to the bottom of the App tab, the update history is collapsed" },
        }),
        new("0.5.1", new DateOnly(2026, 11, 20), new[]
        {
            new[] { "Програма займає значно менше пам'яті й більше не «росте» після скриншотів і налаштувань",
                    "Uses much less memory and no longer grows after screenshots and the settings window" },
        }),
        new("0.5.0", new DateOnly(2026, 11, 20), new[]
        {
            new[] { "Світла тема. Типово тема як у Windows і перемикається разом із нею",
                    "Light theme. By default the theme follows Windows and switches along with it" },
            new[] { "Налаштування розділено на дві вкладки: «Функції» і «Програма»",
                    "Settings are split into two tabs: Features and App" },
            new[] { "Оновлення з GitHub: сповіщення про нову версію і встановлення в один клік. Можна вимкнути",
                    "Updates from GitHub: a notification about a new version and one-click install. Can be turned off" },
        }),
        new("0.4.0", new DateOnly(2026, 11, 20), new[]
        {
            new[] { "Інтерфейс українською та англійською. Мову можна обрати в налаштуваннях і в інсталяторі",
                    "Interface in Ukrainian and English. Pick the language in settings or in the installer" },
            new[] { "Розділ «Що нового» в налаштуваннях",
                    "This “What's new” section in settings" },
        }),
        new("0.3.1", new DateOnly(2026, 11, 20), new[]
        {
            new[] { "Мова, що пропускається (наприклад, UA), більше не блимає в плашці, коли вікно перемикається повільно",
                    "A skipped language (e.g. UA) no longer flashes in the pill when a window switches slowly" },
        }),
        new("0.3.0", new DateOnly(2026, 11, 19), new[]
        {
            new[] { "Розмиття в редакторі скриншотів: виділи область — її буде сховано. Коліщатко змінює силу",
                    "Blur in the screenshot editor: select an area to hide it. The wheel changes the strength" },
        }),
        new("0.2.0", new DateOnly(2026, 11, 19), new[]
        {
            new[] { "Інсталятор: ставить у Program Files, оновлює поверх, видаляється через «Програми»",
                    "Installer: goes to Program Files, updates in place, uninstalls from Apps" },
            new[] { "Автозапуск з Windows без вікна UAC",
                    "Starts with Windows without a UAC prompt" },
        }),
        new("0.1.0", new DateOnly(2026, 11, 19), new[]
        {
            new[] { "Перша версія: плашка розкладки, пропуск мов, блокування Win, довге тире, редактор скриншотів",
                    "First version: layout pill, language skipping, Win blocker, em dash, screenshot editor" },
        }),
    };
}
