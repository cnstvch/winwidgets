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

            for (int i = 3; i < px.Length; i += 4) px[i] = 255;

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

        /// <summary>Акцентный цвет системы (берём у Windows, чтобы панель выглядела родной).</summary>
        public static Color Accent
        {
            get
            {
                try
                {
                    var c = new Windows.UI.ViewManagement.UISettings()
                        .GetColorValue(Windows.UI.ViewManagement.UIColorType.Accent);
                    return Color.FromRgb(c.R, c.G, c.B);
                }
                catch { return Color.FromRgb(0x3B, 0x8B, 0xFF); }
            }
        }
    }

    /// <summary>
    /// «Жидкое стекло» без шейдеров: размытые обои → тонировка → блик сверху →
    /// световой ободок с разной яркостью по диагонали → тонкая внутренняя кромка.
    /// Контент кладётся в Body.
    /// </summary>
    public class GlassPanel : Grid
    {
        public Grid Body { get; }

        readonly double _radius;
        readonly ImageBrush _img;
        readonly Border _blur, _tint, _sheen, _glint, _rim, _inner;

        public GlassPanel(double radius)
        {
            _radius = radius;
            var cr = new CornerRadius(radius);

            _img = new ImageBrush { ViewboxUnits = BrushMappingMode.Absolute, Stretch = Stretch.Fill };
            _blur = Layer(cr);
            _tint = Layer(cr);
            _sheen = Layer(cr);
            _glint = Layer(cr);
            _rim = Layer(cr); _rim.BorderThickness = new Thickness(1.2);
            _inner = Layer(new CornerRadius(Math.Max(0, radius - 1.2)));
            _inner.Margin = new Thickness(1.2); _inner.BorderThickness = new Thickness(1);

            Children.Add(_blur); Children.Add(_tint); Children.Add(_sheen);
            Children.Add(_glint); Children.Add(_inner); Children.Add(_rim);
            Body = new Grid();
            Children.Add(Body);

            Apply();
            SizeChanged += (s, e) => UpdateView();
            Loaded += (s, e) => { ConfigStore.Changed += Apply; Apply(); UpdateView(); };
            Unloaded += (s, e) => ConfigStore.Changed -= Apply;
        }

        static Border Layer(CornerRadius cr)
        {
            return new Border { CornerRadius = cr, IsHitTestVisible = false, SnapsToDevicePixels = false };
        }

        public void Apply()
        {
            var g = ConfigStore.Current.Glass;
            var tint = Theme.Parse(g.Tint, Color.FromRgb(0x14, 0x16, 0x1F));
            var white = Colors.White;

            if (g.WallpaperBlur && Wallpaper.Blurred != null)
            {
                _img.ImageSource = Wallpaper.Blurred;
                _blur.Background = _img;
            }
            else _blur.Background = null;

            _tint.Background = Theme.Solid(Theme.A(tint, g.TintOpacity));

            var sheen = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
            sheen.GradientStops.Add(new GradientStop(Theme.A(white, g.Sheen), 0));
            sheen.GradientStops.Add(new GradientStop(Theme.A(white, g.Sheen * 0.22), 0.35));
            sheen.GradientStops.Add(new GradientStop(Theme.A(white, 0), 0.65));
            sheen.GradientStops.Add(new GradientStop(Theme.A(white, g.Sheen * 0.30), 1));
            _sheen.Background = sheen;

            var glint = new RadialGradientBrush
            {
                Center = new Point(0.12, 0), GradientOrigin = new Point(0.12, 0),
                RadiusX = 0.55, RadiusY = 0.9
            };
            glint.GradientStops.Add(new GradientStop(Theme.A(white, g.Sheen * 0.9), 0));
            glint.GradientStops.Add(new GradientStop(Theme.A(white, 0), 1));
            _glint.Background = glint;

            var rim = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 1) };
            rim.GradientStops.Add(new GradientStop(Theme.A(white, 0.95 * g.Rim), 0));
            rim.GradientStops.Add(new GradientStop(Theme.A(white, 0.14 * g.Rim), 0.32));
            rim.GradientStops.Add(new GradientStop(Theme.A(white, 0.06 * g.Rim), 0.68));
            rim.GradientStops.Add(new GradientStop(Theme.A(white, 0.60 * g.Rim), 1));
            _rim.BorderBrush = rim;

            var inner = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
            inner.GradientStops.Add(new GradientStop(Theme.A(white, 0.16 * g.Rim), 0));
            inner.GradientStops.Add(new GradientStop(Theme.A(white, 0.02), 0.5));
            inner.GradientStops.Add(new GradientStop(Theme.A(white, 0.08 * g.Rim), 1));
            _inner.BorderBrush = inner;
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
    }
}
