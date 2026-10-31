using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace WinWidgets
{
    /// <summary>
    /// Челка сверху по центру. Вся форма — один чёрный Path (без швов у «плечиков»),
    /// размеры двигаются пружиной. Простой → мини-плеер → развёрнутый плеер.
    /// </summary>
    public class IslandWindow : Window
    {
        enum Mode { Idle, Compact, Expanded }

        const double Ear = 14;          // вогнутые «плечики» у верхнего края
        const double BottomR = 22;      // скругление нижних углов
        const string IcoPrev = "M6,5 L8,5 L8,19 L6,19 Z M20,5 L20,19 L9,12 Z";
        const string IcoNext = "M16,5 L18,5 L18,19 L16,19 Z M4,5 L4,19 L15,12 Z";
        const string IcoPlay = "M7,4 L20,12 L7,20 Z";
        const string IcoPause = "M6,4 L10,4 L10,20 L6,20 Z M14,4 L18,4 L18,20 L14,20 Z";

        readonly Grid _root = new Grid();
        readonly Path _shape = new Path { Fill = Brushes.Black, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top };
        readonly Grid _content = new Grid { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top, ClipToBounds = true };
        readonly Grid _idleView = new Grid();
        readonly Grid _compactView = new Grid { Opacity = 0, IsHitTestVisible = false, Width = 250, Height = 34, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top };
        readonly Grid _fullView = new Grid { Opacity = 0, IsHitTestVisible = false, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top };

        readonly TextBlock _idleText = new TextBlock();
        readonly Border _artC = new Border { Width = 22, Height = 22, CornerRadius = new CornerRadius(6) };
        readonly Border _artF = new Border { Width = 52, Height = 52, CornerRadius = new CornerRadius(11) };
        readonly TextBlock _title = new TextBlock();
        readonly TextBlock _artist = new TextBlock();
        readonly TextBlock _tLeft = new TextBlock();
        readonly TextBlock _tRight = new TextBlock();
        readonly Border[] _barsC = new Border[4];
        readonly Border[] _barsF = new Border[5];
        readonly Path _playIcon = new Path();
        readonly Border _track = new Border { Height = 4, CornerRadius = new CornerRadius(2), Background = Theme.Solid(Color.FromArgb(70, 255, 255, 255)) };
        readonly Border _fill = new Border { Height = 4, CornerRadius = new CornerRadius(2), HorizontalAlignment = HorizontalAlignment.Left, Background = Brushes.White };

        readonly SpringGroup _springs;
        readonly Spring _w, _h;
        readonly DispatcherTimer _clockTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
        readonly DispatcherTimer _progTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        readonly DispatcherTimer _evalTimer = new DispatcherTimer();

        Mode _mode = Mode.Idle;
        bool _hover, _barsRunning, _modeSet;
        DateTime _expandUntil = DateTime.MinValue;
        string _lastTrack = "";

        public IslandWindow()
        {
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ShowInTaskbar = false;
            ShowActivated = false;
            Topmost = true;
            ResizeMode = ResizeMode.NoResize;
            Focusable = false;
            TextOptions.SetTextFormattingMode(this, TextFormattingMode.Ideal);

            _springs = new SpringGroup(Layout);
            _w = _springs.Add(168, 300, 22);
            _h = _springs.Add(32, 300, 22);

            BuildUi();
            Content = _root;
            ApplyMediaVisuals();
            ApplyConfig();

            SourceInitialized += (s, e) => Native.MakeToolWindow(this, true);
            ConfigStore.Changed += ApplyConfig;
            MediaService.Changed += OnMedia;

            _clockTimer.Tick += (s, e) => UpdateClock();
            _clockTimer.Start();
            _progTimer.Tick += (s, e) => UpdateProgress();
            _evalTimer.Tick += (s, e) => { _evalTimer.Stop(); Evaluate(); };

            MouseEnter += (s, e) => { _hover = true; _evalTimer.Stop(); Evaluate(); };
            MouseLeave += (s, e) =>
            {
                _hover = false;
                _evalTimer.Stop(); _evalTimer.Interval = TimeSpan.FromMilliseconds(380); _evalTimer.Start();
            };

            UpdateClock();
            Evaluate(true);
        }

        // ---------- конфиг ----------
        void ApplyConfig()
        {
            var c = ConfigStore.Current.Island;
            Width = c.Width + 2 * Ear + 60;
            Height = c.Height + 40;
            Left = (SystemParameters.PrimaryScreenWidth - Width) / 2;
            Top = 0;
            _fullView.Width = c.Width; _fullView.Height = c.Height;
            _idleText.Visibility = c.IdleClock ? Visibility.Visible : Visibility.Collapsed;
            Evaluate(true);
        }

        public void SetSuppressed(bool hide)
        {
            Visibility = hide ? Visibility.Hidden : Visibility.Visible;
        }

        // ---------- форма ----------
        static Geometry Build(double w, double h)
        {
            double e = Ear;
            double r = Math.Max(1, Math.Min(BottomR, Math.Min(h / 2, w / 2)));
            double W = w + 2 * e;
            var g = new StreamGeometry();
            using (var c = g.Open())
            {
                c.BeginFigure(new Point(0, 0), true, true);
                c.LineTo(new Point(W, 0), true, false);
                c.ArcTo(new Point(W - e, e), new Size(e, e), 0, false, SweepDirection.Counterclockwise, true, false);
                c.LineTo(new Point(W - e, h - r), true, false);
                c.ArcTo(new Point(W - e - r, h), new Size(r, r), 0, false, SweepDirection.Clockwise, true, false);
                c.LineTo(new Point(e + r, h), true, false);
                c.ArcTo(new Point(e, h - r), new Size(r, r), 0, false, SweepDirection.Clockwise, true, false);
                c.LineTo(new Point(e, e), true, false);
                c.ArcTo(new Point(0, 0), new Size(e, e), 0, false, SweepDirection.Counterclockwise, true, false);
            }
            g.Freeze();
            return g;
        }

        void Layout()
        {
            double w = Math.Max(60, _w.Value), h = Math.Max(24, _h.Value);
            _shape.Width = w + 2 * Ear; _shape.Height = h;
            _shape.Data = Build(w, h);
            _content.Width = w; _content.Height = h;
            _content.Clip = new RectangleGeometry(new Rect(0, 0, w, h), Math.Min(BottomR, h / 2), Math.Min(BottomR, h / 2));
        }

        // ---------- UI ----------
        void BuildUi()
        {
            _root.Children.Add(_shape);
            _root.Children.Add(_content);
            _content.Children.Add(_idleView);
            _content.Children.Add(_compactView);
            _content.Children.Add(_fullView);

            var white = Brushes.White;
            var dim = Theme.Solid(Color.FromArgb(150, 255, 255, 255));

            // idle
            _idleText.Foreground = Theme.Solid(Color.FromArgb(190, 255, 255, 255));
            _idleText.FontFamily = Theme.UiFont; _idleText.FontSize = 13; _idleText.FontWeight = FontWeights.SemiBold;
            _idleText.HorizontalAlignment = HorizontalAlignment.Center;
            _idleText.VerticalAlignment = VerticalAlignment.Center;
            _idleView.Children.Add(_idleText);

            // compact: обложка слева, эквалайзер справа
            _artC.HorizontalAlignment = HorizontalAlignment.Left; _artC.VerticalAlignment = VerticalAlignment.Center;
            _artC.Margin = new Thickness(Ear, 0, 0, 0);
            var eqC = MakeBars(_barsC, 14);
            eqC.HorizontalAlignment = HorizontalAlignment.Right; eqC.VerticalAlignment = VerticalAlignment.Center;
            eqC.Margin = new Thickness(0, 0, Ear + 4, 0);
            _compactView.Children.Add(_artC); _compactView.Children.Add(eqC);

            // expanded
            var col = new Grid { Margin = new Thickness(22, 12, 22, 10) };
            col.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            col.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            col.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            var top = new Grid();
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            _artF.Margin = new Thickness(0, 0, 14, 0);
            _title.Foreground = white; _title.FontFamily = Theme.UiFont; _title.FontSize = 15; _title.FontWeight = FontWeights.SemiBold;
            _title.TextTrimming = TextTrimming.CharacterEllipsis;
            _artist.Foreground = dim; _artist.FontFamily = Theme.UiFont; _artist.FontSize = 12.5;
            _artist.Margin = new Thickness(0, 2, 0, 0); _artist.TextTrimming = TextTrimming.CharacterEllipsis;
            var meta = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            meta.Children.Add(_title); meta.Children.Add(_artist);
            Grid.SetColumn(meta, 1);
            var eqF = MakeBars(_barsF, 20);
            eqF.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(eqF, 2);
            top.Children.Add(_artF); top.Children.Add(meta); top.Children.Add(eqF);
            col.Children.Add(top);

            var prog = new Grid { Margin = new Thickness(0, 10, 0, 0) };
            prog.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            prog.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            prog.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            foreach (var t in new[] { _tLeft, _tRight })
            {
                t.Foreground = dim; t.FontFamily = Theme.UiFont; t.FontSize = 11; t.VerticalAlignment = VerticalAlignment.Center;
            }
            _tLeft.Margin = new Thickness(0, 0, 9, 0); _tRight.Margin = new Thickness(9, 0, 0, 0);
            _track.VerticalAlignment = VerticalAlignment.Center;
            _track.Child = _fill;
            Grid.SetColumn(_track, 1); Grid.SetColumn(_tRight, 2);
            prog.Children.Add(_tLeft); prog.Children.Add(_track); prog.Children.Add(_tRight);
            Grid.SetRow(prog, 1);
            col.Children.Add(prog);

            var ctl = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            ctl.Children.Add(Btn(IcoPrev, 20, MediaService.Prev, null));
            ctl.Children.Add(Btn(null, 26, MediaService.Toggle, _playIcon));
            ctl.Children.Add(Btn(IcoNext, 20, MediaService.Next, null));
            Grid.SetRow(ctl, 2);
            col.Children.Add(ctl);

            _fullView.Children.Add(col);
        }

        Border Btn(string geo, double size, Action click, Path reuse)
        {
            var p = reuse ?? new Path();
            if (geo != null) p.Data = Geometry.Parse(geo);
            p.Fill = Brushes.White; p.Stretch = Stretch.Uniform; p.Width = size; p.Height = size; p.IsHitTestVisible = false;
            var b = new Border { Background = Brushes.Transparent, Padding = new Thickness(14, 4, 14, 4), Child = p, Cursor = Cursors.Hand };
            b.MouseLeftButtonUp += (s, e) => click();
            b.MouseLeftButtonDown += (s, e) => p.Opacity = 0.6;
            b.MouseLeave += (s, e) => p.Opacity = 1;
            b.MouseLeftButtonUp += (s, e) => p.Opacity = 1;
            return b;
        }

        StackPanel MakeBars(Border[] bars, double h)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal, Height = h };
            for (int i = 0; i < bars.Length; i++)
            {
                bars[i] = new Border
                {
                    Width = 3, Height = h, CornerRadius = new CornerRadius(1.5),
                    Margin = new Thickness(i == 0 ? 0 : 2.5, 0, 0, 0),
                    VerticalAlignment = VerticalAlignment.Bottom,
                    RenderTransformOrigin = new Point(0.5, 1),
                    RenderTransform = new ScaleTransform(1, 0.25),
                };
                sp.Children.Add(bars[i]);
            }
            return sp;
        }

        // ---------- состояния ----------
        void OnMedia()
        {
            var i = MediaService.Info;
            string track = i.Title + "|" + i.Artist;
            if (i.HasSession && i.Playing && track != _lastTrack && i.Title.Length > 0)
            {
                _expandUntil = DateTime.UtcNow.AddSeconds(4);
                _evalTimer.Stop(); _evalTimer.Interval = TimeSpan.FromMilliseconds(4200); _evalTimer.Start();
            }
            _lastTrack = track;
            ApplyMediaVisuals();
            Evaluate();
        }

        void ApplyMediaVisuals()
        {
            var i = MediaService.Info;
            _title.Text = string.IsNullOrEmpty(i.Title) ? "Ничего не играет" : i.Title;
            _artist.Text = i.Artist;
            _artist.Visibility = string.IsNullOrEmpty(i.Artist) ? Visibility.Collapsed : Visibility.Visible;

            Brush art = i.Thumb != null
                ? (Brush)new ImageBrush(i.Thumb) { Stretch = Stretch.UniformToFill }
                : new LinearGradientBrush(Color.FromRgb(0x55, 0x58, 0x6B), Color.FromRgb(0x2B, 0x2D, 0x3A), 45);
            _artC.Background = art; _artF.Background = art;

            var bar = Theme.Solid(i.Accent);
            foreach (var b in _barsC) b.Background = bar;
            foreach (var b in _barsF) b.Background = bar;

            _playIcon.Data = Geometry.Parse(i.Playing ? IcoPause : IcoPlay);
            SetBars(i.HasSession && i.Playing);
        }

        void Evaluate(bool instant = false)
        {
            var c = ConfigStore.Current.Island;
            var i = MediaService.Info;
            bool playing = i.HasSession && i.Playing;
            Mode m;
            if (_hover && c.ExpandOnHover && i.HasSession) m = Mode.Expanded;
            else if (DateTime.UtcNow < _expandUntil && i.HasSession) m = Mode.Expanded;
            else if (playing) m = Mode.Compact;
            else m = Mode.Idle;
            SetMode(m, instant);
        }

        void SetMode(Mode m, bool instant)
        {
            var c = ConfigStore.Current.Island;
            double w, h;
            switch (m)
            {
                case Mode.Expanded: w = c.Width; h = c.Height; break;
                case Mode.Compact: w = 250; h = 34; break;
                default: w = c.IdleClock ? 190 : 168; h = 32; break;
            }

            bool expanding = m == Mode.Expanded;
            _w.Stiffness = _h.Stiffness = expanding ? 260 : 380;
            _w.Damping = _h.Damping = expanding ? 17 : 29;     // раскрытие — с лёгким «перелётом», схлопывание — без
            _w.Target = w; _h.Target = h;

            if (instant || !_modeSet)
            {
                _w.Value = w; _h.Value = h; _w.Velocity = _h.Velocity = 0;
                Layout();
            }
            else _springs.Kick();
            _modeSet = true;

            Fade(_idleView, m == Mode.Idle, 0);
            Fade(_compactView, m == Mode.Compact, 0);
            Fade(_fullView, m == Mode.Expanded, expanding ? 90 : 0);

            if (m == Mode.Expanded) { UpdateProgress(); _progTimer.Start(); }
            else _progTimer.Stop();
            _mode = m;
        }

        static void Fade(UIElement e, bool on, int delayMs)
        {
            e.IsHitTestVisible = on;
            var a = new DoubleAnimation(on ? 1 : 0, TimeSpan.FromMilliseconds(on ? 240 : 100));
            if (on) a.BeginTime = TimeSpan.FromMilliseconds(delayMs);
            e.BeginAnimation(OpacityProperty, a);
        }

        void SetBars(bool run)
        {
            if (run == _barsRunning) return;
            _barsRunning = run;
            Animate(_barsC, run);
            Animate(_barsF, run);
        }

        static void Animate(Border[] bars, bool run)
        {
            var rnd = new Random(7);
            for (int i = 0; i < bars.Length; i++)
            {
                var st = (ScaleTransform)bars[i].RenderTransform;
                if (!run) { st.BeginAnimation(ScaleTransform.ScaleYProperty, null); st.ScaleY = 0.25; continue; }
                var a = new DoubleAnimation(0.25, 1.0, TimeSpan.FromMilliseconds(380 + rnd.Next(0, 260)))
                {
                    AutoReverse = true,
                    RepeatBehavior = RepeatBehavior.Forever,
                    EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
                    BeginTime = TimeSpan.FromMilliseconds(rnd.Next(0, 300)),
                };
                st.BeginAnimation(ScaleTransform.ScaleYProperty, a);
            }
        }

        // ---------- часы / прогресс ----------
        void UpdateClock() { _idleText.Text = DateTime.Now.ToString("HH:mm"); }

        static string Fmt(double s)
        {
            if (s < 0 || double.IsNaN(s)) s = 0;
            int t = (int)s;
            return (t / 60) + ":" + (t % 60).ToString("00");
        }

        void UpdateProgress()
        {
            var i = MediaService.Info;
            double tw = _track.ActualWidth;
            double f = i.Duration > 0 ? i.Position / i.Duration : 0;
            if (tw > 0) _fill.Width = Math.Max(0, Math.Min(1, f)) * tw;
            _tLeft.Text = Fmt(i.Position);
            _tRight.Text = i.Duration > 0 ? "-" + Fmt(i.Duration - i.Position) : "";
        }
    }
}
