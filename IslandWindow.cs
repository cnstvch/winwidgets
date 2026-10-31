using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace WinWidgets
{
    /// <summary>«Челка» сверху по центру: время → мини-плеер → развёрнутый плеер.</summary>
    public class IslandWindow : Window
    {
        enum Mode { Idle, Compact, Expanded }

        const double Flare = 22;                       // вогнутые «плечики» у края экрана
        const string IcoPrev = "M6,5 L8,5 L8,19 L6,19 Z M20,5 L20,19 L9,12 Z";
        const string IcoNext = "M16,5 L18,5 L18,19 L16,19 Z M4,5 L4,19 L15,12 Z";
        const string IcoPlay = "M7,4 L20,12 L7,20 Z";
        const string IcoPause = "M6,4 L10,4 L10,20 L6,20 Z M14,4 L18,4 L18,20 L14,20 Z";

        readonly Grid _shell = new Grid { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top };
        readonly Border _body = new Border { Background = Brushes.Black };
        readonly Border _glow = new Border { IsHitTestVisible = false };
        readonly Grid _idleView = new Grid();
        readonly Grid _compactView = new Grid { Opacity = 0, IsHitTestVisible = false };
        readonly Grid _fullView = new Grid { Opacity = 0, IsHitTestVisible = false };

        readonly TextBlock _idleText = new TextBlock();
        readonly Border _artC = new Border { Width = 24, Height = 24, CornerRadius = new CornerRadius(7) };
        readonly Border _artF = new Border { Width = 54, Height = 54, CornerRadius = new CornerRadius(13) };
        readonly TextBlock _title = new TextBlock();
        readonly TextBlock _artist = new TextBlock();
        readonly Border[] _barsC = new Border[4];
        readonly Border[] _barsF = new Border[5];
        readonly Border _playBtn = new Border { Width = 38, Height = 38, CornerRadius = new CornerRadius(19), Background = Brushes.White };
        readonly Path _playIcon = new Path();
        readonly Border _track = new Border { Height = 3, CornerRadius = new CornerRadius(1.5), Background = Theme.Solid(Color.FromArgb(40, 255, 255, 255)) };
        readonly Border _fill = new Border { Height = 3, CornerRadius = new CornerRadius(1.5), HorizontalAlignment = HorizontalAlignment.Left, Background = Brushes.White };

        readonly DispatcherTimer _clockTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
        readonly DispatcherTimer _progTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        readonly DispatcherTimer _evalTimer = new DispatcherTimer();

        Mode _mode = Mode.Idle;
        bool _hover;
        DateTime _expandUntil = DateTime.MinValue;
        string _lastTrack = "";
        bool _barsRunning;

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
            UseLayoutRounding = true;
            TextOptions.SetTextFormattingMode(this, TextFormattingMode.Ideal);

            BuildUi();
            ApplyMediaVisuals();
            ApplyConfig();

            SourceInitialized += (s, e) => Native.MakeToolWindow(this, true);
            ConfigStore.Changed += ApplyConfig;
            MediaService.Changed += OnMedia;

            _clockTimer.Tick += (s, e) => UpdateClock();
            _clockTimer.Start();
            _progTimer.Tick += (s, e) => UpdateProgress();
            _evalTimer.Tick += (s, e) => { _evalTimer.Stop(); Evaluate(); };

            _body.MouseEnter += (s, e) => { _hover = true; Evaluate(); };
            _body.MouseLeave += (s, e) =>
            {
                _hover = false;
                _evalTimer.Stop(); _evalTimer.Interval = TimeSpan.FromMilliseconds(450); _evalTimer.Start();
            };

            UpdateClock();
            Evaluate(true);
        }

        // ---------- конфиг / позиция ----------
        void ApplyConfig()
        {
            var c = ConfigStore.Current.Island;
            Width = c.Width + 2 * Flare;
            Height = c.Height + 8;
            Left = (SystemParameters.PrimaryScreenWidth - Width) / 2;
            Top = 0;
            Evaluate(true);
        }

        public void SetSuppressed(bool hide)
        {
            Visibility = hide ? Visibility.Hidden : Visibility.Visible;
        }

        // ---------- UI ----------
        void BuildUi()
        {
            var white = Brushes.White;
            var dim = Theme.Solid(Color.FromArgb(150, 255, 255, 255));

            // плечики
            var left = new Path
            {
                Data = Geometry.Parse("M0,0 L22,0 L22,22 A22,22 0 0 0 0,0 Z"),
                Fill = Brushes.Black, Width = Flare, Height = Flare,
                HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
                IsHitTestVisible = false,
            };
            var right = new Path
            {
                Data = Geometry.Parse("M22,0 L0,0 L0,22 A22,22 0 0 1 22,0 Z"),
                Fill = Brushes.Black, Width = Flare, Height = Flare,
                HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top,
                IsHitTestVisible = false,
            };
            _body.Margin = new Thickness(Flare, 0, Flare, 0);
            _body.CornerRadius = new CornerRadius(0, 0, 17, 17);
            _body.SizeChanged += (s, e) =>
            {
                double r = Math.Min(34, _body.ActualHeight / 2);
                _body.CornerRadius = new CornerRadius(0, 0, r, r);
                _glow.CornerRadius = _body.CornerRadius;
            };

            _shell.Children.Add(_body);
            _shell.Children.Add(left);
            _shell.Children.Add(right);

            var inner = new Grid();
            _body.Child = inner;
            inner.Children.Add(_glow);
            inner.Children.Add(_idleView);
            inner.Children.Add(_compactView);
            inner.Children.Add(_fullView);

            // --- idle: время ---
            _idleText.Foreground = Theme.Solid(Color.FromArgb(190, 255, 255, 255));
            _idleText.FontFamily = Theme.UiFont;
            _idleText.FontSize = 13; _idleText.FontWeight = FontWeights.SemiBold;
            _idleText.HorizontalAlignment = HorizontalAlignment.Center;
            _idleText.VerticalAlignment = VerticalAlignment.Center;
            _idleView.Children.Add(_idleText);

            // --- compact: мини-обложка + эквалайзер ---
            _compactView.Margin = new Thickness(12, 0, 16, 0);
            _artC.HorizontalAlignment = HorizontalAlignment.Left;
            _artC.VerticalAlignment = VerticalAlignment.Center;
            var eqC = MakeBars(_barsC, 14);
            eqC.HorizontalAlignment = HorizontalAlignment.Right;
            eqC.VerticalAlignment = VerticalAlignment.Center;
            _compactView.Children.Add(_artC);
            _compactView.Children.Add(eqC);

            // --- expanded ---
            _fullView.Margin = new Thickness(16, 0, 22, 12);
            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.VerticalAlignment = VerticalAlignment.Center;

            Grid.SetColumn(_artF, 0);
            _artF.Margin = new Thickness(0, 0, 14, 0);

            _title.Foreground = white; _title.FontFamily = Theme.UiFont;
            _title.FontSize = 15; _title.FontWeight = FontWeights.SemiBold;
            _title.TextTrimming = TextTrimming.CharacterEllipsis;
            _artist.Foreground = dim; _artist.FontFamily = Theme.UiFont;
            _artist.FontSize = 12.5; _artist.Margin = new Thickness(0, 2, 0, 0);
            _artist.TextTrimming = TextTrimming.CharacterEllipsis;
            var meta = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
            meta.Children.Add(_title); meta.Children.Add(_artist);
            Grid.SetColumn(meta, 1);

            var eqF = MakeBars(_barsF, 22);
            eqF.VerticalAlignment = VerticalAlignment.Center;
            eqF.Margin = new Thickness(0, 0, 18, 0);
            Grid.SetColumn(eqF, 2);

            var ctl = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            ctl.Children.Add(Btn(IcoPrev, 20, MediaService.Prev));
            _playIcon.Fill = Theme.Solid(Color.FromRgb(0x10, 0x12, 0x1C));
            _playIcon.Stretch = Stretch.Uniform; _playIcon.Width = 16; _playIcon.Height = 16;
            _playBtn.Child = _playIcon;
            _playBtn.Margin = new Thickness(8, 0, 8, 0);
            _playBtn.Cursor = Cursors.Hand;
            _playBtn.MouseLeftButtonUp += (s, e) => MediaService.Toggle();
            ctl.Children.Add(_playBtn);
            ctl.Children.Add(Btn(IcoNext, 20, MediaService.Next));
            Grid.SetColumn(ctl, 3);

            row.Children.Add(_artF); row.Children.Add(meta); row.Children.Add(eqF); row.Children.Add(ctl);
            _fullView.Children.Add(row);

            _track.VerticalAlignment = VerticalAlignment.Bottom;
            _track.Margin = new Thickness(68, 0, 4, -4);
            _track.Child = _fill;
            _fullView.Children.Add(_track);

            Content = _shell;
        }

        Border Btn(string geo, double size, Action click)
        {
            var p = new Path
            {
                Data = Geometry.Parse(geo), Fill = Brushes.White, Stretch = Stretch.Uniform,
                Width = size, Height = size, Opacity = 0.9, IsHitTestVisible = false
            };
            var b = new Border { Background = Brushes.Transparent, Padding = new Thickness(6), Child = p, Cursor = Cursors.Hand };
            b.MouseLeftButtonUp += (s, e) => click();
            b.MouseEnter += (s, e) => p.Opacity = 1;
            b.MouseLeave += (s, e) => p.Opacity = 0.9;
            return b;
        }

        StackPanel MakeBars(Border[] bars, double h)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal, Height = h };
            for (int i = 0; i < bars.Length; i++)
            {
                bars[i] = new Border
                {
                    Width = 3.2, Height = h, CornerRadius = new CornerRadius(1.6),
                    Margin = new Thickness(i == 0 ? 0 : 3, 0, 0, 0),
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

            Brush art;
            if (i.Thumb != null) art = new ImageBrush(i.Thumb) { Stretch = Stretch.UniformToFill };
            else
            {
                var g = new LinearGradientBrush(Color.FromRgb(0xFF, 0x7A, 0xA8), Color.FromRgb(0x7A, 0x5C, 0xFF), 45);
                art = g;
            }
            _artC.Background = art; _artF.Background = art;

            var accent = i.Accent;
            var bar = Theme.Solid(accent);
            foreach (var b in _barsC) b.Background = bar;
            foreach (var b in _barsF) b.Background = bar;
            _fill.Background = bar;

            var glow = new RadialGradientBrush
            {
                Center = new Point(0.1, 0.3), GradientOrigin = new Point(0.1, 0.3), RadiusX = 0.55, RadiusY = 1.0
            };
            glow.GradientStops.Add(new GradientStop(Theme.A(accent, 0.38), 0));
            glow.GradientStops.Add(new GradientStop(Theme.A(accent, 0), 1));
            _glow.Background = glow;

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
                case Mode.Compact: w = 250; h = 40; break;
                default: w = 168; h = 34; break;
            }
            double shellW = w + 2 * Flare;

            if (instant || !IsLoaded)
            {
                _shell.BeginAnimation(WidthProperty, null); _shell.BeginAnimation(HeightProperty, null);
                _shell.Width = shellW; _shell.Height = h;
            }
            else if (m != _mode)
            {
                IEasingFunction ease = m == Mode.Expanded
                    ? (IEasingFunction)new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.35 }
                    : new CubicEase { EasingMode = EasingMode.EaseOut };
                var dur = TimeSpan.FromMilliseconds(m == Mode.Expanded ? 420 : 280);
                _shell.BeginAnimation(WidthProperty, new DoubleAnimation(shellW, dur) { EasingFunction = ease });
                _shell.BeginAnimation(HeightProperty, new DoubleAnimation(h, dur) { EasingFunction = ease });
            }

            Fade(_idleView, m == Mode.Idle);
            Fade(_compactView, m == Mode.Compact);
            Fade(_fullView, m == Mode.Expanded);

            if (m == Mode.Expanded) { UpdateProgress(); _progTimer.Start(); }
            else _progTimer.Stop();

            _mode = m;
        }

        static void Fade(UIElement e, bool on)
        {
            e.IsHitTestVisible = on;
            e.BeginAnimation(OpacityProperty, new DoubleAnimation(on ? 1 : 0, TimeSpan.FromMilliseconds(on ? 220 : 90)));
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
        void UpdateClock()
        {
            _idleText.Text = DateTime.Now.ToString("HH:mm");
        }

        void UpdateProgress()
        {
            var i = MediaService.Info;
            double tw = _track.ActualWidth;
            if (tw <= 0) return;
            double f = i.Duration > 0 ? i.Position / i.Duration : 0;
            _fill.Width = Math.Max(0, Math.Min(1, f)) * tw;
        }
    }
}
