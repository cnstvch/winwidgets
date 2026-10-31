using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace WinWidgets
{
    /// <summary>Настройки часов и стекла. Всё применяется сразу и пишется в config.json.</summary>
    public class SettingsWindow : Window
    {
        readonly StackPanel _stack = new StackPanel { Margin = new Thickness(22, 18, 22, 18) };
        readonly DispatcherTimer _save = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        TextBox _hex;

        public SettingsWindow()
        {
            Title = "WinWidgets · Настройки";
            Width = 460; Height = 700;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Background = new SolidColorBrush(Color.FromRgb(0xF4, 0xF5, 0xF8));
            FontFamily = Theme.UiFont;
            FontSize = 13;
            Content = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = _stack };

            _save.Tick += (s, e) => { _save.Stop(); ConfigStore.Save(); };
            Build();
        }

        void Commit()
        {
            ConfigStore.NotifyChanged();
            _save.Stop(); _save.Start();
        }

        void Build()
        {
            var c = ConfigStore.Current.Clock;
            var g = ConfigStore.Current.Glass;

            Header("Часы");
            Check("Показывать часы", c.Enabled, v => { c.Enabled = v; Commit(); });
            Check("24-часовой формат", c.Hour24, v => { c.Hour24 = v; Commit(); });
            Check("Секунды", c.Seconds, v => { c.Seconds = v; Commit(); });
            Check("Дата под часами", c.ShowDate, v => { c.ShowDate = v; Commit(); });

            // шрифт
            var fonts = new List<string>();
            try
            {
                string[] skip = { "wingdings", "webdings", "symbol", "marlett", "mdl2", "fluent icons", "holo", "emoji", "specialty", "bookshelf", "dingbats" };
                foreach (var f in Fonts.SystemFontFamilies)
                {
                    string n = f.Source;
                    string low = n.ToLowerInvariant();
                    bool bad = false;
                    foreach (var k in skip) if (low.Contains(k)) { bad = true; break; }
                    if (!bad) fonts.Add(n);
                }
                fonts.Sort(StringComparer.CurrentCultureIgnoreCase);
            }
            catch { }
            var cb = new ComboBox { ItemsSource = fonts, Margin = new Thickness(0, 6, 0, 6) };
            cb.SelectedItem = fonts.Contains(c.Font) ? c.Font : null;
            cb.SelectionChanged += (s, e) =>
            {
                if (cb.SelectedItem is string f) { c.Font = ConfigStore.FontOrDefault(f); Commit(); }
            };
            Row("Шрифт", cb);

            // цвет
            var colorRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 4) };
            foreach (var hex in new[] { "#FFFFFF", "#8FB0FF", "#FF8FB8", "#7DFFCF", "#FFD27A", "#C9A7FF" })
            {
                string h = hex;
                var sw = new Border
                {
                    Width = 24, Height = 24, CornerRadius = new CornerRadius(12), Margin = new Thickness(0, 0, 8, 0),
                    Background = Theme.Solid(Theme.Parse(h, Colors.White)), Cursor = Cursors.Hand,
                    BorderBrush = new SolidColorBrush(Color.FromArgb(60, 0, 0, 0)), BorderThickness = new Thickness(1),
                };
                sw.MouseLeftButtonUp += (s, e) => { c.Color = h; _hex.Text = h; Commit(); };
                colorRow.Children.Add(sw);
            }
            _hex = new TextBox { Text = c.Color, Width = 84, Padding = new Thickness(4, 2, 4, 2) };
            _hex.LostFocus += (s, e) => { c.Color = _hex.Text.Trim(); Commit(); };
            _hex.KeyDown += (s, e) => { if (e.Key == Key.Enter) { c.Color = _hex.Text.Trim(); Commit(); } };
            colorRow.Children.Add(_hex);
            Row("Цвет", colorRow);

            Slider("Размер", 60, 400, c.Size, v => ((int)v).ToString(), v => { c.Size = Math.Round(v); Commit(); });
            Slider("Жирность", 100, 900, c.Weight, v => ((int)v).ToString(), v => { c.Weight = (int)(Math.Round(v / 100) * 100); Commit(); });
            Slider("Интервал", -20, 30, c.Spacing, v => v.ToString("0.#"), v => { c.Spacing = Math.Round(v, 1); Commit(); });
            Slider("Прозрачность", 0.1, 1, c.Opacity, v => ((int)(v * 100)) + "%", v => { c.Opacity = Math.Round(v, 2); Commit(); });
            Slider("Эффект стекла", 0, 1, c.Glass, v => ((int)(v * 100)) + "%", v => { c.Glass = Math.Round(v, 2); Commit(); });

            Header("Стекло (панель управления, док)");
            Check("Светлое стекло, как в macOS (применится после перезапуска)", g.Light, v => { g.Light = v; Commit(); });
            Check("Размытые обои под стеклом", g.WallpaperBlur, v => { g.WallpaperBlur = v; Commit(); });
            Slider("Плотность", 0, 0.8, g.Opacity, v => ((int)(v * 100)) + "%", v => { g.Opacity = Math.Round(v, 2); Commit(); });

            Header("Виджеты");
            var ac = ConfigStore.Current.AnalogClock; var cal = ConfigStore.Current.Calendar;
            Check("Аналоговые часы", ac.Enabled, v => { ac.Enabled = v; Commit(); });
            Check("Календарь", cal.Enabled, v => { cal.Enabled = v; Commit(); });
            Check("Иконка корзины в доке", ConfigStore.Current.Dock.ShowTrash, v => { ConfigStore.Current.Dock.ShowTrash = v; Commit(); });
            Check("Время в пустой челке", ConfigStore.Current.Island.IdleClock, v => { ConfigStore.Current.Island.IdleClock = v; Commit(); });

            var reload = new Button { Content = "Обновить обои", Padding = new Thickness(12, 5, 12, 5), Margin = new Thickness(0, 12, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
            reload.Click += (s, e) => { Wallpaper.Reload(); ConfigStore.NotifyChanged(); };
            _stack.Children.Add(reload);

            var hint = new TextBlock
            {
                Text = "Док, закреплённые приложения и положение — в файле config.json (ПКМ по часам → Открыть config.json).",
                TextWrapping = TextWrapping.Wrap, Foreground = Brushes.Gray, Margin = new Thickness(0, 16, 0, 0)
            };
            _stack.Children.Add(hint);
        }

        void Header(string text)
        {
            _stack.Children.Add(new TextBlock
            {
                Text = text, FontSize = 15, FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, _stack.Children.Count == 0 ? 0 : 20, 0, 6)
            });
        }

        void Check(string text, bool val, Action<bool> set)
        {
            var cb = new CheckBox { Content = text, IsChecked = val, Margin = new Thickness(0, 3, 0, 3) };
            cb.Checked += (s, e) => set(true);
            cb.Unchecked += (s, e) => set(false);
            _stack.Children.Add(cb);
        }

        void Row(string label, UIElement ctl)
        {
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var t = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(ctl, 1);
            g.Children.Add(t); g.Children.Add(ctl);
            _stack.Children.Add(g);
        }

        void Slider(string label, double min, double max, double val, Func<double, string> fmt, Action<double> set)
        {
            var g = new Grid { Margin = new Thickness(0, 3, 0, 3) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(52) });

            var t = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center };
            var vt = new TextBlock { Text = fmt(val), VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Right, Foreground = Brushes.Gray };
            var s = new Slider { Minimum = min, Maximum = max, Value = val, VerticalAlignment = VerticalAlignment.Center };
            s.ValueChanged += (a, b) => { vt.Text = fmt(s.Value); set(s.Value); };

            Grid.SetColumn(s, 1); Grid.SetColumn(vt, 2);
            g.Children.Add(t); g.Children.Add(s); g.Children.Add(vt);
            _stack.Children.Add(g);
        }

        protected override void OnClosed(EventArgs e)
        {
            _save.Stop();
            ConfigStore.Save();
            base.OnClosed(e);
        }
    }
}
