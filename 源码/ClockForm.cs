using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace WorldBedsideClock
{
    /// <summary>单个城市时钟窗口（520x126 基准）。
    /// 透明实现：背景图透明区预涂品红色 + TransparencyKey 挖空（真透明，兼容各显卡）。
    /// 整窗透明度用 Form.Opacity。</summary>
    public class ClockForm : Form
    {
        public CityEntry Entry;
        public DateTime? TestNow;   // 自检用固定时间
        public bool TestNoBg;       // 自检用：跳过背景图（纯文字渲染）
        TimeZoneInfo _tz;
        Timer _timer;
        string _cityName = "本地";
        bool _isLocal;            // 本地时钟：额外前置中文星期段
        Point _dragStart;
        bool _dragging;
        ContextMenuStrip _menu;
        ToolStripMenuItem _miSilver, _miRoseGold, _miBlack, _miQinghua, _miTop, _miAuto;
        ToolStripMenuItem[] _miSize, _miOp, _miSpeed;
        // 5.6.3：顶级菜单项字段，供语言切换后批量刷新文案
        ToolStripMenuItem _miStyle, _miSizeParent, _miOpParent, _miSpd, _miAdd, _miSet,
            _miHelp, _miAbout, _miClose, _miClearAll, _miLang;

        // ---- 5.5 字幕横滚 ----
        class Seg { public string T; public int Kind; public float W; public Seg(string t, int k, float w) { T = t; Kind = k; W = w; } }
        List<Seg> _scrollSegs;
        float _scrollUnitW;
        string _scrollKey;
        Font _fcCity, _feCity;   // 城市名滚动字体缓存（中文点阵宋 / 英文 date.ttf 按高调整）
        Font _feFallback;        // 英文缺字（非 ASCII）时整段回退系统黑体，避免混排错位
        Font _fY, _fDot, _fD;    // 年份/冒号/日期数字字体缓存（优化-2：不再每帧 new）
        float _scrollPos;
        Timer _scrollTimer;
        const int ScrollTickMs = 33;   // 5.5.2：50→33ms（≈30fps），滚动更平滑
        long _lastScrollT = -1;        // 上次滚动 tick 的毫秒时间戳（时间补偿用）
        readonly System.Diagnostics.Stopwatch _sw = System.Diagnostics.Stopwatch.StartNew(); // BUG-6：替代 32 位 TickCount

        // 背景离屏预渲染缓存：按目标缩放尺寸先插值缩放背景图并重新品红化，
        // 再 1:1 无插值绘制到窗口——彻底避免"缩放插值把透明边缘变成非纯品红
        // 的紫红过渡像素"导致的漏紫边（4.8 曾用溢出 1px 方案，100% 也被
        // 拉伸插值污染，已废弃）。
        Bitmap _bgScaled;
        double _bgScale = -1;
        int _bgTheme = -1;

        const float BaseW = 520f;
        const float BaseH = 126f;
        static readonly Color TextColor = Color.Black;
        static readonly Color KeyColor = Color.Magenta;

        public ClockForm(CityEntry ce)
        {
            Entry = ce;
            _cityName = ce.Name;
            _isLocal = IsLocalEntry(ce.Name, ce.TzId);
            try { _tz = TimeZoneInfo.FindSystemTimeZoneById(ce.TzId); }
            catch (Exception) { _tz = TimeZoneInfo.Local; }
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            BackColor = KeyColor;
            TransparencyKey = KeyColor;
            TopMost = ce.Topmost;
            Opacity = ce.Opacity;
            DoubleBuffered = true;
            ApplyScale();
            BuildMenu();
            _timer = new Timer();
            _timer.Interval = 500;
            _timer.Tick += delegate(object s, EventArgs e) { Invalidate(); };
            _timer.Start();
            // 字幕横滚计时器（33ms/帧，按真实流逝时间步进，速度恒定不随 Timer 抖动）
            _scrollTimer = new Timer();
            _scrollTimer.Interval = ScrollTickMs;
            _scrollTimer.Tick += delegate(object s, EventArgs e)
            {
                long now = _sw.ElapsedMilliseconds;   // BUG-6：Stopwatch 64 位，无 TickCount 24.9 天回绕
                if (_lastScrollT >= 0)
                    _scrollPos += SpeedPx(Entry.Speed) * ((now - _lastScrollT) / 1000f);
                _lastScrollT = now;
                Invalidate();
            };
            _scrollTimer.Start();
        }

        static float SpeedPx(int sp)
        {
            switch (sp)
            {
                case 1: return 20f;
                case 2: return 35f;
                case 3: return 55f;
                case 4: return 75f;
                default: return 100f;
            }
        }

        /// <summary>工具窗口样式：Alt+Tab 切换时不显示（配合 ShowInTaskbar=false）</summary>
        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= 0x80; // WS_EX_TOOLWINDOW
                return cp;
            }
        }

        // v5.6.2：防御性剥离 WS_MINIMIZEBOX——无边框窗体虽天然不带最小化框，
        // 句柄创建后再剥一次，确保 Win+D 不把挂件最小化（穿透方案 A′+B）。
        [DllImport("user32.dll")]
        static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll")]
        static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        const int GWL_STYLE = -16;
        const uint WS_MINIMIZEBOX = 0x00020000;

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            try
            {
                int style = GetWindowLong(Handle, GWL_STYLE);
                SetWindowLong(Handle, GWL_STYLE, style & ~(int)WS_MINIMIZEBOX);
            }
            catch { }
        }

        void ApplyScale()
        {
            ClientSize = new Size((int)(BaseW * Entry.Scale), (int)(BaseH * Entry.Scale));
        }

        /// <summary>按 Entry.Scale 复位客户区尺寸。拔屏回收时调用，避免显示器断开期间
        /// 系统把离屏窗口尺寸带偏、回主屏后显示成默认 100% 大小。</summary>
        public void RestoreSize() { ApplyScale(); }

        /// <summary>BUG-5：关闭窗口时停止两个计时器并释放缓存字体/位图，避免句柄与 GDI 对象泄漏。</summary>
        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            base.OnFormClosed(e);
            try { if (_timer != null) { _timer.Stop(); _timer.Dispose(); } } catch (Exception) { }
            try { if (_scrollTimer != null) { _scrollTimer.Stop(); _scrollTimer.Dispose(); } } catch (Exception) { }
            DisposeCacheFonts();
            if (_bgScaled != null) { _bgScaled.Dispose(); _bgScaled = null; }
        }

        void DisposeCacheFonts()
        {
            try { if (_fcCity != null) { _fcCity.Dispose(); _fcCity = null; } } catch (Exception) { }
            try { if (_feCity != null) { _feCity.Dispose(); _feCity = null; } } catch (Exception) { }
            try { if (_feFallback != null) { _feFallback.Dispose(); _feFallback = null; } } catch (Exception) { }
            try { if (_fY != null) { _fY.Dispose(); _fY = null; } } catch (Exception) { }
            try { if (_fDot != null) { _fDot.Dispose(); _fDot = null; } } catch (Exception) { }
            try { if (_fD != null) { _fD.Dispose(); _fD = null; } } catch (Exception) { }
        }

        /* ---------- 右键菜单 ---------- */
        void BuildMenu()
        {
            _menu = new ContextMenuStrip();

            _miStyle = new ToolStripMenuItem();
            _miSilver = new ToolStripMenuItem("Silver", null, delegate { SetTheme(1); });
            _miRoseGold = new ToolStripMenuItem("Rose Gold", null, delegate { SetTheme(2); });
            _miBlack = new ToolStripMenuItem("Black", null, delegate { SetTheme(3); });
            _miQinghua = new ToolStripMenuItem("青花瓷", null, delegate { SetTheme(4); });
            _miStyle.DropDownItems.AddRange(new ToolStripItem[] { _miSilver, _miRoseGold, _miBlack, _miQinghua });

            _miSizeParent = new ToolStripMenuItem();
            _miSize = new ToolStripMenuItem[7];
            int[] sizePcts = { 120, 110, 100, 90, 80, 70, 60 };
            for (int i = 0; i < sizePcts.Length; i++)
            {
                int pct = sizePcts[i];
                double sv = pct / 100.0;
                ToolStripMenuItem it = new ToolStripMenuItem(pct + "%", null, delegate { SetScale(sv); });
                _miSize[i] = it;
                _miSizeParent.DropDownItems.Add(it);
            }

            _miOpParent = new ToolStripMenuItem();
            _miOp = new ToolStripMenuItem[10];
            for (int i = 0; i < 10; i++)
            {
                int pct = 100 - i * 10;
                double ov = pct / 100.0;
                ToolStripMenuItem it = new ToolStripMenuItem(pct + "%", null, delegate { SetOpacity(ov); });
                _miOp[i] = it;
                _miOpParent.DropDownItems.Add(it);
            }

            _miSpd = new ToolStripMenuItem();
            _miSpeed = new ToolStripMenuItem[5];
            for (int i = 1; i <= 5; i++)
            {
                int sp = i;
                ToolStripMenuItem it = new ToolStripMenuItem("速度 " + sp, null, delegate { SetSpeed(sp); });
                _miSpeed[i - 1] = it;
                _miSpd.DropDownItems.Add(it);
            }

            _miTop = new ToolStripMenuItem(null, null, delegate { ToggleTop(); });
            _miAuto = new ToolStripMenuItem(null, null, delegate { ToggleAutoStart(); });
            _miAdd = new ToolStripMenuItem(null, null, delegate { CityDialog.DoAdd(this); });
            _miSet = new ToolStripMenuItem(null, null, delegate { CityDialog.DoSet(this); });
            _miHelp = new ToolStripMenuItem(null, null, delegate { ShowHelp(); });
            _miAbout = new ToolStripMenuItem(null, null, delegate { ShowAbout(); });
            _miLang = new ToolStripMenuItem(null, null, delegate { Program.ToggleLanguage(); });
            _miClose = new ToolStripMenuItem(null, null, delegate { CloseThisWindow(); });
            _miClearAll = new ToolStripMenuItem(null, null, delegate { ClearAllAndExit(); });

            _menu.Items.Add(_miStyle);
            _menu.Items.Add(_miSizeParent);
            _menu.Items.Add(_miOpParent);
            _menu.Items.Add(_miSpd);
            _menu.Items.Add(_miTop);
            _menu.Items.Add(_miAuto);
            _menu.Items.Add(new ToolStripSeparator());
            _menu.Items.Add(_miAdd);
            _menu.Items.Add(_miSet);
            _menu.Items.Add(new ToolStripSeparator());
            _menu.Items.Add(_miHelp);
            _menu.Items.Add(_miAbout);
            _menu.Items.Add(_miLang);
            _menu.Items.Add(new ToolStripSeparator());
            _menu.Items.Add(_miClose);
            _menu.Items.Add(_miClearAll);

            ApplyMenuTexts();
        }

        public void RefreshMenuChecks()
        {
            _miSilver.Checked = (Entry.Theme == 1);
            _miRoseGold.Checked = (Entry.Theme == 2);
            _miBlack.Checked = (Entry.Theme == 3);
            _miQinghua.Checked = (Entry.Theme == 4);
            int sizePct = (int)Math.Round(Entry.Scale * 100.0);
            for (int i = 0; i < _miSize.Length; i++)
                _miSize[i].Checked = (sizePct == 120 - i * 10);
            int opPct = (int)Math.Round(Entry.Opacity * 100.0);
            for (int i = 0; i < _miOp.Length; i++)
                _miOp[i].Checked = (opPct == 100 - i * 10);
            for (int i = 0; i < _miSpeed.Length; i++)
                _miSpeed[i].Checked = (Entry.Speed == i + 1);
            _miTop.Checked = Entry.Topmost;
            _miAuto.Checked = Program.IsAutoStartEnabled();
        }

        /// <summary>5.6.3：按当前全局语言刷新全部菜单项文案（勾选状态由 RefreshMenuChecks 负责）。</summary>
        void ApplyMenuTexts()
        {
            bool en = Program.IsEn;
            _miStyle.Text = en ? "Theme" : "窗口样式";
            _miSizeParent.Text = en ? "Size" : "窗口大小";
            _miOpParent.Text = en ? "Opacity" : "透明度";
            _miSpd.Text = en ? "Scroll Speed" : "文字滚动速度";
            _miTop.Text = en ? "Always on Top" : "总在最前";
            _miAuto.Text = en ? "Start with Windows" : "开机自启";
            _miAdd.Text = en ? "Add Location" : "添加地点";
            _miSet.Text = en ? "Set Location" : "设置地点";
            _miHelp.Text = en ? "Help" : "使用说明";
            _miAbout.Text = en ? "About" : "关于";
            _miClose.Text = en ? "Close This Window" : "关闭此窗口";
            _miClearAll.Text = en ? "Close All Windows & Clear All Settings" : "关闭全部窗口并清除所有配置";
            _miLang.Text = en ? "中文" : "English";
            _miQinghua.Text = en ? "QingHua" : "青花瓷";
            for (int i = 0; i < _miSpeed.Length; i++)
                _miSpeed[i].Text = en ? "Speed " + (i + 1) : "速度 " + (i + 1);
        }

        /// <summary>5.6.3：语言切换后由 Program 全局调用——刷新菜单文案、重建字幕段并重绘。</summary>
        public void RefreshLanguage()
        {
            ApplyMenuTexts();
            RefreshMenuChecks();
            _scrollKey = null;   // 强制按新语言重建滚动段
            Invalidate();
        }

        void SetSpeed(int sp)
        {
            if (Entry.Speed != sp) { Entry.Speed = sp; RefreshMenuChecks(); Program.SaveConfig(); }
        }

        void ToggleAutoStart()
        {
            bool next = !Program.IsAutoStartEnabled();
            Program.SetAutoStart(next);
            Program.SetAutoStartIntent(next);   // BUG-2：意图持久化，下次启动按此决定是否刷新自启
            RefreshMenuChecks();
        }

        void SetTheme(int t)
        {
            if (Entry.Theme != t) { Entry.Theme = t; RefreshMenuChecks(); Invalidate(); Program.SaveConfig(); }
        }
        void SetScale(double s)
        {
            Entry.Scale = s;
            ApplyScale();
            RefreshMenuChecks();
            Invalidate();
            Program.SaveConfig();
        }
        void SetOpacity(double o)
        {
            Entry.Opacity = o;
            Opacity = o;
            RefreshMenuChecks();
            Program.SaveConfig();
        }
        void ToggleTop()
        {
            Entry.Topmost = !Entry.Topmost;
            TopMost = Entry.Topmost;
            // v5.6.2：取消置顶后把窗口落位到桌面层（插到桌面窗口正上方），不挂 owner
            if (!Entry.Topmost) Program.ReassertAboveDesktop(this, true);
            RefreshMenuChecks();
            Program.SaveConfig();
        }

        void ShowAbout()
        {
            string body = Program.IsEn
                ? "World bedside clock 5.6.6\nVersion: 5.6.6\nAuthor: ༺格桑仁青༻ & Old Newbie\n\nCity time-zone data sources:\n· GeoNames global geographic database (geonames.org),\n  used under the CC-BY 4.0 license;\n· China administrative-division data from the Ministry of Civil Affairs'\n  \"Codes for Administrative Divisions of the People's Republic of China\"."
                : "World bedside clock 5.6.6\n版本号：5.6.6\n作者：༺格桑仁青༻ & Old Newbie\n\n城市时区数据来源：\n· GeoNames 全球地理数据库（geonames.org），\n  数据依据 CC-BY 4.0 许可使用；\n· 中国行政区划数据源自民政部\n  《中华人民共和国行政区划代码》。";
            MessageBox.Show(body, Program.IsEn ? "About" : "关于",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        /// <summary>使用说明弹窗（文案按需求原文，不得改写）</summary>
        void ShowHelp()
        {
            string help;
            if (Program.IsEn)
            {
                help =
                    "★World bedside clock is a green open-source Windows desktop widget written by ༺格桑仁青༻ & Old Newbie with the help of Doubao Pro and CodeBuddy. Users may set any location worldwide with a resident population of more than 20,000 as the clock's location and open multiple desktop clock widgets. This program is intended solely for non-commercial study and testing; any use of it to charge fees or make a profit is unauthorized and illegal. The authors accept no responsibility for any problems arising during use and do not commit to future version upgrades.\r\n" +
                    "\r\n" +
                    "★★No uninstall is required to clear this program, but before deleting the executable, please first choose \"Close all windows and clear all settings\" from the program's right-click menu to thoroughly remove all system traces;\r\n" +
                    "\r\n" +
                    "★★★Acknowledgments:\r\n" +
                    "City time-zone data are based on the GeoNames global geographic database (geonames.org), used under the CC-BY 4.0 license;\r\n" +
                    "China administrative-division data are sourced from the Ministry of Civil Affairs' \"Codes for Administrative Divisions of the People's Republic of China\";\r\n" +
                    "The theme artwork draws on bedside clock 2 from \"Kuyu Desktop / Kuyu Widgets / XWidgets\". Special thanks to Kuyu and the original author Jimking.\r\n" +
                    "\r\n" +
                    "November 2026 · Garzê Tibetan Autonomous Prefecture, Sichuan Province";
            }
            else
            {
                help =
                    "★World bedside clock 是༺格桑仁青༻ & Old Newbie 借助豆包专业版及CodeBuddy编写的绿色开源Windows桌面挂件(widgets)，使用者可将全球任意一个居民数量超过2万的地点设为时钟所在地并开启多个桌面时钟挂件。此程序仅限非商业用途的学习、测试，任何以此收费牟利的行为均为无授权的非法行为。作者对程序使用过程中出现的任何问题不予负责且不承诺后续版本的升级。\r\n" +
                    "\r\n" +
                    "★★清除本程序无需卸载，但删除程序执行文件前请务必先在程序右键菜单内选择“关闭全部窗口并清除所有配置”，以彻底清除全部系统残留；\r\n" +
                    "\r\n" +
                    "★★★鸣谢：\r\n" +
                    "城市时区数据依据CC-BY4.0许可使用GeoNames全球地理数据库(geonames.org);\r\n" +
                    "中国行政区划数据源自民政部《中华人民共和国行政区划代码》\r\n" +
                    "本程序主题美工借鉴了“酷鱼桌面/酷鱼小工具/XWidgets”中的bedside clock 2，特此鸣谢酷鱼和原作者Jimking\r\n" +
                    "\r\n" +
                    "2026年11月·四川省甘孜藏族自治州";
            }

            Form dlg = new Form();
            dlg.Text = Program.IsEn ? "Help" : "使用说明";
            dlg.FormBorderStyle = FormBorderStyle.FixedDialog;
            dlg.MaximizeBox = false;
            dlg.MinimizeBox = false;
            dlg.StartPosition = FormStartPosition.CenterParent;
            dlg.ClientSize = new Size(580, 500);
            dlg.Font = new Font("微软雅黑", 9f);

            Button btn = new Button();
            btn.Text = Program.IsEn ? "OK" : "确定";
            btn.DialogResult = DialogResult.OK;
            btn.SetBounds(470, 458, 90, 28);

            TextBox tb = new TextBox();
            tb.Multiline = true;
            tb.ReadOnly = true;
            tb.ScrollBars = ScrollBars.Vertical;
            tb.WordWrap = true;
            tb.BackColor = SystemColors.Window;
            tb.Font = new Font("微软雅黑", 10f);
            tb.SetBounds(14, 12, 552, 432);
            tb.Text = help;
            tb.Select(0, 0);

            dlg.Controls.Add(btn);
            dlg.Controls.Add(tb);
            dlg.AcceptButton = btn;
            dlg.CancelButton = btn;
            dlg.ShowDialog(this);
        }

        /// <summary>关闭全部窗口并清除所有配置（注册表项、自启动项、日志、配置记录）</summary>
        void ClearAllAndExit()
        {
            DialogResult r = MessageBox.Show(
                Program.IsEn
                    ? "Close all windows and clear all settings?\n\nThis will delete the registry configuration, the start-with-Windows entry, the ini file and the log. This cannot be undone."
                    : "确定要关闭全部窗口并清除所有配置吗？\n\n将删除：注册表配置记录、开机自启动项、ini 配置文件与日志。\n此操作不可恢复。",
                Program.IsEn ? "Close All Windows & Clear All Settings" : "关闭全部窗口并清除所有配置",
                MessageBoxButtons.OKCancel, MessageBoxIcon.Warning);
            if (r != DialogResult.OK) return;
            Program.ClearAllAndExit();
        }

        /// <summary>"关闭此窗口"：
        /// 多窗口 → 关闭当前窗并删除其地点/位置/大小等配置，落盘仅剩其余窗口，下次启动不再加载本窗；
        /// 最后一个窗口 → 仅暂退后台、保留本窗全部配置；程序退出运行，待重新加载或电脑重启
        /// （若该窗勾选了开机自启）时自动恢复显示。</summary>
        void CloseThisWindow()
        {
            bool last = Program.Forms.Count <= 1;
            Close(); // FormClosed 回调已从 Program.Forms 移除本窗
            if (!last)
            {
                // 多窗口：把剩余窗口落盘；被关闭的窗口不在 Forms 中，自然从注册表配置移除
                Program.SaveConfig();
            }
            // 末窗口：不回写注册表，配置原样保留；进程由 Idle 在 Forms 为空时退出后台
        }

        /// <summary>外部（设置城市成功）调用</summary>
        public void ApplyCity(string name, string tzId)
        {
            _cityName = name;
            Entry.Name = name;
            Entry.TzId = tzId;
            _isLocal = IsLocalEntry(name, tzId);
            try { _tz = TimeZoneInfo.FindSystemTimeZoneById(tzId); }
            catch (Exception) { _tz = TimeZoneInfo.Local; }
            Program.SaveConfig();
            Invalidate();
        }

        /// <summary>⑥ 季节规则刷新后由 Program 调用：按保存的 TzId 重新查找时区规则（不重启程序）。</summary>
        public void RefreshTimeZone()
        {
            try { _tz = TimeZoneInfo.FindSystemTimeZoneById(Entry.TzId); }
            catch (Exception) { _tz = TimeZoneInfo.Local; }
        }

        /// <summary>是否本地时钟：名称为"本地/本地时间"，或该窗时区与本机系统本地时区完全一致
        /// （同一物理时区，如系统为北京时间时设甘孜，亦视为本地，给予星期显示待遇）。
        /// 判定以时区 Id 相等为准；Id 别名不一致时按当前 UTC 偏移相等兜底。</summary>
        static bool IsLocalEntry(string name, string tzId)
        {
            string cn, en;
            SplitName(name, out cn, out en);
            if (cn == "本地" || cn == "本地时间") return true;
            if (string.IsNullOrEmpty(tzId)) return false;
            try
            {
                if (tzId == TimeZoneInfo.Local.Id) return true;
                TimeZoneInfo tz = TimeZoneInfo.FindSystemTimeZoneById(tzId);
                return tz.GetUtcOffset(DateTime.UtcNow) ==
                       TimeZoneInfo.Local.GetUtcOffset(DateTime.UtcNow);
            }
            catch (Exception) { return false; }
        }

        /* ---------- 拖拽 ---------- */
        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left)
            {
                _dragging = true;
                _dragStart = e.Location;
            }
        }
        // 鼠标悬停时取消半透明（100% 显示），移开恢复设定的透明度
        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            Opacity = 1.0;
        }
        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            Opacity = Entry.Opacity;
        }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (_dragging && e.Button == MouseButtons.Left)
            {
                Point np = PointToScreen(e.Location);
                Location = new Point(np.X - _dragStart.X, np.Y - _dragStart.Y);
            }
        }
        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button == MouseButtons.Left && _dragging)
            {
                _dragging = false;
                Entry.X = Location.X;
                Entry.Y = Location.Y;
                Program.SaveConfig();
            }
            else if (e.Button == MouseButtons.Right)
            {
                RefreshMenuChecks();
                _menu.Show(this, new Point(e.X, e.Y));
            }
        }

        /* ---------- 双击：时分→系统时间语言；月日→系统日历 ---------- */
        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            base.OnMouseDoubleClick(e);
            if (e.Button != MouseButtons.Left) return;
            float s = Entry.Scale > 0 ? (float)Entry.Scale : 1f;
            float bx = e.X / s, by = e.Y / s;
            // 基准 520x126：时分区（时/冒号/分）与月日区，年份与城市字幕不绑动作
            if (bx >= 95 && bx <= 182 && by >= 28 && by <= 98) { OpenTimeLanguage(); return; }
            if (bx >= 205 && bx <= 310 && by >= 45 && by <= 90) { OpenCalendar(); }
        }

        /// <summary>打开系统"时间和语言 > 日期和时间"设置页。</summary>
        void OpenTimeLanguage()
        {
            try { Process.Start(new ProcessStartInfo("ms-settings:dateandtime") { UseShellExecute = true }); }
            catch (Exception ex) { ResourceBox.Log("open time-language err: " + ex.Message); }
        }

        /// <summary>打开系统日历：优先"邮件和日历"应用，未安装时回退 Outlook，
        /// 再回退日期和时间设置页。shell:AppsFolder 用反斜杠分隔、UseShellExecute 解析。</summary>
        void OpenCalendar()
        {
            const string calendarAumid =
                "microsoft.windowscommunicationsapps_8wekyb3d8bbwe!microsoft.windowslive.calendar";
            const string outlookAumid =
                "Microsoft.OutlookForWindows_8wekyb3d8bbwe!Microsoft.OutlookforWindows";
            try { LaunchApp(calendarAumid); return; }
            catch
            {
                try { LaunchApp(outlookAumid); return; }
                catch
                {
                    try { Process.Start(new ProcessStartInfo("ms-settings:dateandtime") { UseShellExecute = true }); }
                    catch (Exception ex) { ResourceBox.Log("open calendar err: " + ex.Message); }
                }
            }
        }

        static void LaunchApp(string aumid)
        {
            Process.Start(new ProcessStartInfo("shell:AppsFolder\\" + aumid) { UseShellExecute = true });
        }

        /* ---------- 绘制 ---------- */
        static void DrawCentered(Graphics g, string s, Font f, Color color, RectangleF r)
        {
            using (StringFormat sf = new StringFormat())
            {
                sf.Alignment = StringAlignment.Center;
                sf.LineAlignment = StringAlignment.Center;
                sf.FormatFlags = StringFormatFlags.NoWrap;
                using (SolidBrush b = new SolidBrush(color))
                    g.DrawString(s, f, b, r, sf);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            try { PaintAll(e); }
            catch (Exception ex) { ResourceBox.Log("paint err: " + ex.Message); }
        }

        void PaintAll(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            float s = (float)Entry.Scale;
            EnsureBgScaled(s);

            // 背景按物理像素 1:1 无插值绘制（ResetTransform 绕开缩放变换），
            // 圆角外与插值过渡区已在 _bgScaled 内重涂为纯品红 -> 透明。
            g.ResetTransform();
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            if (TestNoBg || _bgScaled == null) g.Clear(Color.Magenta);
            else g.DrawImage(_bgScaled, 0, 0);

            g.ScaleTransform(s, s);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;

            using (SolidBrush br = new SolidBrush(TextColor))
            {
                DateTime now = TestNow ?? DateTime.Now;
                DateTime t;
                try { t = TimeZoneInfo.ConvertTime(now, TimeZoneInfo.Local, _tz); }
                catch (Exception) { t = now; }

                // ① 年份：4 位，字号 17，区 (15,50,80,26)（4.2 左移3、上移2）
                if (_fY == null) _fY = new Font(ResourceBox.FontLed.FontFamily, 17f, FontStyle.Regular, GraphicsUnit.Pixel);
                DrawCentered(g, t.ToString("yyyy"), _fY, TextColor, new Rectangle(15, 50, 80, 26));

                // ② 时间：固定 24 小时制；数字字体/字号/位置回退 5.6.2 设定
                //    （digital display tfb.ttf 76px，"1"槽内右对齐由 DrawHHorMM 处理）。
                //    冒号由中文字库点阵宋（FontAura）渲染。
                string hh = t.ToString("HH");
                string mm = t.ToString("mm");
                Font fDig = ResourceBox.FontDigital;
                SizeF szH = g.MeasureString(hh, fDig);
                float hhX = 153f - szH.Width;
                DrawHHorMM(g, hh, fDig, br, hhX, 32f);
                if (t.Second % 2 == 0)
                {
                    if (_fDot == null) _fDot = new Font(ResourceBox.FontAura.FontFamily, 40f, FontStyle.Regular, GraphicsUnit.Pixel);
                    DrawCentered(g, ":", _fDot, TextColor, new Rectangle(135, 49, 24, 59));
                }
                DrawHHorMM(g, mm, fDig, br, 145f, 32f);

                // ③ 日期 MM/dd：x=210（5.6.1 左移3）；字号 36→32.4（×0.9）
                if (_fD == null) _fD = new Font(ResourceBox.FontLed.FontFamily, 32.4f, FontStyle.Regular, GraphicsUnit.Pixel);
                g.DrawString(t.ToString("MM/dd"), _fD, br, new PointF(210f, 53f));
            }

            // ④ 城市名：改区 (318,46,202,54) 内居中；字号放大（中文 cap40 / 英文 cap40）
            DrawCity(g);
        }

        /// <summary>按目标缩放尺寸离屏预渲染背景（插值缩放），并重新品红化：
        /// 圆角外/插值过渡的"品红系"像素重涂为纯品红，其余半透明像素硬化，
        /// 使窗口内 1:1 绘制时透明区像素恒为纯品红（匹配 TransparencyKey）。
        /// 缓存键 = (Scale, Theme)，变化时重建。</summary>
        void EnsureBgScaled(float s)
        {
            if (_bgScaled != null && _bgScale == (double)s && _bgTheme == Entry.Theme) return;
            int w = (int)(BaseW * s);
            int h = (int)(BaseH * s);
            if (w < 1 || h < 1) return;
            if (_bgScaled != null) { _bgScaled.Dispose(); _bgScaled = null; }
            Bitmap bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            Image bg = ResourceBox.Themes[(Entry.Theme - 1) % 4];
            using (Graphics g2 = Graphics.FromImage(bmp))
            {
                g2.Clear(Color.Magenta);
                if (bg != null)
                {
                    // 用 Bilinear：bicubic 在透明边缘有振铃过冲，会产生紫灰伪像
                    g2.InterpolationMode = InterpolationMode.HighQualityBilinear;
                    g2.PixelOffsetMode = PixelOffsetMode.Half;
                    g2.DrawImage(bg, 0, 0, w, h);
                }
            }
            ReMagenta(bmp);
            _bgScaled = bmp;
            _bgScale = (double)s;
            _bgTheme = Entry.Theme;
        }

        /// <summary>扫描位图像素：圆角外/插值产生的"品红系"像素重涂为纯品红；
        /// alpha<64 的任意像素（圆角外原透明区）也重涂品红；其余半透明像素
        /// 硬化 alpha=255 防紫斑。判定分两支（差量式，不靠单通道硬下限误伤壳色）：
        /// · 暗紫红：g 显著低于 r/b（g<=min(r,b)-15），r/b 下限 60，覆盖黑壳与
        ///   品红插值出的暗紫红（r/b 83~129）。黑壳本体 (58,57,57) 全通道<60、
        ///   黑壳暗灰边 g≈r 不满足 g<=mn-15，均不命中；
        /// · 亮浅粉/浅灰紫：白壳边与品红/透明插值，亮度高（mn>=130）、b 不低于 r、
        ///   g 略低于 r（g<=r-8）。白壳 g=r、青花蓝纹 r 低或 g 高、绿屏 g 高，不命中。
        /// 银灰三通道相等、Rose Gold 金色 b 低，两支均不命中。</summary>
        void ReMagenta(Bitmap bmp)
        {
            Rectangle rc = new Rectangle(0, 0, bmp.Width, bmp.Height);
            BitmapData data = bmp.LockBits(rc, ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
            try
            {
                int stride = data.Stride;
                int n = stride * bmp.Height;
                byte[] px = new byte[n];
                Marshal.Copy(data.Scan0, px, 0, n);
                for (int i = 0; i < n; i += 4)
                {
                    byte b = px[i], g = px[i + 1], r = px[i + 2], a = px[i + 3];
                    int mn = r < b ? r : b;
                    bool darkViolet = mn >= 60 && g <= mn - 15;
                    bool lightPink = mn >= 130 && b >= r && g <= r - 8;
                    bool remag = (a < 64) || darkViolet || lightPink;
                    if (remag) { px[i] = 255; px[i + 1] = 0; px[i + 2] = 255; px[i + 3] = 255; }
                    else if (a < 255) px[i + 3] = 255;
                }
                Marshal.Copy(px, 0, data.Scan0, n);
            }
            finally { bmp.UnlockBits(data); }
        }

        /// <summary>数字对的自然步距（实测：字形 bbox 宽 + 固定间隙，GDI 排版步距）：
        /// 0=30 1=30 2=30 3=27 4=30 5=30 6=30 7=29 8=30 9=30</summary>
        static readonly float[] DigStep = { 30f, 30f, 30f, 27f, 30f, 30f, 30f, 29f, 30f, 30f };

        /// <summary>两位数字串绘制：含"1"的串逐字绘制，数字"1"在其字宽内居中
        /// （1 字形宽约 4.5px、占位区 30px，居中偏移 = (30-4.5)/2 = 12.75），
        /// 使 1 与前后数字的间隙均匀、不贴边不粘连；其余数字保持自然串位置。</summary>
        static void DrawHHorMM(Graphics g, string s, Font f, SolidBrush br, float x, float y)
        {
            if (s.Length != 2 || (s[0] != '1' && s[1] != '1'))
            {
                g.DrawString(s, f, br, x, y);
                return;
            }
            float step = DigStep[s[0] - '0'];
            float pen0 = x;
            float pen1 = x + step;
            float cx0 = (s[0] == '1') ? pen0 + 12.75f : pen0;
            float cx1 = (s[1] == '1') ? pen1 + 12.75f : pen1;
            g.DrawString(s.Substring(0, 1), f, br, cx0, y);
            g.DrawString(s.Substring(1, 1), f, br, cx1, y);
        }

        /// <summary>改区显示地点名（5.5 滚动字幕版）：
        /// 格式 "中文/英文大写" 横向循环滚动（右→左）；中英文统一字高（英文按字形高
        /// 等比调字号）；每遍之间两个全角空格（"泸定县/LU DING　　"循环）；
        /// 滚动窗口 = 改区 (298,45,220,54)，越界裁切不显示到竖线外；
        /// 速度由右键菜单"文字滚动速度"1~5 档控制（20~100px/s，1 最慢 5 最快）；
        /// 中文缺字自动回退系统黑体；纵向沿用 10% 拉长。</summary>
        void DrawCity(Graphics g)
        {
            string cn, en;
            SplitName(_cityName, out cn, out en);
            if (cn.Length == 0) cn = "本地";
            bool enLang = Program.IsEn;
            string week = null;
            if (_isLocal)
            {
                // 本地时区（与本机系统时间同时区）：仅额外前置星期段（语言随界面）；
                // 地名仍显示该地点真实中英文名称，不再强制覆盖为"本地/Local Time"。
                // 仅默认窗（名称本身即"本地"）由 SplitName 兜底显示本地二字。
                DateTime now0 = TestNow ?? DateTime.Now;
                week = Weekday(now0.DayOfWeek);
            }
            if (en.Length == 0) en = enLang ? "Local Time" : "Local";
            // 5.6.3：字幕下移 3px（45→48）、左移 3px（322→319）；右侧再缩 2px（右 498→496）
            Rectangle r = new Rectangle(319, 48, 174, 54);

            // 中文缺字回退黑体（避免 □ 乱码）；星期段一并纳入缺字检测
            bool allAura = true;
            foreach (char c in (cn + (week ?? "")))
                if (!ResourceBox.AuraChars.Contains(c)) { allAura = false; break; }

            // 统一字高：中文 40px 基准，英文按字形高度等比调字号后再 ×0.76
            //（5.5.3：在 5.5.2 的 ×0.8 基础上再 ×0.95，英文略小）
            float fsC = 40f;
            float fsE = fsC;

            // 段缓存：地点名/星期/字号不变时复用；字体与字高测量只在键变化时做（优化-1）
            string key = cn + "\u0001" + en + "\u0001" + (week ?? "") + "\u0001" + (enLang ? "en" : "zh");
            if (key != _scrollKey)
            {
                if (_fcCity != null) _fcCity.Dispose();
                if (_feCity != null) _feCity.Dispose();
                if (_feFallback != null) _feFallback.Dispose();
                using (Font fc0 = allAura
                    ? new Font(ResourceBox.FontAura.FontFamily, fsC, FontStyle.Regular, GraphicsUnit.Pixel)
                    : new Font("黑体", fsC, FontStyle.Regular, GraphicsUnit.Pixel))
                using (Font fe0 = new Font(ResourceBox.FontDate.FontFamily, fsC, FontStyle.Regular, GraphicsUnit.Pixel))
                {
                    float hC = g.MeasureString("永", fc0).Height;
                    float hE = g.MeasureString("A", fe0).Height;
                    if (hE > 1f) fsE = fsC * hC / hE * 0.76f;
                    if (fsE < 6f) fsE = 6f;
                    if (fsE > 80f) fsE = 80f;
                }
                _fcCity = allAura
                    ? new Font(ResourceBox.FontAura.FontFamily, fsC, FontStyle.Regular, GraphicsUnit.Pixel)
                    : new Font("黑体", fsC, FontStyle.Regular, GraphicsUnit.Pixel);
                _feCity = new Font(ResourceBox.FontDate.FontFamily, fsE, FontStyle.Regular, GraphicsUnit.Pixel);
                _feFallback = new Font("黑体", fsE, FontStyle.Regular, GraphicsUnit.Pixel);
                _scrollSegs = new List<Seg>();
                float u = 0f;
                for (int i = 0; i < 6; i++)   // 6 遍保证滚动窗口内永远有文字
                {
                    Seg s;
                    if (week != null)
                    {
                        if (enLang)
                        {
                            // 英文星期：中点走中文字库点阵宋（Kind 0），字母走 date.ttf（Kind 1），避免整段因中点回退黑体
                            Seg d0 = MakeSeg("·", 0, g); u += d0.W; _scrollSegs.Add(d0);
                            Seg w = MakeSeg(week, 1, g); u += w.W; _scrollSegs.Add(w);
                            Seg d1 = MakeSeg("·", 0, g); u += d1.W; _scrollSegs.Add(d1);
                        }
                        else
                        {
                            s = MakeSeg("·" + week + "·", 0, g);
                            u += s.W; _scrollSegs.Add(s);
                        }
                    }
                    if (enLang)
                    {
                        // 英文版：去掉中文段与斜杠，仅保留英文（本地为 Local Time）
                        s = MakeSeg(en, 1, g); u += s.W; _scrollSegs.Add(s);
                    }
                    else
                    {
                        s = MakeSeg(cn, 0, g); u += s.W; _scrollSegs.Add(s);
                        s = MakeSeg("/", 1, g); u += s.W; _scrollSegs.Add(s);
                        s = MakeSeg(en, 1, g); u += s.W; _scrollSegs.Add(s);
                    }
                    s = MakeSeg("\u3000\u3000", 0, g); u += s.W; _scrollSegs.Add(s);
                }
                _scrollUnitW = u / 6f;
                _scrollKey = key;
            }

            using (SolidBrush b = new SolidBrush(TextColor))
            using (StringFormat sf = new StringFormat())
            {
                sf.Alignment = StringAlignment.Near;
                sf.LineAlignment = StringAlignment.Center;
                sf.FormatFlags = StringFormatFlags.NoWrap;
                System.Drawing.Drawing2D.GraphicsState st = g.Save();
                float cx = r.X + r.Width / 2f;
                float cy = r.Y + r.Height / 2f;
                g.TranslateTransform(cx, cy);
                g.ScaleTransform(1f, 1.1f);    // 纵向拉长 10%（沿用 5.x 风格）
                g.TranslateTransform(-cx, -cy);
                g.SetClip(r);                  // 滚动窗口：竖线之间，越界裁切
                float xStart = r.Left - (_scrollPos % _scrollUnitW);
                float x = xStart;
                foreach (Seg s in _scrollSegs)
                {
                    // 5.5.3：英文段（含斜杠）下移 3px（5.6 沿用）；缺字回退段（Kind 2）用黑体
                    float yy = (s.Kind == 1 || s.Kind == 2) ? r.Y + 3 : r.Y;
                    Font sf2 = s.Kind == 0 ? _fcCity : (s.Kind == 2 ? _feFallback : _feCity);
                    g.DrawString(s.T, sf2, b, new RectangleF(x, yy, s.W, r.Height), sf);
                    x += s.W;
                }
                g.Restore(st);
            }
        }

        /// <summary>滚动段：Kind 0=中文/空格（点阵宋）、1=英文/斜杠（LED）、2=英文缺字回退（黑体）</summary>
        Seg MakeSeg(string t, int kind, Graphics g)
        {
            Font f = kind == 0 ? _fcCity : _feCity;
            if (kind == 1)
            {
                // date.ttf 仅覆盖 ASCII 及少量标点：含非 ASCII 字符（如 P·R CHINA 的中点）整段用黑体，避免混排错位
                foreach (char c in t)
                    if (c > 0x7e) { kind = 2; break; }
                if (kind == 2) f = _feFallback;
            }
            SizeF sz = g.MeasureString(t, f);
            return new Seg(t, kind, sz.Width);
        }

        /// <summary>本地时钟星期段：中文版 星期日…星期六；英文版 Sun…Sat 缩写。</summary>
        static string Weekday(DayOfWeek dow)
        {
            if (Program.IsEn)
            {
                switch (dow)
                {
                    case DayOfWeek.Sunday: return "Sunday";
                    case DayOfWeek.Monday: return "Monday";
                    case DayOfWeek.Tuesday: return "Tuesday";
                    case DayOfWeek.Wednesday: return "Wednesday";
                    case DayOfWeek.Thursday: return "Thursday";
                    case DayOfWeek.Friday: return "Friday";
                    case DayOfWeek.Saturday: return "Saturday";
                    default: return "";
                }
            }
            switch (dow)
            {
                case DayOfWeek.Sunday: return "星期日";
                case DayOfWeek.Monday: return "星期一";
                case DayOfWeek.Tuesday: return "星期二";
                case DayOfWeek.Wednesday: return "星期三";
                case DayOfWeek.Thursday: return "星期四";
                case DayOfWeek.Friday: return "星期五";
                case DayOfWeek.Saturday: return "星期六";
                default: return "";
            }
        }

        /// <summary>把保存的地点名拆成中文/英文两部分；旧格式（无斜杠）按本地/拼音兜底。
        /// 英文保留原拼写（区分大小写），拼音兜底为首字母大写（Title Case）。</summary>
        static void SplitName(string name, out string cn, out string en)
        {
            int ix = name == null ? -1 : name.IndexOf('/');
            if (ix > 0 && ix < name.Length - 1)
            {
                cn = name.Substring(0, ix);
                en = name.Substring(ix + 1);
                if (cn == "本地" || cn == "本地时间") { cn = "本地"; en = "Local"; }
                return;
            }
            cn = (name == null || name.Length == 0) ? "本地" : name;
            if (cn == "本地" || cn == "本地时间") { cn = "本地"; en = "Local"; return; }
            if (TimeZoneMatcher.HasCjk(cn)) en = TimeZoneMatcher.PinyinTitle(cn);
            else en = cn;
        }

        /// <summary>离屏渲染到文件（自检用；透明区以品红呈现便于检查）</summary>
        public void RenderToFile(string path)
        {
            int w = (int)(BaseW * Entry.Scale), h = (int)(BaseH * Entry.Scale);
            using (Bitmap b = new Bitmap(w, h, PixelFormat.Format32bppArgb))
            using (Graphics g = Graphics.FromImage(b))
            {
                PaintAll(new PaintEventArgs(g, new Rectangle(0, 0, w, h)));
                b.Save(path, ImageFormat.Png);
            }
        }
    }
}
