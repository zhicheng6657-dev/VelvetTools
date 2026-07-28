# Velvet Tools 发布、测试与开源合规审计

审计日期：2026-07-28  
审计范围：当前工作区源码、Release 构建、`out/publish-win-x64` 发布目录、安装/卸载程序、
开机自启与最高权限配置、内置图片/字体/图标、NuGet 依赖、Everything 引擎及 SDK、
用户指定参考项目的许可边界。

## 结论

- **发布自检：通过。** Release 严格构建为 0 warning / 0 error，最终自包含发布目录的
  全窗口冒烟测试退出码为 0。
- **安装回归：通过。** 安装器许可确认、UAC、普通/最高权限两种登录自启、按需最高权限任务、
  Windows 卸载注册及静默卸载均完成真实系统测试；卸载保留用户数据。
- **核心回归：通过。** 窗口构造与渲染、热键冲突回滚、任务栏静态跟踪、
  Everything 私有索引实际 IPC 查询（返回 10 个样例结果）及知识库版本化存储均通过。
- **安装与温度定向回归：通过。** 安装协议由构建脚本规范化为 UTF-16LE BOM + CRLF 后交给
  Unicode NSIS；本机 ACPI 接口不支持时仍从 NVIDIA 驱动公开接口读到并显示 GPU 53–55°C。
- **资源合规扫描：通过。** 源码未发现 Unicode 私用区图标码位、疑似硬编码 API 密钥；
  发布目录未发现 LibreHardwareMonitor、WinRing0、InpOut 或 `.sys` 驱动。
- **依赖健康：通过（有快照日期）。** 对全部 8 个已解析 NuGet 包使用本机 NuGet 官方缓存目录
  复核；漏洞目录更新时间为 2026-07-24T23:40:32Z。`System.IO.Packaging` 在目录中有历史公告，
  但受影响范围最高到 8.0.0，当前解析版本为 8.0.1；其余包无目录条目。8 个版本的包注册元数据
  均未标记 deprecated。受当前受限网络环境影响，本轮没有刷新到更新的在线目录。
- **开源建议：MIT。** 按当前依赖与资源构成，可以将 Velvet Tools 源码按 MIT 发布；
  发行包必须继续附带 `Licenses/` 与 `Assets/Everything/Everything-License.txt`。

本报告是基于当前仓库状态的工程与许可尽调，不替代律师针对具体司法辖区给出的法律意见。

## 功能验证

| 项目 | 结果 | 证据或说明 |
| --- | --- | --- |
| Release 严格构建 | 通过 | `dotnet build -c Release -p:TreatWarningsAsErrors=true`，0 warning / 0 error |
| 全窗口冒烟测试 | 通过 | `VelvetTools.exe --smoke --shots ...`，退出码 0，日志包含 `SMOKE WINDOWS OK` |
| 安装器构建 | 通过 | NSIS 3.12、zlib 压缩，0 warning；安装包与便携包均为 Windows x64 自包含版本 |
| 许可确认与 UAC | 通过 | 安装前必须勾选同意许可；安装程序清单请求管理员权限以写入 Program Files/HKLM |
| 普通权限登录自启 | 通过 | `/ADMINMODE=0 /AUTOSTART=1` 仅写 HKCU Run，值指向带引号的安装版 EXE，无计划任务 |
| 最高权限登录自启 | 通过 | `/ADMINMODE=1 /AUTOSTART=1` 创建 `Highest + Interactive` 登录计划任务，无 HKCU Run 重复项 |
| 最高权限按需模式 | 通过 | 未开启登录自启时计划任务不含 LogonTrigger；设置页不会再把“任务存在”误判成“开机自启” |
| 卸载回归 | 通过 | 删除安装目录、卸载注册、计划任务、HKCU Run 与快捷方式；保留 `%AppData%\VelvetTools` |
| Everything 实际检索 | 通过 | 独立 `VelvetTools` 索引经公开 IPC 返回 10 个真实样例结果 |
| 知识库核心 | 通过 | 在隔离临时目录完成分块、写入、重载、版本替换与删除 |
| 热键冲突 | 通过 | 临时注册 F24–F21 组合，确认冲突时拒绝新值且旧注册保持有效 |
| 任务栏信息栏稳定性 | 通过 | 静态任务栏句柄/几何轮询不再重复 owner 绑定或 `SetWindowPos` |
| 温度安全回退 | 通过 | ACPI 返回“不支持”时不影响 GPU；系统路径 `nvidia-smi` 返回 RTX 3060 53–55°C，界面正确标注来源 |
| 安装协议编码 | 通过 | 构建时生成 UTF-16LE BOM、CRLF 专用协议文本，避免依赖系统活动代码页 |
| 界面视觉检查 | 通过 | AI 对话、设置/服务商、快捷键、搜索、截图画笔参数均生成实现截图并检查 |
| Logo 与截图工具栏对照 | 通过 | 参考图与实现图已拼接对照；结果记录在根目录 `design-qa.md` |

## 关键问题与修复

### 任务栏详细信息为何会失效

旧实现每 400 ms 验证一次跨进程 owned popup 的 owner。Windows 11 的任务栏由
XAML/DirectComposition 合成，拖动任务栏图标、点击隐藏图标或任务栏重组期间，
系统可能暂时无法按预期返回 owner。旧代码因此反复执行 `SetWindowLongPtr` 与
`SetWindowPos`，与任务栏自身的 Z-order 调整竞争，表现为信息栏闪烁或消失。

现在只在 `Shell_TrayWnd` 句柄真正变化时重新绑定 owner；几何未变化时不再重复定位。
新安装默认关闭任务栏详细信息，用户主动开启后的既有配置仍会保留。

### 文件搜索

默认优先调用 voidtools 官方 Everything SDK；默认索引不可用时，启动配置和数据库均隔离的
`VelvetTools` 私有实例，并通过 Everything 公开 IPC 查询。代码只包含自行编写的最小 P/Invoke
声明与 IPC 客户端，没有复制官方 SDK 示例或第三方开源实现。

Everything 主程序不是开源项目，但官方分发授权文本与 MIT 条款一致，并同时附带 PCRE 的
BSD-3-Clause 声明。当前发布包原样附带官方许可文件，源文件与发布文件 SHA-256 一致。

### 温度读取

没有采用 `Win32_TemperatureProbe.CurrentReading`，因为微软文档明确说明当前 WMI 实现不会
填充该字段。CPU/系统温度优先读取用户已经运行的 Libre/OpenHardwareMonitor WMI，再回退到
ACPI Thermal Zone；ACPI 读数只标成“系统热区”。NVIDIA GPU 使用驱动自带 `nvidia-smi`
只读 CSV 查询，且只允许受保护的系统目录和 Program Files 固定路径。

发行包没有新增 LibreHardwareMonitor、OpenHardwareMonitor、WinRing0、PawnIO 或其他驱动；
没有复制 all-smi、LibreHardwareMonitor 或 OpenHardwareMonitor 的源码。

### 安装协议乱码

NSIS `LicenseData` 要求 DOS CRLF 文本。源协议继续保持便于版本管理的 UTF-8，发布脚本在
`out/release` 中临时生成带 UTF-16LE BOM 和 CRLF 的专用副本，Unicode NSIS 编译完成后立即
删除临时文件。这样协议页不再受构建机器活动代码页影响。

## 图标、字体与品牌资源

- 内置功能图标统一由 `FluentIcons.Wpf` 提供，底层 Microsoft Fluent UI System Icons 为 MIT。
- 已移除 Segoe Fluent Icons / Segoe MDL2 私用区字符，不再依赖 Windows 私有图标码位。
- Inter Regular / Medium / SemiBold 为未修改原版，按 SIL OFL 1.1 分发，保留 `Inter` 保留字体名。
- 启动器中的第三方应用图标仅在用户电脑上通过 Windows API 动态提取，不进入仓库或发行资源。
- V Logo 是本项目新生成并确定性派生的品牌资源；生成提示、处理过程与文件清单记录在
  `src/VelvetTools/Assets/Brand/README.md`。
- `app.ico` 包含 16、20、24、32、40、48、64、128、256 px 九个尺寸。

## 第三方边界

TrafficMonitor（Anti-996）、Chatbox（GPLv3）和 ShareX（GPL-3.0）只用于理解通用产品形态；
仓库未纳入它们的源文件、图标、字体、文案或其他资源。任务栏窗口、聊天界面和截图工具栏均为
当前 WPF 项目的独立实现。更完整的依赖用途与逐项许可见根目录 `THIRD_PARTY.md`。

## 发布包必须保留

- `Licenses/VelvetTools-MIT.txt`
- `Licenses/THIRD_PARTY.md`
- `Licenses/FluentIcons-MIT.txt`
- `Licenses/FluentUI-System-Icons-MIT.txt`
- `Licenses/FluentUI-System-Icons-NOTICE.txt`
- `Licenses/Inter-OFL.txt`
- `Licenses/OpenXml-MIT.txt`
- `Licenses/PdfPig-Apache-2.0-and-NOTICE.txt`
- `Licenses/System.Management-MIT.txt`
- `Licenses/System.Management-THIRD-PARTY-NOTICES.txt`
- `Licenses/NSIS-zlib.txt`
- `Assets/Everything/Everything-License.txt`

## Beta 0.01 发布产物

- `VelvetTools-Setup-0.0.1-beta.1-win-x64.exe`
- `VelvetTools-Portable-0.0.1-beta.1-win-x64.zip`
- `SHA256SUMS.txt`

两份可执行发布物当前均未使用 Authenticode 代码签名证书，Windows SmartScreen 可能显示
“未知发布者”。这不影响 MIT 开源或安装/卸载，但正式稳定版建议使用可信代码签名证书并保留
时间戳签名。构建脚本已经提供证书指纹与时间戳参数，不应在仓库中保存证书私钥。

## 后续发布纪律

1. 每次新增 NuGet、字体、图标、二进制或样例代码前先登记来源和许可。
2. 不从 GPL、Anti-996、来源不明或禁止再分发的项目复制代码与视觉资产。
3. 每次发布重跑严格构建、冒烟、漏洞/弃用依赖扫描、私用区字符扫描和敏感信息扫描。
4. 若更换 Logo 或其他生成式资源，保留输入参考、提示词、生成日期和派生脚本。
