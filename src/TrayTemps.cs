using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace TrayTemps
{
    internal sealed class Reading
    {
        public double Cpu = double.NaN;
        public double Gpu = double.NaN;
        public double Disk = double.NaN;
        public double Board = double.NaN;
        public double CpuWatts = double.NaN;
        public double GpuWatts = double.NaN;
        public double DiskWatts = double.NaN;
    }

    internal sealed class SensorReader : IDisposable
    {
        private readonly Type _computerType;
        private readonly object _computer;
        private readonly MethodInfo _open;
        private readonly MethodInfo _close;

        public string OpenError = null;

        public SensorReader()
        {
            var lib = Assembly.LoadFrom(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "LibreHardwareMonitorLib.dll"));
            _computerType = lib.GetType("LibreHardwareMonitor.Hardware.Computer", true);
            _computer = Activator.CreateInstance(_computerType);
            Set("IsCpuEnabled", true);
            Set("IsGpuEnabled", true);
            Set("IsMotherboardEnabled", true);
            Set("IsStorageEnabled", true);
            Set("IsMemoryEnabled", false);
            _open = _computerType.GetMethod("Open");
            _close = _computerType.GetMethod("Close");
            try { _open.Invoke(_computer, null); }
            catch (Exception error) { OpenError = error.InnerException != null ? error.InnerException.Message : error.Message; }
        }

        private void Set(string property, bool value)
        {
            _computerType.GetProperty(property).SetValue(_computer, value, null);
        }

        public Reading Read()
        {
            var reading = new Reading();
            try
            {
                var collection = (System.Collections.IEnumerable)_computerType.GetProperty("Hardware").GetValue(_computer, null);
                foreach (var hardware in collection)
                {
                    Update(hardware);
                    var type = hardware.GetType().GetProperty("HardwareType").GetValue(hardware, null).ToString();
                    Apply(reading, type, Temperatures(hardware), Powers(hardware));

                    var subProperty = hardware.GetType().GetProperty("SubHardware");
                    if (subProperty == null) continue;
                    foreach (var sub in (System.Collections.IEnumerable)subProperty.GetValue(hardware, null))
                    {
                        Update(sub);
                        var subType = sub.GetType().GetProperty("HardwareType").GetValue(sub, null).ToString();
                        Apply(reading, subType, Temperatures(sub), Powers(sub));
                    }
                }
            }
            catch
            {
                // transient sensor failure keeps the previous frame
            }
            if (double.IsNaN(reading.Board))
            {
                var acpi = AcpiZone();
                if (!double.IsNaN(acpi)) reading.Board = acpi;
            }
            if (double.IsNaN(reading.Gpu)) reading.Gpu = NvidiaTemperature();
            if (double.IsNaN(reading.Disk)) reading.Disk = StorageTemperature();
            if (double.IsNaN(reading.GpuWatts)) reading.GpuWatts = NvidiaPower();
            return reading;
        }

        private static void Update(object hardware)
        {
            var update = hardware.GetType().GetMethod("Update");
            if (update != null) update.Invoke(hardware, null);
        }

        private static List<KeyValuePair<string, double>> Temperatures(object hardware)
        {
            var list = new List<KeyValuePair<string, double>>();
            var sensors = (System.Collections.IEnumerable)hardware.GetType().GetProperty("Sensors").GetValue(hardware, null);
            foreach (var sensor in sensors)
            {
                if (sensor.GetType().GetProperty("SensorType").GetValue(sensor, null).ToString() != "Temperature") continue;
                var valueObject = sensor.GetType().GetProperty("Value").GetValue(sensor, null);
                if (valueObject == null) continue;
                var value = Convert.ToDouble(valueObject);
                if (double.IsNaN(value) || value <= 0 || value > 150) continue;
                var name = (string)sensor.GetType().GetProperty("Name").GetValue(sensor, null);
                list.Add(new KeyValuePair<string, double>(name, value));
            }
            return list;
        }

        private static List<KeyValuePair<string, double>> Powers(object hardware)
        {
            var list = new List<KeyValuePair<string, double>>();
            var sensors = (System.Collections.IEnumerable)hardware.GetType().GetProperty("Sensors").GetValue(hardware, null);
            foreach (var sensor in sensors)
            {
                if (sensor.GetType().GetProperty("SensorType").GetValue(sensor, null).ToString() != "Power") continue;
                var valueObject = sensor.GetType().GetProperty("Value").GetValue(sensor, null);
                if (valueObject == null) continue;
                var value = Convert.ToDouble(valueObject);
                if (double.IsNaN(value) || value <= 0 || value > 1000) continue;
                var name = (string)sensor.GetType().GetProperty("Name").GetValue(sensor, null);
                list.Add(new KeyValuePair<string, double>(name, value));
            }
            return list;
        }

        private static void Apply(Reading reading, string type, List<KeyValuePair<string, double>> temperatures, List<KeyValuePair<string, double>> powers)
        {
            if (temperatures.Count == 0) return;
            if (type.IndexOf("Cpu", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                double best = double.NaN;
                foreach (var pair in temperatures)
                {
                    var key = pair.Key.ToLowerInvariant();
                    if (key.Contains("package") || key.Contains("core max")) { best = pair.Value; break; }
                    if (key.Contains("core average") && double.IsNaN(best)) best = pair.Value;
                }
                if (double.IsNaN(best))
                    foreach (var pair in temperatures) if (double.IsNaN(best) || pair.Value > best) best = pair.Value;
                if (!double.IsNaN(best)) reading.Cpu = best;
                foreach (var pair in powers)
                {
                    if (pair.Key.ToLowerInvariant().Contains("package")) { reading.CpuWatts = pair.Value; break; }
                }
                if (double.IsNaN(reading.CpuWatts) && powers.Count > 0) reading.CpuWatts = powers[0].Value;
                return;
            }
            if (type.IndexOf("Gpu", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                foreach (var pair in temperatures)
                {
                    if (pair.Key.ToLowerInvariant().Contains("core")) { reading.Gpu = pair.Value; return; }
                }
                if (double.IsNaN(reading.Gpu)) reading.Gpu = temperatures[0].Value;
                foreach (var pair in powers)
                {
                    var key = pair.Key.ToLowerInvariant();
                    if (key.Contains("power") || key.Contains("package")) { reading.GpuWatts = pair.Value; break; }
                }
                return;
            }
            if (type.IndexOf("Storage", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                if (double.IsNaN(reading.Disk)) reading.Disk = temperatures[0].Value;
                if (powers.Count > 0 && double.IsNaN(reading.DiskWatts)) reading.DiskWatts = powers[0].Value;
                return;
            }
            if (type.IndexOf("Motherboard", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                if (double.IsNaN(reading.Board)) reading.Board = temperatures[0].Value;
            }
        }

        private static double _diskCacheValue = double.NaN;
        private static DateTime _diskCacheAt = DateTime.MinValue;

        internal static double NvidiaPower()
        {
            try
            {
                var info = new ProcessStartInfo("nvidia-smi", "--query-gpu=power.draw --format=csv,noheader,nounits")
                {
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true,
                };
                using (var process = Process.Start(info))
                {
                    var text = process.StandardOutput.ReadToEnd().Trim();
                    process.WaitForExit(4000);
                    double value;
                    if (double.TryParse(text.Split('\n')[0].Trim(), out value) && value > 0) return value;
                }
            }
            catch { }
            return double.NaN;
        }

        internal static double StorageTemperature()
        {
            if ((DateTime.UtcNow - _diskCacheAt).TotalSeconds < 30 && !double.IsNaN(_diskCacheValue)) return _diskCacheValue;
            var file = Path.Combine(Path.GetTempPath(), "traytemps-disk.txt");
            try
            {
                if (File.Exists(file)) File.Delete(file);
                var script = "(Get-PhysicalDisk | Get-StorageReliabilityCounter | Where-Object { $_.Temperature -gt 0 } | Select-Object -First 1).Temperature | Set-Content -Path '" + file + "' -Encoding ASCII";
                var info = new ProcessStartInfo("powershell.exe", "-NoProfile -NonInteractive -WindowStyle Hidden -Command \"" + script + "\"")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                using (var process = Process.Start(info))
                {
                    process.WaitForExit(8000);
                }
                if (File.Exists(file))
                {
                    var text = File.ReadAllText(file).Trim();
                    double value;
                    if (double.TryParse(text, out value) && value > 0 && value < 150)
                    {
                        _diskCacheValue = value;
                        _diskCacheAt = DateTime.UtcNow;
                        return value;
                    }
                }
            }
            catch (Exception error) { Program.Log("storage: " + error.Message); }
            return _diskCacheValue;
        }

        internal static double AcpiZone()
        {
            try
            {
                using (var searcher = new System.Management.ManagementObjectSearcher(@"root\WMI", "SELECT CurrentTemperature FROM MSAcpi_ThermalZoneTemperature"))
                {
                    foreach (var item in searcher.Get())
                        return Math.Round(Convert.ToDouble(item["CurrentTemperature"]) / 10.0 - 273.15, 0);
                }
            }
            catch { }
            return double.NaN;
        }

        internal static double NvidiaTemperature()
        {
            try
            {
                var info = new ProcessStartInfo("nvidia-smi", "--query-gpu=temperature.gpu --format=csv,noheader,nounits")
                {
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true,
                };
                using (var process = Process.Start(info))
                {
                    var text = process.StandardOutput.ReadToEnd().Trim();
                    process.WaitForExit(4000);
                    double value;
                    if (double.TryParse(text.Split('\n')[0].Trim(), out value)) return value;
                }
            }
            catch { }
            return double.NaN;
        }

        public void Reopen()
        {
            try { _close.Invoke(_computer, null); } catch { }
            try { _open.Invoke(_computer, null); } catch { }
        }

        public string Inventory()
        {
            var sb = new System.Text.StringBuilder();
            try
            {
                var collection = (System.Collections.IEnumerable)_computerType.GetProperty("Hardware").GetValue(_computer, null);
                foreach (var hw in collection)
                {
                    Update(hw);
                    var type = hw.GetType().GetProperty("HardwareType").GetValue(hw, null).ToString();
                    var name = (string)hw.GetType().GetProperty("Name").GetValue(hw, null);
                    var temps = Temperatures(hw);
                    sb.Append(type).Append(" | ").Append(name).Append(" | ");
                    foreach (var pair in temps) sb.Append(pair.Key).Append('=').Append(pair.Value.ToString("0")).Append(' ');
                    sb.AppendLine();
                }
            }
            catch (Exception error) { sb.AppendLine("ERR " + error.Message); }
            return sb.ToString();
        }

        public void Dispose()
        {
            try { if (_close != null) _close.Invoke(_computer, null); } catch { }
        }

        public static bool IsElevated()
        {
            using (var identity = System.Security.Principal.WindowsIdentity.GetCurrent())
                return new System.Security.Principal.WindowsPrincipal(identity).IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
        }
    }

    internal static class Native
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct RECT { public int Left, Top, Right, Bottom; }

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT { public int X, Y; public POINT(int x, int y) { X = x; Y = y; } }

        [StructLayout(LayoutKind.Sequential)]
        public struct SIZE { public int Width, Height; public SIZE(int w, int h) { Width = w; Height = h; } }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct BLENDFUNCTION
        {
            public byte BlendOp;
            public byte BlendFlags;
            public byte SourceConstantAlpha;
            public byte AlphaFormat;
        }

        public const int WS_EX_LAYERED = 0x00080000;
        public const int WS_EX_TRANSPARENT = 0x00000020;
        public const int WS_EX_TOOLWINDOW = 0x00000080;
        public const int WS_EX_NOACTIVATE = 0x08000000;
        public const int ULW_ALPHA = 0x00000002;
        public const byte AC_SRC_OVER = 0x00;
        public const byte AC_SRC_ALPHA = 0x01;

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern IntPtr FindWindow(string className, string windowName);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern IntPtr FindWindowEx(IntPtr parent, IntPtr child, string className, string windowName);

        [DllImport("user32.dll")]
        public static extern bool GetWindowRect(IntPtr handle, out RECT rect);

        [DllImport("user32.dll")]
        public static extern IntPtr GetDC(IntPtr handle);

        [DllImport("user32.dll")]
        public static extern int ReleaseDC(IntPtr handle, IntPtr dc);

        [DllImport("gdi32.dll")]
        public static extern IntPtr CreateCompatibleDC(IntPtr dc);

        [DllImport("gdi32.dll")]
        public static extern bool DeleteDC(IntPtr dc);

        [DllImport("gdi32.dll")]
        public static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);

        [DllImport("gdi32.dll")]
        public static extern bool DeleteObject(IntPtr obj);

        public delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr module, uint threadId);

        [DllImport("user32.dll")]
        public static extern bool UnhookWindowsHookEx(IntPtr hook);

        [DllImport("user32.dll")]
        public static extern IntPtr CallNextHookEx(IntPtr hook, int nCode, IntPtr wParam, IntPtr lParam);

        public const int WH_MOUSE_LL = 14;
        public const int WM_LBUTTONDOWN = 0x0201;
        public const int WM_RBUTTONDOWN = 0x0204;

        [StructLayout(LayoutKind.Sequential)]
        public struct POINTLL { public int X, Y; }

        [StructLayout(LayoutKind.Sequential)]
        public struct MSLLHOOKSTRUCT
        {
            public POINTLL Point;
            public uint MouseData;
            public uint Flags;
            public uint Time;
            public IntPtr ExtraInfo;
        }

        [DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern int GetClassName(IntPtr handle, System.Text.StringBuilder name, int max);

        [DllImport("user32.dll")]
        public static extern IntPtr MonitorFromWindow(IntPtr handle, int flags);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFO info);

        [DllImport("user32.dll")]
        public static extern bool SetWindowPos(IntPtr handle, IntPtr insertAfter, int x, int y, int width, int height, int flags);

        [StructLayout(LayoutKind.Sequential)]
        public struct MONITORINFO
        {
            public int Size;
            public RECT Monitor;
            public RECT Work;
            public int Flags;
        }

        public static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        public const int SWP_NOMOVE = 0x0002;
        public const int SWP_NOSIZE = 0x0001;
        public const int SWP_NOACTIVATE = 0x0010;

        [DllImport("user32.dll")]
        public static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr dstDc, ref POINT dstPoint, ref SIZE size,
            IntPtr srcDc, ref POINT srcPoint, int colorKey, ref BLENDFUNCTION blend, int flags);
    }


    internal sealed class PowerPopup : Form
    {
        public PowerPopup()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            Size = new Size(220, 26);
        }

        protected override CreateParams CreateParams
        {
            get
            {
                var parameters = base.CreateParams;
                parameters.ExStyle |= Native.WS_EX_LAYERED | Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE;
                return parameters;
            }
        }

        private static GraphicsPath RoundedRect(int width, int height, int radius)
        {
            var path = new GraphicsPath();
            var d = radius * 2;
            path.AddArc(0, 0, d, d, 180, 90);
            path.AddArc(width - d, 0, d, d, 270, 90);
            path.AddArc(width - d, height - d, d, d, 0, 90);
            path.AddArc(0, height - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        public void ShowText(string text, Rectangle anchor)
        {
            using (var family = new FontFamily("Segoe UI"))
            using (var font = new Font(family, 9f, FontStyle.Regular, GraphicsUnit.Point))
            using (var probe = Graphics.FromHwnd(IntPtr.Zero))
            {
                probe.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                var measured = probe.MeasureString(text, font);
                var width = (int)Math.Ceiling(measured.Width) + 22;
                var height = 26;

                var workingArea = Screen.PrimaryScreen.WorkingArea;
                var x = anchor.Right - width;
                if (x + width > workingArea.Right) x = workingArea.Right - width;
                if (x < workingArea.Left) x = workingArea.Left;
                var y = anchor.Top - height - 6;
                if (y < workingArea.Top) y = anchor.Bottom + 6;

                using (var bitmap = new Bitmap(width, height, PixelFormat.Format32bppPArgb))
                using (var graphics = Graphics.FromImage(bitmap))
                {
                    graphics.Clear(Color.Transparent);
                    graphics.SmoothingMode = SmoothingMode.AntiAlias;
                    graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

                    using (var path = RoundedRect(width - 1, height - 1, 7))
                    using (var fill = new SolidBrush(Color.FromArgb(232, 22, 22, 26)))
                    using (var edge = new Pen(Color.FromArgb(70, 255, 255, 255)))
                    {
                        graphics.FillPath(fill, path);
                        graphics.DrawPath(edge, path);
                    }

                    using (var brush = new SolidBrush(Color.FromArgb(245, 245, 247)))
                        graphics.DrawString(text, font, brush, 11, (height - measured.Height) / 2f);

                    Location = new Point(x, y);
                    ApplyLayered(bitmap);
                }
            }

            Visible = true;
            Native.SetWindowPos(Handle, Native.HWND_TOPMOST, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
        }

        private void ApplyLayered(Bitmap bitmap)
        {
            var screenDc = Native.GetDC(IntPtr.Zero);
            var memoryDc = Native.CreateCompatibleDC(screenDc);
            var hBitmap = IntPtr.Zero;
            var oldBitmap = IntPtr.Zero;
            try
            {
                hBitmap = bitmap.GetHbitmap(Color.FromArgb(0));
                oldBitmap = Native.SelectObject(memoryDc, hBitmap);
                var size = new Native.SIZE(bitmap.Width, bitmap.Height);
                var source = new Native.POINT(0, 0);
                var blend = new Native.BLENDFUNCTION
                {
                    BlendOp = Native.AC_SRC_OVER,
                    BlendFlags = 0,
                    SourceConstantAlpha = 255,
                    AlphaFormat = Native.AC_SRC_ALPHA,
                };
                var destination = new Native.POINT(Location.X, Location.Y);
                Native.UpdateLayeredWindow(Handle, screenDc, ref destination, ref size, memoryDc, ref source, 0, ref blend, Native.ULW_ALPHA);
            }
            finally
            {
                Native.ReleaseDC(IntPtr.Zero, screenDc);
                if (hBitmap != IntPtr.Zero) Native.DeleteObject(hBitmap);
                if (oldBitmap != IntPtr.Zero) Native.SelectObject(memoryDc, oldBitmap);
                Native.DeleteDC(memoryDc);
            }
        }
    }

    internal sealed class Program : Form
    {
        private readonly SensorReader _reader;
        private readonly Timer _timer;
        private Timer _topTimer;
        private Native.HookProc _mouseHook;
        private IntPtr _hookHandle = IntPtr.Zero;
        private readonly NotifyIcon _tray;
        private IntPtr _trayIconHandle = IntPtr.Zero;

        private const int PanelHeight = 24;
        private const int ShadowOffset = 1;

        public Program()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            Size = new Size(320, PanelHeight);

            try
            {
                _reader = new SensorReader();
                if (_reader.OpenError != null) Log("open: " + _reader.OpenError);
            }
            catch (Exception error)
            {
                _reader = null;
                Log("reader: " + error);
            }

            _timer = new Timer { Interval = 2000 };
            _timer.Tick += (sender, args) => Refresh2();
            _timer.Start();

            // keeps the panel above the taskbar (it is a topmost window too) and,
            // when enabled, above borderless fullscreen games
            _topTimer = new Timer { Interval = 300 };
            _topTimer.Tick += (sender, args) =>
            {
                if (!Visible) return;
                if (!_alwaysOnTop && IsFullscreenForeground()) return;
                Native.SetWindowPos(Handle, Native.HWND_TOPMOST, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
            };
            _topTimer.Start();

            _tray = new NotifyIcon
            {
                Icon = SystemIcons.Information,
                Text = "\u0422\u0435\u043c\u043f\u0435\u0440\u0430\u0442\u0443\u0440\u044b",
                Visible = true,
                ContextMenuStrip = BuildMenu(),
            };
            _tray.DoubleClick += (sender, args) => ToggleVisible();
            LoadSettings();
            _popup = new PowerPopup();
            MouseEnter += (sender, args) => ShowPower();
            MouseLeave += (sender, args) => HidePower();
            _menu = BuildPanelMenu();
            _menu.Closed += (sender, args) =>
            {
                if (_hookHandle != IntPtr.Zero)
                {
                    Native.UnhookWindowsHookEx(_hookHandle);
                    _hookHandle = IntPtr.Zero;
                }
                Refresh2();
            };
            MouseClick += (sender, args) =>
            {
                if (args.Button == MouseButtons.Right) TogglePanelMenu();
            };

            Refresh2();
            Timer probe = new Timer { Interval = 6000 };
            probe.Tick += (sender, args) =>
            {
                probe.Stop();
                if (_reader != null && double.IsNaN(_last.Disk)) Log("inventory:" + Environment.NewLine + _reader.Inventory());
            };
            probe.Start();
        }

        private static string Watts(double value)
        {
            return double.IsNaN(value) ? "\u2014" : value.ToString("0.0") + " \u0412\u0442";
        }

        private string BuildPowerLine()
        {
            var reading = _lastReading;
            var parts = new List<string> { "\u0426\u041f " + Watts(reading.CpuWatts) };
            if (!double.IsNaN(reading.GpuWatts)) parts.Add("\u0413\u041f " + Watts(reading.GpuWatts));
            if (!double.IsNaN(reading.DiskWatts)) parts.Add("\u0414\u0418\u0421\u041a " + Watts(reading.DiskWatts));
            var sum = 0.0;
            var count = 0;
            foreach (var value in new[] { reading.CpuWatts, reading.GpuWatts, reading.DiskWatts })
            {
                if (double.IsNaN(value)) continue;
                sum += value;
                count++;
            }
            if (count > 1) parts.Add("\u2211 " + sum.ToString("0.0") + " \u0412\u0442");
            return string.Join("   ", parts.ToArray());
        }

        private void ShowPower()
        {
            if (_popup == null) return;
            _popup.ShowText(BuildPowerLine(), new Rectangle(Location, Size));
        }

        private void HidePower()
        {
            if (_popup != null) _popup.Hide();
        }

        private void TogglePanelMenu()
        {
            if (_menu.Visible) { ClosePanelMenu(); return; }

            var size = _menu.PreferredSize;
            var x = Math.Max(0, Math.Min(Location.X, Screen.PrimaryScreen.WorkingArea.Right - size.Width));
            var y = Location.Y - size.Height - 6;
            if (y < 0) y = Location.Y + Height + 6;
            _menu.Show(new Point(x, y));

            _mouseHook = MouseHookCallback;
            _hookHandle = Native.SetWindowsHookEx(Native.WH_MOUSE_LL, _mouseHook, IntPtr.Zero, 0);
        }

        private void ClosePanelMenu()
        {
            if (_hookHandle != IntPtr.Zero)
            {
                Native.UnhookWindowsHookEx(_hookHandle);
                _hookHandle = IntPtr.Zero;
            }
            if (_menu.Visible) _menu.Close();
        }

        private IntPtr MouseHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && _menu != null && _menu.Visible)
            {
                var message = wParam.ToInt32();
                if (message == Native.WM_LBUTTONDOWN || message == Native.WM_RBUTTONDOWN)
                {
                    var info = (Native.MSLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(Native.MSLLHOOKSTRUCT));
                    var point = new Point(info.Point.X, info.Point.Y);
                    if (!_menu.Bounds.Contains(point))
                        BeginInvoke((Action)ClosePanelMenu);
                }
            }
            return Native.CallNextHookEx(_hookHandle, nCode, wParam, lParam);
        }

        private ContextMenuStrip BuildPanelMenu()
        {
            var menu = new ContextMenuStrip();
            var top = new ToolStripMenuItem("Поверх всех окон") { CheckOnClick = true, Checked = _alwaysOnTop };
            top.CheckedChanged += (sender, args) =>
            {
                _alwaysOnTop = top.Checked;
                SaveSettings();
            };
            var startup = new ToolStripMenuItem("Запускать с Windows") { CheckOnClick = true, Checked = StartupShortcutExists() };
            startup.CheckedChanged += (sender, args) => SetStartupShortcut(startup.Checked);
            var hide = new ToolStripMenuItem("Скрыть");
            hide.Click += (sender, args) => ToggleVisible();
            var exit = new ToolStripMenuItem("Выход");
            exit.Click += (sender, args) => Close();

            menu.Items.Add(top);
            menu.Items.Add(startup);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(hide);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(exit);
            return menu;
        }

        private static string StartupLink()
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup), "TrayTemps.lnk");
        }

        private bool StartupShortcutExists()
        {
            return File.Exists(StartupLink());
        }

        private void SetStartupShortcut(bool enabled)
        {
            try
            {
                var link = StartupLink();
                if (!enabled)
                {
                    if (File.Exists(link)) File.Delete(link);
                    return;
                }
                var shellType = Type.GetTypeFromProgID("WScript.Shell");
                dynamic shell = Activator.CreateInstance(shellType);
                dynamic shortcut = shell.CreateShortcut(link);
                shortcut.TargetPath = Application.ExecutablePath;
                shortcut.WorkingDirectory = AppDomain.CurrentDomain.BaseDirectory;
                shortcut.Save();
            }
            catch (Exception error) { Log("startup: " + error.Message); }
        }

        private void LoadSettings()
        {
            try
            {
                if (!File.Exists(_settingsPath)) return;
                foreach (var line in File.ReadAllLines(_settingsPath))
                {
                    if (line.StartsWith("alwaysOnTop=")) _alwaysOnTop = line.EndsWith("1");
                }
            }
            catch { }
        }

        private void SaveSettings()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath));
                File.WriteAllText(_settingsPath, "alwaysOnTop=" + (_alwaysOnTop ? "1" : "0") + Environment.NewLine);
            }
            catch { }
        }

        private ContextMenuStrip BuildMenu()
        {
            var menu = new ContextMenuStrip();
            menu.Items.Add("\u041f\u043e\u043a\u0430\u0437\u0430\u0442\u044c / \u0441\u043a\u0440\u044b\u0442\u044c", null, (s, e) => ToggleVisible());
            menu.Items.Add("\u041e\u0431\u043d\u043e\u0432\u0438\u0442\u044c \u0441\u0435\u0439\u0447\u0430\u0441", null, (s, e) => Refresh2());
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("\u0412\u044b\u0445\u043e\u0434", null, (s, e) => Close());
            return menu;
        }

        private bool _hidden;
        private bool _alwaysOnTop;
        private ContextMenuStrip _menu;
        private PowerPopup _popup;
        private Reading _lastReading = new Reading();
        private readonly string _settingsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "dsh-tray-temps", "settings.txt");
        private readonly Reading _last = new Reading();
        private int _ticks;
        private string[] _lastLabels;
        private double[] _lastValues;
        private bool _reopened;
        private void ToggleVisible()
        {
            _hidden = !_hidden;
            Visible = !_hidden;
        }

        internal static void Log(string text)
        {
            try
            {
                var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "dsh-tray-temps");
                Directory.CreateDirectory(dir);
                File.AppendAllText(Path.Combine(dir, "error.log"), DateTime.Now.ToString("s") + "  " + text + Environment.NewLine);
            }
            catch { }
        }

        private static string Format(double value)
        {
            return double.IsNaN(value) ? "\u2014" : value.ToString("0") + "\u00b0";
        }

        private bool IsFullscreenForeground()
        {
            var foreground = Native.GetForegroundWindow();
            if (foreground == IntPtr.Zero || foreground == Handle) return false;
            Native.RECT bounds;
            if (!Native.GetWindowRect(foreground, out bounds)) return false;
            var monitor = Native.MonitorFromWindow(foreground, 2);
            var info = new Native.MONITORINFO { Size = Marshal.SizeOf(typeof(Native.MONITORINFO)) };
            if (!Native.GetMonitorInfo(monitor, ref info)) return false;
            if (bounds.Left > info.Monitor.Left || bounds.Top > info.Monitor.Top ||
                bounds.Right < info.Monitor.Right || bounds.Bottom < info.Monitor.Bottom) return false;
            var name = new System.Text.StringBuilder(256);
            Native.GetClassName(foreground, name, 256);
            var cls = name.ToString();
            return cls != "Progman" && cls != "WorkerW" && cls != "Shell_TrayWnd";
        }

        private void Refresh2()
        {
            try { RefreshCore(); }
            catch (Exception error) { Log("refresh: " + error); }
        }

        private void RefreshCore()
        {
            Reading reading;
            if (_reader == null)
            {
                reading = new Reading();
                reading.Gpu = SensorReader.NvidiaTemperature();
                reading.Board = SensorReader.AcpiZone();
                reading.Disk = SensorReader.StorageTemperature();
            }
            else
            {
                try { reading = _reader.Read(); }
                catch { reading = new Reading(); }
            }

            // keep the last good value when a sensor is asleep or momentarily unavailable
            if (double.IsNaN(reading.Cpu)) reading.Cpu = _last.Cpu; else _last.Cpu = reading.Cpu;
            if (double.IsNaN(reading.Gpu)) reading.Gpu = _last.Gpu; else _last.Gpu = reading.Gpu;
            if (double.IsNaN(reading.Disk)) reading.Disk = _last.Disk; else _last.Disk = reading.Disk;
            if (double.IsNaN(reading.Board)) reading.Board = _last.Board; else _last.Board = reading.Board;

            _ticks++;
            if (double.IsNaN(reading.Disk) && !_reopened && _ticks > 3)
            {
                _reopened = true;
                try { if (_reader != null) _reader.Reopen(); } catch { }
            }

            var values = new[] { reading.Cpu, reading.Gpu, reading.Disk, reading.Board };
            var labels = new[] { "\u0426\u041f", "\u0413\u041f", "\u0414\u0418\u0421\u041a", "\u041f\u041b\u0410\u0422\u0410" };
            var tooltip = string.Format("{0} {1}   {2} {3}   {4} {5}   {6} {7}",
                labels[0], Format(values[0]), labels[1], Format(values[1]), labels[2], Format(values[2]), labels[3], Format(values[3]));
            _tray.Text = tooltip.Length > 63 ? tooltip.Substring(0, 63) : tooltip;
            if (Visible == _hidden)
            {
                Log("visibility=" + !_hidden);
                Visible = !_hidden;
            }
            // the taskbar is a topmost window too: without a periodic re-assert it
            // covers the panel as soon as anything else gets clicked
            Native.SetWindowPos(Handle, Native.HWND_TOPMOST, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
            if (!Visible) return;

            _lastLabels = labels;
            _lastValues = values;
            _lastReading = reading;
            if (_popup != null && _popup.Visible) _popup.ShowText(BuildPowerLine(), new Rectangle(Location, Size));
            DrawPanel(labels, values);
            UpdateIcon(reading.Cpu);
        }

        private static Color ValueColor(double value)
        {
            if (double.IsNaN(value)) return Color.FromArgb(160, 160, 165);
            if (value >= 85) return Color.FromArgb(255, 110, 110);
            if (value >= 70) return Color.FromArgb(255, 190, 90);
            if (value >= 50) return Color.FromArgb(250, 250, 250);
            return Color.FromArgb(190, 230, 195);
        }

        private void DrawPanel(string[] labels, double[] values)
        {
            const float fontSize = 9.5f;
            using (var family = new FontFamily("Segoe UI"))
            using (var labelFont = new Font(family, fontSize, FontStyle.Regular, GraphicsUnit.Point))
            using (var valueFont = new Font(family, fontSize, FontStyle.Bold, GraphicsUnit.Point))
            using (var probe = Graphics.FromHwnd(IntPtr.Zero))
            {
                probe.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                var parts = new List<KeyValuePair<string, SizeF>>();
                float total = 0;
                for (var i = 0; i < labels.Length; i++)
                {
                    var label = labels[i] + " ";
                    var value = Format(values[i]);
                    var labelSize = probe.MeasureString(label, labelFont);
                    var valueSize = probe.MeasureString(value, valueFont);
                    parts.Add(new KeyValuePair<string, SizeF>("label:" + i, labelSize));
                    parts.Add(new KeyValuePair<string, SizeF>("value:" + i, valueSize));
                    total += labelSize.Width + valueSize.Width;
                    if (i < labels.Length - 1) total += probe.MeasureString("  ", labelFont).Width;
                }

                var width = (int)Math.Ceiling(total) + 8;
                var height = PanelHeight;

                using (var bitmap = new Bitmap(width, height, PixelFormat.Format32bppPArgb))
                using (var graphics = Graphics.FromImage(bitmap))
                {
                    graphics.Clear(Color.FromArgb(1, 0, 0, 0));
                    graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                    graphics.SmoothingMode = SmoothingMode.AntiAlias;

                    float x = 4;
                    var baseline = (height - labelFont.GetHeight(graphics)) / 2f;

                    Action<string, Font, Color> draw = (text, font, color) =>
                    {
                        using (var shadow = new SolidBrush(Color.FromArgb(140, 0, 0, 0)))
                            graphics.DrawString(text, font, shadow, x + ShadowOffset, baseline + ShadowOffset);
                        using (var brush = new SolidBrush(color))
                            graphics.DrawString(text, font, brush, x, baseline);
                        x += graphics.MeasureString(text, font).Width;
                    };

                    for (var i = 0; i < labels.Length; i++)
                    {
                        draw(labels[i] + " ", labelFont, Color.FromArgb(185, 185, 190));
                        draw(Format(values[i]), valueFont, ValueColor(values[i]));
                        if (i < labels.Length - 1) draw("  ", labelFont, Color.Transparent);
                    }

                    DockToTaskbar(width);
                    ApplyLayered(bitmap);
                }
            }
        }

        private void ApplyLayered(Bitmap bitmap)
        {
            var screenDc = Native.GetDC(IntPtr.Zero);
            var memoryDc = Native.CreateCompatibleDC(screenDc);
            var hBitmap = IntPtr.Zero;
            var oldBitmap = IntPtr.Zero;
            try
            {
                hBitmap = bitmap.GetHbitmap(Color.FromArgb(0));
                oldBitmap = Native.SelectObject(memoryDc, hBitmap);

                var size = new Native.SIZE(bitmap.Width, bitmap.Height);
                var source = new Native.POINT(0, 0);
                var blend = new Native.BLENDFUNCTION
                {
                    BlendOp = Native.AC_SRC_OVER,
                    BlendFlags = 0,
                    SourceConstantAlpha = 255,
                    AlphaFormat = Native.AC_SRC_ALPHA,
                };
                var destination = new Native.POINT(Location.X, Location.Y);
                Native.UpdateLayeredWindow(Handle, screenDc, ref destination, ref size, memoryDc, ref source, 0, ref blend, Native.ULW_ALPHA);
            }
            finally
            {
                Native.ReleaseDC(IntPtr.Zero, screenDc);
                if (hBitmap != IntPtr.Zero) Native.DeleteObject(hBitmap);
                if (oldBitmap != IntPtr.Zero) Native.SelectObject(memoryDc, oldBitmap);
                Native.DeleteDC(memoryDc);
            }
        }

        private void DockToTaskbar(int width)
        {
            var taskbar = Native.FindWindow("Shell_TrayWnd", null);
            if (taskbar == IntPtr.Zero) return;
            Native.RECT taskbarRect;
            if (!Native.GetWindowRect(taskbar, out taskbarRect)) return;

            var notifyHandle = Native.FindWindowEx(taskbar, IntPtr.Zero, "TrayNotifyWnd", null);
            Native.RECT notifyRect;
            if (notifyHandle == IntPtr.Zero || !Native.GetWindowRect(notifyHandle, out notifyRect))
                notifyRect = new Native.RECT { Left = taskbarRect.Right - 260 };

            var taskbarHeight = taskbarRect.Bottom - taskbarRect.Top;
            var y = taskbarRect.Top + Math.Max(0, (taskbarHeight - PanelHeight) / 2);
            var x = notifyRect.Left - width - 10;
            if (x < taskbarRect.Left + 8) x = taskbarRect.Left + 8;
            if (Location.X != x || Location.Y != y) Location = new Point(x, y);
        }

        private void UpdateIcon(double cpu)
        {
            try
            {
                using (var bitmap = new Bitmap(32, 32))
                using (var graphics = Graphics.FromImage(bitmap))
                {
                    graphics.SmoothingMode = SmoothingMode.AntiAlias;
                    graphics.Clear(Color.Transparent);
                    using (var brush = new SolidBrush(double.IsNaN(cpu) ? Color.Gray : (cpu >= 80 ? Color.IndianRed : Color.SeaGreen)))
                        graphics.FillEllipse(brush, 1, 1, 30, 30);
                    using (var font = new Font("Segoe UI", 13f, FontStyle.Bold, GraphicsUnit.Pixel))
                    using (var textBrush = new SolidBrush(Color.White))
                    {
                        var label = double.IsNaN(cpu) ? "?" : cpu.ToString("0");
                        var size = graphics.MeasureString(label, font);
                        graphics.DrawString(label, font, textBrush, (32 - size.Width) / 2f, (32 - size.Height) / 2f);
                    }
                    if (_trayIconHandle != IntPtr.Zero) Native.DeleteObject(_trayIconHandle);
                    var handle = bitmap.GetHicon();
                    _trayIconHandle = handle;
                    _tray.Icon = Icon.FromHandle(handle);
                }
            }
            catch { }
        }

        protected override void WndProc(ref Message message)
        {
            const int WM_PAINT = 0x000F;
            const int WM_DISPLAYCHANGE = 0x007E;
            const int WM_SETTINGCHANGE = 0x001A;
            base.WndProc(ref message);
            if ((message.Msg == WM_PAINT || message.Msg == WM_DISPLAYCHANGE || message.Msg == WM_SETTINGCHANGE)
                && _lastLabels != null && Visible)
            {
                try { DrawPanel(_lastLabels, _lastValues); } catch { }
            }
        }

        protected override CreateParams CreateParams
        {
            get
            {
                var parameters = base.CreateParams;
                parameters.ExStyle |= Native.WS_EX_LAYERED | Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE;
                return parameters;
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            _timer.Stop();
            if (_reader != null) _reader.Dispose();
            _tray.Visible = false;
            _tray.Dispose();
            if (_popup != null) _popup.Dispose();
            base.OnFormClosing(e);
        }

        [STAThread]
        private static void Main(string[] args)
        {
            if (!SensorReader.IsElevated() && Array.IndexOf(args, "--no-elevate") < 0)
            {
                try
                {
                    Process.Start(new ProcessStartInfo(Application.ExecutablePath, "--no-elevate") { Verb = "runas", UseShellExecute = true });
                    return;
                }
                catch
                {
                    MessageBox.Show("\u0414\u043b\u044f \u0447\u0442\u0435\u043d\u0438\u044f \u0434\u0430\u0442\u0447\u0438\u043a\u043e\u0432 \u043d\u0443\u0436\u0435\u043d \u0437\u0430\u043f\u0443\u0441\u043a \u043e\u0442 \u0430\u0434\u043c\u0438\u043d\u0438\u0441\u0442\u0440\u0430\u0442\u043e\u0440\u0430.", "TrayTemps", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new Program());
        }
    }
}
