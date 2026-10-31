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
        public double Width { get; set; } = 500;
        public double Height { get; set; } = 86;
    }

    public class DockCfg
    {
        public bool Enabled { get; set; } = true;
        public double IconSize { get; set; } = 56;
        public double MaxScale { get; set; } = 1.7;
        public bool ShowRunning { get; set; } = true;
        public bool HideWindowsTaskbar { get; set; } = false;
        public double BottomMargin { get; set; } = 14;
        public List<AppEntry> Apps { get; set; } = new List<AppEntry>
        {
            new AppEntry { Name = "Проводник", Path = @"C:\Windows\explorer.exe" },
            new AppEntry { Name = "Edge", Path = @"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe" },
            new AppEntry { Name = "Блокнот", Path = @"C:\Windows\System32\notepad.exe" },
            new AppEntry { Name = "Калькулятор", Path = @"C:\Windows\System32\calc.exe" },
            new AppEntry { Name = "Paint", Path = @"C:\Windows\System32\mspaint.exe" },
        };
    }

    public class GlassCfg
    {
        public string Tint { get; set; } = "#14161F";
        public double TintOpacity { get; set; } = 0.42;
        public bool WallpaperBlur { get; set; } = true;
        public double Rim { get; set; } = 0.7;          // яркость светового ободка 0..1
        public double Sheen { get; set; } = 0.20;       // блик сверху 0..1
    }

    public class Config
    {
        public ClockCfg Clock { get; set; } = new ClockCfg();
        public IslandCfg Island { get; set; } = new IslandCfg();
        public DockCfg Dock { get; set; } = new DockCfg();
        public GlassCfg Glass { get; set; } = new GlassCfg();
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
                Current = cfg;
                return true;
            }
            catch
            {
                return false; // битый JSON — остаёмся на предыдущем конфиге
            }
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
