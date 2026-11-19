using System;
using System.Collections.Generic;

namespace WorldBedsideClock
{
    /// <summary>匹配逻辑控制台自检（/main 指定此入口，绕过 Program.Main）</summary>
    public static class TestMain
    {
        [STAThread]
        static void Main()
        {
            ResourceBox.LogPath = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location), "test.log");
            ResourceBox.Load();
            string[] cases = {
                "亚的斯亚贝巴", "泸定县", "康定", "甘孜", "Woodinville", "Seattle",
                "美国", "华盛顿州", "龘州", "北京", "西雅图", "Addis Ababa", "yading",
                "阿维尼翁", "甘孜藏族自治州", "科隆", "乌兰巴托", "圣托里尼", "洛杉矶",
                "LONDON", "Koln", "Avignon", "Ulan Bator", "santorini",
                "Garzê", "Garzê Tibetan Autonomous Prefecture", "阿坝藏族羌族自治州",
                "延边朝鲜族自治州", "伊犁哈萨克自治州", "凉山彝族自治州", "成都市", "泸州市",
                "甘孜州", "阿坝州", "凉山州", "巴音郭勒", "中华人民共和国", "中国",
                "大韩民国", "美利坚合众国", "大不列颠及北爱尔兰联合王国",
                "朝鲜民主主义人民共和国", "朝鲜民主主义共和国", "刚果民主共和国"
            };
            foreach (string s in cases)
            {
                List<MatchResult> r = TimeZoneMatcher.Match(s);
                System.Text.StringBuilder sb = new System.Text.StringBuilder();
                foreach (MatchResult x in r) { if (sb.Length > 0) sb.Append(" | "); sb.Append(x.Cn + "/" + x.En + " [" + x.TzId + "]"); }
                Console.WriteLine(s + " => " + r.Count + ": " + sb.ToString());
            }
        }
    }
}
