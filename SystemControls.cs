using System;
using System.Diagnostics;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using NAudio.CoreAudioApi;
using Windows.Devices.Radios;

namespace WinWidgets
{
    /// <summary>Громкость, яркость, Wi-Fi, Bluetooth. Всё, что может не поддерживаться, молча возвращает «нет».</summary>
    public static class SystemControls
    {
        [DllImport("kernel32.dll")] static extern uint GetOEMCP();

        // ---------- звук ----------
        public static float GetVolume()
        {
            try
            {
                using (var en = new MMDeviceEnumerator())
                using (var dev = en.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia))
                    return dev.AudioEndpointVolume.MasterVolumeLevelScalar;
            }
            catch { return -1; }
        }

        public static void SetVolume(float v)
        {
            try
            {
                using (var en = new MMDeviceEnumerator())
                using (var dev = en.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia))
                {
                    dev.AudioEndpointVolume.MasterVolumeLevelScalar = Math.Max(0f, Math.Min(1f, v));
                    if (v > 0.001f) dev.AudioEndpointVolume.Mute = false;
                }
            }
            catch { }
        }

        // ---------- яркость (ноутбуки/встроенные экраны) ----------
        public static int GetBrightness()
        {
            try
            {
                using (var s = new ManagementObjectSearcher(@"root\WMI", "SELECT CurrentBrightness FROM WmiMonitorBrightness"))
                    foreach (ManagementObject o in s.Get())
                        return Convert.ToInt32(o["CurrentBrightness"]);
            }
            catch { }
            return -1;
        }

        public static void SetBrightness(int percent)
        {
            try
            {
                percent = Math.Max(0, Math.Min(100, percent));
                using (var s = new ManagementObjectSearcher(@"root\WMI", "SELECT * FROM WmiMonitorBrightnessMethods"))
                    foreach (ManagementObject o in s.Get())
                        o.InvokeMethod("WmiSetBrightness", new object[] { (uint)1, (byte)percent });
            }
            catch { }
        }

        // ---------- радио ----------
        static async Task<Radio> FindRadio(RadioKind kind)
        {
            try
            {
                var access = await Radio.RequestAccessAsync();
                if (access != RadioAccessStatus.Allowed) return null;
                var all = await Radio.GetRadiosAsync();
                return all.FirstOrDefault(r => r.Kind == kind);
            }
            catch { return null; }
        }

        /// <summary>null — адаптера нет или нет доступа.</summary>
        public static async Task<bool?> GetRadioState(RadioKind kind)
        {
            var r = await FindRadio(kind);
            if (r == null) return null;
            return r.State == RadioState.On;
        }

        public static async Task<bool?> ToggleRadio(RadioKind kind)
        {
            var r = await FindRadio(kind);
            if (r == null) return null;
            bool turnOn = r.State != RadioState.On;
            await r.SetStateAsync(turnOn ? RadioState.On : RadioState.Off);
            return turnOn;
        }

        /// <summary>Имя сети Wi-Fi через netsh (null — не подключено).</summary>
        public static Task<string> GetSsid()
        {
            return Task.Run(() =>
            {
                try
                {
                    Encoding enc;
                    try { enc = Encoding.GetEncoding((int)GetOEMCP()); } catch { enc = Encoding.UTF8; }
                    var psi = new ProcessStartInfo("netsh", "wlan show interfaces")
                    {
                        RedirectStandardOutput = true,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        StandardOutputEncoding = enc,
                    };
                    using (var p = Process.Start(psi))
                    {
                        string text = p.StandardOutput.ReadToEnd();
                        p.WaitForExit(2000);
                        foreach (var raw in text.Split('\n'))
                        {
                            string line = raw.Trim();
                            if (line.StartsWith("SSID", StringComparison.Ordinal) && !line.StartsWith("SSID BSSID"))
                            {
                                int i = line.IndexOf(':');
                                if (i > 0) { var v = line.Substring(i + 1).Trim(); if (v.Length > 0) return v; }
                            }
                        }
                    }
                }
                catch { }
                return null;
            });
        }

        // ---------- запуск ----------
        public static void OpenUri(string uri)
        {
            try { Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true }); } catch { }
        }
    }
}
