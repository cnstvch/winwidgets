using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace WinWidgets
{
    /// <summary>Часы на рабочем столе: шрифт, цвет, стекло, прозрачность, жирность, интервал — из конфига.</summary>
    public class ClockWindow : Window
    {
        static readonly string[] Days =
            { "ВОСКРЕСЕНЬЕ", "ПОНЕДЕЛЬНИК", "ВТОРНИК", "СРЕДА", "ЧЕТВЕРГ", "ПЯТНИЦА", "СУББОТА" };
        static readonly string[] Months =
            { "ЯНВАРЯ", "ФЕВРАЛЯ", "МАРТА", "АПРЕЛЯ", "МАЯ", "ИЮНЯ", "ИЮЛЯ", "АВГУСТА", "СЕНТЯБРЯ", "ОКТЯБРЯ", "НОЯБРЯ", "ДЕКАБРЯ" };

        readonly StackPanel _root = new StackPanel();
        readonly StackPanel _timeRow = new StackPanel { Orientation = Orientation.Horizontal };
        readonly StackPanel _dateRow = new StackPanel { Orientation = Orientation.Horizontal };
        readonly DispatcherTimer _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        readonly DispatcherTimer _bottomTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(8) };
        string _lastTime = "", _lastDate = "";

        public ClockWindow()
        {
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ShowInTaskbar = false;
            ShowActivated = false;
            ResizeMode = ResizeMode.NoResize;
            SizeToContent = SizeToContent.WidthAndHeight;
            Focusable = false;
            UseLayoutRounding = true;
            TextOptions.SetTextFormattingMode(this, TextFormattingMode.Ideal);

            _root.Margin = new Thickness(10);
            _root.Children.Add(_timeRow);
            _root.Children.Add(_dateRow);
            Content = _root;

            ContextMenu = BuildMenu();
            MouseLeftButtonDown += OnDown;

            SourceInitialized += (s, e) => { Native.MakeToolWindow(this, true); PushBack(); };
            Activated += (s, e) => PushBack();

            _timer.Tick += (s, e) => Refresh(false);
            _timer.Start();
            _bottomTimer.Tick += (s, e) => { if (IsVisible) PushBack(); };
            _bottomTimer.Start();

            ConfigStore.Changed += () => { ApplyPosition(); Refresh(true); };
            ApplyPosition();
            Refresh(true);
        }

        void PushBack()
        {
            if (ConfigStore.Current.Clock.SendToBack) Native.SendToBottom(this);
        }

        void ApplyPosition()
        {
            var c = ConfigStore.Current.Clock;
            Left = c.Left; Top = c.Top;
        }

        ContextMenu BuildMenu()
        {
            var m = new ContextMenu();
            m.Items.Add(Item("Настройки…", () => AppActions.OpenSettings()));
            m.Items.Add(Item("Панель управления", () => AppActions.ToggleControlCenter()));
            m.Items.Add(Item("Открыть config.json", () => AppActions.OpenConfigFile()));
            m.Items.Add(new Separator());
            m.Items.Add(Item("Выход", () => AppActions.Exit()));
            return m;
        }

        static MenuItem Item(string header, Action a)
        {
            var mi = new MenuItem { Header = header };
            mi.Click += (s, e) => a();
            return mi;
        }

        void OnDown(object s, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2) { AppActions.OpenSettings(); return; }
            try
            {
                DragMove();
                var c = ConfigStore.Current.Clock;
                c.Left = Math.Round(Left); c.Top = Math.Round(Top);
                ConfigStore.Save();
            }
            catch { }
        }

        void Refresh(bool force)
        {
            try { RefreshCore(force); }
            catch (Exception ex)
            {
                Log.Write("CLOCK", ex);
                // плохой шрифт или значение: откатываемся к безопасному, чтобы не падать каждую секунду
                var k = ConfigStore.Current.Clock;
                k.Font = "Segoe UI"; k.Weight = 300;
                try { RefreshCore(true); } catch { }
            }
        }

        void RefreshCore(bool force)
        {
            var c = ConfigStore.Current.Clock;
            if (!c.Enabled) { Hide(); return; }
            if (!IsVisible) Show();

            var now = DateTime.Now;
            string t = now.ToString(c.Hour24 ? "HH:mm" : "h:mm");
            if (c.Seconds) t += now.ToString(":ss");
            string d = Days[(int)now.DayOfWeek] + " · " + now.Day + " " + Months[now.Month - 1];

            if (!force && t == _lastTime && d == _lastDate) return;
            _lastTime = t; _lastDate = d;

            Color col = Theme.Parse(c.Color, Colors.White);
            double a = Math.Max(0.05, Math.Min(1, c.Opacity));
            double g = Math.Max(0, Math.Min(1, c.Glass));

            // «стеклянные» буквы: сверху плотнее, к низу растворяются
            var fill = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
            fill.GradientStops.Add(new GradientStop(Theme.A(col, a), 0.05));
            fill.GradientStops.Add(new GradientStop(Theme.A(col, a * (1 - 0.62 * g)), 0.95));
            fill.Freeze();

            var dateBrush = Theme.Solid(Theme.A(col, a * 0.72));
            var ff = new FontFamily(string.IsNullOrWhiteSpace(c.Font) ? "Segoe UI" : c.Font);
            var weight = FontWeight.FromOpenTypeWeight(Math.Max(1, Math.Min(999, c.Weight)));

            Fill(_timeRow, t, c.Size, c.Spacing, ff, weight, fill, true);

            _dateRow.Visibility = c.ShowDate ? Visibility.Visible : Visibility.Collapsed;
            double ds = Math.Max(13, c.Size * 0.12);
            Fill(_dateRow, d, ds, 2.2, ff, FontWeight.FromOpenTypeWeight(Math.Max(300, Math.Min(600, c.Weight + 200))),
                dateBrush, false);
            _dateRow.Margin = new Thickness(c.Size * 0.05, -c.Size * 0.04, 0, 0);
        }

        static void Fill(StackPanel row, string text, double size, double spacing, FontFamily ff,
            FontWeight fw, Brush brush, bool tabular)
        {
            // у TextBlock нет межсимвольного интервала — раскладываем по символам
            if (row.Children.Count != text.Length)
            {
                row.Children.Clear();
                for (int i = 0; i < text.Length; i++)
                {
                    var tb = new TextBlock();
                    if (tabular) Typography.SetNumeralAlignment(tb, FontNumeralAlignment.Tabular);
                    row.Children.Add(tb);
                }
            }
            for (int i = 0; i < text.Length; i++)
            {
                var tb = (TextBlock)row.Children[i];
                tb.Text = text[i].ToString();
                tb.FontFamily = ff;
                tb.FontSize = size;
                tb.FontWeight = fw;
                tb.Foreground = brush;
                tb.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
                tb.LineHeight = size * 1.12;
                tb.Margin = new Thickness(0, 0, spacing, 0);
            }
        }
    }
}
