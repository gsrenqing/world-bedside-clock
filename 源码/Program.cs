using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using Microsoft.Win32;

namespace WorldBedsideClock
{
    /// <summary>全局资源：嵌入字体 + 三色背景图</summary>
    public static class ResourceBox
    {
        public static Font FontDigital;   // 时分秒数字（digital display tfb.ttf）
        public static Font FontLed;       // 年份/日期
        public static Font FontAura;      // 中文点阵字库（Auraka點陣宋子集，含冒号）
        public static Font FontDate;      // 英文城市名/星期字母（date.ttf，区分大小写）
        public static HashSet<char> AuraChars; // 字库覆盖字符集（缺字→回退系统黑体）
        public static Image[] Themes = new Image[4]; // 1=Silver 2=Rose Gold 3=Black 4=Qinghua(青花瓷,默认)
        public static string LogPath = "";
        static List<IntPtr> _fontPtrs = new List<IntPtr>();

        // 每个字体独立一个 PrivateFontCollection：AddMemoryFont 多字体共享一个
        // pfc 时 Families 数组顺序不稳定（曾导致冒号/城市名拿到错误字体字形，
        // 渲染成大黑块）。独立 pfc 后每个 Font 固定取 Families[0]，顺序问题根治。
        static PrivateFontCollection _pfcDig, _pfcLed, _pfcAura, _pfcDate;

        public static void Log(string s)
        {
            try { File.AppendAllText(LogPath, s + "\r\n", Encoding.UTF8); }
            catch (Exception) { }
        }

        public static void Load()
        {
            _pfcDig = AddFontPfc("digital display tfb.ttf");
            _pfcLed = AddFontPfc("TEXASLED.TTF");
            _pfcAura = AddFontPfc("AuraDianZhenSong-2 subset.ttf");
            _pfcDate = AddFontPfc("date.ttf");
            Log("pfc dig=" + (_pfcDig != null) + " led=" + (_pfcLed != null) + " aura=" + (_pfcAura != null) + " date=" + (_pfcDate != null));
            if (_pfcDig != null && _pfcDig.Families.Length >= 1) FontDigital = new Font(_pfcDig.Families[0], 76f, FontStyle.Regular, GraphicsUnit.Pixel);
            if (_pfcLed != null && _pfcLed.Families.Length >= 1) FontLed = new Font(_pfcLed.Families[0], 36f, FontStyle.Regular, GraphicsUnit.Pixel);
            if (_pfcAura != null && _pfcAura.Families.Length >= 1) FontAura = new Font(_pfcAura.Families[0], 40f, FontStyle.Regular, GraphicsUnit.Pixel);
            if (_pfcDate != null && _pfcDate.Families.Length >= 1) FontDate = new Font(_pfcDate.Families[0], 36f, FontStyle.Regular, GraphicsUnit.Pixel);
            Log("fonts dig=" + (FontDigital != null) + " led=" + (FontLed != null) + " aura=" + (FontAura != null) + " date=" + (FontDate != null));
            AuraChars = LoadChars("aura_chars.txt");
            Themes[0] = PrepBg(LoadImg("silver.png"));
            Themes[1] = PrepBg(LoadImg("rosegold.png"));
            Themes[2] = PrepBg(LoadImg("black.png"));
            Themes[3] = PrepBg(LoadImg("qinghua.png"));
            Log("themes=" + (Themes[0] != null ? "1" : "0") + (Themes[1] != null ? ",1" : ",0") + (Themes[2] != null ? ",1" : ",0") + (Themes[3] != null ? ",1" : ",0"));
        }

        /// <summary>加载单个内嵌字体到独立 pfc，返回该 pfc（每个 pfc 仅一个字体）</summary>
        static PrivateFontCollection AddFontPfc(string res)
        {
            try
            {
                using (Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream(res))
                {
                    if (s == null) { Log("font null: " + res); return null; }
                    byte[] buf = new byte[s.Length];
                    int off = 0;
                    while (off < buf.Length)
                    {
                        int n = s.Read(buf, off, buf.Length - off);
                        if (n <= 0) break;
                        off += n;
                    }
                    PrivateFontCollection pfc = new PrivateFontCollection();
                    IntPtr mem = Marshal.AllocHGlobal(buf.Length);
                    Marshal.Copy(buf, 0, mem, buf.Length);
                    pfc.AddMemoryFont(mem, buf.Length);
                    _fontPtrs.Add(mem); // 保持内存有效，程序退出前不释放
                    Log("font ok: " + res + " bytes=" + buf.Length + " families=" + pfc.Families.Length);
                    return pfc;
                }
            }
            catch (Exception ex) { Log("font err: " + res + " " + ex.Message); return null; }
        }

        /// <summary>读取内嵌文本资源（UTF-8）</summary>
        public static string ReadText(string res)
        {
            try
            {
                using (Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream(res))
                {
                    if (s == null) { Log("text null: " + res); return ""; }
                    using (StreamReader r = new StreamReader(s, Encoding.UTF8))
                        return r.ReadToEnd();
                }
            }
            catch (Exception ex) { Log("text err: " + res + " " + ex.Message); return ""; }
        }

        /// <summary>读取内嵌字符集资源 → 字符集合（缺字回退黑体判断用）</summary>
        static HashSet<char> LoadChars(string res)
        {
            HashSet<char> set = new HashSet<char>();
            string t = ReadText(res);
            foreach (char c in t) set.Add(c);
            Log("aura_chars=" + set.Count);
            return set;
        }

        /// <summary>背景图预处理：alpha<128 的透明像素涂品红（配合 TransparencyKey 挖空实现真透明）；
        /// 半透明像素（128~254）alpha 强制为 255 保留原色，避免与品红底混合产生紫斑</summary>
        static Image PrepBg(Image img)
        {
            if (img == null) return null;
            Bitmap b = new Bitmap(img);
            try
            {
                Rectangle r = new Rectangle(0, 0, b.Width, b.Height);
                BitmapData d = b.LockBits(r, ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
                int stride = d.Stride;
                byte[] buf = new byte[stride * b.Height];
                System.Runtime.InteropServices.Marshal.Copy(d.Scan0, buf, 0, buf.Length);
                for (int i = 3; i < buf.Length; i += 4)
                {
                    byte a = buf[i];
                    if (a < 128) { buf[i] = 255; buf[i - 3] = 255; buf[i - 2] = 0; buf[i - 1] = 255; } // 品红
                    else if (a < 255) { buf[i] = 255; } // 去掉半透明，保留原色
                }
                System.Runtime.InteropServices.Marshal.Copy(buf, 0, d.Scan0, buf.Length);
                b.UnlockBits(d);
                return b;
            }
            catch (Exception ex)
            {
                Log("prepbg err: " + ex.Message);
                return null; // 宁可无背景，不返回底层流已释放、随时可能 GDI+ 报错的 img
            }
        }

        static Image LoadImg(string res)
        {
            try
            {
                using (Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream(res))
                {
                    if (s == null) { Log("img null: " + res); return null; }
                    // 像素级复制：流在 using 结束后即可安全释放，不留底层流依赖（BUG-4）
                    return new Bitmap(Image.FromStream(s));
                }
            }
            catch (Exception ex) { Log("img err: " + res + " " + ex.Message); return null; }
        }

        /// <summary>主屏亮绿色（数码管亮色）</summary>
        public static Color DigitColor { get { return Color.FromArgb(255, 236, 255, 243); } }
    }

    /// <summary>城市窗口配置条目（X/Y=-1 表示首次居中显示，拖拽后保存实际坐标）</summary>
    public class CityEntry
    {
        public string Name = "本地";
        public string TzId = "";
        public int X = -1;
        public int Y = -1;
        public int Theme = 4;          // 1=Silver 2=Rose Gold 3=Black 4=Qinghua(青花瓷,默认)
        public double Scale = 1.0;     // 窗口缩放 0.6~1.2
        public double Opacity = 1.0;
        public bool Topmost = true;
        public int Speed = 2;          // 字幕滚动速度 1~5（1 最慢 5 最快）
    }

    static class Program
    {
        public const int MaxCities = 8;
        public static List<ClockForm> Forms = new List<ClockForm>();
        public static List<CityEntry> Config = new List<CityEntry>();
        public static string IniPath;
        public static bool Exiting = false;
        // 彻底清除配置退出时置位：关闭链路跳过 SaveConfig，避免刚清掉的注册表被静默回写重建
        public static bool SuppressSaveOnClose = false;
        static System.Threading.Mutex _mutex;

        // 5.6.3：全局界面语言（"zh"默认 / "en"）；单窗口切换后全局所有窗口同步刷新
        public static string Lang = "zh";
        public static bool IsEn { get { return Lang == "en"; } }

        // 开机自启（VU 音量表同款机制）：注册表 HKCU\...\Run
        const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string RunValueName = "WorldBedsideClock";
        const string RegRootPath = @"Software\WorldBedsideClock";

        public static bool IsAutoStartEnabled()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKeyPath, false))
                {
                    if (key == null) return false;
                    return key.GetValue(RunValueName) != null;
                }
            }
            catch (Exception) { return false; }
        }

        public static void SetAutoStart(bool enable)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKeyPath, true))
                {
                    if (key == null) return;
                    if (enable) key.SetValue(RunValueName, "\"" + Application.ExecutablePath + "\"");
                    else key.DeleteValue(RunValueName, false);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show((IsEn ? "Failed to enable start with Windows: " : "设置开机自启动失败: ") + ex.Message,
                    "World bedside clock", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        /// <summary>读取用户自启意图（root\AutoStart，默认 1=曾启用）。与 Run 键实际状态分开持久化。</summary>
        public static bool GetAutoStartIntent()
        {
            try
            {
                using (RegistryKey root = Registry.CurrentUser.OpenSubKey(RegRootPath))
                    return root == null || Convert.ToString(root.GetValue("AutoStart", "1")) == "1";
            }
            catch (Exception) { return true; }
        }

        /// <summary>持久化用户自启意图；菜单切换自启时同步写入，避免下次启动被默认值覆盖。</summary>
        public static void SetAutoStartIntent(bool on)
        {
            try
            {
                using (RegistryKey root = Registry.CurrentUser.CreateSubKey(RegRootPath))
                    root.SetValue("AutoStart", on ? "1" : "0");
            }
            catch (Exception) { }
        }

        /// <summary>读取全局界面语言：用户手动切换过则沿用 root\Lang；
        /// 从未设置时按系统 UI 语言决定（中文环境 zh，其余 en）。</summary>
        public static string GetLang()
        {
            try
            {
                using (RegistryKey root = Registry.CurrentUser.OpenSubKey(RegRootPath))
                {
                    if (root != null)
                    {
                        string saved = Convert.ToString(root.GetValue("Lang", ""));
                        if (saved == "en" || saved == "zh") return saved;
                    }
                }
            }
            catch (Exception) { }
            return SystemDefaultLang();
        }

        /// <summary>系统 UI 语言：zh 开头（简体/繁体）视为中文，其余为英文。</summary>
        static string SystemDefaultLang()
        {
            try
            {
                return System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "zh" ? "zh" : "en";
            }
            catch (Exception) { return "zh"; }
        }

        /// <summary>持久化全局界面语言。</summary>
        public static void SetLang(string lang)
        {
            Lang = lang == "en" ? "en" : "zh";
            try
            {
                using (RegistryKey root = Registry.CurrentUser.CreateSubKey(RegRootPath))
                    root.SetValue("Lang", Lang);
            }
            catch (Exception) { }
        }

        /// <summary>5.6.3：在中英之间切换，并全局通知所有窗口刷新菜单/字幕/星期/文案。</summary>
        public static void ToggleLanguage()
        {
            SetLang(IsEn ? "zh" : "en");
            foreach (ClockForm f in Forms)
            {
                try { f.RefreshLanguage(); } catch (Exception) { }
            }
        }

        /// <summary>⑥ 季节规则键：5/1~10/31 为夏段(0)，11/1~次年4/30 为冬段(1)；跨年同段归并到前一年。</summary>
        static int CurrentSeasonKey()
        {
            DateTime n = DateTime.Now;
            int seg = (n.Month >= 5 && n.Month <= 10) ? 0 : 1;
            int y = (seg == 1 && n.Month <= 4) ? n.Year - 1 : n.Year;
            return y * 2 + seg;
        }

        /// <summary>⑥ 每年 5/1、11/1 之后的首次启动，重刷一次各窗口时区规则（无界面提示）；
        /// 其余时刻依赖 TimeZoneInfo 自动 DST，不内置夏令时日期表。</summary>
        public static void RefreshSeasonalTzIfNeeded()
        {
            try
            {
                int cur = CurrentSeasonKey();
                int saved = cur;
                using (RegistryKey root = Registry.CurrentUser.OpenSubKey(RegRootPath))
                {
                    if (root != null)
                        int.TryParse(Convert.ToString(root.GetValue("LastTzSeason", cur.ToString())), out saved);
                }
                if (saved == cur) return;
                foreach (ClockForm f in Forms) { try { f.RefreshTimeZone(); } catch (Exception) { } }
                using (RegistryKey root = Registry.CurrentUser.CreateSubKey(RegRootPath))
                    root.SetValue("LastTzSeason", cur.ToString());
            }
            catch (Exception) { }
        }

        [STAThread]
        static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            IniPath = Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), "World bedside clock.ini");
            ResourceBox.LogPath = Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), "wbc.log");
            try { File.Delete(ResourceBox.LogPath); } catch (Exception) { }
            Lang = GetLang();
            ResourceBox.Log("start v5.6.6 lang=" + Lang);
            ResourceBox.Load();
            // 自检模式：/cap <输出png> [城市名] [时间] [nobg] [scale=0.6~1.2]——离屏渲染固定时间一帧后退出
            if (args.Length >= 2 && args[0] == "/cap")
            {
                CityEntry ce = new CityEntry();
                ce.Name = args.Length >= 3 ? args[2] : "本地";
                ce.TzId = "";
                ClockForm f = new ClockForm(ce);
                if (args.Length >= 4)
                {
                    DateTime tt;
                    if (DateTime.TryParse(args[3], out tt)) f.TestNow = tt;
                }
                for (int i = 0; i < args.Length; i++)
                {
                    if (args[i] == "nobg") f.TestNoBg = true;
                    if (args[i] == "en") Lang = "en";
                    if (args[i].StartsWith("scale="))
                    {
                        double sc;
                        if (double.TryParse(args[i].Substring(6), System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out sc)) f.Entry.Scale = sc;
                    }
                    if (args[i].StartsWith("tz=")) f.Entry.TzId = args[i].Substring(3);
                    if (args[i].StartsWith("theme="))
                    {
                        int th;
                        if (int.TryParse(args[i].Substring(6), out th)) f.Entry.Theme = th;
                    }
                }
                f.RenderToFile(args[1]);
                return;
            }
            // 单实例互斥：开机自启与手动双击并存会启动多个进程、各自只保存自己的
            // 窗口配置并互相覆盖 ini（曾导致重启后仅剩一个实例且设定全丢）。
            // 第二个实例直接退出，窗口管理始终归首个进程。
            bool createdNew;
            _mutex = new System.Threading.Mutex(true, "WorldBedsideClock_SingleInstance", out createdNew);
            if (!createdNew)
            {
                MessageBox.Show(IsEn
                    ? "World bedside clock is already running.\nPlease operate in an open window."
                    : "World bedside clock 已在运行。\n请在已运行的窗口中操作。",
                    "World bedside clock", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            LoadConfig();
            EnsureDesktopWatcher(); // v5.6.2：显示桌面穿透看门狗（200ms 检测重升）
            // 自启偏好持久化到 root\AutoStart（默认 1）；仅用户曾启用时刷新 Run 键路径，
            // 不再每次启动强制把被用户关闭的自启重新打开（BUG-2）
            if (GetAutoStartIntent()) { try { SetAutoStart(true); } catch (Exception) { } }
            if (Config.Count == 0)
            {
                CityEntry ce = new CityEntry();
                ce.TzId = TimeZoneInfo.Local.Id;
                CreateWindow(ce);
            }
            else
            {
                foreach (CityEntry ce in Config) CreateWindow(ce);
            }
            // ⑥ 季节规则刷新：跨过 5/1、11/1 后的首次启动，重刷一次各窗口时区规则（窗口已就绪）
            RefreshSeasonalTzIfNeeded();
            Application.Idle += delegate(object s, EventArgs e) { if (Forms.Count == 0 && !Exiting) Application.ExitThread(); };
            Application.Run();
        }

        /// <summary>创建城市窗口（上限 8）。x/y 传 -1 表示居中于屏幕</summary>
        public static ClockForm CreateWindow(CityEntry ce)
        {
            if (Forms.Count >= MaxCities)
            {
                MessageBox.Show(IsEn
                    ? "A maximum of " + MaxCities + " city windows can be added; the limit has been reached."
                    : "最多只能添加 " + MaxCities + " 个城市窗口，已达到上限。",
                    "World bedside clock");
                return null;
            }
            ClockForm f = new ClockForm(ce);
            if (ce.X >= 0 && ce.Y >= 0)
            {
                f.StartPosition = FormStartPosition.Manual;
                f.Location = ClampToVisible(new Point(ce.X, ce.Y), f.ClientSize);
            }
            else
            {
                f.StartPosition = FormStartPosition.CenterScreen;
            }
            f.Show();
            // v5.6.2：桌面层窗口首帧落位到桌面正上方（无 owner 路线，主动落位）
            if (!ce.Topmost) ReassertAboveDesktop(f, true);
            Forms.Add(f);
            f.FormClosed += delegate(object s, FormClosedEventArgs e) { Forms.Remove(f); };
            // 添加/新建窗口后立即落盘，防止重启丢失配置
            SaveConfig();
            return f;
        }

        /// <summary>隐患-2：保存坐标若落在全部屏幕联合区域之外（如外接显示器拔掉），
        /// 钳制回主屏居中；至少保留 40px 可见余量。</summary>
        static Point ClampToVisible(Point saved, Size sz)
        {
            Rectangle union = Rectangle.Empty;
            foreach (Screen sc in Screen.AllScreens)
                union = union.IsEmpty ? sc.Bounds : Rectangle.Union(union, sc.Bounds);
            int maxX = union.Right - Math.Min(40, sz.Width);
            int maxY = union.Bottom - Math.Min(40, sz.Height);
            bool inside = saved.X >= union.Left && saved.X <= maxX && saved.Y >= union.Top && saved.Y <= maxY;
            if (inside) return saved;
            Screen primary = Screen.PrimaryScreen ?? Screen.AllScreens[0];
            Rectangle pb = primary.Bounds;
            return new Point(pb.Left + (pb.Width - sz.Width) / 2, pb.Top + (pb.Height - sz.Height) / 2);
        }

        /// <summary>窗口是否落在现存所有屏幕之外：与任一现存屏可见交集宽/高均不足 40px 即视为离屏。
        /// 用于外接显示器拔掉后，定位停留在已断开屏幕上、无法找回的窗口。</summary>
        static bool IsOffScreen(Point loc, Size sz)
        {
            Rectangle win = new Rectangle(loc, sz);
            foreach (Screen sc in Screen.AllScreens)
            {
                Rectangle inter = Rectangle.Intersect(win, sc.Bounds);
                if (inter.Width >= 40 && inter.Height >= 40) return false;
            }
            return true;
        }

        /// <summary>拔屏回收：把落在已断开显示器上的窗口拉回主屏，以主屏中心为轴垂直居中、
        /// 窗口之间留固定间距分开排列（不叠在同一点）；回收后同步坐标并落盘，避免下次启动又按旧离屏坐标恢复。</summary>
        static void RepatriateOffscreenWindows()
        {
            if (Forms.Count == 0) return;
            List<ClockForm> off = new List<ClockForm>();
            foreach (ClockForm f in Forms)
            {
                if (f == null || f.IsDisposed || !f.IsHandleCreated) continue;
                if (IsOffScreen(f.Location, f.Size)) off.Add(f);
            }
            if (off.Count == 0) return;

            Screen primary = Screen.PrimaryScreen ?? Screen.AllScreens[0];
            Rectangle pb = primary.Bounds;
            int cx = pb.Left + pb.Width / 2;
            int cy = pb.Top + pb.Height / 2;
            const int gap = 24;

            // 先退出最小化/异常状态并按 Entry.Scale 复位尺寸，防止拔屏期间系统把
            // 离屏窗口尺寸带偏、回主屏后显示成默认 100% 大小。
            foreach (ClockForm f in off)
            {
                if (f.WindowState != FormWindowState.Normal) f.WindowState = FormWindowState.Normal;
                f.RestoreSize();
            }
            int totalH = 0;
            foreach (ClockForm f in off) totalH += f.Size.Height;
            if (off.Count > 1) totalH += gap * (off.Count - 1);
            int y = cy - totalH / 2;

            foreach (ClockForm f in off)
            {
                int w = f.Size.Width;
                int h = f.Size.Height;
                int x = cx - w / 2;
                x = Math.Max(pb.Left, Math.Min(x, pb.Right - w));
                int ny = Math.Max(pb.Top, Math.Min(y, pb.Bottom - h));
                f.Location = new Point(x, ny);
                f.Entry.X = x;
                f.Entry.Y = ny;
                y += h + gap;
            }
            SaveConfig();
            ResourceBox.Log("repatriated " + off.Count + " off-screen window(s) to primary screen");
        }

        public static void SaveConfig()
        {
            if (SuppressSaveOnClose) return; // 清除配置退出：不回写注册表
            try
            {
                // 照抄 VU 音量表：配置持久化在注册表 HKCU\Software\WorldBedsideClock\Windows，
                // 每窗口一个子键 Window0..N；删除的窗口从键树清除，重启后按键树全量恢复
                using (RegistryKey root = Registry.CurrentUser.CreateSubKey(@"Software\WorldBedsideClock"))
                {
                    using (RegistryKey winKey = root.CreateSubKey("Windows"))
                    {
                        string[] old = winKey.GetSubKeyNames();
                        foreach (string s in old) winKey.DeleteSubKeyTree(s, false);
                        int i = 0;
                        foreach (ClockForm f in Forms)
                        {
                            CityEntry ce = f.Entry;
                            using (RegistryKey ck = winKey.CreateSubKey("Window" + i))
                            {
                                ck.SetValue("Name", ce.Name);
                                ck.SetValue("Tz", ce.TzId);
                                ck.SetValue("X", ce.X);
                                ck.SetValue("Y", ce.Y);
                                ck.SetValue("Theme", ce.Theme);
                                ck.SetValue("Scale", ce.Scale.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture));
                                ck.SetValue("Opacity", ce.Opacity.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture));
                                ck.SetValue("Topmost", ce.Topmost ? "1" : "0");
                                ck.SetValue("Speed", ce.Speed);
                            }
                            i++;
                        }
                    }
                }
            }
            catch (Exception) { }
        }

        static void LoadConfig()
        {
            try
            {
                using (RegistryKey root = Registry.CurrentUser.OpenSubKey(RegRootPath))
                {
                    if (root == null) return;
                    using (RegistryKey winKey = root.OpenSubKey("Windows"))
                    {
                        if (winKey == null) return;
                        string[] names = winKey.GetSubKeyNames();
                        Array.Sort(names);
                        foreach (string n in names)
                        {
                            using (RegistryKey ck = winKey.OpenSubKey(n))
                            {
                                if (ck == null) continue;
                                CityEntry ce = new CityEntry();
                                ce.Name = (string)ck.GetValue("Name", "本地");
                                ce.TzId = (string)ck.GetValue("Tz", "");
                                int t;
                                if (int.TryParse(Convert.ToString(ck.GetValue("X", -1)), out t)) ce.X = t;
                                if (int.TryParse(Convert.ToString(ck.GetValue("Y", -1)), out t)) ce.Y = t;
                                if (int.TryParse(Convert.ToString(ck.GetValue("Theme", 4)), out t))
                                {
                                    if (t < 1 || t > 4) t = 4; // 兼容旧版编号，越界回退默认青花瓷
                                    ce.Theme = t;
                                }
                                double d;
                                if (double.TryParse(Convert.ToString(ck.GetValue("Scale", "1.00")), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out d)) ce.Scale = d;
                                if (double.TryParse(Convert.ToString(ck.GetValue("Opacity", "1.00")), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out d)) ce.Opacity = d;
                                ce.Topmost = (Convert.ToString(ck.GetValue("Topmost", "1")) == "1");
                                int sp;
                                if (int.TryParse(Convert.ToString(ck.GetValue("Speed", 2)), out sp))
                                {
                                    if (sp < 1) sp = 1;
                                    if (sp > 5) sp = 5;
                                    ce.Speed = sp;
                                }
                                Config.Add(ce);
                            }
                        }
                    }
                }
            }
            catch (Exception) { Config.Clear(); }
        }

        // ================= v5.6.2：Win+D 穿透（无 owner + 显示桌面检测重升，移植自 VU Widgets 3.6.6） =================
        // 依据（2026-11 探针实测 + Raymond Chen 2024-10 文章）：
        //  1. 挂件窗口为无边框工具窗、无 WS_MINIMIZEBOX，Win+D 不会最小化它们（探针实测零消息）；
        //  2. "显示桌面"会把桌面窗口（Progman/WorkerW）抬升到普通窗口之上，把挂件盖住；
        //  3. 检测到"桌面在挂件之上"时，把桌面层窗口按固定顺序重升到桌面窗口正上方即可恢复可见；
        //  4. 不挂 SHELLDLL_DefView owner（灰色用法，花屏根因），也不挂顶层桌面窗口（无法随抬升的桌面走）。

        [DllImport("user32.dll")]
        static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll")]
        static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

        [DllImport("user32.dll")]
        static extern IntPtr FindWindowEx(IntPtr hwndParent, IntPtr hwndChildAfter, string lpszClass, string lpszWindow);

        [DllImport("user32.dll")]
        static extern IntPtr GetDesktopWindow();

        [DllImport("user32.dll")]
        static extern IntPtr GetTopWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        static extern IntPtr GetWindow(IntPtr hWnd, uint nCmd);

        [DllImport("user32.dll")]
        static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

        const int GWL_EXSTYLE = -20;
        const int GWL_STYLE = -16;
        const uint WS_EX_TOOLWINDOW = 0x00000080;
        const uint WS_EX_TOPMOST = 0x00000008;
        const uint WS_MINIMIZEBOX = 0x00020000;
        const uint GW_HWNDNEXT = 2;
        const uint GW_HWNDPREV = 3;
        // NOSIZE|NOMOVE|NOACTIVATE|NOOWNERZORDER|NOSENDCHANGING：纯 Z 序调整
        const uint SWP_ZFLAGS = 0x1 | 0x2 | 0x10 | 0x200 | 0x400;

        static Timer _desktopCheckTimer;

        /// <summary>启动 200ms 显示桌面检测看门狗（Main 初始化后调用一次）</summary>
        public static void EnsureDesktopWatcher()
        {
            if (_desktopCheckTimer != null) return;
            _desktopCheckTimer = new Timer();
            _desktopCheckTimer.Interval = 200;
            _desktopCheckTimer.Tick += delegate(object s, EventArgs e)
            {
                try { CheckDesktopWindows(); } catch { }
                // 5.6.4：拔屏后离屏窗口自动拉回主屏（200ms 轮询，UI 线程无需封送）
                try { RepatriateOffscreenWindows(); } catch { }
            };
            _desktopCheckTimer.Start();
        }

        /// <summary>Z 序名次：0 = 最顶层；-1 = 未找到</summary>
        static int ZRankOf(IntPtr target)
        {
            IntPtr h = GetTopWindow(IntPtr.Zero);
            int i = 0;
            while (h != IntPtr.Zero)
            {
                if (h == target) return i;
                h = GetWindow(h, GW_HWNDNEXT);
                i++;
                if (i > 10000) break;
            }
            return -1;
        }

        /// <summary>桌面系窗口（Progman / 各 WorkerW）中 Z 序最高者；找不到返回 IntPtr.Zero</summary>
        static IntPtr TopDesktopWindow()
        {
            IntPtr best = IntPtr.Zero;
            int bestRank = int.MaxValue;
            IntPtr progman = FindWindow("Progman", null);
            if (progman != IntPtr.Zero)
            {
                int r = ZRankOf(progman);
                if (r >= 0 && r < bestRank) { bestRank = r; best = progman; }
            }
            IntPtr desktop = GetDesktopWindow();
            IntPtr w = IntPtr.Zero;
            do
            {
                w = FindWindowEx(desktop, w, "WorkerW", null);
                if (w != IntPtr.Zero)
                {
                    int r = ZRankOf(w);
                    if (r >= 0 && r < bestRank) { bestRank = r; best = w; }
                }
            } while (w != IntPtr.Zero);
            return best;
        }

        static int DesktopTopRank()
        {
            IntPtr w = TopDesktopWindow();
            return w == IntPtr.Zero ? -1 : ZRankOf(w);
        }

        /// <summary>单窗口重升：插到"桌面窗口正上方"。
        /// force=false：仅当窗口被桌面盖住时才动作（看门狗轮询用）；
        /// force=true ：无条件落位（新建窗口 / 层级切换用）。</summary>
        public static void ReassertAboveDesktop(ClockForm form, bool force)
        {
            try
            {
                if (form == null || !form.IsHandleCreated) return;
                IntPtr desk = TopDesktopWindow();
                if (desk == IntPtr.Zero) return;
                int dRank = ZRankOf(desk);
                int myRank = ZRankOf(form.Handle);
                if (dRank < 0 || myRank < 0) return;
                if (!force && dRank >= myRank) return; // 桌面在窗口之下（未被盖住），无需处理
                IntPtr insertAfter = GetWindow(desk, GW_HWNDPREV); // 桌面正上方那扇
                if (insertAfter == IntPtr.Zero) return;
                // 插入点若已在置顶 band 内，跳过本轮（避免把挂件带成置顶），看门狗下轮重试
                if ((GetWindowLong(insertAfter, GWL_EXSTYLE) & WS_EX_TOPMOST) != 0) return;
                SetWindowPos(form.Handle, insertAfter, 0, 0, 0, 0, SWP_ZFLAGS);
            }
            catch { }
        }

        /// <summary>全体桌面层窗口检查 + 有序重升（看门狗每 200ms 调一次）。
        /// 多窗口按当前 Z 序"从底到顶"逐个插入同一插入点，保持彼此相对顺序。</summary>
        static void CheckDesktopWindows()
        {
            try
            {
                List<ClockForm> list = new List<ClockForm>();
                foreach (ClockForm w in Forms)
                {
                    if (w == null || w.Entry.Topmost || !w.IsHandleCreated || !w.Visible) continue;
                    list.Add(w);
                }
                if (list.Count == 0) return;

                int dRank = DesktopTopRank();
                if (dRank < 0) return;

                bool needFix = false;
                foreach (ClockForm w in list)
                {
                    int r = ZRankOf(w.Handle);
                    if (r >= 0 && dRank < r) { needFix = true; break; }
                }
                if (!needFix) return; // 桌面未抬升（常态），零开销直通

                list.Sort(delegate (ClockForm a, ClockForm b)
                {
                    return ZRankOf(b.Handle).CompareTo(ZRankOf(a.Handle)); // 从底到顶
                });
                IntPtr insertAfter = GetWindow(TopDesktopWindow(), GW_HWNDPREV);
                if (insertAfter == IntPtr.Zero) return;
                if ((GetWindowLong(insertAfter, GWL_EXSTYLE) & WS_EX_TOPMOST) != 0) return;
                foreach (ClockForm w in list)
                {
                    SetWindowPos(w.Handle, insertAfter, 0, 0, 0, 0, SWP_ZFLAGS);
                }
            }
            catch { }
        }

        /// <summary>右键"关闭全部窗口并清除所有配置"：抑制回写 → 删自启/注册表/ini/日志 → 关窗退出。</summary>
        public static void ClearAllAndExit()
        {
            try
            {
                Exiting = true;
                SuppressSaveOnClose = true;

                // 1. 移除开机自启 Run 值（不弹失败提示，清除流程静默执行）
                try
                {
                    using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKeyPath, true))
                    {
                        if (key != null) key.DeleteValue(RunValueName, false);
                    }
                }
                catch { }

                // 2. 删除注册表整树（窗口配置、全部记录）
                try { Registry.CurrentUser.DeleteSubKeyTree(RegRootPath, false); } catch { }

                // 3. 删除 ini 配置文件与日志
                try { if (!string.IsNullOrEmpty(IniPath) && File.Exists(IniPath)) File.Delete(IniPath); } catch { }
                try { if (!string.IsNullOrEmpty(ResourceBox.LogPath) && File.Exists(ResourceBox.LogPath)) File.Delete(ResourceBox.LogPath); } catch { }

                // 4. 关闭全部窗口（SuppressSaveOnClose 已置位，关闭链路不回写注册表）
                List<ClockForm> all = new List<ClockForm>(Forms);
                foreach (ClockForm f in all)
                {
                    try { if (f != null && !f.IsDisposed) f.Close(); } catch { }
                }
            }
            finally
            {
                try { if (_mutex != null) { _mutex.ReleaseMutex(); } } catch { }
                Application.Exit();
            }
        }
    }
}
