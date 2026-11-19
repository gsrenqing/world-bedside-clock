using System;
using System.Collections.Generic;

namespace WorldBedsideClock
{
    public class MatchResult
    {
        public string Display;
        public string TzId;
        public string Cn;   // 中文名（无中文名时为英文名）
        public string En;   // 英文名/拼音（原文大小写，显示端转大写）
        public override string ToString() { return Display; }
    }

    /// <summary>地点-时区匹配（5.5 全量版）：
    /// ① 主要城市中英文双语表（代码内置 + capitals.txt 资源，覆盖全球首都/大城市）
    /// ② 中国全量区县（chinadist.txt，民政部区划代码，约 2900 个）
    /// ③ 境外人口>1.5 万城市（worldcities2.txt，GeoNames，name+asciiname 双键约 3.3 万个）
    /// ④ 中英对照表（cn_table.txt，1262 条常用城市中文译名 × GeoNames 全别名匹配 + 65 条旅游景点，中文|英文|wid）
    /// ⑤ 中国省/地级（cn_admin.txt，民政部省+337 地级）
    /// ⑥ 国家表（countries.txt）+ 州/省表（states.txt）
    /// ⑦ 汉字拼音表（pinyin.txt，无英文名地点用拼音大写兜底，如 泸定县 → LU DING）
    /// 中文/英文同时匹配，多候选（同名多时区/多地点）列出由用户点选；
    /// 每个结果同时给出中文名与英文名（显示 "中文/英文"）。
    /// 匹配失败由调用方（CityDialog）弹手动指定时区列表兜底。</summary>
    public static class TimeZoneMatcher
    {
        class Row { public string Cn; public string En; public string Tz; public Row(string c, string e, string t) { Cn = c; En = e; Tz = t; } }
        class NatRow { public string Cn; public string En; public string[] Tzs; public NatRow(string c, string e, string[] t) { Cn = c; En = e; Tzs = t; } }
        class StateRow { public string Cn; public string En; public string Tz; public StateRow(string c, string e, string t) { Cn = c; En = e; Tz = t; } }
        class CnRow { public string Cn; public string En; public string Tz; public CnRow(string c, string e, string t) { Cn = c; En = e; Tz = t; } }

        static List<Row> _major = new List<Row>();
        static List<string> _cnNames = new List<string>();
        static Dictionary<string, string[]> _world = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase); // norm键 -> [显示名, wid]
        static Dictionary<string, List<CnRow>> _cnWorld = new Dictionary<string, List<CnRow>>(StringComparer.OrdinalIgnoreCase); // 中文名 -> [(中文名,英文名, wid)]
        static Dictionary<string, List<CnRow>> _cnEnWorld = new Dictionary<string, List<CnRow>>(StringComparer.OrdinalIgnoreCase); // 英文名(规范键) -> [(中文名,英文名, wid)]（景点等 GeoNames 未收录）
        static Dictionary<string, string> _cnAdmin = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase); // 省/地级 -> wid
        static Dictionary<string, string> _pinyin = new Dictionary<string, string>(); // 汉字 -> 拼音
        static Dictionary<string, string> _cnEn = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase); // 英文名 -> 中文名（反查）
        static List<NatRow> _nat = new List<NatRow>();
        static List<StateRow> _st = new List<StateRow>();
        static Dictionary<string, string> _ruleCn = new Dictionary<string, string>();

        /// <summary>常见误写/别称 → 标准地名（如 巴音郭勒→巴音郭楞蒙古自治州）</summary>
        static readonly Dictionary<string, string> _aliasCn = new Dictionary<string, string> {
            { "巴音郭勒", "巴音郭楞蒙古自治州" }
        };

        /// <summary>国家全称 → 简称（简称必须与 countries.txt 首列完全一致）；
        /// 解决全称与简称无重叠关键词时的匹配失败（大韩民国→韩国、美利坚合众国→美国、
        /// 大不列颠及北爱尔兰联合王国→英国 等）。</summary>
        static readonly Dictionary<string, string> _natFull = new Dictionary<string, string> {
            { "中华人民共和国", "中国" },
            { "日本国", "日本" },
            { "大韩民国", "韩国" },
            { "朝鲜民主主义人民共和国", "朝鲜" },
            { "朝鲜民主主义共和国", "朝鲜" },
            { "蒙古国", "蒙古" },
            { "新加坡共和国", "新加坡" },
            { "马来西亚联邦", "马来西亚" },
            { "印度尼西亚共和国", "印度尼西亚" },
            { "泰王国", "泰国" },
            { "越南社会主义共和国", "越南" },
            { "柬埔寨王国", "柬埔寨" },
            { "老挝人民民主共和国", "老挝" },
            { "缅甸联邦共和国", "缅甸" },
            { "菲律宾共和国", "菲律宾" },
            { "印度共和国", "印度" },
            { "巴基斯坦伊斯兰共和国", "巴基斯坦" },
            { "孟加拉人民共和国", "孟加拉国" },
            { "斯里兰卡民主社会主义共和国", "斯里兰卡" },
            { "尼泊尔联邦民主共和国", "尼泊尔" },
            { "不丹王国", "不丹" },
            { "马尔代夫共和国", "马尔代夫" },
            { "阿富汗伊斯兰共和国", "阿富汗" },
            { "阿富汗伊斯兰酋长国", "阿富汗" },
            { "哈萨克斯坦共和国", "哈萨克斯坦" },
            { "乌兹别克斯坦共和国", "乌兹别克斯坦" },
            { "塔吉克斯坦共和国", "塔吉克斯坦" },
            { "吉尔吉斯共和国", "吉尔吉斯斯坦" },
            { "伊朗伊斯兰共和国", "伊朗" },
            { "伊拉克共和国", "伊拉克" },
            { "沙特阿拉伯王国", "沙特阿拉伯" },
            { "阿拉伯联合酋长国", "阿联酋" },
            { "卡塔尔国", "卡塔尔" },
            { "科威特国", "科威特" },
            { "巴林王国", "巴林" },
            { "阿曼苏丹国", "阿曼" },
            { "也门共和国", "也门" },
            { "约旦哈希姆王国", "约旦" },
            { "黎巴嫩共和国", "黎巴嫩" },
            { "阿拉伯叙利亚共和国", "叙利亚" },
            { "以色列国", "以色列" },
            { "巴勒斯坦国", "巴勒斯坦" },
            { "土耳其共和国", "土耳其" },
            { "塞浦路斯共和国", "塞浦路斯" },
            { "阿塞拜疆共和国", "阿塞拜疆" },
            { "亚美尼亚共和国", "亚美尼亚" },
            { "文莱达鲁萨兰国", "文莱" },
            { "东帝汶民主共和国", "东帝汶" },
            { "大不列颠及北爱尔兰联合王国", "英国" },
            { "法兰西共和国", "法国" },
            { "德意志联邦共和国", "德国" },
            { "意大利共和国", "意大利" },
            { "西班牙王国", "西班牙" },
            { "葡萄牙共和国", "葡萄牙" },
            { "荷兰王国", "荷兰" },
            { "比利时王国", "比利时" },
            { "卢森堡大公国", "卢森堡" },
            { "瑞士联邦", "瑞士" },
            { "奥地利共和国", "奥地利" },
            { "丹麦王国", "丹麦" },
            { "挪威王国", "挪威" },
            { "瑞典王国", "瑞典" },
            { "芬兰共和国", "芬兰" },
            { "冰岛共和国", "冰岛" },
            { "俄罗斯联邦", "俄罗斯" },
            { "白俄罗斯共和国", "白俄罗斯" },
            { "波兰共和国", "波兰" },
            { "捷克共和国", "捷克" },
            { "斯洛伐克共和国", "斯洛伐克" },
            { "保加利亚共和国", "保加利亚" },
            { "希腊共和国", "希腊" },
            { "塞尔维亚共和国", "塞尔维亚" },
            { "克罗地亚共和国", "克罗地亚" },
            { "斯洛文尼亚共和国", "斯洛文尼亚" },
            { "波斯尼亚和黑塞哥维那", "波黑" },
            { "北马其顿共和国", "北马其顿" },
            { "阿尔巴尼亚共和国", "阿尔巴尼亚" },
            { "爱沙尼亚共和国", "爱沙尼亚" },
            { "拉脱维亚共和国", "拉脱维亚" },
            { "立陶宛共和国", "立陶宛" },
            { "摩尔多瓦共和国", "摩尔多瓦" },
            { "马耳他共和国", "马耳他" },
            { "摩纳哥公国", "摩纳哥" },
            { "安道尔公国", "安道尔" },
            { "列支敦士登公国", "列支敦士登" },
            { "圣马力诺共和国", "圣马力诺" },
            { "梵蒂冈城国", "梵蒂冈" },
            { "阿拉伯埃及共和国", "埃及" },
            { "利比亚国", "利比亚" },
            { "阿尔及利亚民主人民共和国", "阿尔及利亚" },
            { "摩洛哥王国", "摩洛哥" },
            { "突尼斯共和国", "突尼斯" },
            { "尼日利亚联邦共和国", "尼日利亚" },
            { "南非共和国", "南非" },
            { "肯尼亚共和国", "肯尼亚" },
            { "埃塞俄比亚联邦民主共和国", "埃塞俄比亚" },
            { "坦桑尼亚联合共和国", "坦桑尼亚" },
            { "乌干达共和国", "乌干达" },
            { "加纳共和国", "加纳" },
            { "科特迪瓦共和国", "科特迪瓦" },
            { "塞内加尔共和国", "塞内加尔" },
            { "喀麦隆共和国", "喀麦隆" },
            { "安哥拉共和国", "安哥拉" },
            { "苏丹共和国", "苏丹" },
            { "南苏丹共和国", "南苏丹" },
            { "刚果民主共和国", "刚果金" },
            { "刚果共和国", "刚果布" },
            { "赞比亚共和国", "赞比亚" },
            { "津巴布韦共和国", "津巴布韦" },
            { "莫桑比克共和国", "莫桑比克" },
            { "博茨瓦纳共和国", "博茨瓦纳" },
            { "纳米比亚共和国", "纳米比亚" },
            { "卢旺达共和国", "卢旺达" },
            { "马里共和国", "马里" },
            { "尼日尔共和国", "尼日尔" },
            { "乍得共和国", "乍得" },
            { "索马里联邦共和国", "索马里" },
            { "吉布提共和国", "吉布提" },
            { "厄立特里亚国", "厄立特里亚" },
            { "毛里求斯共和国", "毛里求斯" },
            { "塞舌尔共和国", "塞舌尔" },
            { "马达加斯加共和国", "马达加斯加" },
            { "美利坚合众国", "美国" },
            { "墨西哥合众国", "墨西哥" },
            { "巴西联邦共和国", "巴西" },
            { "阿根廷共和国", "阿根廷" },
            { "智利共和国", "智利" },
            { "秘鲁共和国", "秘鲁" },
            { "哥伦比亚共和国", "哥伦比亚" },
            { "委内瑞拉玻利瓦尔共和国", "委内瑞拉" },
            { "多民族玻利维亚国", "玻利维亚" },
            { "厄瓜多尔共和国", "厄瓜多尔" },
            { "乌拉圭东岸共和国", "乌拉圭" },
            { "巴拉圭共和国", "巴拉圭" },
            { "古巴共和国", "古巴" },
            { "巴拿马共和国", "巴拿马" },
            { "哥斯达黎加共和国", "哥斯达黎加" },
            { "危地马拉共和国", "危地马拉" },
            { "萨尔瓦多共和国", "萨尔瓦多" },
            { "洪都拉斯共和国", "洪都拉斯" },
            { "尼加拉瓜共和国", "尼加拉瓜" },
            { "巴哈马国", "巴哈马" },
            { "多米尼加共和国", "多米尼加" },
            { "海地共和国", "海地" },
            { "波多黎各自由邦", "波多黎各" },
            { "特立尼达和多巴哥共和国", "特立尼达" },
            { "圭亚那合作共和国", "圭亚那" },
            { "苏里南共和国", "苏里南" },
            { "澳大利亚联邦", "澳大利亚" },
            { "斐济共和国", "斐济" },
            { "巴布亚新几内亚独立国", "巴布亚新几内亚" },
            { "汤加王国", "汤加" },
            { "萨摩亚独立国", "萨摩亚" },
            { "瓦努阿图共和国", "瓦努阿图" },
            { "基里巴斯共和国", "基里巴斯" },
            { "帕劳共和国", "帕劳" },
            { "密克罗尼西亚联邦", "密克罗尼西亚" },
            { "马绍尔群岛共和国", "马绍尔群岛" },
            { "瑙鲁共和国", "瑙鲁" }
        };

        /// <summary>中国 30 个自治州标准英文名（沿用 VU 音量表英文说明的官方译法）；
        /// 全部使用纯 ASCII 字符且为常规词首大写（甘孜不写 Garzê，避免 ê 变音符
        /// 触发内置字库缺字黑体替代导致错位；英文名保留原拼写（区分大小写）。
        /// 州以外地级市仍用拼音兜底（PinyinTitle，首字母大写）。</summary>
        static readonly Dictionary<string, string> _zhouEn = new Dictionary<string, string> {
            { "甘孜藏族自治州", "Garze Tibetan Autonomous Prefecture" },
            { "阿坝藏族羌族自治州", "Ngawa Tibetan and Qiang Autonomous Prefecture" },
            { "凉山彝族自治州", "Liangshan Yi Autonomous Prefecture" },
            { "延边朝鲜族自治州", "Yanbian Korean Autonomous Prefecture" },
            { "恩施土家族苗族自治州", "Enshi Tujia and Miao Autonomous Prefecture" },
            { "湘西土家族苗族自治州", "Xiangxi Tujia and Miao Autonomous Prefecture" },
            { "黔东南苗族侗族自治州", "Qiandongnan Miao and Dong Autonomous Prefecture" },
            { "黔南布依族苗族自治州", "Qiannan Buyei and Miao Autonomous Prefecture" },
            { "黔西南布依族苗族自治州", "Qianxinan Buyei and Miao Autonomous Prefecture" },
            { "大理白族自治州", "Dali Bai Autonomous Prefecture" },
            { "楚雄彝族自治州", "Chuxiong Yi Autonomous Prefecture" },
            { "红河哈尼族彝族自治州", "Honghe Hani and Yi Autonomous Prefecture" },
            { "文山壮族苗族自治州", "Wenshan Zhuang and Miao Autonomous Prefecture" },
            { "西双版纳傣族自治州", "Xishuangbanna Dai Autonomous Prefecture" },
            { "德宏傣族景颇族自治州", "Dehong Dai and Jingpo Autonomous Prefecture" },
            { "怒江傈僳族自治州", "Nujiang Lisu Autonomous Prefecture" },
            { "迪庆藏族自治州", "Diqing Tibetan Autonomous Prefecture" },
            { "临夏回族自治州", "Linxia Hui Autonomous Prefecture" },
            { "甘南藏族自治州", "Gannan Tibetan Autonomous Prefecture" },
            { "海北藏族自治州", "Haibei Tibetan Autonomous Prefecture" },
            { "黄南藏族自治州", "Huangnan Tibetan Autonomous Prefecture" },
            { "海南藏族自治州", "Hainan Tibetan Autonomous Prefecture" },
            { "果洛藏族自治州", "Golog Tibetan Autonomous Prefecture" },
            { "玉树藏族自治州", "Yushu Tibetan Autonomous Prefecture" },
            { "海西蒙古族藏族自治州", "Haixi Mongol and Tibetan Autonomous Prefecture" },
            { "博尔塔拉蒙古自治州", "Bortala Mongol Autonomous Prefecture" },
            { "巴音郭楞蒙古自治州", "Bayingolin Mongol Autonomous Prefecture" },
            { "昌吉回族自治州", "Changji Hui Autonomous Prefecture" },
            { "伊犁哈萨克自治州", "Ili Kazakh Autonomous Prefecture" },
            { "克孜勒苏柯尔克孜自治州", "Kizilsu Kirgiz Autonomous Prefecture" }
        };

        static TimeZoneMatcher()
        {
            // ---- ① 代码内置主要城市（保留原 160 条）----
            Add("北京", "Beijing", "China Standard Time");
            Add("上海", "Shanghai", "China Standard Time");
            Add("广州", "Guangzhou", "China Standard Time");
            Add("深圳", "Shenzhen", "China Standard Time");
            Add("成都", "Chengdu", "China Standard Time");
            Add("重庆", "Chongqing", "China Standard Time");
            Add("杭州", "Hangzhou", "China Standard Time");
            Add("武汉", "Wuhan", "China Standard Time");
            Add("西安", "Xi'an", "China Standard Time");
            Add("南京", "Nanjing", "China Standard Time");
            Add("天津", "Tianjin", "China Standard Time");
            Add("苏州", "Suzhou", "China Standard Time");
            Add("青岛", "Qingdao", "China Standard Time");
            Add("大连", "Dalian", "China Standard Time");
            Add("厦门", "Xiamen", "China Standard Time");
            Add("昆明", "Kunming", "China Standard Time");
            Add("长沙", "Changsha", "China Standard Time");
            Add("郑州", "Zhengzhou", "China Standard Time");
            Add("沈阳", "Shenyang", "China Standard Time");
            Add("哈尔滨", "Harbin", "China Standard Time");
            Add("乌鲁木齐", "Urumqi", "China Standard Time");
            Add("拉萨", "Lhasa", "China Standard Time");
            Add("香港", "Hong Kong", "China Standard Time");
            Add("澳门", "Macau", "China Standard Time");
            Add("台北", "Taipei", "China Standard Time");
            Add("高雄", "Kaohsiung", "China Standard Time");

            Add("东京", "Tokyo", "Tokyo Standard Time");
            Add("大阪", "Osaka", "Tokyo Standard Time");
            Add("京都", "Kyoto", "Tokyo Standard Time");
            Add("名古屋", "Nagoya", "Tokyo Standard Time");
            Add("首尔", "Seoul", "Korea Standard Time");
            Add("釜山", "Busan", "Korea Standard Time");
            Add("新加坡", "Singapore", "Singapore Standard Time");
            Add("吉隆坡", "Kuala Lumpur", "Singapore Standard Time");
            Add("雅加达", "Jakarta", "SE Asia Standard Time");
            Add("曼谷", "Bangkok", "SE Asia Standard Time");
            Add("胡志明市", "Ho Chi Minh City", "SE Asia Standard Time");
            Add("河内", "Hanoi", "SE Asia Standard Time");
            Add("马尼拉", "Manila", "Singapore Standard Time");
            Add("仰光", "Rangoon", "Myanmar Standard Time");
            Add("新德里", "New Delhi", "India Standard Time");
            Add("孟买", "Mumbai", "India Standard Time");
            Add("班加罗尔", "Bangalore", "India Standard Time");
            Add("卡拉奇", "Karachi", "Pakistan Standard Time");
            Add("科伦坡", "Colombo", "Sri Lanka Standard Time");
            Add("迪拜", "Dubai", "Arabian Standard Time");
            Add("阿布扎比", "Abu Dhabi", "Arabian Standard Time");
            Add("利雅得", "Riyadh", "Arab Standard Time");
            Add("多哈", "Doha", "Arab Standard Time");
            Add("特拉维夫", "Tel Aviv", "Israel Standard Time");
            Add("耶路撒冷", "Jerusalem", "Israel Standard Time");
            Add("伊斯坦布尔", "Istanbul", "Turkey Standard Time");
            Add("安卡拉", "Ankara", "Turkey Standard Time");
            Add("德黑兰", "Tehran", "Iran Standard Time");
            Add("莫斯科", "Moscow", "Russian Standard Time");
            Add("圣彼得堡", "St Petersburg", "Russian Standard Time");
            Add("伦敦", "London", "GMT Standard Time");
            Add("曼彻斯特", "Manchester", "GMT Standard Time");
            Add("巴黎", "Paris", "Romance Standard Time");
            Add("柏林", "Berlin", "W. Europe Standard Time");
            Add("慕尼黑", "Munich", "W. Europe Standard Time");
            Add("法兰克福", "Frankfurt", "W. Europe Standard Time");
            Add("罗马", "Rome", "W. Europe Standard Time");
            Add("米兰", "Milan", "W. Europe Standard Time");
            Add("马德里", "Madrid", "Romance Standard Time");
            Add("巴塞罗那", "Barcelona", "Romance Standard Time");
            Add("里斯本", "Lisbon", "GMT Standard Time");
            Add("阿姆斯特丹", "Amsterdam", "W. Europe Standard Time");
            Add("布鲁塞尔", "Brussels", "Romance Standard Time");
            Add("苏黎世", "Zurich", "W. Europe Standard Time");
            Add("日内瓦", "Geneva", "W. Europe Standard Time");
            Add("伯尔尼", "Bern", "W. Europe Standard Time");
            Add("维也纳", "Vienna", "W. Europe Standard Time");
            Add("斯德哥尔摩", "Stockholm", "W. Europe Standard Time");
            Add("哥本哈根", "Copenhagen", "Romance Standard Time");
            Add("奥斯陆", "Oslo", "W. Europe Standard Time");
            Add("赫尔辛基", "Helsinki", "FLE Standard Time");
            Add("雅典", "Athens", "GTB Standard Time");
            Add("华沙", "Warsaw", "Central European Standard Time");
            Add("布拉格", "Prague", "Central European Standard Time");
            Add("布达佩斯", "Budapest", "Central European Standard Time");
            Add("基辅", "Kiev", "FLE Standard Time");
            Add("布加勒斯特", "Bucharest", "GTB Standard Time");
            Add("开罗", "Cairo", "Egypt Standard Time");
            Add("约翰内斯堡", "Johannesburg", "South Africa Standard Time");
            Add("开普敦", "Cape Town", "South Africa Standard Time");
            Add("内罗毕", "Nairobi", "E. Africa Standard Time");
            Add("拉各斯", "Lagos", "W. Central Africa Standard Time");
            Add("纽约", "New York", "Eastern Standard Time");
            Add("华盛顿", "Washington", "Eastern Standard Time");
            Add("波士顿", "Boston", "Eastern Standard Time");
            Add("迈阿密", "Miami", "Eastern Standard Time");
            Add("芝加哥", "Chicago", "Central Standard Time");
            Add("休斯顿", "Houston", "Central Standard Time");
            Add("达拉斯", "Dallas", "Central Standard Time");
            Add("丹佛", "Denver", "Mountain Standard Time");
            Add("盐湖城", "Salt Lake City", "Mountain Standard Time");
            Add("凤凰城", "Phoenix", "US Mountain Standard Time");
            Add("洛杉矶", "Los Angeles", "Pacific Standard Time");
            Add("旧金山", "San Francisco", "Pacific Standard Time");
            Add("西雅图", "Seattle", "Pacific Standard Time");
            Add("拉斯维加斯", "Las Vegas", "Pacific Standard Time");
            Add("圣地亚哥", "San Diego", "Pacific Standard Time");
            Add("檀香山", "Honolulu", "Hawaiian Standard Time");
            Add("安克雷奇", "Anchorage", "Alaskan Standard Time");
            Add("多伦多", "Toronto", "Eastern Standard Time");
            Add("渥太华", "Ottawa", "Eastern Standard Time");
            Add("蒙特利尔", "Montreal", "Eastern Standard Time");
            Add("温哥华", "Vancouver", "Pacific Standard Time");
            Add("卡尔加里", "Calgary", "Mountain Standard Time");
            Add("埃德蒙顿", "Edmonton", "Mountain Standard Time");
            Add("温尼伯", "Winnipeg", "Central Standard Time");
            Add("墨西哥城", "Mexico City", "Central Standard Time");
            Add("蒂华纳", "Tijuana", "Pacific Standard Time");
            Add("坎昆", "Cancun", "Eastern Standard Time");
            Add("圣保罗", "Sao Paulo", "E. South America Standard Time");
            Add("里约热内卢", "Rio de Janeiro", "E. South America Standard Time");
            Add("布宜诺斯艾利斯", "Buenos Aires", "Argentina Standard Time");
            Add("圣地亚哥", "Santiago", "Pacific SA Standard Time");
            Add("利马", "Lima", "SA Pacific Standard Time");
            Add("波哥大", "Bogota", "SA Pacific Standard Time");
            Add("悉尼", "Sydney", "AUS Eastern Standard Time");
            Add("墨尔本", "Melbourne", "AUS Eastern Standard Time");
            Add("堪培拉", "Canberra", "AUS Eastern Standard Time");
            Add("布里斯班", "Brisbane", "E. Australia Standard Time");
            Add("阿德莱德", "Adelaide", "Cen. Australia Standard Time");
            Add("珀斯", "Perth", "W. Australia Standard Time");
            Add("奥克兰", "Auckland", "New Zealand Standard Time");
            Add("惠灵顿", "Wellington", "New Zealand Standard Time");
            Add("苏瓦", "Suva", "Fiji Standard Time");

            // ---- ② capitals.txt（全球首都/大城市中文音译，约 300 条）----
            foreach (string line in ResourceBox.ReadText("capitals.txt").Split('\n'))
            {
                string t = line.Trim();
                if (t.Length == 0) continue;
                string[] p = t.Split('|');
                if (p.Length >= 3) Add(p[0], p[1], p[2]);
            }

            // ---- ③ 中国全量区县 ----
            foreach (string line in ResourceBox.ReadText("chinadist.txt").Split('\n'))
            {
                string t = line.Trim();
                if (t.Length > 0) _cnNames.Add(t);
            }

            // ---- ④ 境外城市（GeoNames，norm键 -> [显示名, 时区]；name+asciiname 双键）----
            foreach (string line in ResourceBox.ReadText("worldcities2.txt").Split('\n'))
            {
                string t = line.Trim();
                if (t.Length == 0) continue;
                string[] p = t.Split('|');
                if (p.Length < 3) continue;
                string key = p[0].Trim();
                string disp = p[1].Trim();
                string tz = p[2].Trim();
                if (key.Length == 0 || tz.Length == 0) continue;
                if (!_world.ContainsKey(key)) _world.Add(key, new string[] { disp, tz });
            }

            // ---- ④b 中文对照表（cn_table.txt：中文|英文|wid；同名多行多时区）----
            foreach (string line in ResourceBox.ReadText("cn_table.txt").Split('\n'))
            {
                string t = line.Trim();
                if (t.Length == 0) continue;
                string[] p = t.Split('|');
                if (p.Length < 3) continue;
                string cn = p[0].Trim();
                string en = p[1].Trim();
                string tz = p[2].Trim();
                if (cn.Length == 0 || tz.Length == 0) continue;
                List<CnRow> lst;
                if (!_cnWorld.TryGetValue(cn, out lst)) { lst = new List<CnRow>(); _cnWorld.Add(cn, lst); }
                bool dup = false;
                foreach (CnRow cr in lst) if (cr.Tz == tz) { dup = true; break; }
                if (!dup) lst.Add(new CnRow(cn, en, tz));
                if (en.Length > 0)
                {
                    string ek = NormKey(en);
                    List<CnRow> el;
                    if (!_cnEnWorld.TryGetValue(ek, out el)) { el = new List<CnRow>(); _cnEnWorld.Add(ek, el); }
                    bool edup = false;
                    foreach (CnRow cr in el) if (cr.Tz == tz) { edup = true; break; }
                    if (!edup) el.Add(new CnRow(cn, en, tz));
                    if (!_cnEn.ContainsKey(en)) _cnEn.Add(en, cn);
                }
            }

            // ---- ④d 汉字拼音表（pinyin.txt：字|拼音）----
            foreach (string line in ResourceBox.ReadText("pinyin.txt").Split('\n'))
            {
                string t = line.Trim();
                if (t.Length == 0) continue;
                string[] p = t.Split('|');
                if (p.Length >= 2 && p[0].Length > 0 && p[1].Length > 0)
                    if (!_pinyin.ContainsKey(p[0])) _pinyin.Add(p[0], p[1]);
            }

            // ---- ④c 中国省/地级（cn_admin.txt：名称 -> China Standard Time）----
            foreach (string line in ResourceBox.ReadText("cn_admin.txt").Split('\n'))
            {
                string t = line.Trim();
                if (t.Length == 0) continue;
                int ix = t.LastIndexOf('|');
                if (ix <= 0 || ix >= t.Length - 1) continue;
                string nm = t.Substring(0, ix).Trim();
                string tz = t.Substring(ix + 1).Trim();
                if (nm.Length > 0 && tz.Length > 0 && !_cnAdmin.ContainsKey(nm)) _cnAdmin.Add(nm, tz);
            }

            // ---- ⑤ 国家表 ----
            foreach (string line in ResourceBox.ReadText("countries.txt").Split('\n'))
            {
                string t = line.Trim();
                if (t.Length == 0) continue;
                string[] p = t.Split('|');
                if (p.Length >= 3)
                    _nat.Add(new NatRow(p[0], p[1], p[2].Split(';')));
            }

            // ---- ⑥ 州/省表 ----
            foreach (string line in ResourceBox.ReadText("states.txt").Split('\n'))
            {
                string t = line.Trim();
                if (t.Length == 0) continue;
                string[] p = t.Split('|');
                if (p.Length >= 3)
                    _st.Add(new StateRow(p[0], p[1], p[2]));
            }

            // ---- ⑦ 中文区域规则（省名等）----
            string[] china = { "中国", "华北", "华东", "华中", "华南", "西南", "西北", "东北",
                "河北", "山西", "辽宁", "吉林", "黑龙江", "江苏", "浙江", "安徽", "福建", "江西", "山东",
                "河南", "湖北", "湖南", "广东", "海南", "四川", "贵州", "云南", "陕西", "甘肃", "青海",
                "内蒙古", "广西", "宁夏", "新疆", "西藏", "石家庄", "太原", "呼和浩特", "长春", "福州",
                "南昌", "济南", "南宁", "海口", "贵阳", "兰州", "西宁", "银川", "台湾", "台中", "台南", "基隆", "新竹", "桃园" };
            foreach (string c in china) { if (!_ruleCn.ContainsKey(c)) _ruleCn.Add(c, "China Standard Time"); }
            AddRule("日本", "Tokyo Standard Time");
            AddRule("北海道", "Tokyo Standard Time");
            AddRule("本州", "Tokyo Standard Time");
            AddRule("四国", "Tokyo Standard Time");
            AddRule("九州", "Tokyo Standard Time");
            AddRule("韩国", "Korea Standard Time");
            AddRule("朝鲜", "Korea Standard Time");
            AddRule("香港", "China Standard Time");
            AddRule("澳门", "China Standard Time");
        }

        static void Add(string c, string e, string t)
        {
            foreach (Row r in _major) { if (r.Cn == c) return; }
            _major.Add(new Row(c, e, t));
            if (e.Length > 0 && !_cnEn.ContainsKey(e)) _cnEn.Add(e, c);
        }
        static void AddRule(string k, string v) { if (!_ruleCn.ContainsKey(k)) _ruleCn.Add(k, v); }

        // ---- 中文拼音/行政区划后缀辅助（5.5）----
        static readonly string[] SuffixList = {
            "藏族自治州", "维吾尔自治区", "壮族自治区", "回族自治区", "蒙古族自治州", "朝鲜族自治州",
            "自治州", "自治县", "自治旗", "特别行政区", "经济特区", "自治区", "地区", "新区", "林区", "矿区",
            "盟", "旗", "省", "市", "区", "县", "州", "镇", "乡", "街道", "村", "巷" };

        public static bool HasCjk(string s)
        {
            foreach (char c in s) if (c >= 0x4e00 && c <= 0x9fff) return true;
            return false;
        }

        /// <summary>剥离行政区划后缀（如 泸定县→泸定、甘孜藏族自治州→甘孜），剩余至少保留 2 字</summary>
        static string StripSuffix(string cn)
        {
            string s = cn;
            bool changed = true;
            while (changed)
            {
                changed = false;
                foreach (string suf in SuffixList)
                {
                    if (s.EndsWith(suf) && s.Length - suf.Length >= 2)
                    { s = s.Substring(0, s.Length - suf.Length); changed = true; break; }
                }
            }
            return s;
        }

        /// <summary>拼音 Title Case：每个音节首字母大写、其余小写（如 泸定县 → Lu Ding）。</summary>
        public static string PinyinTitle(string cn)
        {
            string body = StripSuffix(cn);
            var sb = new System.Text.StringBuilder();
            bool first = true;
            foreach (char c in body)
            {
                string py;
                if (_pinyin.TryGetValue(c.ToString(), out py))
                {
                    if (!first) sb.Append(' ');
                    sb.Append(TitleCaseWord(py));
                    first = false;
                }
            }
            string r = sb.ToString();
            return r.Length > 0 ? r : TitleCaseWord(cn);
        }

        static string TitleCaseWord(string w)
        {
            if (string.IsNullOrEmpty(w)) return w;
            return char.ToUpperInvariant(w[0]) + w.Substring(1).ToLowerInvariant();
        }

        /// <summary>隐患-1：单字输入只做精确/前缀匹配，避免命中上千条；多字才双向包含。</summary>
        static bool LooseContains(string a, string b)
        {
            if (b.Length <= 1) return a == b || a.StartsWith(b);
            return a.IndexOf(b) >= 0 || b.IndexOf(a) >= 0;
        }

        /// <summary>英文名反查中文名；查不到则用英文名本身（中文位兜底）</summary>
        static string CnFor(string en)
        {
            string c;
            if (_cnEn.TryGetValue(en, out c)) return c;
            return en;
        }

        /// <summary>返回匹配候选列表；为空表示未找到（调用方弹手动指定时区兜底）</summary>
        public static List<MatchResult> Match(string input)
        {
            List<MatchResult> res = new List<MatchResult>();
            string s = input.Trim();
            if (s.Length == 0) return res;
            // 本地时钟：识别"本地""本地时间"，时区即系统当前时区（Local）
            if (s == "本地" || s == "本地时间")
            {
                AddResult(res, "本地", "Local", TimeZoneInfo.Local.Id);
                return res;
            }
            bool cjk = false;
            foreach (char c in s) { if (c >= 0x4e00 && c <= 0x9fff) { cjk = true; break; } }

            if (cjk)
            {
                // ⓪ 别名/误写（巴音郭勒 → 巴音郭楞蒙古自治州）
                string canon;
                if (_aliasCn.TryGetValue(s, out canon))
                {
                    string admTzA;
                    if (_cnAdmin.TryGetValue(canon, out admTzA))
                    {
                        string enA;
                        if (!_zhouEn.TryGetValue(canon, out enA)) enA = PinyinTitle(canon);
                        AddResult(res, canon, enA, admTzA);
                    }
                }
                // ⓪b 自治州简称："甘孜州/阿坝州/凉山州" → 以该前缀开头的自治州全称
                if (s.EndsWith("州") && s.Length >= 3)
                {
                    string pre = s.Substring(0, s.Length - 1);
                    foreach (KeyValuePair<string, string> kv in _cnAdmin)
                    {
                        if (kv.Key.StartsWith(pre) && kv.Key.EndsWith("自治州"))
                        {
                            string enB;
                            if (!_zhouEn.TryGetValue(kv.Key, out enB)) enB = PinyinTitle(kv.Key);
                            AddResult(res, kv.Key, enB, kv.Value);
                        }
                    }
                }
                // 模糊候选上限（隐患-1）：达到 30 条后不再追加模糊命中，避免单字/短词命中上千条卡顿
                const int MatchCap = 30;
                bool fullHit = false;
                // ① 主要城市中文名
                foreach (Row r in _major)
                    if (res.Count < MatchCap && LooseContains(r.Cn, s))
                        AddResult(res, r.Cn, r.En, r.Tz);
                // ② 中国区县（无英文名 → 拼音兜底；过滤单字脏数据）
                foreach (string nm in _cnNames)
                    if (res.Count < MatchCap && nm.Length >= 2 && LooseContains(nm, s))
                        AddResult(res, nm, PinyinTitle(nm), "China Standard Time");
                // ③b 国家全称 → 简称（全称与简称无重叠关键词时也能命中；
                //     显示用全称，英文/时区取对应简称国家条目；输入含"爱尔兰"等仍走 ③ 追加候选）
                foreach (KeyValuePair<string, string> kv in _natFull)
                {
                    if (res.Count < MatchCap && (s == kv.Key || LooseContains(kv.Key, s)))
                    {
                        fullHit = true;
                        foreach (NatRow n in _nat)
                            if (n.Cn == kv.Value)
                                foreach (string tz in n.Tzs) AddResult(res, kv.Key, n.En, tz);
                    }
                }
                // ③ 国家中文名（多时区给多候选）；全称已命中则仅保留精确同名，跳过模糊扫描（优化-7）
                foreach (NatRow n in _nat)
                    if (res.Count < MatchCap && (!fullHit || n.Cn == s) && LooseContains(n.Cn, s))
                        foreach (string tz in n.Tzs) AddResult(res, n.Cn, n.En, tz);
                // ④ 州/省中文名
                foreach (StateRow st in _st)
                    if (res.Count < MatchCap && LooseContains(st.Cn, s))
                        AddResult(res, st.Cn, st.En, st.Tz);
                // ⑤ 中国省/地级（自治州用标准英文名，其余拼音兜底）
                foreach (KeyValuePair<string, string> kv in _cnAdmin)
                {
                    if (res.Count < MatchCap && kv.Key.Length >= 2 && LooseContains(kv.Key, s))
                    {
                        string en;
                        if (!_zhouEn.TryGetValue(kv.Key, out en)) en = PinyinTitle(kv.Key);
                        AddResult(res, kv.Key, en, kv.Value);
                    }
                }
                // ⑥ 中英对照表（同名多时区列出；包含匹配限 30）
                int cntc = 0;
                foreach (KeyValuePair<string, List<CnRow>> kv in _cnWorld)
                {
                    if (kv.Key.Length >= 2 && LooseContains(kv.Key, s))
                    {
                        foreach (CnRow cr in kv.Value) AddResult(res, kv.Key, cr.En, cr.Tz);
                        if (++cntc >= MatchCap) break;
                    }
                }
                // ⑦ 中文区域规则
                if (res.Count == 0)
                {
                    foreach (KeyValuePair<string, string> kv in _ruleCn)
                        if (s.IndexOf(kv.Key) >= 0 || kv.Key.IndexOf(s) >= 0)
                        { AddResult(res, s, PinyinTitle(s), kv.Value); break; }
                }
            }
            else
            {
                string low = s.ToLower();
                // 变音符归一（Garzê→garze），匹配纯 ASCII 化的自治州英文名
                low = low.Replace("ê", "e").Replace("é", "e");
                string nlow = NormKey(low);
                // ① 主要城市英文名
                foreach (Row r in _major)
                    if (res.Count < 30 && (r.En.ToLower().IndexOf(low) >= 0 || low.IndexOf(r.En.ToLower()) >= 0))
                        AddResult(res, r.Cn, r.En, r.Tz);
                // ② 境外城市（GeoNames）：精确优先（norm 键）；包含匹配收集全部候选（最多 20 个）
                string[] hit;
                if (_world.TryGetValue(nlow, out hit))
                    AddResult(res, CnFor(hit[0]), hit[0], hit[1]);
                else
                {
                    int cnt = 0;
                    foreach (KeyValuePair<string, string[]> kv in _world)
                    {
                        if (kv.Key.IndexOf(nlow) >= 0)
                        {
                            AddResult(res, CnFor(kv.Value[0]), kv.Value[0], kv.Value[1]);
                            cnt++;
                            if (cnt >= 20) break;
                        }
                    }
                }
                // ③ 国家英文名
                foreach (NatRow n in _nat)
                    if (res.Count < 30 && (n.En.ToLower().IndexOf(low) >= 0 || low.IndexOf(n.En.ToLower()) >= 0))
                        foreach (string tz in n.Tzs) AddResult(res, n.Cn, n.En, tz);
                // ④ 州/省英文名
                foreach (StateRow st in _st)
                    if (res.Count < 30 && (st.En.ToLower().IndexOf(low) >= 0 || low.IndexOf(st.En.ToLower()) >= 0))
                        AddResult(res, st.Cn, st.En, st.Tz);
                // ⑤ 中英对照表英文名（GeoNames 未收录的景点等；规范键精确匹配）
                List<CnRow> el;
                if (_cnEnWorld.TryGetValue(nlow, out el))
                    foreach (CnRow cr in el) AddResult(res, cr.Cn, cr.En, cr.Tz);
                // ⑥ 自治州标准英文名（输入 Garzê / Tibetan 等可命中 甘孜藏族自治州）
                string admTz;
                foreach (KeyValuePair<string, string> kv in _zhouEn)
                {
                    if (kv.Value.ToLower().IndexOf(low) >= 0 || low.IndexOf(kv.Value.ToLower()) >= 0)
                        if (_cnAdmin.TryGetValue(kv.Key, out admTz))
                            AddResult(res, kv.Key, kv.Value, admTz);
                }
            }
            // 精确匹配优先：候选中文名与输入完全一致的排最前
            //（如输入"中华人民共和国"时，"中华人民共和国/P·R CHINA"排到"中国/CHINA"前面）
            List<MatchResult> exact = new List<MatchResult>();
            List<MatchResult> rest = new List<MatchResult>();
            foreach (MatchResult m in res) { if (m.Cn == s) exact.Add(m); else rest.Add(m); }
            exact.AddRange(rest);
            return exact;
        }

        /// <summary>英文键归一：小写 + 去变音 + 去空格/连字符/撇号（与 worldcities2 键一致）</summary>
        static string NormKey(string s)
        {
            var sb = new System.Text.StringBuilder(s.Length);
            foreach (char c in s)
            {
                char n = c;
                if (n == ' ') continue;
                // 常见变音字符映射为 ASCII
                switch (n)
                {
                    case 'é': case 'è': case 'ê': case 'ë': n = 'e'; break;
                    case 'á': case 'à': case 'â': case 'ä': n = 'a'; break;
                    case 'í': case 'ì': case 'î': case 'ï': n = 'i'; break;
                    case 'ó': case 'ò': case 'ô': case 'ö': case 'ø': n = 'o'; break;
                    case 'ú': case 'ù': case 'û': case 'ü': n = 'u'; break;
                    case 'ç': n = 'c'; break;
                    case 'ñ': n = 'n'; break;
                    case 'š': n = 's'; break;
                    case 'ž': n = 'z'; break;
                    case 'å': n = 'a'; break;
                    case 'æ': sb.Append("ae"); continue;
                    case 'œ': sb.Append("oe"); continue;
                    case 'đ': n = 'd'; break;
                    case 'ł': n = 'l'; break;
                    case 'ş': case 'ș': n = 's'; break;
                    case 'ţ': case 'ț': n = 't'; break;
                    case 'ğ': n = 'g'; break;
                    case 'ı': n = 'i'; break;
                    case '\'': case '`': case '-': continue;
                }
                sb.Append(n);
            }
            return sb.ToString();
        }

        static void AddResult(List<MatchResult> res, string cn, string en, string tzId)
        {
            // 去重键加入英文名（优化-7）：同中文名不同英文名/时区视为不同候选
            foreach (MatchResult r in res)
                if (r.TzId == tzId && r.Cn == cn && r.En == en) return;
            string disp = cn + "/" + en;
            try
            {
                TimeZoneInfo tz = TimeZoneInfo.FindSystemTimeZoneById(tzId);
                TimeSpan off = tz.GetUtcOffset(DateTime.UtcNow);
                // BUG-3：按总分钟拼 hh:mm，修正半小时/三刻钟时区（印度+5:30、尼泊尔+5:45 等）被四舍五入
                int totalMin = (int)Math.Round(off.TotalMinutes);
                string sign = totalMin >= 0 ? "+" : "-";
                int abs = Math.Abs(totalMin);
                int oh = abs / 60, om = abs % 60;
                string offStr = sign + oh.ToString("00") + ":" + (om == 0 ? "00" : om.ToString("00"));
                disp = cn + "/" + en + " · (UTC" + offStr + ") " + tz.DisplayName;
            }
            catch (Exception) { disp = cn + "/" + en + " · " + tzId; }
            MatchResult mr = new MatchResult();
            mr.Display = disp;
            mr.TzId = tzId;
            mr.Cn = cn;
            mr.En = en;
            res.Add(mr);
        }
    }
}
