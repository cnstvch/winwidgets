using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using Windows.Devices.Radios;

namespace WinWidgets
{
    /// <summary>Ползунок как в Control Center macOS: светлый трек, иконка внутри, круглый бегунок.</summary>
    public class BarSlider : Border
    {
        readonly Border _fill = new Border { HorizontalAlignment = HorizontalAlignment.Left, CornerRadius = new CornerRadius(14), Background = Brushes.White };
        readonly Border _thumb = new Border
        {
            Width = 28, Height = 28, CornerRadius = new CornerRadius(14), Background = Brushes.White,
            HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center,
            BorderBrush = new SolidColorBrush(Color.FromArgb(40, 0, 0, 0)), BorderThickness = new Thickness(1),
        };
        double _v;
        public event Action<double> Changed;

        public BarSlider(string icon)
        {
            Height = 28;
            CornerRadius = new CornerRadius(14);
            Background = Ui.TrackBrush;
            Cursor = Cursors.Hand;
            var g = new Grid();
            g.Children.Add(_fill);
            if (icon != null)
            {
                var ic = new System.Windows.Shapes.Path
                {
                    Data = Geometry.Parse(icon), Stroke = Theme.Solid(Theme.A(Ui.Fg, 0.45)), StrokeThickness = 1.6,
                    StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round,
                    Stretch = Stretch.Uniform, Width = 15, Height = 15, IsHitTestVisible = false,
                    HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(7, 0, 0, 0),
                };
                g.Children.Add(ic);
            }
            g.Children.Add(_thumb);
            Child = g;
            SizeChanged += (s, e) => Relayout();
        }

        public double Value
        {
            get { return _v; }
            set { _v = Math.Max(0, Math.Min(1, value)); Relayout(); }
        }

        void Relayout()
        {
            double room = Math.Max(0, ActualWidth - 28);
            _thumb.Margin = new Thickness(_v * room, 0, 0, 0);
            _fill.Width = 28 + _v * room;
        }

        void SetFrom(MouseEventArgs e)
        {
            double x = e.GetPosition(this).X;
            _v = Math.Max(0, Math.Min(1, (x - 14) / Math.Max(1, ActualWidth - 28)));
            Relayout();
            Changed?.Invoke(_v);
        }

        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            CaptureMouse(); SetFrom(e); base.OnMouseLeftButtonDown(e);
        }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (IsMouseCaptured) SetFrom(e); base.OnMouseMove(e);
        }
        protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
        {
            ReleaseMouseCapture(); base.OnMouseLeftButtonUp(e);
        }
    }

    /// <summary>Панель управления: Ctrl+Alt+C или клик по иконке в трее.</summary>
    public class ControlCenterWindow : Window
    {
        // 24x24 штриховые иконки
        const string IcoWifi = "M2,9 A15,15 0 0 1 22,9 M5,12.5 A10.5,10.5 0 0 1 19,12.5 M8.5,16 A5.5,5.5 0 0 1 15.5,16 M12,19.5 L12,19.6";
        const string IcoBt = "M7,7 L17,17 L12,21 L12,3 L17,7 L7,17";
        const string IcoShare = "M12,3 L12,15 M8,7 L12,3 L16,7 M5,13 L5,19 L19,19 L19,13";
        const string IcoMoon = "M20,14.5 A8,8 0 0 1 9.5,4 A8,8 0 1 0 20,14.5 Z";
        const string IcoWin = "M3,4 L21,4 L21,17 L3,17 Z M8,21 L16,21";
        const string IcoProj = "M2,5 L16,5 L16,15 L2,15 Z M8,9 L22,9 L22,19 L8,19 Z";
        const string IcoShot = "M4,8 L4,4 L8,4 M16,4 L20,4 L20,8 M20,16 L20,20 L16,20 M8,20 L4,20 L4,16";
        const string IcoGear = "M15.5,12 A3.5,3.5 0 1 1 8.5,12 A3.5,3.5 0 1 1 15.5,12 Z M12,2 L12,5 M12,19 L12,22 M2,12 L5,12 M19,12 L22,12 M4.9,4.9 L7,7 M17,17 L19.1,19.1 M4.9,19.1 L7,17 M17,7 L19.1,4.9";
        const string IcoSun = "M15.5,12 A3.5,3.5 0 1 1 8.5,12 A3.5,3.5 0 1 1 15.5,12 Z M12,2.5 L12,5 M12,19 L12,21.5 M2.5,12 L5,12 M19,12 L21.5,12 M5.3,5.3 L7,7 M17,17 L18.7,18.7 M5.3,18.7 L7,17 M17,7 L18.7,5.3";
        const string IcoPhones = "M4,15 L4,12 A8,8 0 0 1 20,12 L20,15 M4,14 L7,14 L7,19 L4,19 Z M20,14 L17,14 L17,19 L20,19 Z";
        const string IcoSpeaker = "M4,9 L8,9 L13,5 L13,19 L8,15 L4,15 Z M16,9 A4,4 0 0 1 16,15";
        const string IcoPrev = "M6,5 L8,5 L8,19 L6,19 Z M20,5 L20,19 L9,12 Z";
        const string IcoNext = "M16,5 L18,5 L18,19 L16,19 Z M4,5 L4,19 L15,12 Z";
        const string IcoPlay = "M7,4 L20,12 L7,20 Z";
        const string IcoPause = "M6,4 L10,4 L10,20 L6,20 Z M14,4 L18,4 L18,20 L14,20 Z";

        readonly GlassPanel _glass = new GlassPanel(22);
        readonly StackPanel _col = new StackPanel();
        readonly TranslateTransform _slide = new TranslateTransform();

        Border _wifiCircle, _btCircle;
        TextBlock _wifiSub, _btSub;
        BarSlider _vol, _bri;
        TextBlock _briLabel;
        Border _art;
        TextBlock _title, _artist;
        Path _ppIcon;

        DateTime _hiddenAt = DateTime.MinValue;
        int _pendingBri = -1;
        bool _briBusy;

        public ControlCenterWindow()
        {
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ShowInTaskbar = false;
            Topmost = true;
            ResizeMode = ResizeMode.NoResize;
            SizeToContent = SizeToContent.Height;
            Width = 376;
            UseLayoutRounding = true;
            FontFamily = Theme.UiFont;
            TextOptions.SetTextFormattingMode(this, TextFormattingMode.Ideal);

            Left = SystemParameters.PrimaryScreenWidth - Width - 16;
            Top = 16;

            _col.Margin = new Thickness(14);
            _glass.Body.Children.Add(_col);
            _glass.RenderTransform = _slide;
            Content = _glass;

            BuildUi();

            SourceInitialized += (s, e) => Native.MakeToolWindow(this, false);
            Deactivated += (s, e) => { if (IsVisible) { _hiddenAt = DateTime.UtcNow; Hide(); } };
            MediaService.Changed += UpdateMedia;
            UpdateMedia();
        }

        public void Toggle()
        {
            if (IsVisible) { Hide(); return; }
            if ((DateTime.UtcNow - _hiddenAt).TotalMilliseconds < 250) return;   // клик по трею закрыл панель
            Left = SystemParameters.PrimaryScreenWidth - Width - 16;
            Top = 16;
            Opacity = 0;
            Show();
            Activate();
            Opacity = 1;
            BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(140)));
            _slide.BeginAnimation(TranslateTransform.YProperty,
                new DoubleAnimation(-10, 0, TimeSpan.FromMilliseconds(240)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
            Refresh();
        }

        // ---------- построение ----------
        static Path Ico(string data, double size, Brush stroke = null)
        {
            return new Path
            {
                Data = Geometry.Parse(data),
                Stroke = stroke ?? Ui.FgBrush,
                StrokeThickness = 1.8,
                StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
                StrokeLineJoin = PenLineJoin.Round,
                Stretch = Stretch.Uniform, Width = size, Height = size,
                IsHitTestVisible = false,
            };
        }

        static Border Card(UIElement child, double radius = 16)
        {
            return new Border
            {
                CornerRadius = new CornerRadius(radius),
                Background = Ui.CardBrush,
                Child = child,
            };
        }

        static TextBlock Text(string s, double size, FontWeight w, double alpha = 1)
        {
            return new TextBlock
            {
                Text = s, FontSize = size, FontWeight = w,
                Foreground = Theme.Solid(Theme.A(Ui.Fg, alpha)),
                TextTrimming = TextTrimming.CharacterEllipsis,
            };
        }

        static Border Circle(string ico, double d = 36)
        {
            return new Border
            {
                Width = d, Height = d, CornerRadius = new CornerRadius(d / 2),
                Background = Ui.CircleOffBrush,
                Child = Ico(ico, 19),
            };
        }

        static void SetOn(Border circle, bool on)
        {
            circle.Background = on ? Theme.Solid(Theme.Accent) : Ui.CircleOffBrush;
            var p = circle.Child as Path;
            if (p != null) p.Stroke = on ? Brushes.White : Ui.FgBrush;
        }

        Grid ToggleLine(string ico, string title, out Border circle, out TextBlock sub, string subText, Action click)
        {
            var g = new Grid { Background = Brushes.Transparent, Cursor = Cursors.Hand, Margin = new Thickness(10, 0, 8, 0) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            circle = Circle(ico);
            circle.VerticalAlignment = VerticalAlignment.Center;
            var tp = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(11, 0, 0, 0) };
            tp.Children.Add(Text(title, 13.5, FontWeights.SemiBold));
            sub = Text(subText, 11.5, FontWeights.Normal, 0.58);
            tp.Children.Add(sub);
            Grid.SetColumn(tp, 1);
            g.Children.Add(circle); g.Children.Add(tp);
            g.MouseLeftButtonUp += (s, e) => click();
            return g;
        }

        Border Tile(string ico, string label, Action click)
        {
            var sp = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
            var i = Ico(ico, 22); i.HorizontalAlignment = HorizontalAlignment.Center;
            var t = Text(label, 11.5, FontWeights.Medium); t.Margin = new Thickness(0, 6, 0, 0);
            t.TextAlignment = TextAlignment.Center;
            sp.Children.Add(i); sp.Children.Add(t);
            var b = Card(sp);
            b.Cursor = Cursors.Hand;
            b.MouseLeftButtonUp += (s, e) => click();
            return b;
        }

        Border SliderCard(string label, out BarSlider slider, out TextBlock lbl)
        {
            var sp = new StackPanel { Margin = new Thickness(14, 11, 14, 14) };
            lbl = Text(label, 13, FontWeights.SemiBold);
            lbl.Margin = new Thickness(0, 0, 0, 10);
            slider = new BarSlider(label == "Дисплей" ? IcoSun : IcoPhones);
            sp.Children.Add(lbl); sp.Children.Add(slider);
            return Card(sp);
        }

        Border Pill(string ico, string label, Action click)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            row.Children.Add(Ico(ico, 19));
            var t = Text(label, 12, FontWeights.SemiBold); t.Margin = new Thickness(8, 0, 0, 0);
            t.VerticalAlignment = VerticalAlignment.Center;
            row.Children.Add(t);
            var b = Card(row);
            b.Height = 52; b.Cursor = Cursors.Hand;
            b.MouseLeftButtonUp += (s, e) => click();
            return b;
        }

        void BuildUi()
        {
            // --- ряд 1: связь + плитки ---
            var top = new Grid { Height = 178, Margin = new Thickness(0, 0, 0, 12) };
            top.ColumnDefinitions.Add(new ColumnDefinition());
            top.ColumnDefinitions.Add(new ColumnDefinition());

            var conn = new Grid();
            for (int i = 0; i < 3; i++) conn.RowDefinitions.Add(new RowDefinition());
            var wifi = ToggleLine(IcoWifi, "Wi-Fi", out _wifiCircle, out _wifiSub, "…", ToggleWifi);
            var bt = ToggleLine(IcoBt, "Bluetooth", out _btCircle, out _btSub, "…", ToggleBt);
            Border shareCircle; TextBlock shareSub;
            var share = ToggleLine(IcoShare, "Обмен", out shareCircle, out shareSub, "Устройства рядом",
                () => RunAfterHide(() => SystemControls.OpenUri("ms-settings:crossdevice")));
            Grid.SetRow(bt, 1); Grid.SetRow(share, 2);
            conn.Children.Add(wifi); conn.Children.Add(bt); conn.Children.Add(share);
            var connCard = Card(conn);
            connCard.Margin = new Thickness(0, 0, 6, 0);

            var right = new Grid { Margin = new Thickness(6, 0, 0, 0) };
            right.RowDefinitions.Add(new RowDefinition());
            right.RowDefinitions.Add(new RowDefinition());

            var night = new Grid { Background = Brushes.Transparent, Cursor = Cursors.Hand, Margin = new Thickness(12, 0, 8, 0) };
            night.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            night.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var nc = Circle(IcoMoon); nc.VerticalAlignment = VerticalAlignment.Center;
            var nt = Text("Ночной свет", 13.5, FontWeights.SemiBold);
            nt.VerticalAlignment = VerticalAlignment.Center; nt.Margin = new Thickness(11, 0, 0, 0);
            Grid.SetColumn(nt, 1);
            night.Children.Add(nc); night.Children.Add(nt);
            night.MouseLeftButtonUp += (s, e) => RunAfterHide(() => SystemControls.OpenUri("ms-settings:nightlight"));
            var nightCard = Card(night);
            nightCard.Margin = new Thickness(0, 0, 0, 6);

            var small = new Grid { Margin = new Thickness(0, 6, 0, 0) };
            small.ColumnDefinitions.Add(new ColumnDefinition());
            small.ColumnDefinitions.Add(new ColumnDefinition());
            var tWin = Tile(IcoWin, "Окна", () => RunAfterHide(() => Native.Chord(Native.VK_LWIN, Native.VK_TAB)));
            tWin.Margin = new Thickness(0, 0, 5, 0);
            var tProj = Tile(IcoProj, "Проекция", () => RunAfterHide(() => Native.Chord(Native.VK_LWIN, Native.VK_P)));
            tProj.Margin = new Thickness(5, 0, 0, 0);
            Grid.SetColumn(tProj, 1);
            small.Children.Add(tWin); small.Children.Add(tProj);
            Grid.SetRow(small, 1);
            right.Children.Add(nightCard); right.Children.Add(small);
            Grid.SetColumn(right, 1);

            top.Children.Add(connCard); top.Children.Add(right);
            _col.Children.Add(top);

            // --- ползунки ---
            var briCard = SliderCard("Дисплей", out _bri, out _briLabel);
            briCard.Margin = new Thickness(0, 0, 0, 12);
            _bri.Changed += v => QueueBrightness((int)Math.Round(v * 100));
            _col.Children.Add(briCard);

            TextBlock volLbl;
            var volCard = SliderCard("Звук", out _vol, out volLbl);
            volCard.Margin = new Thickness(0, 0, 0, 12);
            _vol.Changed += v => SystemControls.SetVolume((float)v);
            _col.Children.Add(volCard);

            // --- плеер ---
            var np = new Grid { Margin = new Thickness(12, 11, 14, 11) };
            np.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            np.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            np.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            _art = new Border { Width = 46, Height = 46, CornerRadius = new CornerRadius(10), Margin = new Thickness(0, 0, 12, 0) };
            _title = Text("", 14, FontWeights.SemiBold);
            _artist = Text("", 12, FontWeights.Normal, 0.58);
            var meta = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            meta.Children.Add(_title); meta.Children.Add(_artist);
            Grid.SetColumn(meta, 1);
            var ctl = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            ctl.Children.Add(MediaBtn(IcoPrev, MediaService.Prev));
            _ppIcon = new Path { Fill = Ui.FgBrush, Stretch = Stretch.Uniform, Width = 17, Height = 17, IsHitTestVisible = false };
            var pp = new Border { Background = Brushes.Transparent, Padding = new Thickness(8), Child = _ppIcon, Cursor = Cursors.Hand };
            pp.MouseLeftButtonUp += (s, e) => MediaService.Toggle();
            ctl.Children.Add(pp);
            ctl.Children.Add(MediaBtn(IcoNext, MediaService.Next));
            Grid.SetColumn(ctl, 2);
            np.Children.Add(_art); np.Children.Add(meta); np.Children.Add(ctl);
            var npCard = Card(np);
            npCard.Margin = new Thickness(0, 0, 0, 12);
            _col.Children.Add(npCard);

            // --- нижний ряд ---
            var bottom = new Grid();
            bottom.ColumnDefinitions.Add(new ColumnDefinition());
            bottom.ColumnDefinitions.Add(new ColumnDefinition());
            bottom.ColumnDefinitions.Add(new ColumnDefinition());
            var p1 = Pill(IcoShot, "Скриншот", () => RunAfterHide(() => Native.Chord(Native.VK_LWIN, Native.VK_SHIFT, Native.VK_S)));
            p1.Margin = new Thickness(0, 0, 5, 0);
            var p2 = Pill(IcoGear, "Параметры", () => RunAfterHide(() => SystemControls.OpenUri("ms-settings:")));
            p2.Margin = new Thickness(5, 0, 5, 0); Grid.SetColumn(p2, 1);
            var p3 = Pill(IcoSpeaker, "Звук", () => RunAfterHide(() => SystemControls.OpenUri("ms-settings:sound")));
            p3.Margin = new Thickness(5, 0, 0, 0); Grid.SetColumn(p3, 2);
            bottom.Children.Add(p1); bottom.Children.Add(p2); bottom.Children.Add(p3);
            _col.Children.Add(bottom);
        }

        Border MediaBtn(string ico, Action click)
        {
            var p = new Path { Data = Geometry.Parse(ico), Fill = Ui.FgBrush, Stretch = Stretch.Uniform, Width = 17, Height = 17, IsHitTestVisible = false };
            var b = new Border { Background = Brushes.Transparent, Padding = new Thickness(8), Child = p, Cursor = Cursors.Hand };
            b.MouseLeftButtonUp += (s, e) => click();
            return b;
        }

        // ---------- действия ----------
        void RunAfterHide(Action a)
        {
            _hiddenAt = DateTime.UtcNow;
            Hide();
            Task.Delay(150).ContinueWith(_ => { try { a(); } catch { } });
        }

        void QueueBrightness(int percent)
        {
            _pendingBri = percent;
            if (_briBusy) return;
            _briBusy = true;
            Task.Run(() =>
            {
                while (_pendingBri >= 0)
                {
                    int v = _pendingBri; _pendingBri = -1;
                    SystemControls.SetBrightness(v);
                }
                _briBusy = false;
            });
        }

        async void ToggleWifi()
        {
            var r = await SystemControls.ToggleRadio(RadioKind.WiFi);
            if (r == null) return;
            SetOn(_wifiCircle, r.Value);
            _wifiSub.Text = r.Value ? "Подключение…" : "Выкл.";
            if (r.Value) { await Task.Delay(2500); _wifiSub.Text = (await SystemControls.GetSsid()) ?? "Не подключено"; }
        }

        async void ToggleBt()
        {
            var r = await SystemControls.ToggleRadio(RadioKind.Bluetooth);
            if (r == null) return;
            SetOn(_btCircle, r.Value);
            _btSub.Text = r.Value ? "Вкл." : "Выкл.";
        }

        async void Refresh()
        {
            float v = SystemControls.GetVolume();
            if (v >= 0) _vol.Value = v;

            int b = await Task.Run(() => SystemControls.GetBrightness());
            if (b >= 0) { _bri.Value = b / 100.0; _bri.IsEnabled = true; _bri.Opacity = 1; _briLabel.Text = "Дисплей"; }
            else { _bri.IsEnabled = false; _bri.Opacity = 0.4; _briLabel.Text = "Дисплей · недоступно"; }

            bool? wifi = await SystemControls.GetRadioState(RadioKind.WiFi);
            if (wifi == null) { SetOn(_wifiCircle, false); _wifiSub.Text = "Нет адаптера"; }
            else
            {
                SetOn(_wifiCircle, wifi.Value);
                _wifiSub.Text = wifi.Value ? ((await SystemControls.GetSsid()) ?? "Не подключено") : "Выкл.";
            }

            bool? bt = await SystemControls.GetRadioState(RadioKind.Bluetooth);
            if (bt == null) { SetOn(_btCircle, false); _btSub.Text = "Нет адаптера"; }
            else { SetOn(_btCircle, bt.Value); _btSub.Text = bt.Value ? "Вкл." : "Выкл."; }
        }

        void UpdateMedia()
        {
            var i = MediaService.Info;
            _title.Text = i.HasSession && i.Title.Length > 0 ? i.Title : "Ничего не играет";
            _artist.Text = i.Artist;
            _ppIcon.Data = Geometry.Parse(i.Playing ? IcoPause : IcoPlay);
            if (i.Thumb != null) _art.Background = new ImageBrush(i.Thumb) { Stretch = Stretch.UniformToFill };
            else _art.Background = new LinearGradientBrush(Color.FromRgb(0x55, 0x58, 0x6B), Color.FromRgb(0x2B, 0x2D, 0x3A), 45);
        }
    }
}
