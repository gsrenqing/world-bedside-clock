# World Bedside Clock（世界床头钟）

一款 Windows 桌面挂件式 **世界时钟 / 床头钟**，支持多城市时区显示、皮肤切换、城市选择对话框与中英文界面。

- 当前版本：**v5.6.6**
- 运行环境：Windows 10 / 11（.NET Framework 4.x）
- 技术栈：C# / WinForms

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
