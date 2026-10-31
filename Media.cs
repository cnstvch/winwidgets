using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Windows.Media.Control;

namespace WinWidgets
{
    public class MediaInfo
    {
        public bool HasSession;
        public bool Playing;
        public string Title = "";
        public string Artist = "";
        public ImageSource Thumb;
        public Color Accent = Color.FromRgb(0xFF, 0x7A, 0xA8);
        public double Duration;                 // сек
        public double PosBase;                  // сек на момент PosStamp
        public DateTimeOffset PosStamp = DateTimeOffset.UtcNow;

        public double Position
        {
            get
            {
                double p = PosBase;
                if (Playing) p += (DateTimeOffset.UtcNow - PosStamp).TotalSeconds;
                if (Duration > 0 && p > Duration) p = Duration;
                return p < 0 ? 0 : p;
            }
        }
    }

    /// <summary>Любой плеер Windows (Spotify, браузер, Яндекс.Музыка...) через системный медиа-API.</summary>
    public static class MediaService
    {
        public static MediaInfo Info { get; private set; } = new MediaInfo();
        public static event Action Changed;

        static GlobalSystemMediaTransportControlsSessionManager _mgr;
        static GlobalSystemMediaTransportControlsSession _s;

        public static async void Init()
        {
            try
            {
                _mgr = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
                _mgr.CurrentSessionChanged += (m, a) => Attach();
                Attach();
            }
            catch { }
        }

        static void Attach()
        {
            if (_s != null)
            {
                _s.MediaPropertiesChanged -= OnEvt;
                _s.PlaybackInfoChanged -= OnEvt;
                _s.TimelinePropertiesChanged -= OnEvt;
            }
            _s = _mgr?.GetCurrentSession();
            if (_s != null)
            {
                _s.MediaPropertiesChanged += OnEvt;
                _s.PlaybackInfoChanged += OnEvt;
                _s.TimelinePropertiesChanged += OnEvt;
            }
            _ = Refresh();
        }

        static void OnEvt(GlobalSystemMediaTransportControlsSession s, object a) { _ = Refresh(); }

        static async Task Refresh()
        {
            var info = new MediaInfo();
            try
            {
                var s = _s;
                if (s != null)
                {
                    info.HasSession = true;
                    var props = await s.TryGetMediaPropertiesAsync();
                    info.Title = props.Title ?? "";
                    info.Artist = props.Artist ?? "";

                    var pb = s.GetPlaybackInfo();
                    info.Playing = pb != null &&
                        pb.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;

                    var tl = s.GetTimelineProperties();
                    if (tl != null)
                    {
                        info.Duration = (tl.EndTime - tl.StartTime).TotalSeconds;
                        info.PosBase = tl.Position.TotalSeconds;
                        info.PosStamp = tl.LastUpdatedTime;
                    }

                    if (props.Thumbnail != null)
                    {
                        using (var rs = await props.Thumbnail.OpenReadAsync())
                        using (var st = System.IO.WindowsRuntimeStreamExtensions.AsStream(rs))
                        {
                            var ms = new MemoryStream();
                            await st.CopyToAsync(ms);
                            ms.Position = 0;
                            var bi = new BitmapImage();
                            bi.BeginInit();
                            bi.StreamSource = ms;
                            bi.DecodePixelWidth = 128;
                            bi.CacheOption = BitmapCacheOption.OnLoad;
                            bi.EndInit();
                            bi.Freeze();
                            info.Thumb = bi;
                            info.Accent = AccentOf(bi);
                        }
                    }
                }
            }
            catch { }

            var app = Application.Current;
            if (app == null) return;
            app.Dispatcher.BeginInvoke(new Action(() =>
            {
                Info = info;
                Changed?.Invoke();
            }));
        }

        /// <summary>Средний «живой» цвет обложки (с упором на насыщенные пиксели).</summary>
        static Color AccentOf(BitmapSource bmp)
        {
            try
            {
                var fc = new FormatConvertedBitmap(bmp, PixelFormats.Bgra32, null, 0);
                int w = fc.PixelWidth, h = fc.PixelHeight;
                var px = new byte[w * h * 4];
                fc.CopyPixels(px, w * 4, 0);
                double r = 0, g = 0, b = 0, wsum = 0;
                for (int i = 0; i < px.Length; i += 16)   // каждый 4-й пиксель
                {
                    double B = px[i], G = px[i + 1], R = px[i + 2];
                    double mx = Math.Max(R, Math.Max(G, B)), mn = Math.Min(R, Math.Min(G, B));
                    double sat = mx <= 0 ? 0 : (mx - mn) / mx;
                    double wt = 0.05 + sat * sat * (mx / 255.0);
                    r += R * wt; g += G * wt; b += B * wt; wsum += wt;
                }
                if (wsum <= 0) return Color.FromRgb(0xFF, 0x7A, 0xA8);
                r /= wsum; g /= wsum; b /= wsum;
                // подтянуть яркость, чтобы акцент читался на чёрном
                double m = Math.Max(r, Math.Max(g, b));
                if (m < 170 && m > 1) { double k = 170 / m; r = Math.Min(255, r * k); g = Math.Min(255, g * k); b = Math.Min(255, b * k); }
                return Color.FromRgb((byte)r, (byte)g, (byte)b);
            }
            catch { return Color.FromRgb(0xFF, 0x7A, 0xA8); }
        }

        public static void Toggle() { try { _s?.TryTogglePlayPauseAsync(); } catch { } }
        public static void Next() { try { _s?.TrySkipNextAsync(); } catch { } }
        public static void Prev() { try { _s?.TrySkipPreviousAsync(); } catch { } }
    }
}
