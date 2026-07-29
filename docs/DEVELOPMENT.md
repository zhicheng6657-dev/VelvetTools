# Velvet Tools 本地开发文档

面向想参与开发或二次修改的同学。读完本文即可完成环境搭建、跑起来、看懂架构、加新功能。

---

## 1. 环境要求

| 依赖 | 版本 | 说明 |
| --- | --- | --- |
| Windows | 10 1809+，推荐 11 | Win11 才有 DWM 圆角；亚克力两者都支持 |
| .NET SDK | 8.0.x | `winget install Microsoft.DotNet.SDK.8` |
| IDE | VS 2022 / Rider / VS Code | 打开根目录 `VelvetTools.sln` |

NuGet 依赖为 `System.Management`（MIT，WMI 亮度/公开温度提供程序）、`DocumentFormat.OpenXml`
（MIT，Office 文档）、`PdfPig`（Apache-2.0，PDF 文本）以及 `FluentIcons.Wpf`
（MIT，内置功能图标）。无任何 Node/前端依赖，也不随包安装或加载第三方内核驱动。

Windows 安装程序由 NSIS 3.12 构建。NSIS 只在发布阶段使用，不是应用运行时依赖；
安装脚本固定使用 zlib 压缩，未引入来源不明的第三方插件。发布脚本还会从实际参与
自包含构建的 .NET 运行时包复制许可证和第三方通知，缺少任一文件时直接终止构建。

## 2. 快速开始

```bash
git clone <repo>
cd VelvetTools
dotnet build                                   # 构建
dotnet run --project src/VelvetTools           # 运行（托盘常驻）
```

冒烟自检（CI 可用，启动全部服务 4 秒后自动退出，exit 0 为通过）：

```bash
src/VelvetTools/bin/Debug/net8.0-windows10.0.19041.0/VelvetTools.exe --smoke
```

结果写入 `%AppData%\VelvetTools\logs\app.log`，形如
`SMOKE OK | monitors=2 apps=176 net=... mem=... audio=... dark=False`，
随后逐个构造并渲染全部窗口（`SelfTestWindows`），能抓到只在运行时才暴露的
XAML 资源键写错、绑定失败之类的问题，每个窗口一行 `[自检] xxx ✓`。
最后还会在隔离的系统临时目录回归知识库分块、版本化向量写入/重载与删除，
不会读写用户现有知识库。

改完界面想肉眼验收，加 `--shots <目录>` 把每个窗口渲染成 PNG：

```bash
VelvetTools.exe --smoke --shots C:\temp\shots
```

走的是 `RenderTargetBitmap`，不经过屏幕，所以窗口被遮挡也拍得到；
但**亚克力背景拍不出来**（那是 DWM 在窗口后面合成的），图里看到的是 `WindowTintBrush` 的底色。

温度功能不需要管理员权限，也不捆绑或加载传感器驱动。它会只读查询用户已运行的
Libre/OpenHardwareMonitor WMI、Windows ACPI Thermal Zone，并在 NVIDIA 设备上从受保护
系统路径调用驱动自带 `nvidia-smi` 的只读 CSV 查询。各来源独立失败并回退；不存在可信来源时
相应项目显示 `--`。

## 3. 目录结构

```
src/VelvetTools/
├── App.xaml(.cs)          入口；ServiceHub 服务定位器；全局异常；单实例；免 UAC 提权
├── app.manifest           PerMonitorV2 DPI / asInvoker
├── Assets/
│   ├── app.ico            应用图标（多尺寸 PNG-ICO，由 logo 生成）
│   ├── logo.png           应用内展示用 Logo
│   └── Fonts/             Inter Regular/Medium/SemiBold + OFL 许可证全文
├── Common/
│   ├── GlassWindow.cs     玻璃窗口基类（WindowChrome 去帧 / Esc / 失焦隐藏 / 拖动）
│   ├── ThemeManager.cs    深浅色主题：换调色板字典 + 事件广播
│   ├── HotkeyManager.cs   RegisterHotKey 封装，"Ctrl+Alt+A" 字符串解析
│   ├── MessageWindow.cs   隐藏消息窗：托盘/热键/剪贴板/显示器/主题变更共用
│   ├── SingleInstance.cs  互斥体 + 事件唤起
│   ├── StartupManager.cs  Run 键自启 + 计划任务最高权限（免 UAC）
│   ├── Elevation.cs       IsAdmin / RestartAsAdmin
│   ├── Toast.cs           右下角轻提示（不抢焦点）
│   ├── FuzzyMatcher.cs    启动器模糊匹配打分
│   └── Interop/           Native.cs（通用 P/Invoke）、GlassEffect.cs（亚克力）
├── Themes/
│   ├── Palette.Dark.xaml  深色调色板（键名与浅色完全一致）
│   ├── Palette.Light.xaml 浅色调色板（Win11 任务管理器风）
│   └── Glass.xaml         全部控件样式，只用 DynamicResource → 运行时换肤
├── Settings/              AppSettings（JSON 模型）+ 设置窗口
└── Modules/
    ├── Tray/              单个托盘图标 + 右键菜单 + 悬停提示（实时数据交给任务栏信息栏）
    ├── Dashboard/         控制中心面板（网速/CPU/内存/温度/亮度/音量/磁贴）
    ├── NetSpeed/          采样服务 + 任务栏内嵌信息栏 + 桌面悬浮窗
    ├── Hardware/          安全公开接口/WMI 温度探测（本软件无内核驱动）
    ├── Chat/              AI 对话（OpenAI 兼容 + SSE 流式 + 会话历史 + Markdown 渲染
    │                      + 文档解析 DocumentParser + 联网搜索 WebSearchService）
    ├── Knowledge/         知识库 RAG（分块 / 嵌入 / 本地向量检索 / 管理窗口）
    ├── Search/            Everything IPC 客户端 + 随包引擎拉起 + 搜索窗口
    ├── Brightness/        DDC/CI (dxva2) + WMI 亮度
    ├── Audio/             CoreAudio IAudioEndpointVolume（自研 COM 互操作）
    ├── Screenshot/        捕获(BitBlt)/遮罩(窗口识别+放大镜+标注)/贴图/流程编排
    ├── Ocr/               Windows.Media.Ocr + OpenAI 兼容视觉接口
    ├── Translate/         OpenAI 兼容 / DeepL / 百度翻译
    ├── ColorPicker/       放大镜取色 + 结果窗
    ├── Clipboard/         剪贴板监听 + 历史存储 + 面板
    └── Launcher/          开始菜单索引(IShellLink) + 模糊搜索启动器
```

## 4. 架构要点

- **ServiceHub**（`App.xaml.cs`）：所有服务的组合根，`App.Services.Xxx` 全局访问。
  窗口按需懒创建并缓存；主题切换时重建缓存了旧画刷的弹窗。
- **MessageWindow**：一个隐藏 Win32 窗口承接所有系统消息（`WM_HOTKEY`、`WM_CLIPBOARDUPDATE`、
  托盘回调、`WM_DISPLAYCHANGE`、`WM_SETTINGCHANGE`、`TaskbarCreated`），
  各服务通过 `AddHook` 订阅，避免每个模块自建窗口。
- **主题**：调色板与样式分离。`ThemeManager` 替换 `Application.Resources.MergedDictionaries[0]`；
  样式全部 `DynamicResource`，已打开窗口即刻变色。
  **窗口底衬用 `WindowTintBrush` 主题资源自绘**（不要依赖亚克力接口的 tint 参数——
  运行时重新下发经常不生效，那正是"切浅色后窗口还是黑的"的根因）。
- **无边框窗口**：`WindowStyle=None` 仍会让 DWM 画玻璃帧/标题帧（浅色下是刺眼白条）。
  `GlassWindow` 用 `WindowChrome` 把 `GlassFrameThickness`/`CaptionHeight`/`ResizeBorderThickness`
  全部归零，并剥掉 `WS_CAPTION`。**新建窗口务必继承 `GlassWindow`**，否则白条会回来。

## 5. 关键技术点备忘

| 主题 | 要点 |
| --- | --- |
| 屏幕捕获 | 必须直接 BitBlt（`SRCCOPY\|CAPTUREBLT`）。`Graphics.CopyFromScreen` 校验枚举时会拒绝组合值并抛异常 |
| 多屏 DPI | 进程 PerMonitorV2；每个显示器一个遮罩窗，用 `SetWindowPos` 物理坐标摆放；DIP↔物理换算用 `VisualTreeHelper.GetDpi` |
| 窗口识别 | `EnumWindows` + `DwmGetWindowAttribute(EXTENDED_FRAME_BOUNDS)`，过滤 cloaked/最小化/本进程 |
| 托盘图标 | 单个 `Shell_NotifyIcon`（早期做过 ↓↑ 双图标显示网速，占两格且很丑，已移除）；**不处理 `WM_LBUTTONDBLCLK`**，否则单击消息会连带触发三次 |
| 托盘图标尺寸 | `GetSystemMetrics(SM_CXSMICON)` 取当前 DPI 物理尺寸再绘制，字号自适应 |
| WinForms 冲突 | csproj 里 `<Using Remove="System.Windows.Forms" />` 与 `System.Drawing`，避免与 WPF 类型二义 |
| OCR | TFM 自带 WinRT 投影；zh 识别需系统语言包，无包时给出引导 |
| 剪贴板 | 尊重 `Clipboard Viewer Ignore` 排除格式；写回时抑制自采集；COM 忙碌重试 |
| 亮度 | DDC/CI 写入慢，滑块 120ms 防抖；`WM_DISPLAYCHANGE` 时重枚举并销毁旧句柄 |
| 最高权限 | 计划任务 `RunLevel Highest`；非管理员实例启动时 `schtasks /Run` 拉起特权实例后退出。**该逻辑必须在单实例互斥体获取之前**，否则新实例会被当成重复实例踢掉 |
| 命名空间 | 模块命名空间 `VelvetTools.Modules.Clipboard` / `.Search` 会遮蔽 `System.Windows.Clipboard` 等，跨模块用全限定名 |
| 界面图标 | 内置功能图标统一使用 `FluentIcons.Wpf` 的语义枚举或 `<fi:FluentIcon>`；禁止新增系统 PUA 字符、Emoji 或来源不明的 SVG/字体图标 |

## 6. 任务栏内嵌信息栏（TaskbarBarWindow）

实现要点：

1. **不要使用跨进程 `WS_CHILD`**：Windows 11 的任务栏内容由 XAML/DirectComposition
   合成。即使从创建时就用 `HwndSourceParameters.ParentWindow` 指向 `Shell_TrayWnd`，
   子窗口的句柄、位置和 `WS_VISIBLE` 都正常，任务栏合成层仍会盖住外部进程的 WPF/GDI
   内容，实机表现为一段透明空白。
2. **任务栏拥有的透明 popup**：信息栏保持顶层透明 WPF 窗口，通过
   `GWLP_HWNDPARENT` 把 `Shell_TrayWnd` 设为 owner。任务栏本身处于 Topmost band，
   信息栏只在创建/定位时进入同一 band；owner 关系保证它始终在任务栏之上。
3. **稳定性**：位置和尺寸没有变化时绝不重复调用 `SetWindowPos`，也不再运行定时 z 序
   “保活”。拖动任务栏图标或点击隐藏图标面板时，由 owner 关系维持相对顺序，
   避免信息栏与 Explorer 来回争抢层级。
4. **定位**：右边界 = `TrayNotifyWnd`（托盘图标区）左缘 - 8px；高度取内容实际高度并在任务栏内居中。
   垂直任务栏直接禁用（两行布局塞不下）。
5. **首次显示要等布局**：`BuildContent()` 末尾必须
   `Dispatcher.BeginInvoke(..., DispatcherPriority.Loaded)` 再定位，
   否则 `ActualWidth` 还是旧值，表现为"设置里改完要再保存一次才生效"。
6. **两种模式**：
   - `detailed`：两行 N 列布局，列间有发丝分隔线。**监控项逐项开关**：网速、CPU 占用、
     内存占用、CPU 温度、显卡温度、硬盘温度六项各自独立勾选（设置 → 通用 →
     "信息栏内容"，仅主开关勾选时显示该区域）。网速开启时 ↑/↓ 固定占第一列，
     其余勾选项按顺序两两一列，落单项跨两行垂直居中；全部关闭时回退为只显示网速，
     避免出现空信息栏。
   - `simple`：不显示信息栏，只保留一个普通托盘图标。
   - **设置迁移**：旧存档只有 `TaskbarShowCpuMem` / `TaskbarShowTemp` 两个合并开关。
     新逐项开关声明为 `bool?`，`AppSettings.Load()` 里 `MigrateTaskbarItems()` 用 `??=`
     从旧开关派生初值；保存时再反向同步旧开关，保证降级回旧版本行为一致。
     以后给设置加"拆分旧开关"类字段照抄这个模式即可。
7. **主题**：背景**全透明**，文字颜色跟随**系统**深浅色（注册表 `SystemUsesLightTheme`）——
   浅色任务栏用黑字、深色用白字，与应用自身主题无关。
8. **Explorer 重启**：监听 `TaskbarCreated` 广播消息 → 更新 owner 并重新定位。

## 7. 硬件温度（HardwareMonitorService）

- 数据源优先级：用户已运行的 Libre/OpenHardwareMonitor `Sensor` WMI → Windows
  `root\WMI / MSAcpi_ThermalZoneTemperature`；GPU 缺失时再查询 NVIDIA 驱动自带
  `nvidia-smi`。
- `nvidia-smi` 只允许来自 `System32` 或 `Program Files\NVIDIA Corporation\NVSMI`，
  不搜索当前目录或任意 `PATH`，只传只读 CSV 参数，3 秒超时后终止，避免路径劫持和挂起。
- 不需要管理员权限，不安装、解压、下载、启动或加载第三方监控工具/内核驱动。
- ACPI Thermal Zone 只能标为“系统热区”，不能假装是 CPU Package/Core 温度。
- 采样 5 秒一次并防止重入；WMI 调用在后台线程运行，不阻塞 UI。
- **硬盘温度**（`DiskTemperatureReader`，全自研）：NVMe 走 `IOCTL_STORAGE_QUERY_PROPERTY`
  协议查询读取健康信息日志页（Log Page 02h）的复合温度，普通权限即可；SATA/ATA 走
  `SMART_RCV_DRIVE_DATA` 读属性 194/190，Windows 将该 IOCTL 限定为管理员，普通权限下
  自动跳过。全部是标准只读查询，不写设备、不装驱动。
- 微软明确说明 `Win32_TemperatureProbe.CurrentReading` 目前不会被 WMI 填充，因此不使用
  该类伪造回退。没有可信来源的 CPU 温度继续显示 `--`。
- 颜色阈值：`< 70°C` 正常、`70–85°C` 警告、`≥ 85°C` 危险（`ColorKeyFor`）。

## 8. AI 对话（Modules/Chat）

- 统一走 **OpenAI 兼容协议** `/chat/completions`：千问、Kimi、豆包、DeepSeek、智谱、硅基流动
  官方都提供该端点，一套代码全覆盖。
- **流式**：手工解析 SSE（`data: {...}` 行），支持 `delta.content` 与 `delta.reasoning_content`
  （推理模型的思维链单独折叠展示）。
- 会话历史存 `%AppData%\VelvetTools\chats.json`，最多保留 100 个会话。
- 预设在 `ChatSettings.BuiltinPresets()`，只给**名称与端点**，`Model` / `Models` 一律留空。
  `EnsurePresets()` 在升级时补齐新预设、**但不覆盖用户已填的密钥**。
  豆包比较特殊：模型位填的是"推理接入点 ID"（`ep-xxxx`），UI 有专门提示。
- **模型清单只来自 `/models` 接口**，展开下拉时按需拉取一次，拉到后打 `ModelsFromApi=true`。
  早期版本曾把内置候选写进存档，`EnsurePresets()` 会在 `ModelsFromApi=false` 时清掉
  （`Model` 只清和 `LegacyDefaultModels` 完全一致的，手填过的不动），
  免得用户看到"我没获取过却有一堆模型"。
- 加新服务商：往 `BuiltinPresets()` 加一条即可，无需改其他代码。
- **界面排版**：助手回复走"头像 + 通栏正文"，正文不套气泡（主流 AI 客户端的做法，
  长回答和代码块才有地方展开）；用户消息是右对齐气泡。鼠标移上去才浮出复制/重生成/删除操作条。
  流式输出期间用纯文本 `TextBlock` 追加（每 token 重解析 Markdown 会卡），
  收流后 `RenderSession()` 整条重渲染成 Markdown。

## 9. 文件搜索（Modules/Search）

- **默认实例**：优先用 voidtools 官方 SDK DLL 查询，`EverythingSdk` 通过 `NativeLibrary`
  动态加载当前架构的 DLL；加载失败时退回公开的 `WM_COPYDATA` IPC。只有默认数据库已加载且
  项目数大于 0 才复用默认实例，避免“Everything 正在运行但数据库为空”导致界面看似可用、
  实际永远搜不到文件。
- **私有实例**：默认索引不可用时，`EverythingBootstrap` 启动名为 `VelvetTools` 的独立实例，
  配置和数据库放在 `%AppData%\VelvetTools\everything-index\`，不修改用户自己的 Everything
  设置。官方 SDK 1.4 不支持命名实例，因此私有实例通过其公开 IPC 窗口
  `EVERYTHING_TASKBAR_NOTIFICATION_(VelvetTools)` 查询。
- **权限策略**：管理员进程使用 Everything 的 NTFS/USN 索引；普通进程不安装服务、不请求 UAC，
  自动为所有固定磁盘启用官方“文件夹索引”和变更监控。后者初次建库较慢，但不依赖管理员权限，
  能覆盖 Windows Search 未配置的目录。
- **引擎分发**：Everything 主程序与官方 SDK DLL 都放在 `Assets/Everything/` 并随输出目录复制，
  开箱即用，运行时不下载。应用退出时只向本次由自己启动的命名实例发送 `-exit`，不会终止用户实例。
- 许可说明：Everything 主程序**闭源**，但授权文本与 MIT 逐字一致且作者公开允许集成，
  官方 SDK 也按同一许可提供，因此分发这些二进制合法。义务是原样附带
  `Everything-License.txt`（已随包）。
  README 与界面文案里**不要**称它为"开源软件"。

## 9.5 知识库 / RAG（Modules/Knowledge）

整条链路都是自己实现的，没引入向量数据库，也没引入任何 RAG 框架 —— 个人知识库量级
（几千到几万块）下暴力检索一次只要几毫秒，引依赖反而是负担。

- **分块** `KnowledgeService.SplitIntoChunks`：目标 700 字/块、重叠 120 字。
  优先在空行断开，其次中英文句末标点，都找不到才硬切。重叠是为了避免答案正好被切在边界上。
- **向量化**：批量 10 条打给服务商的 `/embeddings`。返回的 `data` **顺序不保证**，
  按 `index` 归位；服务商没给 `index` 时退化为按返回顺序对齐。
  向量入库前**归一化**，这样检索时点积就是余弦相似度，省掉每次开方。
- **文档上限**：普通对话附件仍只发送 6 万字；知识库单文档最多索引 100 万字，
  解析器按调用场景接收不同上限并流式读取纯文本，避免把大日志整体展开后再截断。
- **存储** `KnowledgeStore`：元数据（文档、分块正文）走 JSON `knowledge/bases.json`，
  向量另存紧凑二进制 `knowledge/vectors_{baseId}_{revision}.bin`。
  **分开存**是因为几千个 1536 维向量转成 JSON 数字文本会膨胀十几倍且加载极慢。
  改动向量时先完整写一个新 revision，再原子替换元数据，成功后才删旧文件；
  中途崩溃最多留下孤儿文件，不会让文本块和另一版向量错位。旧 v0.8 单文件格式会在首次变更时迁移。
- **检索** `SearchAsync`：归一化查询向量与每块点积，叠加小权重关键词覆盖分
  （补型号、错误码、函数名），再给同文档相邻重叠块降权，避免 topK 全被同一小段占满。
  维度不符或无向量的块直接跳过。
- **模型绑定**：一个库严格绑定 `EmbedProviderId` / `EmbedModel` / `Dimension`。
  仅维度相同不代表向量空间相同，禁止偷偷回退到当前聊天服务商或混用另一模型。
  `RebuildIndexAsync` 在内存生成全部新向量并版本化落盘成功后才切换绑定，失败/取消保留旧索引。
  文档删空时自动解除绑定。
- **接入对话**：`ChatWindow.SendAsync` 在联网搜索之前先检索，命中的片段拼进
  `ChatMessage.HiddenContext`（只发给模型、气泡里不显示），命中来源另存
  `KnowledgeSources` 供气泡下方展示。检索用的服务商按 `kb.EmbedProviderId` 找，
  **不是**当前聊天用的那个 —— 建库和聊天完全可以用不同服务商。知识片段会标成
  `【K1】` 等引用，并明确作为“不可信参考资料”包裹，降低文档内提示注入影响。

## 10. 字体

- 内嵌未修改原版 **Inter**（SIL OFL 1.1；`Inter` 是保留字名 RFN）
  Regular/Medium/SemiBold，随包附 `Licenses/Inter-OFL.txt`。若修改、子集化或重建字体，
  必须遵循 OFL 的改名要求。
- 回退链：`Inter → Microsoft YaHei UI → Segoe UI`。**汉字刻意走系统雅黑**——
  雅黑有大量手工 hinting，在任务栏/悬浮窗 11–12px 下比任何打包中文字体都锐利，
  且省掉 5–7MB 体积。
- WPF 引用内嵌字体：`pack://application:,,,/Assets/Fonts/#Inter`（`#` 后是**字体家族名**，
  不是文件名）。WPF **不支持可变字体轴**，必须用静态字重文件。

## 11. 截图快捷键速查

| 阶段 | 键 | 行为 |
|---|---|---|
| 选区前 | C / Shift+C | 复制光标处 HEX / R,G,B |
| 任意 | Enter / 双击 | 复制并结束 |
| 选定后 | 方向键 | 移动选区（Shift=×10，Ctrl=调大小，Ctrl+Shift=大小×10） |
| 选定后 | Ctrl+S / Ctrl+Shift+S | 快速保存 / 另存为 |
| 选定后 | F3 | 钉住贴图 |
| 标注 | Ctrl+Z / Ctrl+Y | 撤销 / 重做 |
| 标注 | Shift | 正方/正圆/45° 吸附 |
| 任意 | Esc | 取消；右键=重选 |

## 12. 添加一个新工具模块

1. 新建 `Modules/YourTool/`，服务类 + 窗口（**继承 `GlassWindow`**，样式用现成键，
   颜色一律用调色板键，不要硬编码颜色值）。
2. 在 `ServiceHub` 注册服务与 `ShowXxxWindow()`。
3. 入口三选一（通常全加）：`TrayController.BuildMenu` 菜单项、
   `DashboardWindow.BuildTools` 磁贴、`LauncherWindow.BuiltinCommands` 启动器命令。
4. 需要热键 → 设置模型加字段 + `ServiceHub.ApplyHotkeys` 注册 + 设置窗口快捷键页加一行。
5. 需要配置 → `AppSettings` 加节 + 设置窗口加页（导航项、`PageXxx` StackPanel、
   `OnNavChanged` 的 pages 数组、`LoadFromSettings` / `OnSaveClick`）。
6. **引入新依赖前**：先去 `THIRD_PARTY.md` 登记许可证并确认与 GPL-3.0-or-later 兼容。

## 13. 发布

```bash
# 完整发布：严格构建 + win-x64 自包含目录 + NSIS 安装包 + 便携 ZIP + SHA-256
.\installer\build-release.ps1

# makensis.exe 不在 PATH 时
.\installer\build-release.ps1 -MakeNsis C:\Tools\NSIS\makensis.exe

# 有 Authenticode 代码签名证书时
.\installer\build-release.ps1 -CertificateThumbprint <thumbprint>
```

发布前 checklist：`--smoke` 通过、版本号（csproj `<Version>`）、
`THIRD_PARTY.md` 的合规清单逐项打勾、双显示器 + 混合 DPI 手测截图与亮度、
深浅色各切一遍看有没有漏掉 `DynamicResource` 的地方。

标签格式为 `v0.0.1-beta.N`。推送标签后，`.github/workflows/release.yml` 会在
Windows runner 上重新构建、上传工作流产物，并创建标记为 prerelease 的 GitHub Release。

**覆盖升级（无提示）**：安装器 `.onInit` 读取 `HKLM\Software\VelvetTools\InstallDir`，
非空即判定为升级（`$IsUpgrade`）：沿用原安装目录，跳过欢迎/协议/权限/目录四页直接复制文件，
且不重跑快捷方式与 `Configure-Privileges.ps1`（避免重置用户已配置的计划任务/自启），
完成页保留"启动"入口。用户拿到新包双击（或 `/S` 静默）即可完成升级，全程无需再确认。
注意 MUI2 的 `MUI_PAGE_CUSTOMFUNCTION_PRE` 只作用于紧随其后的一个页面（用后自动
undef），跳多个页需要在每页前重复 define；自定义 nsDialogs 页则在 Create 函数开头 `Abort`。
复制文件前会 `taskkill` 旧进程并 `Sleep 400`，防止句柄尚未释放导致覆盖失败。

**PowerShell 脚本编码**：仓库内 `.ps1` 必须保存为 **UTF-8 BOM + CRLF**。
Windows PowerShell 5.1 对无 BOM 文件按 ANSI(GBK) 解码，中文注释会被吞行/错位执行
（安装协议曾经空白的根因）。`build-release.ps1` 已内置编码断言防回归。

## 14. 常见问题

- **热键注册失败**：多为被微信/QQ 占用，设置里换组合。
- **OCR 认不了中文**：Windows 设置 → 语言 → 中文 → 语言选项 → 添加"光学字符识别"；或切接口模式。
- **亮度卡片是空的**：显示器 OSD 里开 DDC/CI；部分老显示器/HDMI 转接头不支持。
- **CPU 温度是 `--`**：Windows 并不统一公开 CPU 核心温度。可选择自行运行来自官方仓库的
  LibreHardwareMonitor 并启用 WMI；Velvet Tools 只读其数据，不会代为安装驱动。
- **任务栏信息栏看不见**：先确认不是垂直任务栏；查日志有没有"已挂载"；
  个别 Win11 版本会拒绝跨进程 `SetParent`，此时应自动降级为覆盖模式。
- **调试时"始终最高权限"失效**：计划任务存的是注册时的 exe 路径，重新生成到别的目录后需在设置里重新保存。
- **日志**：`%AppData%\VelvetTools\logs\app.log`，设置 → 关于 → 打开日志。

## 15. 代码风格

- C# 12 / file-scoped namespace / `nullable enable`；4 空格缩进。
- UI 文案中文；注释解释"为什么"而不是"做了什么"。
- P/Invoke：通用的进 `Common/Interop/Native.cs`，模块专用的留在模块内。
- 提交信息遵循 Conventional Commits（`feat:` / `fix:` / `docs:` …）。
