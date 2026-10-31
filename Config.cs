using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;

namespace WinWidgets
{
    public class AppEntry
    {
        public string Name { get; set; } = "";
        public string Path { get; set; } = "";
    }

    public class ClockCfg
    {
        public bool Enabled { get; set; } = true;
        public string Font { get; set; } = "Segoe UI";
        public string Color { get; set; } = "#FFFFFF";
        public int Weight { get; set; } = 200;          // 100..900
        public double Spacing { get; set; } = -3;       // межсимвольный интервал, px
        public double Opacity { get; set; } = 0.92;     // 0..1
        public double Glass { get; set; } = 0.64;       // 0..1, сила «стеклянного» градиента в буквах
        public double Size { get; set; } = 200;         // размер цифр
        public bool Seconds { get; set; } = false;
        public bool Hour24 { get; set; } = true;
        public bool ShowDate { get; set; } = true;
        public bool SendToBack { get; set; } = false;   // true: часы под всеми окнами (если пропали — верни false)
        public double Left { get; set; } = 96;
        public double Top { get; set; } = 96;
    }

    public class IslandCfg
    {
        public bool Enabled { get; set; } = true;
        public bool ExpandOnHover { get; set; } = true;
        public bool IdleClock { get; set; } = false;    // как на маке: в простое челка пустая
        public double Width { get; set; } = 440;
        public double Height { get; set; } = 132;
    }

    public class WidgetCfg
    {
        public bool Enabled { get; set; } = true;
        public double Size { get; set; } = 150;
        public double Left { get; set; } = 96;
        public double Top { get; set; } = 690;
    }

    public class DockCfg
    {
        public bool Enabled { get; set; } = true;
        public double IconSize { get; set; } = 56;
        public double MaxScale { get; set; } = 1.7;
        public bool ShowRunning { get; set; } = true;
        public bool HideWindowsTaskbar { get; set; } = false;
        public bool ShowTrash { get; set; } = true;
        public double BottomMargin { get; set; } = 10;
        // Пути понимают переменные окружения (%APPDATA% и т.д.). Несуществующие пропускаются.
        public List<AppEntry> Apps { get; set; } = new List<AppEntry>
        {
            new AppEntry { Name = "Проводник", Path = @"%WINDIR%\explorer.exe" },
            new AppEntry { Name = "Edge", Path = @"%ProgramFiles(x86)%\Microsoft\Edge\Application\msedge.exe" },
            new AppEntry { Name = "Chrome", Path = @"%ProgramFiles%\Google\Chrome\Application\chrome.exe" },
            new AppEntry { Name = "Telegram", Path = @"%APPDATA%\Telegram Desktop\Telegram.exe" },
            new AppEntry { Name = "Spotify", Path = @"%APPDATA%\Spotify\Spotify.exe" },
            new AppEntry { Name = "VS Code", Path = @"%LOCALAPPDATA%\Programs\Microsoft VS Code\Code.exe" },
            new AppEntry { Name = "Блокнот", Path = @"%WINDIR%\System32\notepad.exe" },
            new AppEntry { Name = "Калькулятор", Path = @"%WINDIR%\System32\calc.exe" },
        };
    }

    public class GlassCfg
    {
        public bool Light { get; set; } = true;          // светлое стекло как в macOS; false — тёмное
        public string Accent { get; set; } = "#0A84FF";  // синий переключателей
        public bool WallpaperBlur { get; set; } = true;
        public double Opacity { get; set; } = 0.40;      // плотность подложки стекла 0..1
    }

    public class Config
    {
        public ClockCfg Clock { get; set; } = new ClockCfg();
        public IslandCfg Island { get; set; } = new IslandCfg();
        public DockCfg Dock { get; set; } = new DockCfg();
        public GlassCfg Glass { get; set; } = new GlassCfg();
        public WidgetCfg AnalogClock { get; set; } = new WidgetCfg { Left = 96, Top = 690 };
        public WidgetCfg Calendar { get; set; } = new WidgetCfg { Left = 276, Top = 690 };
    }

    public static class ConfigStore
    {
        public static Config Current { get; private set; } = new Config();
        public static event Action Changed;

        public static readonly string Dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WinWidgets");
        public static readonly string FilePath = Path.Combine(Dir, "config.json");

        static readonly JsonSerializerOptions Opt = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        };

        static FileSystemWatcher _watcher;
        static Timer _debounce;
        static DateTime _lastOwnSave = DateTime.MinValue;

        public static void Init()
        {
            Directory.CreateDirectory(Dir);
            if (!File.Exists(FilePath)) Save();
            else Load();

            _watcher = new FileSystemWatcher(Dir, "config.json")
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName,
                EnableRaisingEvents = true,
            };
            _watcher.Changed += OnFileEvent;
            _watcher.Created += OnFileEvent;
            _watcher.Renamed += OnFileEvent;
        }

        static void OnFileEvent(object s, FileSystemEventArgs e)
        {
            if ((DateTime.UtcNow - _lastOwnSave).TotalMilliseconds < 700) return;
            _debounce?.Dispose();
            _debounce = new Timer(_ =>
            {
                var app = System.Windows.Application.Current;
                if (app == null) return;
                app.Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (Load()) Changed?.Invoke();
                }));
            }, null, 350, Timeout.Infinite);
        }

        public static bool Load()
        {
            try
            {
                string json = File.ReadAllText(FilePath);
                var cfg = JsonSerializer.Deserialize<Config>(json, Opt);
                if (cfg == null) return false;
                Sanitize(cfg);
                Current = cfg;
                return true;
            }
            catch
            {
                return false; // битый JSON — остаёмся на предыдущем конфиге
            }
        }

        /// <summary>Сбросить конфиг (старый сохраняется рядом как config.bad.json).</summary>
        public static void ResetToDefaults()
        {
            try
            {
                if (File.Exists(FilePath)) File.Copy(FilePath, Path.Combine(Dir, "config.bad.json"), true);
            }
            catch { }
            Current = new Config();
            Save();
        }

        static double Clamp(double v, double lo, double hi)
        {
            if (double.IsNaN(v) || double.IsInfinity(v)) return lo;
            return v < lo ? lo : (v > hi ? hi : v);
        }

        static string GoodColor(string s, string fallback)
        {
            try { System.Windows.Media.ColorConverter.ConvertFromString(s); return s; }
            catch { return fallback; }
        }

        /// <summary>Любое мусорное значение превращается в безопасное — виджеты не должны падать из-за конфига.</summary>
        public static void Sanitize(Config c)
        {
            if (c.Clock == null) c.Clock = new ClockCfg();
            if (c.Island == null) c.Island = new IslandCfg();
            if (c.Dock == null) c.Dock = new DockCfg();
            if (c.Glass == null) c.Glass = new GlassCfg();
            if (c.AnalogClock == null) c.AnalogClock = new WidgetCfg();
            if (c.Calendar == null) c.Calendar = new WidgetCfg();
            if (c.Dock.Apps == null) c.Dock.Apps = new List<AppEntry>();

            var k = c.Clock;
            k.Size = Clamp(k.Size, 40, 400);
            k.Weight = (int)Clamp(k.Weight, 100, 900);
            k.Spacing = Clamp(k.Spacing, -30, 60);
            k.Opacity = Clamp(k.Opacity, 0.05, 1);
            k.Glass = Clamp(k.Glass, 0, 1);
            k.Left = Clamp(k.Left, -2000, 8000);
            k.Top = Clamp(k.Top, -2000, 8000);
            k.Color = GoodColor(k.Color, "#FFFFFF");
            k.Font = FontOrDefault(k.Font);

            c.Island.Width = Clamp(c.Island.Width, 300, 800);
            c.Island.Height = Clamp(c.Island.Height, 100, 260);

            c.Dock.IconSize = Clamp(c.Dock.IconSize, 32, 110);
            c.Dock.MaxScale = Clamp(c.Dock.MaxScale, 1, 2.4);
            c.Dock.BottomMargin = Clamp(c.Dock.BottomMargin, 0, 120);

            c.Glass.Opacity = Clamp(c.Glass.Opacity, 0, 1);
            c.Glass.Accent = GoodColor(c.Glass.Accent, "#0A84FF");

            foreach (var w in new[] { c.AnalogClock, c.Calendar })
            {
                w.Size = Clamp(w.Size, 90, 320);
                w.Left = Clamp(w.Left, -2000, 8000);
                w.Top = Clamp(w.Top, -2000, 8000);
            }
        }

        static System.Collections.Generic.HashSet<string> _fonts;

        public static string FontOrDefault(string name)
        {
            try
            {
                if (_fonts == null)
                {
                    _fonts = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var f in System.Windows.Media.Fonts.SystemFontFamilies) _fonts.Add(f.Source);
                }
                if (!string.IsNullOrWhiteSpace(name) && _fonts.Contains(name.Trim())) return name.Trim();
            }
            catch { }
            return "Segoe UI";
        }

        public static void Save()
        {
            try
            {
                Directory.CreateDirectory(Dir);
                _lastOwnSave = DateTime.UtcNow;
                File.WriteAllText(FilePath, JsonSerializer.Serialize(Current, Opt));
            }
            catch { }
        }

        public static void NotifyChanged() { Changed?.Invoke(); }
    }
}
