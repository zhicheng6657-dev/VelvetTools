# 第三方组件与许可证

Velvet Tools 自有源码以 **GNU GPL-3.0-or-later** 发布。第三方组件不会因此被改成
GPL；它们继续遵循各自的许可证、版权声明和 NOTICE。

本文件只列出进入源码仓库、安装包或便携包的第三方代码与资源。下列内容不属于随包
分发，因此不作为第三方组件登记：

- Windows 系统 API、系统字体和系统自带组件；
- 用户自行安装的驱动、硬件监控程序或其他本机软件；
- 用户自行配置的在线 AI、翻译、OCR 和搜索服务；
- 仅用于理解通用需求或界面布局、但未复制代码和资源的产品参考。

## 随软件分发

| 组件 | 当前版本 | 许可证 | 用途与随包文件 |
| --- | --- | --- | --- |
| Microsoft .NET / WPF / Windows Forms x64 运行时 | 8.0.x，自包含发布时确定 | `coreclr.dll` 与 WPF 原生运行库适用 Microsoft .NET Library License；`D3DCompiler_47_cor3.dll` 适用 Windows SDK License；其余运行时文件通常为 MIT，并带第三方通知 | 应用运行时。发布脚本从实际参与构建的运行时包复制许可证与 `THIRD-PARTY-NOTICES`；逐文件边界见 `Licenses/DotNet-Windows-License-Information.txt` |
| `System.Management` | 9.0.0 | MIT | WMI 查询；随包复制原 NuGet 包的 MIT 和第三方通知 |
| `System.CodeDom` | 9.0.0 | MIT | .NET 运行库依赖；随包复制原 NuGet 包的 MIT 和第三方通知 |
| `System.IO.Packaging` | 8.0.1 | MIT | Open XML 容器支持；随包复制原 NuGet 包的 MIT 和第三方通知 |
| `DocumentFormat.OpenXml` / `DocumentFormat.OpenXml.Framework` | 3.5.1 | MIT | Office 文档读取；许可证见 `Licenses/OpenXml-MIT.txt` |
| `PdfPig` | 0.1.15 | Apache-2.0 | PDF 文本读取；Apache-2.0 及上游通知见 `Licenses/PdfPig-Apache-2.0-and-NOTICE.txt` |
| `FluentIcons.Wpf` / `FluentIcons.Common` | 2.1.333 | MIT | WPF 图标控件与资源；许可证见 `Licenses/FluentIcons-MIT.txt` |
| Microsoft Fluent UI System Icons | 由 FluentIcons 包带入 | MIT | 内置功能图标；许可证和上游通知见 `Licenses/FluentUI-System-Icons-MIT.txt`、`Licenses/FluentUI-System-Icons-NOTICE.txt` |
| Inter Regular / Medium / SemiBold | 4.1 | SIL Open Font License 1.1；`Inter` 为 Reserved Font Name | 未修改的内嵌字体；许可证见 `Licenses/Inter-OFL.txt` |
| Everything 与 Everything SDK（voidtools） | Everything 1.4.1.1026 x64；SDK 1.4 | Everything 授权文本采用 MIT 条款；其内含 PCRE 使用 BSD-3-Clause | 文件名索引与查询。官方二进制、SDK DLL 和完整许可见 `Assets/Everything/` |

### Everything 的分发边界

Everything 主程序不是 Velvet Tools 的 GPL 源码，也不应被描述成 Velvet Tools 的
开源实现。项目只分发 voidtools 提供的未修改二进制，通过官方 SDK 或公开 IPC 与它通信。

`Assets/Everything/Everything-License.txt` 原样保留了 David Carpenter 的版权与
许可，以及 PCRE 的 BSD-3-Clause 通知。MIT 条款要求许可证随软件副本保留，PCRE 条款
要求二进制再分发在文档或其他材料中重现其通知；该文件必须始终进入安装包和便携包。

## 项目原创资源

Velvet V 标识、应用图标、托盘图标和 `Assets/Brand/` 中的派生图片是本项目原创资源，
不含第三方字体或图标文件。它们与本项目自有源码一并按 GPL-3.0-or-later 分发；生成和
派生方式见 `Assets/Brand/README.md`。

启动器显示的其他应用图标只在用户电脑上通过 Windows API 动态读取，不进入仓库或
发行包。Microsoft YaHei UI、Segoe UI 等字体只作为系统回退字体引用，不随软件复制。

## 发布包应包含的许可文件

- `Licenses/VelvetTools-GPL-3.0-or-later.txt`
- `Licenses/THIRD_PARTY.md`
- `Licenses/DotNet-Windows-Library-License.txt`
- `Licenses/DotNet-Windows-License-Information.txt`
- `Licenses/DotNet-RuntimePack-MIT.txt`
- `Licenses/DotNet-RuntimePack-THIRD-PARTY-NOTICES.txt`
- `Licenses/DotNet-WindowsDesktop-RuntimePack-MIT.txt`
- `Licenses/System.Management-MIT.txt` 与对应第三方通知
- `Licenses/System.CodeDom-MIT.txt` 与对应第三方通知
- `Licenses/System.IO.Packaging-MIT.txt` 与对应第三方通知
- `Licenses/OpenXml-MIT.txt`
- `Licenses/PdfPig-Apache-2.0-and-NOTICE.txt`
- `Licenses/FluentIcons-MIT.txt`
- `Licenses/FluentUI-System-Icons-MIT.txt`
- `Licenses/FluentUI-System-Icons-NOTICE.txt`
- `Licenses/Inter-OFL.txt`
- `Assets/Everything/Everything-License.txt`

## 维护规则

新增或升级 NuGet 包、字体、图标、可执行文件、DLL 或复制代码前，应先核对上游许可证、
版权声明和 NOTICE，并更新本文件及发布包。构建工具只有在许可证要求对生成物保留通知时，
才进入面向用户的许可清单。
