using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace WinWidgets
{
    public struct OpenWindow
    {
        public IntPtr Hwnd;
        public string Exe;
        public string Title;
    }

    public static class Native
    {
        // ---------- константы ----------
        public const int GWL_EXSTYLE = -20;
        public const int WS_EX_TOOLWINDOW = 0x00000080;
        public const int WS_EX_NOACTIVATE = 0x08000000;
        public const int WS_EX_APPWINDOW = 0x00040000;
        public static readonly IntPtr HWND_BOTTOM = new IntPtr(1);
        public static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        public const uint SWP_NOSIZE = 0x0001, SWP_NOMOVE = 0x0002, SWP_NOACTIVATE = 0x0010, SWP_SHOWWINDOW = 0x0040;
        public const int SW_RESTORE = 9;
        public const uint GW_OWNER = 4;
        public const int DWMWA_CLOAKED = 14;
        public const uint MOD_ALT = 1, MOD_CONTROL = 2, MOD_NOREPEAT = 0x4000;
        public const int WM_HOTKEY = 0x0312;
        public const byte VK_LWIN = 0x5B, VK_SHIFT = 0x10, VK_TAB = 0x09, VK_S = 0x53, VK_P = 0x50;
        public const uint KEYEVENTF_KEYUP = 2;

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT { public int Left, Top, Right, Bottom; }

        [StructLayout(LayoutKind.Sequential)]
        struct MONITORINFO { public int cbSize; public RECT rcMonitor, rcWork; public uint dwFlags; }

        [StructLayout(LayoutKind.Sequential)]
        struct APPBARDATA
        {
            public int cbSize; public IntPtr hWnd; public uint uCallbackMessage; public uint uEdge;
            public RECT rc; public IntPtr lParam;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct SIZE { public int cx, cy; }

        delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        // ---------- user32 ----------
        [DllImport("user32.dll")] static extern bool EnumWindows(EnumWindowsProc cb, IntPtr lParam);
        [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
        [DllImport("user32.dll")] static extern bool IsIconic(IntPtr h);
        [DllImport("user32.dll")] static extern int GetWindowTextLength(IntPtr h);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr h, StringBuilder sb, int max);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr h, StringBuilder sb, int max);
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
        [DllImport("user32.dll")] static extern IntPtr GetWindow(IntPtr h, uint cmd);
        [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr h, int idx);
        [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr h, int idx, int val);
        [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr h);
        [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr h, int cmd);
        [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
        [DllImport("user32.dll")] static extern IntPtr MonitorFromWindow(IntPtr h, uint flags);
        [DllImport("user32.dll")] static extern bool GetMonitorInfo(IntPtr m, ref MONITORINFO mi);
        [DllImport("user32.dll")] static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
        [DllImport("user32.dll")] public static extern bool RegisterHotKey(IntPtr h, int id, uint mods, uint vk);
        [DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr h, int id);

        // ---------- прочее ----------
        [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr h, int attr, out int val, int size);
        [DllImport("kernel32.dll")] static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);
        [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        static extern bool QueryFullProcessImageName(IntPtr h, uint flags, StringBuilder sb, ref int size);
        [DllImport("shell32.dll")] static extern UIntPtr SHAppBarMessage(uint msg, ref APPBARDATA data);
        [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr h);

        [ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IShellItemImageFactory
        {
            [PreserveSig] int GetImage(SIZE size, int flags, out IntPtr phbm);
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
        static extern void SHCreateItemFromParsingName(string path, IntPtr pbc, ref Guid riid, out IShellItemImageFactory ppv);

        // ---------- окна ----------
        /// <summary>Скрыть из Alt+Tab и не отбирать фокус.</summary>
        public static void MakeToolWindow(Window w, bool noActivate)
        {
            var h = new WindowInteropHelper(w).Handle;
            int ex = GetWindowLong(h, GWL_EXSTYLE);
            ex |= WS_EX_TOOLWINDOW;
            ex &= ~WS_EX_APPWINDOW;
            if (noActivate) ex |= WS_EX_NOACTIVATE;
            SetWindowLong(h, GWL_EXSTYLE, ex);
        }

        public static void SendToBottom(Window w)
        {
            var h = new WindowInteropHelper(w).Handle;
            SetWindowPos(h, HWND_BOTTOM, 0, 0, 0, 0, SWP_NOSIZE | SWP_NOMOVE | SWP_NOACTIVATE);
        }

        public static void KeepTopmost(Window w)
        {
            var h = new WindowInteropHelper(w).Handle;
            SetWindowPos(h, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOSIZE | SWP_NOMOVE | SWP_NOACTIVATE);
        }

        public static void ActivateWindow(IntPtr h)
        {
            if (IsIconic(h)) ShowWindow(h, SW_RESTORE);
            SetForegroundWindow(h);
        }

        public static string ProcessPath(uint pid)
        {
            IntPtr p = OpenProcess(0x1000 /*QUERY_LIMITED_INFORMATION*/, false, pid);
            if (p == IntPtr.Zero) return null;
            try
            {
                var sb = new StringBuilder(1024);
                int size = sb.Capacity;
                return QueryFullProcessImageName(p, 0, sb, ref size) ? sb.ToString() : null;
            }
            finally { CloseHandle(p); }
        }

        /// <summary>Окна, которые показал бы Alt+Tab (без своих собственных).</summary>
        public static List<OpenWindow> GetOpenWindows()
        {
            var list = new List<OpenWindow>();
            uint self = (uint)Environment.ProcessId;
            EnumWindows((h, l) =>
            {
                if (!IsWindowVisible(h)) return true;
                if (GetWindow(h, GW_OWNER) != IntPtr.Zero) return true;
                int ex = GetWindowLong(h, GWL_EXSTYLE);
                if ((ex & WS_EX_TOOLWINDOW) != 0 && (ex & WS_EX_APPWINDOW) == 0) return true;
                if (DwmGetWindowAttribute(h, DWMWA_CLOAKED, out int cloaked, 4) == 0 && cloaked != 0) return true;
                int len = GetWindowTextLength(h);
                if (len == 0) return true;
                GetWindowThreadProcessId(h, out uint pid);
                if (pid == self) return true;
                var sb = new StringBuilder(len + 1);
                GetWindowText(h, sb, sb.Capacity);
                string exe = ProcessPath(pid);
                if (string.IsNullOrEmpty(exe)) return true;
                list.Add(new OpenWindow { Hwnd = h, Exe = exe, Title = sb.ToString() });
                return true;
            }, IntPtr.Zero);
            return list;
        }

        /// <summary>Передний план — полноэкранное приложение (игра, видео)?</summary>
        public static bool IsForegroundFullscreen()
        {
            IntPtr h = GetForegroundWindow();
            if (h == IntPtr.Zero) return false;
            var cls = new StringBuilder(64);
            GetClassName(h, cls, cls.Capacity);
            string c = cls.ToString();
            if (c == "Progman" || c == "WorkerW" || c == "Shell_TrayWnd") return false;
            GetWindowThreadProcessId(h, out uint pid);
            if (pid == (uint)Environment.ProcessId) return false;
            if (!GetWindowRect(h, out RECT r)) return false;
            IntPtr mon = MonitorFromWindow(h, 2 /*NEAREST*/);
            var mi = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
            if (!GetMonitorInfo(mon, ref mi)) return false;
            return r.Left <= mi.rcMonitor.Left && r.Top <= mi.rcMonitor.Top &&
                   r.Right >= mi.rcMonitor.Right && r.Bottom >= mi.rcMonitor.Bottom;
        }

        // ---------- таскбар Windows ----------
        public static void SetTaskbarAutoHide(bool hide)
        {
            var d = new APPBARDATA { cbSize = Marshal.SizeOf<APPBARDATA>(), lParam = (IntPtr)(hide ? 1 : 0) };
            SHAppBarMessage(0x0000000A /*ABM_SETSTATE*/, ref d);
        }

        // ---------- клавиши ----------
        public static void Chord(params byte[] keys)
        {
            foreach (var k in keys) keybd_event(k, 0, 0, UIntPtr.Zero);
            for (int i = keys.Length - 1; i >= 0; i--) keybd_event(keys[i], 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
        }

        // ---------- HBITMAP -> BitmapSource с сохранением альфы ----------
        [StructLayout(LayoutKind.Sequential)]
        struct BITMAP
        {
            public int bmType, bmWidth, bmHeight, bmWidthBytes;
            public ushort bmPlanes, bmBitsPixel;
            public IntPtr bmBits;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct BITMAPINFOHEADER
        {
            public int biSize, biWidth, biHeight;
            public short biPlanes, biBitCount;
            public int biCompression, biSizeImage, biXPelsPerMeter, biYPelsPerMeter, biClrUsed, biClrImportant;
        }

        [DllImport("gdi32.dll")] static extern int GetObject(IntPtr h, int cb, out BITMAP bmp);
        [DllImport("gdi32.dll")]
        static extern int GetDIBits(IntPtr hdc, IntPtr hbm, uint start, uint lines, byte[] bits, ref BITMAPINFOHEADER bi, uint usage);
        [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr h);
        [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr h, IntPtr dc);

        static BitmapSource HBitmapToSource(IntPtr hbm)
        {
            if (GetObject(hbm, Marshal.SizeOf<BITMAP>(), out BITMAP bm) == 0) return null;
            int w = bm.bmWidth, h = Math.Abs(bm.bmHeight);
            if (w <= 0 || h <= 0) return null;

            var bi = new BITMAPINFOHEADER
            {
                biSize = Marshal.SizeOf<BITMAPINFOHEADER>(),
                biWidth = w,
                biHeight = -h,          // сверху вниз
                biPlanes = 1,
                biBitCount = 32,
                biCompression = 0,
            };
            var bytes = new byte[w * h * 4];
            IntPtr dc = GetDC(IntPtr.Zero);
            int got;
            try { got = GetDIBits(dc, hbm, 0, (uint)h, bytes, ref bi, 0); }
            finally { ReleaseDC(IntPtr.Zero, dc); }
            if (got == 0) return null;

            bool anyAlpha = false;
            for (int i = 3; i < bytes.Length; i += 4) if (bytes[i] != 0) { anyAlpha = true; break; }
            if (!anyAlpha) for (int i = 3; i < bytes.Length; i += 4) bytes[i] = 255;

            var src = BitmapSource.Create(w, h, 96, 96, PixelFormats.Pbgra32, null, bytes, w * 4);
            src.Freeze();
            return src;
        }

        // ---------- иконки ----------
        /// <summary>Иконка файла/ярлыка в высоком разрешении (через оболочку).</summary>
        public static ImageSource GetIcon(string path, int px)
        {
            try
            {
                Guid iid = new Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b");
                SHCreateItemFromParsingName(path, IntPtr.Zero, ref iid, out IShellItemImageFactory f);
                var size = new SIZE { cx = px, cy = px };
                int hr = f.GetImage(size, 0x4 /*ICONONLY*/ | 0x1 /*BIGGERSIZEOK*/, out IntPtr hbm);
                Marshal.ReleaseComObject(f);
                if (hr == 0 && hbm != IntPtr.Zero)
                {
                    try
                    {
                        var src = HBitmapToSource(hbm);
                        if (src != null) return src;
                    }
                    finally { DeleteObject(hbm); }
                }
            }
            catch { }

            try
            {
                using (var ic = System.Drawing.Icon.ExtractAssociatedIcon(path))
                {
                    if (ic == null) return null;
                    var src = Imaging.CreateBitmapSourceFromHIcon(ic.Handle, Int32Rect.Empty,
                        BitmapSizeOptions.FromEmptyOptions());
                    src.Freeze();
                    return src;
                }
            }
            catch { return null; }
        }
    }
}
