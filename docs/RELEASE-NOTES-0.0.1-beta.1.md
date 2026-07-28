# Velvet Tools Beta 0.01

这是 Velvet Tools 的首个公开测试版，支持 Windows 10 1809 及以上版本和 Windows 11 x64。

## 主要功能

- 截图、标注、贴图、OCR、翻译和屏幕取色
- AI 对话、文档附件、联网搜索和本地知识库
- 文件搜索、剪贴板历史和快速启动器
- 可逐项配置的任务栏信息栏与桌面监控
- 显示器亮度、系统音量和硬件温度读取
- 可选开机自启动和管理员模式

## 安装

- `VelvetTools-Setup-0.0.1-beta.1-win-x64.exe`：支持覆盖升级和完整卸载
- `VelvetTools-Portable-0.0.1-beta.1-win-x64.zip`：解压后直接运行
- `SHA256SUMS.txt`：用于核对下载文件

安装包与便携包均已包含 .NET 8，不需要额外安装运行库。当前测试版尚未使用商业
Authenticode 证书签名，Windows SmartScreen 可能显示“未知发布者”，请只从本仓库下载。

## 说明

Windows 并不会在所有电脑上公开 CPU 核心温度；没有可信数据源时，软件会显示 `--`，
不会猜测或伪造温度，也不会为了读取传感器而捆绑不安全的第三方内核驱动。
