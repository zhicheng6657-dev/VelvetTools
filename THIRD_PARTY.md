# 第三方组件与许可合规说明

Velvet Tools 以 **GNU GPL-3.0-or-later** 协议开源。本文件逐项列出：**随包分发的依赖**（许可必须兼容）、
**运行时可选依赖**（不分发）、以及**仅参考产品思路的项目**（未使用其代码）。

> 原则：只有来源和再分发条件明确的代码或资源才进入发行包；许可不明的内容不使用。
> 第三方文件继续保留其原始许可证、版权声明与 NOTICE；项目主许可证不会覆盖或抹去这些信息。
> Anti-996、闭源或许可证不清晰的项目只用于理解通用产品需求，具体代码和视觉资产均独立实现。

---

## 一、随发行包分发的组件（许可已核对）

| 组件 | 版本 | 许可证 | 用途 | 合规说明 |
| --- | --- | --- | --- | --- |
| .NET 8 / WPF / Windows Forms | 8.0 | MIT (Microsoft) | 应用框架 | 兼容 |
| [System.Management](https://www.nuget.org/packages/System.Management) | 9.0 | MIT (Microsoft) | WMI（笔记本内屏亮度、公开温度提供程序） | 兼容 |
| [System.CodeDom](https://www.nuget.org/packages/System.CodeDom) | 9.0 | MIT (Microsoft) | `System.Management` 的运行时依赖 | 显式固定传递依赖版本；随包附原 NuGet 包的 LICENSE 与 THIRD-PARTY-NOTICES |
| [System.IO.Packaging](https://www.nuget.org/packages/System.IO.Packaging) | 8.0.1 | MIT (Microsoft) | Open XML 的 OPC 容器支持 | 显式固定传递依赖版本；随包附原 NuGet 包的 LICENSE 与 THIRD-PARTY-NOTICES |
| [FluentIcons.Wpf / FluentIcons.Common](https://www.nuget.org/packages/FluentIcons.Wpf) | 2.1.333 | **MIT** (davidxuang) | WPF 图标控件封装 | 仅引用 NuGet 二进制；随包附 `Licenses/FluentIcons-MIT.txt` |
| [Microsoft Fluent UI System Icons](https://github.com/microsoft/fluentui-system-icons) | FluentIcons 2.1.333 内含版本 | **MIT** (Microsoft) | 全部内置界面功能图标 | 由上述 WPF 包提供；随包附 Microsoft MIT 全文与上游 NOTICE |
| Windows ACPI Thermal Zone（系统 WMI） | 系统组件 | Windows API | 可用时读取 ACPI 温度 | 自行实现 WMI 查询，不安装、解压或加载第三方内核驱动 |
| [Inter](https://github.com/rsms/inter) 字体（Regular / Medium / SemiBold） | 4.1 | **SIL OFL 1.1**；`Inter` 是保留字名 RFN | 界面拉丁文与数字 | OFL 允许随软件嵌入分发；我们使用未修改原版字体（未子集化、未改名），随包附 `Licenses/Inter-OFL.txt` 全文 |
| [PdfPig](https://github.com/UglyToad/PdfPig) | 0.1.15 | **Apache-2.0** | AI 对话的 PDF 文档解析 | 未修改源码，仅引用 NuGet 二进制；随包附完整 Apache-2.0 和 PDFBox/FontBox、Adobe AFM/CMap notices |
| [DocumentFormat.OpenXml](https://github.com/dotnet/Open-XML-SDK) | 3.5.1 | **MIT** (.NET Foundation) | Word / Excel / PowerPoint 文档解析 | 随包附上游 MIT 全文 |

**仅引用不打包的系统字体**：Microsoft YaHei UI（中文回退）、Segoe UI（系统界面回退）、
Cascadia Mono（等宽数字）。项目不再使用 Segoe Fluent Icons / Segoe MDL2 私用区码位；
内置功能图标统一来自 MIT 许可的 Microsoft Fluent UI System Icons。启动器扫描到的第三方应用
图标只在用户本机按 Windows 公开 API 动态读取，不进入 Velvet Tools 仓库或发行资源。

**品牌资产**：`Assets/app.ico`（多尺寸）、`Assets/logo.png` / `logo-light.png`、
`Assets/tray-*.png` 与 `Assets/Brand/*.png` 均由项目原创 V 母版确定性生成。母版由 OpenAI
内置图像生成能力辅助创作，参考图只用于蓝青配色、笔画粗细与上下轮廓方向；没有复用旧
skill-icons SVG 路径。生成提示词与处理过程记录在 `Assets/Brand/README.md`。

---

## 二、安装包构建工具

| 组件 | 版本 | 许可证 | 用途 | 合规说明 |
| --- | --- | --- | --- | --- |
| [NSIS](https://nsis.sourceforge.io/) | 3.12 | zlib/libpng；本项目明确使用 zlib 压缩 | 生成 Windows 安装/卸载程序 | 仅作为构建工具；生成的安装器含 NSIS stub。zlib 许可允许任何用途及再分发，通知文本随应用放在 `Licenses/NSIS-zlib.txt` |

安装脚本、权限配置脚本和许可确认文本均为本项目自行编写。未使用来源不明的 NSIS 插件，
也未使用 WiX、Inno Setup 或商业安装器模板。安装器请求管理员权限是为了写入 Program Files、
注册卸载信息，以及在用户主动勾选时创建按需最高权限计划任务。

---

## 三、随包分发的第三方可执行文件

| 组件 | 版本 | 许可证 | 我们如何使用 |
| --- | --- | --- | --- |
| [Everything](https://www.voidtools.com/) 与 [Everything SDK](https://www.voidtools.com/support/everything/sdk/) (voidtools) | 引擎 1.4.1.1026 x64；SDK 1.4 | 授权文本与 **MIT 逐字一致**（Copyright © David Carpenter）；引擎内含 PCRE 为 BSD-3-Clause。**主程序闭源** | `Assets/Everything/` 随发行包分发引擎、原始许可文本及官方 x86/x64/ARM/ARM64 SDK DLL。默认实例通过 SDK 查询，私有命名实例通过公开 IPC 查询；未复制或修改 SDK 示例/头文件源码 |

**合规要点**：
- 授权文本是逐字 MIT，明确允许 "use, copy, modify, merge, publish, distribute, sublicense, and/or sell"，
  作者也在官方论坛确认过可以集成进第三方软件，因此**随包分发其二进制合法**。
- 义务：原样附带许可证全文 —— 已放在 `Assets/Everything/Everything-License.txt`（含 MIT 与 PCRE 的 BSD-3 两段）。
- README 与界面文案中**不得称 Everything 为"开源软件"**：它是 MIT 授权的**闭源免费软件**。
- 我们**未修改**其二进制文件；随附的 `Everything.ini` 只是便携模式的默认配置，不属于对程序的修改。

---

## 四、运行时可选的本机温度提供程序（不随包分发）

| 提供程序 | 许可证/归属 | 我们如何使用 | 安全与合规边界 |
| --- | --- | --- | --- |
| NVIDIA 驱动自带 `nvidia-smi.exe` | NVIDIA 驱动组件 | 仅从受保护的 Windows 系统目录或 Program Files 固定路径启动，传入只读 CSV 查询参数以读取 GPU 名称、温度和负载 | 不随包复制、不下载、不修改；不搜索当前目录或普通 `PATH`，设置 3 秒超时，不执行管理/写入命令 |
| [LibreHardwareMonitor](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor) WMI | MPL-2.0；部分上游文件另有声明 | 用户已自行运行且开放 `root\LibreHardwareMonitor` 时，只读查询 `Sensor` 类 | 不引用其 NuGet/源码，不启动其程序，不分发或控制其驱动 |
| [Open Hardware Monitor](https://github.com/openhardwaremonitor/openhardwaremonitor) WMI | 主要为 MPL-2.0，另含各自许可证组件 | 用户已自行运行且开放 `root\OpenHardwareMonitor` 时，只读查询 `Sensor` 类 | 不引用其代码、二进制或 WinRing0；不启动其程序，不分发或控制其驱动 |

温度查询逻辑是本项目独立实现。外部 WMI 命名空间不存在或权限不足时会静默回退；
Velvet Tools 不会为了获得 CPU 核心温度而下载、安装或加载 WinRing0、PawnIO 等内核驱动。

---

## 五、用户自行配置的在线服务（不内置密钥）

**AI 对话 / OCR / 翻译**：通义千问（阿里云百炼）、Kimi（月之暗面）、豆包（火山方舟）、
DeepSeek、智谱 GLM、硅基流动、OpenAI 兼容接口、DeepL、百度翻译。

**知识库（RAG）嵌入**：走上述服务商的 OpenAI 兼容 `/embeddings` 接口，用户自选模型。
检索完全在本机完成（向量语义与关键词混合评分），**未使用任何向量数据库或第三方 RAG 框架**，
分块与检索逻辑为自行实现；文档正文与向量只存本机 `%AppData%\VelvetTools\knowledge\`。

**联网搜索**：
- **DuckDuckGo**（默认）—— 通过其公开的 HTML 端点检索，**无需密钥**。这是页面结构解析，
  对方改版时可能失效；仅用于为模型补充公开网页摘要，不做批量抓取。
- **Tavily** / **Bing Web Search（Azure）** —— 需用户自备密钥。

以上密钥均由用户在设置中自行填写，保存在本机 `%AppData%\VelvetTools\settings.json`；
应用不内置、不代理、不收集任何密钥或对话内容。使用时请遵守各服务商条款。

---

## 六、上游设计与实现参考

| 项目 | 许可证 | 我们参考了什么 | 合规说明 |
| --- | --- | --- | --- |
| [Twinkle Tray](https://github.com/xanderfrangos/twinkle-tray) | MIT | 托盘式多显示器亮度调节形态；DDC/CI + WMI 双通道思路 | 其为 Electron/JS，我们用 C# 自研，无代码复用 |
| [ZTools](https://github.com/ZToolsCenter/ZTools) | MIT | 启动器交互、设置页信息架构（图标导航 + 行式布局 + 开关） | 其为 Electron/Vue/TS，无代码复用 |
| [Microsoft PowerToys](https://github.com/microsoft/PowerToys) | MIT | Text Extractor（OCR）、Color Picker、Run 的功能定义 | 无代码复用 |
| [Flow Launcher](https://github.com/Flow-Launcher/Flow.Launcher) | MIT | 启动器搜索评分思路 | 无代码复用 |
| [Text-Grab](https://github.com/TheJoeFin/Text-Grab) | MIT | 用 `Windows.Media.Ocr` 做本地离线 OCR 的技术路线 | 无代码复用；该 API 是系统公开接口 |
| [all-smi](https://github.com/lablup/all-smi) | Apache-2.0 | Windows 上按公开接口逐级回退、明确标注 CPU 温度能力缺失的产品策略 | 仅核对公开文档与能力边界；温度查询代码独立实现，未复制 Rust 源码 |
| [Hermes Agent Desktop](https://github.com/NousResearch/hermes-agent/tree/main/apps/desktop) | MIT（Copyright © 2025 Nous Research） | AI 对话页的信息架构、扁平消息排版、侧栏、标题栏、贴底输入区、主题令牌与响应式优先级 | 已下载官方桌面端源码进行源码级参考；Velvet Tools 使用 WPF/C# 重新实现，不分发 Hermes 的 React/Electron 运行时、品牌素材或图标；随包附 `Licenses/Hermes-Agent-MIT.txt` |
| [TrafficMonitor](https://github.com/zhongyang219/TrafficMonitor) | **Anti-996 License**（非标准） | 任务栏内嵌信息条 / 简约图标模式的**产品形态** | **仅借鉴思路，未使用任何代码**，规避非标准许可证风险；窗口内嵌、多列布局、保活策略均自行设计 |
| [ShareX](https://github.com/ShareX/ShareX) | GPL-3.0 | 截图后操作栏的工作流概念 | 仅借鉴通用工作流，未复制源码或视觉资源 |
| [Chatbox](https://github.com/chatboxai/chatbox) | GPLv3（社区版） | AI 对话的产品形态（会话列表 + 气泡 + 模型切换） | 仅借鉴通用产品形态，未复制源码或视觉资源 |
| [EarTrumpet](https://github.com/File-New-Project/EarTrumpet) | 修改版 MIT（含额外排除条款） | 托盘音量控制形态 | 仅思路，无代码复用 |
| Snipaste / PixPin / uTools / Ditto / QuickLook | 闭源或各异 | 贴图、标注、剪贴板等大众化产品概念 | 无代码接触 |

> 合规边界：通用功能需求和抽象工作流通常可以独立实现；具体源码、文案、图形、独创的视觉表达、
> 商标和其他受保护资产必须有明确授权才能使用。Hermes Agent Desktop 的 MIT 授权与版权声明
> 已保留；本项目没有移植上述 Anti-996、闭源或许可证不清晰项目的代码或资源。
> 每次发布仍应保留来源记录和相似度检查，不能把本段当作绝对法律结论。

---

## 七、商标声明

"Everything"、"Snipaste"、"uTools"、"PowerToys"、"Chatbox"、"通义千问"、"Kimi"、"豆包"、
"DeepSeek" 等名称为各自所有者的商标，本文仅作事实性提及。
Velvet Tools 与上述项目及厂商均无从属或背书关系。

---

## 八、发布前合规检查清单

- [x] 发行包内 `Licenses/VelvetTools-GPL-3.0-or-later.txt`、`Licenses/THIRD_PARTY.md` 存在
- [x] `Licenses/Hermes-Agent-MIT.txt` 存在且保留 Nous Research 版权声明
- [x] `Licenses/Inter-OFL.txt` 存在且完整
- [x] `Assets/Brand/README.md` 记录原创 Logo 的生成来源与最终提示词
- [x] `Licenses/PdfPig-Apache-2.0-and-NOTICE.txt`、`Licenses/OpenXml-MIT.txt` 存在
- [x] `Licenses/FluentIcons-MIT.txt`、`Licenses/FluentUI-System-Icons-MIT.txt` 与上游 NOTICE 存在
- [x] `Licenses/NSIS-zlib.txt` 存在，安装脚本未引入来源不明的插件
- [x] `Licenses/System.Management-*`、`System.CodeDom-*`、`System.IO.Packaging-*` 由对应 NuGet 包原样复制到输出目录
- [x] `Assets/Everything/Everything-License.txt` 随包分发且内容未改动
- [x] 发行包不含 LibreHardwareMonitor、WinRing0、InpOut 或其他第三方内核驱动
- [x] 温度回退只查询系统/厂商公开接口和已运行工具的 WMI，不下载或启动外部监控工具
- [x] README 与界面文案未将 Everything 描述为“Velvet Tools 的 MIT 源码”
- [x] 安装/卸载回归通过，最高权限计划任务仅在用户明确勾选后创建并在卸载时删除
- [x] 新增任何依赖前，先在本文件登记许可证并确认与 GPL-3.0-or-later 兼容
