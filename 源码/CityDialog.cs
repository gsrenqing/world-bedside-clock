using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace WorldBedsideClock
{
    /// <summary>添加/设置城市：输入城市名 → 匹配时区 → 点选结果</summary>
    public class CityDialog : Form
    {
        TextBox _edit;
        bool _isAdd;
        string _cityName;

        CityDialog(bool isAdd)
        {
            _isAdd = isAdd;
            bool en = Program.IsEn;
            Text = isAdd ? (en ? "Add Location" : "添加地点") : (en ? "Set Location" : "设置地点");
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(380, 150);
            Font = new Font("微软雅黑", 9f);

            Label tip = new Label();
            tip.Text = en
                ? "Enter a location name (Chinese or English),\nthen click \"Match\" to find its time zone automatically:"
                : "请输入地点名称（中文或英文均可），\n然后点击“匹配”按地点自动匹配所在时区：";
            tip.SetBounds(14, 12, 352, 40);

            _edit = new TextBox();
            _edit.SetBounds(14, 60, 352, 26);
            _edit.Font = new Font("黑体", 12f);
            // BUG-1：回车仅由 AcceptButton=bMatch 触发一次；不再在 KeyDown 里重复调 DoMatch，避免双开对话框

            Button bMatch = new Button();
            bMatch.Text = en ? "Match" : "匹配";
            bMatch.SetBounds(150, 104, 100, 32);
            bMatch.Click += delegate(object s, EventArgs e) { DoMatch(); };

            Button bCancel = new Button();
            bCancel.Text = en ? "Cancel" : "取消";
            bCancel.SetBounds(262, 104, 100, 32);
            bCancel.Click += delegate(object s, EventArgs e) { DialogResult = DialogResult.Cancel; Close(); };

            Controls.Add(tip);
            Controls.Add(_edit);
            Controls.Add(bMatch);
            Controls.Add(bCancel);
            AcceptButton = bMatch;
            CancelButton = bCancel;
            Load += delegate(object s, EventArgs e) { _edit.Focus(); };
        }

        void DoMatch()
        {
            string raw = _edit.Text.Trim();
            if (raw.Length == 0)
            {
                MessageBox.Show(Program.IsEn ? "Please enter a location name." : "请输入地点名称。",
                    "World bedside clock", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            // 5.5：解除 7 字/15 字母显示限制；仅防御超长输入防匹配卡顿
            if (raw.Length > 40) raw = raw.Substring(0, 40);
            _cityName = raw;

            List<MatchResult> res = TimeZoneMatcher.Match(raw);
            if (res.Count == 0)
            {
                // 自动匹配失败 → 手动指定时区列表兜底（按 UTC 偏移排序，可过滤）
                ManualTzDialog dlg = new ManualTzDialog(raw);
                if (dlg.ShowDialog(this) == DialogResult.OK && dlg.Selected != null)
                {
                    DialogResult = DialogResult.OK;
                    Finish(dlg.Selected);
                    Close();
                }
                return;
            }
            SelectTzDialog dlg2 = new SelectTzDialog(raw, res);
            if (dlg2.ShowDialog(this) == DialogResult.OK && dlg2.Selected != null)
            {
                DialogResult = DialogResult.OK;
                Finish(dlg2.Selected);
                Close();
            }
        }

        void Finish(MatchResult mr)
        {
            // 5.6.3：以"中文/英文原拼写"格式保存并显示（区分大小写，不再一律大写）
            string displayName = mr.Cn + "/" + mr.En;
            if (_isAdd)
            {
                CityEntry ce = new CityEntry();
                ce.Name = displayName;
                ce.TzId = mr.TzId;
                // 继承"前一实例"（右键点击添加城市所在实例）的全部设定；
                // 仅位置做偏移，而非每次从全部默认开始
                if (_src != null)
                {
                    ce.Theme = _src.Entry.Theme;
                    ce.Scale = _src.Entry.Scale;
                    ce.Opacity = _src.Entry.Opacity;
                    ce.Topmost = _src.Entry.Topmost;
                    ce.Speed = _src.Entry.Speed;
                    if (_src.Entry.X >= 0 && _src.Entry.Y >= 0)
                    {
                        ce.X = _src.Entry.X + 30;
                        ce.Y = _src.Entry.Y + 30;
                    }
                    else { ce.X = -1; ce.Y = -1; }
                }
                Program.CreateWindow(ce);
            }
            else
            {
                _target.ApplyCity(displayName, mr.TzId);
            }
        }

        ClockForm _target;
        ClockForm _src;

        public static void DoAdd(IWin32Window owner)
        {
            CityDialog d = new CityDialog(true);
            d._src = owner as ClockForm;
            d.ShowDialog(owner);
        }

        public static void DoSet(ClockForm target)
        {
            CityDialog d = new CityDialog(false);
            d._target = target;
            d.ShowDialog(target);
        }
    }

    /// <summary>匹配结果点选对话框</summary>
    public class SelectTzDialog : Form
    {
        public MatchResult Selected;

        public SelectTzDialog(string city, List<MatchResult> results)
        {
            bool en = Program.IsEn;
            Text = en ? "Matches: " + city : "匹配结果：" + city;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(440, 260);
            Font = new Font("微软雅黑", 9f);

            Label tip = new Label();
            tip.Text = en
                ? "Found " + results.Count + " matches. Select one and click \"OK\":"
                : "找到 " + results.Count + " 个匹配结果，请点选一项后点“确定”：";
            tip.SetBounds(14, 12, 412, 22);

            ListBox lb = new ListBox();
            lb.SetBounds(14, 42, 412, 170);
            lb.Font = new Font("微软雅黑", 9.5f);
            foreach (MatchResult r in results) lb.Items.Add(r);
            lb.SelectedIndex = 0;

            Button bOk = new Button();
            bOk.Text = en ? "OK" : "确定";
            bOk.SetBounds(160, 222, 110, 30);
            bOk.Click += delegate(object s, EventArgs e)
            {
                if (lb.SelectedItem != null) Selected = (MatchResult)lb.SelectedItem;
                DialogResult = DialogResult.OK;
                Close();
            };

            Button bNo = new Button();
            bNo.Text = en ? "Cancel" : "取消";
            bNo.SetBounds(282, 222, 110, 30);
            bNo.Click += delegate(object s, EventArgs e) { DialogResult = DialogResult.Cancel; Close(); };

            Controls.Add(tip);
            Controls.Add(lb);
            Controls.Add(bOk);
            Controls.Add(bNo);
            AcceptButton = bOk;
            CancelButton = bNo;
            lb.DoubleClick += delegate(object s, EventArgs e) { bOk.PerformClick(); };
        }
    }

    /// <summary>自动匹配失败后的手动指定时区对话框：
    /// 列出 Windows 全部时区，按当前 UTC 偏移排序，带过滤框；选中即确定。</summary>
    public class ManualTzDialog : Form
    {
        public MatchResult Selected;
        List<TimeZoneInfo> _all = new List<TimeZoneInfo>();
        TextBox _filter;
        string _placeholder;

        public ManualTzDialog(string name)
        {
            bool en = Program.IsEn;
            Text = en ? "No match found · Select time zone manually" : "未找到匹配 · 请手动指定时区";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(480, 320);
            Font = new Font("微软雅黑", 9f);

            Label tip = new Label();
            tip.Text = en
                ? "\"" + name + "\" was not matched. Please choose a time zone from the list below:"
                : "“" + name + "”未在时区库中匹配到，请在下方时区列表中手动选择：";
            tip.SetBounds(14, 12, 452, 22);

            _filter = new TextBox();
            _filter.SetBounds(14, 42, 452, 24);
            _placeholder = en ? "Type keyword to filter…" : "输入关键字过滤…";
            _filter.Text = _placeholder;
            _filter.ForeColor = SystemColors.GrayText;
            _filter.Enter += delegate(object s, EventArgs e) { if (_filter.ForeColor == SystemColors.GrayText) { _filter.Text = ""; _filter.ForeColor = SystemColors.WindowText; } };
            _filter.TextChanged += delegate(object s, EventArgs e) { Reload(); };

            _list = new ListBox();
            _list.SetBounds(14, 74, 452, 190);
            _list.Font = new Font("微软雅黑", 9f);
            _list.SelectedIndexChanged += delegate(object s, EventArgs e) { _ok.Enabled = _list.SelectedItem != null; };

            _ok = new Button();
            _ok.Text = en ? "OK" : "确定";
            _ok.SetBounds(160, 276, 130, 32);
            _ok.Enabled = false;
            _ok.Click += delegate(object s, EventArgs e)
            {
                TimeZoneInfo tz = _list.SelectedItem as TimeZoneInfo;
                if (tz == null) return;
                TimeSpan off = tz.GetUtcOffset(DateTime.UtcNow);
                // BUG-3：按总分钟拼 hh:mm，修正半小时/三刻钟时区偏移被四舍五入
                int totalMin = (int)Math.Round(off.TotalMinutes);
                string sign = totalMin >= 0 ? "+" : "-";
                int abs = Math.Abs(totalMin);
                int oh = abs / 60, om = abs % 60;
                string offStr = sign + oh.ToString("00") + ":" + (om == 0 ? "00" : om.ToString("00"));
                bool cjk = TimeZoneMatcher.HasCjk(name);
                string enName = cjk ? TimeZoneMatcher.PinyinTitle(name) : name;
                MatchResult mr = new MatchResult();
                mr.Display = name + "/" + enName + " · (UTC" + offStr + ") " + tz.DisplayName;
                mr.TzId = tz.Id;
                mr.Cn = name;
                mr.En = enName;
                Selected = mr;
                DialogResult = DialogResult.OK;
                Close();
            };

            Button bNo = new Button();
            bNo.Text = en ? "Cancel" : "取消";
            bNo.SetBounds(300, 276, 130, 32);
            bNo.Click += delegate(object s, EventArgs e) { DialogResult = DialogResult.Cancel; Close(); };

            Controls.Add(tip);
            Controls.Add(_filter);
            Controls.Add(_list);
            Controls.Add(_ok);
            Controls.Add(bNo);
            CancelButton = bNo;
            AcceptButton = _ok;
            _list.DoubleClick += delegate(object s, EventArgs e) { if (_list.SelectedItem != null) _ok.PerformClick(); };
            Load += delegate(object s, EventArgs e) { InitList(); };
        }

        ListBox _list;
        Button _ok;

        void InitList()
        {
            _all.Clear();
            foreach (TimeZoneInfo tz in TimeZoneInfo.GetSystemTimeZones())
                _all.Add(tz);
            // 按当前 UTC 偏移排序（DST 感知），偏移相同按标准名
            _all.Sort(delegate(TimeZoneInfo a, TimeZoneInfo b)
            {
                int c = a.GetUtcOffset(DateTime.UtcNow).CompareTo(b.GetUtcOffset(DateTime.UtcNow));
                if (c != 0) return c;
                return string.Compare(a.StandardName, b.StandardName, StringComparison.OrdinalIgnoreCase);
            });
            Reload();
        }

        void Reload()
        {
            _list.Items.Clear();
            string kw = _filter.Text.Trim().ToLower();
            foreach (TimeZoneInfo tz in _all)
            {
                if (kw.Length == 0 || kw == _placeholder.Trim().ToLower()) { _list.Items.Add(tz); continue; }
                string dn = tz.DisplayName != null ? tz.DisplayName.ToLower() : "";
                string sn = tz.StandardName != null ? tz.StandardName.ToLower() : "";
                if (dn.IndexOf(kw) >= 0 || sn.IndexOf(kw) >= 0) _list.Items.Add(tz);
            }
            _ok.Enabled = _list.Items.Count > 0 && _list.SelectedItem != null;
        }
    }
}
