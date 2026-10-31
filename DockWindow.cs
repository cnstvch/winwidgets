using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace WinWidgets
{
    class DockItem
    {
        public string Path = "";
        public string Name = "";
        public bool Pinned;
        public List<IntPtr> Windows = new List<IntPtr>();
        public int Cycle;
        public Grid Cell;
        public Image Img;
        public Ellipse Dot;
        public TranslateTransform Bounce = new TranslateTransform();
        public double Scale = 1, Target = 1, BaseX;
        public bool Special;
    }

    /// <summary>Плавающий док: увеличение под курсором, точки у запущенных, закрепление через ПКМ.</summary>
    public class DockWindow : Window
    {
        const double Gap = 6, PadX = 10, PadY = 8, SepW = 15, TipH = 44;
        const string TrashParse = "::{645FF040-5081-101B-9F08-00AA002F954E}";

        readonly Grid _root = new Grid();
        readonly GlassPanel _glass = new GlassPanel(24) { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom };
        readonly StackPanel _row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom };
        readonly Border _tip = new Border
        {
            CornerRadius = new CornerRadius(7), Padding = new Thickness(11, 4, 11, 5), IsHitTestVisible = false,
            Background = Theme.Solid(Color.FromArgb(214, 38, 38, 42)),
            HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom, Opacity = 0,
        };
        readonly TextBlock _tipText = new TextBlock { Foreground = Brushes.White, FontSize = 12.5, FontWeight = FontWeights.Medium };
        readonly DispatcherTimer _poll = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };

        readonly List<DockItem> _items = new List<DockItem>();
        readonly Dictionary<string, ImageSource> _icons = new Dictionary<string, ImageSource>(StringComparer.OrdinalIgnoreCase);
        double _baseLeft, _size;
        bool _animating, _polling;
        DockItem _hover;
        string _sig = "";

        public DockWindow()
        {
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ShowInTaskbar = false;
            ShowActivated = false;
            Topmost = true;
            ResizeMode = ResizeMode.NoResize;
            Focusable = false;
            UseLayoutRounding = false;
            FontFamily = Theme.UiFont;
            TextOptions.SetTextFormattingMode(this, TextFormattingMode.Ideal);

            _tip.Child = _tipText;
            _root.Children.Add(_glass);
            _root.Children.Add(_row);
            _root.Children.Add(_tip);
            Content = _root;

            SourceInitialized += (s, e) => Native.MakeToolWindow(this, true);
            MouseMove += OnMove;
            MouseLeave += (s, e) => SetHover(null, 0);

            _poll.Tick += (s, e) => PollRunning();
            _poll.Start();

            _cfgSig = CfgSig();
            ConfigStore.Changed += () =>
            {
                string s = CfgSig();
                if (s == _cfgSig) return;          // двигают ползунок часов — док не трогаем
                _cfgSig = s; _sig = ""; Rebuild();
            };
            Rebuild();
        }

        // ---------- построение ----------
        void Rebuild()
        {
            var c = ConfigStore.Current.Dock;
            if (!c.Enabled) { Hide(); return; }
            if (!IsVisible) Show();

            _size = c.IconSize;
            var running = Native.GetOpenWindows();
            BuildItems(c, running);
            LayoutWindow(c);
            ApplyScales();
            UpdateTopmost();
        }

        void BuildItems(DockCfg c, List<OpenWindow> running)
        {
            _items.Clear();
            foreach (var a in c.Apps)
            {
                string ap = Exp(a.Path);
                if (string.IsNullOrWhiteSpace(ap) || !File.Exists(ap)) continue;
                _items.Add(new DockItem { Path = ap, Name = string.IsNullOrWhiteSpace(a.Name) ? System.IO.Path.GetFileNameWithoutExtension(ap) : a.Name, Pinned = true });
            }
            if (c.ShowRunning)
            {
                foreach (var w in running)
                {
                    string n = System.IO.Path.GetFileName(w.Exe);
                    if (n.Equals("ApplicationFrameHost.exe", StringComparison.OrdinalIgnoreCase) ||
                        n.Equals("TextInputHost.exe", StringComparison.OrdinalIgnoreCase)) continue;
                    if (_items.Any(i => i.Path.Equals(w.Exe, StringComparison.OrdinalIgnoreCase))) continue;
                    _items.Add(new DockItem { Path = w.Exe, Name = System.IO.Path.GetFileNameWithoutExtension(w.Exe), Pinned = false });
                }
            }
            AssignWindows(running);
            if (c.ShowTrash && GetIcon(TrashParse) != null)
                _items.Add(new DockItem { Path = TrashParse, Name = "Корзина", Pinned = false, Special = true });

            _row.Children.Clear();
            int lastGroup = -1;
            double x = 0;
            foreach (var it in _items)
            {
                int group = it.Special ? 2 : (it.Pinned ? 0 : 1);
                if (lastGroup >= 0 && group != lastGroup)
                {
                    _row.Children.Add(new Border
                    {
                        Width = 1, Height = _size * 0.8, Margin = new Thickness(SepW / 2 - 0.5, 0, SepW / 2 - 0.5, 4),
                        VerticalAlignment = VerticalAlignment.Bottom,
                        Background = Theme.Solid(Theme.A(Ui.Fg, 0.25)),
                    });
                    x += SepW;
                }
                lastGroup = group;
                it.BaseX = x + _size / 2;
                x += _size + Gap;
                it.Scale = it.Target = 1;

                var img = new Image
                {
                    Source = GetIcon(it.Path),
                    Stretch = Stretch.Uniform,
                    RenderTransform = it.Bounce,
                    SnapsToDevicePixels = false,
                };
                RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
                var dot = new Ellipse
                {
                    Width = 4, Height = 4, Fill = Theme.Solid(Theme.A(Ui.Fg, 0.75)),
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom,
                    Margin = new Thickness(0, 0, 0, -8), IsHitTestVisible = false,
                    Visibility = (!it.Special && it.Windows.Count > 0) ? Visibility.Visible : Visibility.Collapsed,
                };
                var cell = new Grid { Width = _size, Height = _size, Margin = new Thickness(0, 0, Gap, 0), Background = Brushes.Transparent, Cursor = Cursors.Hand };
                cell.Children.Add(img); cell.Children.Add(dot);
                var captured = it;
                cell.MouseLeftButtonUp += (s, e) => Activate(captured);
                cell.MouseRightButtonUp += (s, e) => ShowMenu(captured, cell);
                it.Cell = cell; it.Img = img; it.Dot = dot;
                _row.Children.Add(cell);
            }
            if (_items.Count > 0) x -= Gap;
            _baseTotal = x;
            _sig = Signature();
        }

        double _baseTotal;
        string _cfgSig = "";

        static string CfgSig()
        {
            try { return System.Text.Json.JsonSerializer.Serialize(ConfigStore.Current.Dock) + ConfigStore.Current.Glass.Light; }
            catch { return ""; }
        }

        static string Exp(string path)
        {
            try { return Environment.ExpandEnvironmentVariables(path ?? ""); }
            catch { return path ?? ""; }
        }

        void LayoutWindow(DockCfg c)
        {
            double maxScale = Math.Max(1, c.MaxScale);
            double stretch = _size * (maxScale - 1) * 3.2;
            Width = _baseTotal + stretch + 2 * PadX + 60;
            Height = _size * maxScale + 2 * PadY + TipH + c.BottomMargin;
            Left = (SystemParameters.PrimaryScreenWidth - Width) / 2;
            Top = SystemParameters.PrimaryScreenHeight - Height;
            _root.Margin = new Thickness(0, 0, 0, c.BottomMargin);
            _row.Margin = new Thickness(0, 0, 0, PadY);
            _glass.Height = _size + 2 * PadY;
            _baseLeft = (Width - _baseTotal) / 2;
        }

        ImageSource GetIcon(string path)
        {
            if (_icons.TryGetValue(path, out var s)) return s;
            s = Native.GetIcon(path, 128);
            _icons[path] = s;
            return s;
        }

        string Signature()
        {
            return string.Join("|", _items.Where(i => !i.Special).Select(i => i.Path + (i.Windows.Count > 0 ? "*" : "")));
        }

        // ---------- запущенные приложения ----------
        void AssignWindows(List<OpenWindow> running)
        {
            foreach (var w in running)
            {
                var it = _items.FirstOrDefault(i => i.Path.Equals(w.Exe, StringComparison.OrdinalIgnoreCase));
                if (it != null) it.Windows.Add(w.Hwnd);
            }
        }

        async void PollRunning()
        {
            if (_polling || !IsVisible) return;
            _polling = true;
            try
            {
                var list = await Task.Run(() => Native.GetOpenWindows());
                var c = ConfigStore.Current.Dock;
                var saved = _items.ToList();
                var tmp = new List<DockItem>();
                foreach (var a in c.Apps)
                    if (!string.IsNullOrWhiteSpace(Exp(a.Path)) && File.Exists(Exp(a.Path)))
                        tmp.Add(new DockItem { Path = Exp(a.Path), Pinned = true });
                if (c.ShowRunning)
                    foreach (var w in list)
                    {
                        string n = System.IO.Path.GetFileName(w.Exe);
                        if (n.Equals("ApplicationFrameHost.exe", StringComparison.OrdinalIgnoreCase) ||
                            n.Equals("TextInputHost.exe", StringComparison.OrdinalIgnoreCase)) continue;
                        if (!tmp.Any(i => i.Path.Equals(w.Exe, StringComparison.OrdinalIgnoreCase)))
                            tmp.Add(new DockItem { Path = w.Exe });
                    }
                foreach (var w in list)
                {
                    var it = tmp.FirstOrDefault(i => i.Path.Equals(w.Exe, StringComparison.OrdinalIgnoreCase));
                    if (it != null) it.Windows.Add(w.Hwnd);
                }
                string sig = string.Join("|", tmp.Select(i => i.Path + (i.Windows.Count > 0 ? "*" : "")));

                if (sig != _sig && _hover == null) Rebuild();
                else
                    foreach (var it in _items)
                    {
                        var t = tmp.FirstOrDefault(x => x.Path.Equals(it.Path, StringComparison.OrdinalIgnoreCase));
                        it.Windows = t != null ? t.Windows : new List<IntPtr>();
                        it.Dot.Visibility = it.Windows.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
                    }
            }
            catch { }
            finally { _polling = false; }
        }

        public void SetSuppressed(bool hide)
        {
            Visibility = hide ? Visibility.Hidden : Visibility.Visible;
        }

        void UpdateTopmost() { Native.KeepTopmost(this); }

        // ---------- действия ----------
        void Activate(DockItem it)
        {
            if (it.Windows.Count > 0)
            {
                it.Cycle = (it.Cycle + 1) % it.Windows.Count;
                Native.ActivateWindow(it.Windows[it.Cycle]);
                return;
            }
            Launch(it);
        }

        void Launch(DockItem it)
        {
            try
            {
                if (it.Special) Process.Start(new ProcessStartInfo("explorer.exe", "shell:RecycleBinFolder") { UseShellExecute = true });
                else Process.Start(new ProcessStartInfo(it.Path) { UseShellExecute = true });
                var a = new DoubleAnimation(0, -_size * 0.45, TimeSpan.FromMilliseconds(260))
                {
                    AutoReverse = true, RepeatBehavior = new RepeatBehavior(3),
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
                };
                it.Bounce.BeginAnimation(TranslateTransform.YProperty, a);
            }
            catch { }
        }

        void ShowMenu(DockItem it, FrameworkElement anchor)
        {
            var m = new ContextMenu { PlacementTarget = anchor, Placement = System.Windows.Controls.Primitives.PlacementMode.Top };
            var title = new MenuItem { Header = it.Name, IsEnabled = false, FontWeight = FontWeights.SemiBold };
            m.Items.Add(title);
            m.Items.Add(new Separator());

            var open = new MenuItem { Header = it.Windows.Count > 0 ? "Запустить ещё одно окно" : "Открыть" };
            open.Click += (s, e) => Launch(it);
            m.Items.Add(open);
            if (it.Special) { m.IsOpen = true; return; }

            var pin = new MenuItem { Header = it.Pinned ? "Открепить от дока" : "Закрепить в доке" };
            pin.Click += (s, e) => TogglePin(it);
            m.Items.Add(pin);

            m.IsOpen = true;
        }

        void TogglePin(DockItem it)
        {
            var apps = ConfigStore.Current.Dock.Apps;
            if (it.Pinned) apps.RemoveAll(a => string.Equals(Exp(a.Path), it.Path, StringComparison.OrdinalIgnoreCase));
            else apps.Add(new AppEntry { Name = it.Name, Path = it.Path });
            ConfigStore.Save();
            _sig = "";
            Rebuild();
        }

        // ---------- увеличение ----------
        void OnMove(object sender, MouseEventArgs e)
        {
            if (_items.Count == 0) return;
            var c = ConfigStore.Current.Dock;
            double mx = e.GetPosition(this).X - _baseLeft;
            double sigma = _size * 1.35, maxS = Math.Max(1, c.MaxScale);

            DockItem nearest = null; double best = double.MaxValue;
            foreach (var it in _items)
            {
                double d = Math.Abs(mx - it.BaseX);
                it.Target = 1 + (maxS - 1) * Math.Exp(-(d * d) / (2 * sigma * sigma));
                if (d < best) { best = d; nearest = it; }
            }
            SetHover(best < _size * 0.75 ? nearest : null, mx);
            StartAnim();
        }

        void SetHover(DockItem it, double mx)
        {
            if (it == null)
            {
                if (_hover != null || _tip.Opacity > 0)
                {
                    foreach (var i in _items) i.Target = 1;
                    _tip.BeginAnimation(OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(100)));
                    _hover = null;
                    StartAnim();
                }
                return;
            }
            if (_hover != it)
            {
                _hover = it;
                _tipText.Text = it.Name;
                _tip.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(120)));
            }
            PositionTip(it);
        }

        void PositionTip(DockItem it)
        {
            _tip.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            double w = _tip.DesiredSize.Width;
            double cx = _baseLeft + it.BaseX;
            double iconTop = _size * Math.Max(1, it.Scale) + PadY + 10;
            _tip.Margin = new Thickness(Math.Max(0, Math.Min(Width - w, cx - w / 2)), 0, 0, iconTop);
        }

        void StartAnim()
        {
            if (_animating) return;
            _animating = true;
            CompositionTarget.Rendering += OnFrame;
        }

        void OnFrame(object s, EventArgs e)
        {
            bool moving = false;
            foreach (var it in _items)
            {
                double d = it.Target - it.Scale;
                if (Math.Abs(d) < 0.003) { it.Scale = it.Target; continue; }
                it.Scale += d * 0.3;
                moving = true;
            }
            ApplyScales();
            if (_hover != null) PositionTip(_hover);
            if (!moving) { CompositionTarget.Rendering -= OnFrame; _animating = false; }
        }

        void ApplyScales()
        {
            foreach (var it in _items)
            {
                double sz = _size * it.Scale;
                it.Cell.Width = sz; it.Cell.Height = sz;
            }
            _row.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            _glass.Width = _row.DesiredSize.Width + 2 * PadX;
        }
    }
}
