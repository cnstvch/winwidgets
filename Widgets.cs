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
    /// <summary>Основа виджета на столе: стеклянная плитка, перетаскивание, масштаб через Viewbox.</summary>
    public abstract class WidgetWindow : Window
    {
        readonly Func<WidgetCfg> _cfg;
        protected readonly GlassPanel Glass = new GlassPanel(28);
        protected readonly Grid Design = new Grid { Width = 150, Height = 150 };

        protected WidgetWindow(Func<WidgetCfg> cfg)
        {
            _cfg = cfg;
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ShowInTaskbar = false;
            ShowActivated = false;
            ResizeMode = ResizeMode.NoResize;
            Focusable = false;
            TextOptions.SetTextFormattingMode(this, TextFormattingMode.Ideal);

            Glass.Body.Children.Add(new Viewbox { Stretch = Stretch.Uniform, Child = Design });
            Content = Glass;

            var m = new ContextMenu();
            var s1 = new MenuItem { Header = "Настройки…" }; s1.Click += (s, e) => AppActions.OpenSettings();
            var s2 = new MenuItem { Header = "Выход" }; s2.Click += (s, e) => AppActions.Exit();
            m.Items.Add(s1); m.Items.Add(new Separator()); m.Items.Add(s2);
            ContextMenu = m;

            MouseLeftButtonDown += (s, e) =>
            {
                try
                {
                    DragMove();
                    var c = _cfg();
                    c.Left = Math.Round(Left); c.Top = Math.Round(Top);
                    ConfigStore.Save();
                    Glass.Reposition();
                }
                catch { }
            };
            SourceInitialized += (s, e) => Native.MakeToolWindow(this, true);
            ConfigStore.Changed += ApplyCfg;
            ApplyCfg();
        }

        void ApplyCfg()
        {
            var c = _cfg();
            Width = c.Size; Height = c.Size;
            Left = c.Left; Top = c.Top;
            if (c.Enabled) { if (!IsVisible) Show(); }
            else if (IsVisible) Hide();
            Glass.Reposition();
        }
    }

    /// <summary>Классические аналоговые часы: белое матовое стекло, оранжевая секундная стрелка.</summary>
    public class AnalogClockWindow : WidgetWindow
    {
        readonly RotateTransform _h = new RotateTransform(0, 75, 75);
        readonly RotateTransform _m = new RotateTransform(0, 75, 75);
        readonly RotateTransform _s = new RotateTransform(0, 75, 75);
        readonly DispatcherTimer _t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        double _secAngle = double.NaN;

        public AnalogClockWindow() : base(() => ConfigStore.Current.AnalogClock)
        {
            Build();
            _t.Tick += (s, e) => Tick();
            _t.Start();
            Tick();
        }

        static Line Hand(double len, double thick, double tail, Brush b, RotateTransform rot)
        {
            return new Line
            {
                X1 = 75, Y1 = 75 + tail, X2 = 75, Y2 = 75 - len,
                Stroke = b, StrokeThickness = thick,
                StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
                RenderTransform = rot, IsHitTestVisible = false,
            };
        }

        void Build()
        {
            var fg = Ui.FgBrush;
            var dim = Theme.Solid(Theme.A(Ui.Fg, 0.35));
            var orange = Theme.Solid(Color.FromRgb(0xFF, 0x95, 0x00));
            var cv = new Canvas { Width = 150, Height = 150 };

            for (int i = 0; i < 60; i++)
            {
                double a = i * 6 * Math.PI / 180, sn = Math.Sin(a), cs = Math.Cos(a);
                bool hr = i % 5 == 0;
                double r1 = 67, r2 = hr ? 60 : 63.5;
                cv.Children.Add(new Line
                {
                    X1 = 75 + r1 * sn, Y1 = 75 - r1 * cs, X2 = 75 + r2 * sn, Y2 = 75 - r2 * cs,
                    Stroke = hr ? fg : dim, StrokeThickness = hr ? 1.5 : 0.8, IsHitTestVisible = false,
                });
            }

            for (int n = 1; n <= 12; n++)
            {
                double a = n * 30 * Math.PI / 180;
                double cx = 75 + 47 * Math.Sin(a), cy = 75 - 47 * Math.Cos(a);
                var t = new TextBlock
                {
                    Text = n.ToString(), Width = 24, TextAlignment = TextAlignment.Center,
                    FontFamily = Theme.UiFont, FontSize = 14, FontWeight = FontWeights.Light,
                    Foreground = fg, IsHitTestVisible = false,
                };
                Canvas.SetLeft(t, cx - 12); Canvas.SetTop(t, cy - 10);
                cv.Children.Add(t);
            }

            cv.Children.Add(Hand(31, 4.2, 6, fg, _h));
            cv.Children.Add(Hand(46, 3.2, 8, fg, _m));
            cv.Children.Add(Hand(54, 1.3, 15, orange, _s));

            var hub = new Ellipse { Width = 8, Height = 8, Fill = orange, IsHitTestVisible = false };
            Canvas.SetLeft(hub, 71); Canvas.SetTop(hub, 71);
            cv.Children.Add(hub);
            var pin = new Ellipse { Width = 3, Height = 3, Fill = Brushes.White, IsHitTestVisible = false };
            Canvas.SetLeft(pin, 73.5); Canvas.SetTop(pin, 73.5);
            cv.Children.Add(pin);

            Design.Children.Add(cv);
        }

        void Tick()
        {
            var n = DateTime.Now;
            _h.Angle = ((n.Hour % 12) + n.Minute / 60.0 + n.Second / 3600.0) * 30;
            _m.Angle = (n.Minute + n.Second / 60.0) * 6;

            double target = n.Second * 6;
            if (double.IsNaN(_secAngle)) { _secAngle = target; _s.Angle = target; return; }

            double delta = ((target - _secAngle) % 360 + 360) % 360;   // секундная стрелка идёт только вперёд
            if (delta < 0.01) return;
            _secAngle += delta;
            // как на маке: быстрый «тик» с лёгким перелётом
            _s.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(_secAngle, TimeSpan.FromMilliseconds(260))
            {
                EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.5 },
            });
        }
    }

    /// <summary>Виджет «Календарь»: красный день недели, крупное число, месяц.</summary>
    public class CalendarWindow : WidgetWindow
    {
        static readonly string[] Days =
            { "ВОСКРЕСЕНЬЕ", "ПОНЕДЕЛЬНИК", "ВТОРНИК", "СРЕДА", "ЧЕТВЕРГ", "ПЯТНИЦА", "СУББОТА" };
        static readonly string[] Months =
            { "Январь", "Февраль", "Март", "Апрель", "Май", "Июнь", "Июль", "Август", "Сентябрь", "Октябрь", "Ноябрь", "Декабрь" };

        readonly TextBlock _dow = new TextBlock();
        readonly TextBlock _day = new TextBlock();
        readonly TextBlock _month = new TextBlock();
        readonly DispatcherTimer _t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };

        public CalendarWindow() : base(() => ConfigStore.Current.Calendar)
        {
            _dow.FontFamily = Theme.UiFont; _dow.FontSize = 12.5; _dow.FontWeight = FontWeights.SemiBold;
            _dow.Foreground = Theme.Solid(Color.FromRgb(0xFF, 0x3B, 0x30)); _dow.TextAlignment = TextAlignment.Center;
            _day.FontFamily = Theme.UiFont; _day.FontSize = 68; _day.FontWeight = FontWeights.Light;
            _day.Foreground = Ui.FgBrush; _day.TextAlignment = TextAlignment.Center;
            _day.Margin = new Thickness(0, -6, 0, -4);
            _month.FontFamily = Theme.UiFont; _month.FontSize = 13; _month.FontWeight = FontWeights.Medium;
            _month.Foreground = Ui.DimBrush; _month.TextAlignment = TextAlignment.Center;

            var sp = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
            sp.Children.Add(_dow); sp.Children.Add(_day); sp.Children.Add(_month);
            Design.Children.Add(sp);

            _t.Tick += (s, e) => Tick();
            _t.Start();
            Tick();
        }

        void Tick()
        {
            var n = DateTime.Now;
            _dow.Text = Days[(int)n.DayOfWeek];
            _day.Text = n.Day.ToString();
            _month.Text = Months[n.Month - 1] + " " + n.Year;
        }
    }
}
