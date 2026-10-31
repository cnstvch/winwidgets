using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Threading;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using Microsoft.Win32;

namespace WinWidgets
{
    public static class Program
    {
        [STAThread]
        public static void Main()
        {
            bool created;
            using (var mutex = new Mutex(true, "WinWidgets.SingleInstance.v1", out created))
            {
                if (!created) return;
                var app = new System.Windows.Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                app.DispatcherUnhandledException += (s, e) => { e.Handled = true; };   // виджет не должен падать из-за мелочи
                AppActions.Start(app);
                app.Run();
            }
        }
    }

    public static class AppActions
    {
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string RunName = "WinWidgets";

        static System.Windows.Application _app;
        static IslandWindow _island;
        static ClockWindow _clock;
        static DockWindow _dock;
        static ControlCenterWindow _cc;
        static SettingsWindow _settings;
        static System.Windows.Forms.NotifyIcon _tray;
        static System.Windows.Forms.ToolStripMenuItem _autoStartItem;
        static HwndSource _msg;
        static DispatcherTimer _watch;
        static bool _taskbarHidden;
        static bool _suppressed;

        public static void Start(System.Windows.Application app)
        {
            _app = app;
            ConfigStore.Init();
            Wallpaper.Reload();
            MediaService.Init();

            _island = new IslandWindow();
            _clock = new ClockWindow();
            _dock = new DockWindow();
            _cc = new ControlCenterWindow();

            if (ConfigStore.Current.Island.Enabled) _island.Show();

            BuildTray();
            RegisterHotkey();

            ConfigStore.Changed += OnConfigChanged;
            OnConfigChanged();

            SystemEvents.UserPreferenceChanged += (s, e) =>
            {
                if (e.Category == UserPreferenceCategory.Desktop)
                    _app.Dispatcher.BeginInvoke(new Action(() => { Wallpaper.Reload(); ConfigStore.NotifyChanged(); }));
            };
            SystemEvents.DisplaySettingsChanged += (s, e) =>
                _app.Dispatcher.BeginInvoke(new Action(() => { Wallpaper.Reload(); ConfigStore.NotifyChanged(); }));

            // раз в 1.5 с: прячем остров и док над полноэкранными играми/видео, иначе поддерживаем «поверх всех»
            _watch = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1500) };
            _watch.Tick += (s, e) =>
            {
                bool fs = Native.IsForegroundFullscreen();
                if (fs != _suppressed)
                {
                    _suppressed = fs;
                    _island.SetSuppressed(fs || !ConfigStore.Current.Island.Enabled);
                    _dock.SetSuppressed(fs || !ConfigStore.Current.Dock.Enabled);
                }
                if (!fs)
                {
                    if (_island.IsVisible) Native.KeepTopmost(_island);
                    if (_dock.IsVisible) Native.KeepTopmost(_dock);
                }
            };
            _watch.Start();

            // сборка мусора в простое: держим память низкой
            var gc = new DispatcherTimer { Interval = TimeSpan.FromMinutes(5) };
            gc.Tick += (s, e) => { GC.Collect(2, GCCollectionMode.Optimized, false); };
            gc.Start();
        }

        static void OnConfigChanged()
        {
            var c = ConfigStore.Current;

            if (c.Island.Enabled) { if (!_suppressed) _island.SetSuppressed(false); if (!_island.IsVisible) _island.Show(); }
            else _island.SetSuppressed(true);

            bool wantHide = c.Dock.Enabled && c.Dock.HideWindowsTaskbar;
            if (wantHide != _taskbarHidden)
            {
                Native.SetTaskbarAutoHide(wantHide);
                _taskbarHidden = wantHide;
            }
        }

        // ---------- действия ----------
        public static void ToggleControlCenter() { _cc.Toggle(); }

        public static void OpenSettings()
        {
            if (_settings == null || !_settings.IsLoaded)
            {
                _settings = new SettingsWindow();
                _settings.Closed += (s, e) => _settings = null;
                _settings.Show();
            }
            _settings.Activate();
        }

        public static void OpenConfigFile()
        {
            try { ConfigStore.Save(); Process.Start(new ProcessStartInfo(ConfigStore.FilePath) { UseShellExecute = true }); }
            catch { }
        }

        public static void Exit()
        {
            try
            {
                if (_taskbarHidden) Native.SetTaskbarAutoHide(false);
                ConfigStore.Save();
                if (_msg != null) Native.UnregisterHotKey(_msg.Handle, 1);
                if (_tray != null) { _tray.Visible = false; _tray.Dispose(); }
            }
            catch { }
            _app.Shutdown();
        }

        // ---------- автозапуск ----------
        static bool IsAutoStart()
        {
            try { using (var k = Registry.CurrentUser.OpenSubKey(RunKey)) return k?.GetValue(RunName) != null; }
            catch { return false; }
        }

        static void SetAutoStart(bool on)
        {
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(RunKey, true))
                {
                    if (on) k.SetValue(RunName, "\"" + Environment.ProcessPath + "\"");
                    else k.DeleteValue(RunName, false);
                }
            }
            catch { }
        }

        // ---------- хоткей Ctrl+Alt+C ----------
        static void RegisterHotkey()
        {
            var p = new HwndSourceParameters("WinWidgetsMsg") { ParentWindow = new IntPtr(-3) /* HWND_MESSAGE */, Width = 0, Height = 0 };
            _msg = new HwndSource(p);
            _msg.AddHook((IntPtr h, int m, IntPtr w, IntPtr l, ref bool handled) =>
            {
                if (m == Native.WM_HOTKEY && w.ToInt32() == 1) { ToggleControlCenter(); handled = true; }
                return IntPtr.Zero;
            });
            Native.RegisterHotKey(_msg.Handle, 1, Native.MOD_CONTROL | Native.MOD_ALT | Native.MOD_NOREPEAT, 0x43);
        }

        // ---------- трей ----------
        static void BuildTray()
        {
            _tray = new System.Windows.Forms.NotifyIcon
            {
                Icon = MakeIcon(),
                Text = "WinWidgets",
                Visible = true,
            };
            var menu = new System.Windows.Forms.ContextMenuStrip();
            menu.Items.Add("Панель управления  (Ctrl+Alt+C)", null, (s, e) => ToggleControlCenter());
            menu.Items.Add("Настройки…", null, (s, e) => OpenSettings());
            menu.Items.Add("Открыть config.json", null, (s, e) => OpenConfigFile());
            menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
            _autoStartItem = new System.Windows.Forms.ToolStripMenuItem("Запускать вместе с Windows") { CheckOnClick = true, Checked = IsAutoStart() };
            _autoStartItem.CheckedChanged += (s, e) => SetAutoStart(_autoStartItem.Checked);
            menu.Items.Add(_autoStartItem);
            menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
            menu.Items.Add("Выход", null, (s, e) => Exit());
            _tray.ContextMenuStrip = menu;
            _tray.MouseClick += (s, e) =>
            {
                if (e.Button == System.Windows.Forms.MouseButtons.Left) ToggleControlCenter();
            };
        }

        static Icon MakeIcon()
        {
            using (var bmp = new Bitmap(32, 32))
            {
                using (var g = Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    using (var path = new GraphicsPath())
                    {
                        path.AddArc(1, 1, 12, 12, 180, 90);
                        path.AddArc(19, 1, 12, 12, 270, 90);
                        path.AddArc(19, 19, 12, 12, 0, 90);
                        path.AddArc(1, 19, 12, 12, 90, 90);
                        path.CloseFigure();
                        using (var br = new LinearGradientBrush(new Rectangle(0, 0, 32, 32),
                            Color.FromArgb(255, 122, 92, 255), Color.FromArgb(255, 42, 196, 255), 45f))
                            g.FillPath(br, path);
                    }
                    using (var w = new SolidBrush(Color.FromArgb(235, 255, 255, 255)))
                        g.FillRectangle(w, 8, 9, 16, 4);
                    using (var w2 = new SolidBrush(Color.FromArgb(150, 255, 255, 255)))
                        g.FillRectangle(w2, 8, 17, 16, 6);
                }
                return Icon.FromHandle(bmp.GetHicon());
            }
        }
    }
}
