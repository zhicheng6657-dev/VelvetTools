# Velvet Tools

Velvet Tools 是一个常驻系统托盘的 Windows 工具箱。它把日常会用到的小功能集中在一起，
需要时从托盘打开，不用时安静地在后台运行。

## 主要功能

- 截图、标注、贴图、OCR、截图翻译和屏幕取色
- AI 对话、文档附件、联网搜索和本地知识库
- 文件搜索、剪贴板历史和快速启动器
- 网速、CPU、内存与硬件温度监控
- 显示器亮度和系统音量调节
- 可选任务栏信息栏、开机自启动和管理员模式

快捷键和任务栏显示内容都可以在设置中调整。检测到快捷键冲突时，软件会保留原来的设置，
不会强行覆盖。

## 下载与安装

当前版本为 **Beta 0.01**，支持 Windows 10 1809 及以上版本和 Windows 11 x64。

请从 [Releases](https://github.com/zhicheng6657-dev/VelvetTools-cess/releases) 下载：

- `VelvetTools-Setup-0.0.1-beta.1-win-x64.exe`：安装版，支持覆盖升级和完整卸载
- `VelvetTools-Portable-0.0.1-beta.1-win-x64.zip`：便携版，解压后直接运行

安装版会请求一次管理员权限，用于写入 Program Files 和注册卸载信息。开机自启动与
“始终以管理员权限运行”都是可选项，不会默认强制开启。当前测试版尚未使用商业代码签名证书，
Windows SmartScreen 可能提示“未知发布者”，请从本仓库下载并核对 `SHA256SUMS.txt`。

## 数据与隐私

设置、对话、知识库和 API 密钥默认保存在本机 `%AppData%\VelvetTools`。
需要联网的 AI、翻译、OCR 和搜索功能，只会连接你自己选择并配置的服务。
不配置在线服务时，截图、文件搜索、剪贴板、启动器等本地功能仍可正常使用。

## 从源码构建

需要 .NET 8 SDK：

```powershell
dotnet build src\VelvetTools\VelvetTools.csproj -c Release
```

生成自包含安装包和便携包：

```powershell
.\installer\build-release.ps1
```

详细开发说明见 [docs/DEVELOPMENT.md](docs/DEVELOPMENT.md)，第三方组件与许可见
[THIRD_PARTY.md](THIRD_PARTY.md)。

## 作者

[Velvet](https://github.com/zhicheng6657-dev) · [个人网站](https://www.whyinstitution.online/)

## 开源许可

[GNU GPL-3.0-or-later](LICENSE) © 2026 [Velvet](https://github.com/zhicheng6657-dev/VelvetTools-cess)

你可以使用、研究、修改和重新分发 Velvet Tools；发布修改版或衍生版时，需要继续按
GPL-3.0-or-later 提供对应源码。字体、图标、Everything 与 NuGet 组件仍分别遵循
[THIRD_PARTY.md](THIRD_PARTY.md) 中列出的原始许可证。
