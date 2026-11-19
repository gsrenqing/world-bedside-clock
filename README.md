# World Bedside Clock

A Windows desktop widget **world clock / bedside clock** with multi-city timezone display, skin switching, a city picker dialog, and a bilingual (Chinese/English) interface.

- Version: **v5.6.6**
- Runtime: Windows 10 / 11 (.NET Framework 4.x)
- Stack: C# / WinForms

## Repository Layout

```
├─ 源码/            # C# sources, fonts, skin assets, city/timezone data, compile.rsp
├─ 成品/            # Compiled World bedside clock 5.6.6.exe and notes
├─ LICENSE          # GPL-3.0
└─ README.md
```

## Build from Source

Use the .NET Framework built-in `csc.exe` (no Visual Studio required):

```powershell
cd 源码
C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe @compile.rsp
```

## Run

Double-click `成品\World bedside clock 5.6.6.exe`.

## Data Sources

- City/timezone data: GeoNames geographic database under CC-BY 4.0 (geonames.org)
- Chinese administrative divisions: codes from the Ministry of Civil Affairs of the PRC
- Artwork inspired by the bedside clock 2 widget in Coolfish Desktop / XWidgets; thanks to the original author Jimking

## License

This project is open-sourced under **GPL-3.0**; see [LICENSE](LICENSE). For non-commercial study and testing only. The author is not liable for any issues arising from its use and does not commit to future upgrades.

---

# 中文说明

一款 Windows 桌面挂件式 **世界时钟 / 床头钟**，支持多城市时区显示、皮肤切换、城市选择对话框与中英文界面。

## 目录结构

```
├─ 源码/            # C# 源码、字体、皮肤素材、城市/时区数据、compile.rsp
├─ 成品/            # 已编译的 World bedside clock 5.6.6.exe 与说明
├─ LICENSE          # GPL-3.0
└─ README.md
```

## 从源码编译

使用 .NET Framework 自带的 `csc.exe`（无需 Visual Studio）：

```powershell
cd 源码
C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe @compile.rsp
```

## 运行

直接双击 `成品\World bedside clock 5.6.6.exe`。

## 数据来源

- 城市时区数据依据 CC-BY 4.0 许可使用 [GeoNames](https://www.geonames.org/) 全球地理数据库
- 中国行政区划数据源自民政部《中华人民共和国行政区划代码》
- 主题美工借鉴酷鱼桌面 / XWidgets 中的 bedside clock 2，鸣谢原作者 Jimking

## 许可

本项目以 **GPL-3.0** 协议开源，详见 [LICENSE](LICENSE)。仅限非商业用途的学习、测试；作者不对使用过程中出现的问题负责，也不承诺后续版本升级。
