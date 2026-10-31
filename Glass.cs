using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace WinWidgets
{
    /// <summary>
    /// Один раз размывает обои в крошечную картинку. Дальше «стекло» просто рисует её
    /// кусок под собой — это ноль нагрузки в простое и без шейдеров.
    /// </summary>
    public static class Wallpaper
    {
        public static BitmapSource Blurred { get; private set; }

        public static void Reload()
        {
            try { Blurred = Build(); }
            catch { Blurred = null; }
        }

        static string FindPath()
        {
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop"))
                {
                    var p = k?.GetValue("WallPaper") as string;
                    if (!string.IsNullOrWhiteSpace(p) && File.Exists(p)) return p;
                }
            }
            catch { }
            string t = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                @"Microsoft\Windows\Themes\TranscodedWallpaper");
            return File.Exists(t) ? t : null;
        }

        static BitmapSource Build()
        {
            string path = FindPath();
            if (path == null) return null;

            var bi = new BitmapImage();
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                bi.BeginInit();
                bi.StreamSource = fs;
                bi.DecodePixelWidth = 192;
                bi.CacheOption = BitmapCacheOption.OnLoad;
                bi.EndInit();
            }
            bi.Freeze();

            var fc = new FormatConvertedBitmap(bi, PixelFormats.Bgra32, null, 0);
            int iw = fc.PixelWidth, ih = fc.PixelHeight;
            var src = new byte[iw * ih * 4];
            fc.CopyPixels(src, iw * 4, 0);

            // «заливка» обоями на весь экран: обрезаем по пропорциям экрана
            double sw = SystemParameters.PrimaryScreenWidth, sh = SystemParameters.PrimaryScreenHeight;
            double aspect = sw / sh;
            int cw = iw, ch = ih, x0 = 0, y0 = 0;
            if ((double)iw / ih > aspect) { cw = (int)Math.Round(ih * aspect); x0 = (iw - cw) / 2; }
            else { ch = (int)Math.Round(iw / aspect); y0 = (ih - ch) / 2; }
            if (cw < 4 || ch < 4) return null;

            var px = new byte[cw * ch * 4];
            for (int y = 0; y < ch; y++)
                Buffer.BlockCopy(src, ((y + y0) * iw + x0) * 4, px, y * cw * 4, cw * 4);

            BoxBlur(px, cw, ch, 2);
            BoxBlur(px, cw, ch, 2);

            // насыщенность + лёгкий подъём яркости: так выглядит «vibrancy» macOS
            for (int i = 0; i < px.Length; i += 4)
            {
                double b = px[i], g = px[i + 1], r = px[i + 2];
                double l = 0.299 * r + 0.587 * g + 0.114 * b;
                r = l + (r - l) * 1.45 + 6; g = l + (g - l) * 1.45 + 6; b = l + (b - l) * 1.45 + 6;
                px[i] = (byte)Math.Max(0, Math.Min(255, b));
                px[i + 1] = (byte)Math.Max(0, Math.Min(255, g));
                px[i + 2] = (byte)Math.Max(0, Math.Min(255, r));
                px[i + 3] = 255;
            }

            // DPI подбираем так, чтобы размер картинки в DIP совпадал с размером экрана
            double dpiX = 96.0 * cw / sw, dpiY = 96.0 * ch / sh;
            var bmp = BitmapSource.Create(cw, ch, dpiX, dpiY, PixelFormats.Bgra32, null, px, cw * 4);
            bmp.Freeze();
            return bmp;
        }

        static void BoxBlur(byte[] d, int w, int h, int r)
        {
            var tmp = new byte[d.Length];
            // горизонталь
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    for (int c = 0; c < 3; c++)
                    {
                        int sum = 0, n = 0;
                        for (int k = -r; k <= r; k++)
                        {
                            int xx = Math.Min(w - 1, Math.Max(0, x + k));
                            sum += d[(y * w + xx) * 4 + c]; n++;
                        }
                        tmp[(y * w + x) * 4 + c] = (byte)(sum / n);
                    }
            // вертикаль
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    for (int c = 0; c < 3; c++)
                    {
                        int sum = 0, n = 0;
                        for (int k = -r; k <= r; k++)
                        {
                            int yy = Math.Min(h - 1, Math.Max(0, y + k));
                            sum += tmp[(yy * w + x) * 4 + c]; n++;
                        }
                        d[(y * w + x) * 4 + c] = (byte)(sum / n);
                    }
        }
    }

    public static class Theme
    {
        public static Color Parse(string hex, Color fallback)
        {
            try { return (Color)ColorConverter.ConvertFromString(hex); }
            catch { return fallback; }
        }

        public static Color A(Color c, double alpha)
        {
            return Color.FromArgb((byte)Math.Max(0, Math.Min(255, alpha * 255)), c.R, c.G, c.B);
        }

        public static SolidColorBrush Solid(Color c)
        {
            var b = new SolidColorBrush(c); b.Freeze(); return b;
        }

        public static readonly FontFamily UiFont = new FontFamily("Segoe UI Variable Text, Segoe UI");

        /// <summary>Синий акцент переключателей (как в macOS), настраивается в конфиге.</summary>
        public static Color Accent
        {
            get { return Parse(ConfigStore.Current.Glass.Accent, Color.FromRgb(0x0A, 0x84, 0xFF)); }
        }
    }

    /// <summary>Цвета интерфейса в зависимости от светлой/тёмной темы стекла.</summary>
    public static class Ui
    {
        public static bool Light { get { return ConfigStore.Current.Glass.Light; } }
        public static Color Fg { get { return Light ? Color.FromRgb(0x1D, 0x1D, 0x1F) : Colors.White; } }
        public static Brush FgBrush { get { return Theme.Solid(Fg); } }
        public static Brush DimBrush { get { return Theme.Solid(Theme.A(Fg, 0.58)); } }
        public static Brush CardBrush { get { return Theme.Solid(Light ? Color.FromArgb(120, 255, 255, 255) : Color.FromArgb(28, 255, 255, 255)); } }
        public static Brush CircleOffBrush { get { return Theme.Solid(Light ? Color.FromArgb(34, 0, 0, 0) : Color.FromArgb(44, 255, 255, 255)); } }
        public static Brush TrackBrush { get { return Theme.Solid(Light ? Color.FromArgb(150, 255, 255, 255) : Color.FromArgb(40, 255, 255, 255)); } }
    }

    /// <summary>Пружина: даёт то самое «упругое» движение macOS.</summary>
    public class Spring
    {
        public double Value, Target, Velocity;
        public double Stiffness, Damping;

        public Spring(double v, double stiffness = 320, double damping = 24)
        {
            Value = Target = v; Stiffness = stiffness; Damping = damping;
        }

        /// <summary>true — ещё движется.</summary>
        public bool Step(double dt)
        {
            double a = -Stiffness * (Value - Target) - Damping * Velocity;
            Velocity += a * dt;
            Value += Velocity * dt;
            if (Math.Abs(Value - Target) < 0.04 && Math.Abs(Velocity) < 0.6)
            {
                Value = Target; Velocity = 0; return false;
            }
            return true;
        }
    }

    /// <summary>Крутит пружины только пока они движутся — в простое ноль нагрузки.</summary>
    public class SpringGroup
    {
        readonly System.Collections.Generic.List<Spring> _springs = new System.Collections.Generic.List<Spring>();
        readonly Action _onFrame;
        bool _on;
        long _last;

        public SpringGroup(Action onFrame) { _onFrame = onFrame; }

        public Spring Add(double v, double stiffness = 320, double damping = 24)
        {
            var s = new Spring(v, stiffness, damping);
            _springs.Add(s);
            return s;
        }

        public void Kick()
        {
            if (_on) return;
            _on = true;
            _last = System.Diagnostics.Stopwatch.GetTimestamp();
            CompositionTarget.Rendering += Tick;
        }

        void Tick(object sender, EventArgs e)
        {
            long now = System.Diagnostics.Stopwatch.GetTimestamp();
            double dt = (now - _last) / (double)System.Diagnostics.Stopwatch.Frequency;
            _last = now;
            if (dt > 1.0 / 30) dt = 1.0 / 30;
            if (dt <= 0) return;

            bool any = false;
            foreach (var s in _springs) if (s.Step(dt)) any = true;
            _onFrame();
            if (!any)
            {
                CompositionTarget.Rendering -= Tick;
                _on = false;
            }
        }
    }

    /// <summary>
    /// Матовое стекло как в macOS: размытые обои → светлая (или тёмная) подложка → мягкий блик →
    /// тонкая тёмная кромка снаружи и светлая внутри. Контент кладётся в Body.
    /// </summary>
    public class GlassPanel : Grid
    {
        public Grid Body { get; }

        readonly ImageBrush _img;
        readonly Border _blur, _wash, _sheen, _edgeOut, _edgeIn;

        public GlassPanel(double radius)
        {
            var cr = new CornerRadius(radius);
            _img = new ImageBrush { ViewboxUnits = BrushMappingMode.Absolute, Stretch = Stretch.Fill };
            _blur = Layer(cr);
            _wash = Layer(cr);
            _sheen = Layer(cr);
            _edgeOut = Layer(cr); _edgeOut.BorderThickness = new Thickness(1);
            _edgeIn = Layer(new CornerRadius(Math.Max(0, radius - 1)));
            _edgeIn.Margin = new Thickness(1); _edgeIn.BorderThickness = new Thickness(1);

            Children.Add(_blur); Children.Add(_wash); Children.Add(_sheen);
            Children.Add(_edgeIn); Children.Add(_edgeOut);
            Body = new Grid();
            Children.Add(Body);

            Apply();
            SizeChanged += (s, e) => UpdateView();
            Loaded += (s, e) => { ConfigStore.Changed += Apply; Apply(); UpdateView(); };
            Unloaded += (s, e) => ConfigStore.Changed -= Apply;
        }

        static Border Layer(CornerRadius cr)
        {
            return new Border { CornerRadius = cr, IsHitTestVisible = false };
        }

        public void Apply()
        {
            var g = ConfigStore.Current.Glass;
            bool light = g.Light;
            bool haveImg = g.WallpaperBlur && Wallpaper.Blurred != null;

            if (haveImg) { _img.ImageSource = Wallpaper.Blurred; _blur.Background = _img; }
            else _blur.Background = null;

            Color wash = light ? Color.FromRgb(0xF4, 0xF4, 0xF8) : Color.FromRgb(0x1C, 0x1C, 0x24);
            double a = haveImg ? Math.Min(0.92, g.Opacity + (light ? 0.14 : 0.12)) : (light ? 0.86 : 0.88);
            _wash.Background = Theme.Solid(Theme.A(wash, a));

            var sheen = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
            sheen.GradientStops.Add(new GradientStop(Theme.A(Colors.White, light ? 0.30 : 0.10), 0));
            sheen.GradientStops.Add(new GradientStop(Theme.A(Colors.White, 0), 0.55));
            sheen.Freeze();
            _sheen.Background = sheen;

            _edgeOut.BorderBrush = Theme.Solid(Theme.A(Colors.Black, light ? 0.18 : 0.55));
            _edgeIn.BorderBrush = Theme.Solid(Theme.A(Colors.White, light ? 0.55 : 0.20));
        }

        void UpdateView()
        {
            if (_img.ImageSource == null || ActualWidth < 2 || ActualHeight < 2) return;
            var w = Window.GetWindow(this);
            if (w == null) return;
            try
            {
                Point p = TranslatePoint(new Point(0, 0), w);
                _img.Viewbox = new Rect(w.Left + p.X, w.Top + p.Y, ActualWidth, ActualHeight);
            }
            catch { }
        }

        /// <summary>Вызывать, если окно с панелью переместили.</summary>
        public void Reposition() { UpdateView(); }
    }
}
