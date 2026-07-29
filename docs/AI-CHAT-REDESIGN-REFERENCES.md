# AI 对话界面改造参考与合规边界

更新日期：2026-07-29

本次改造以 Velvet Tools 的 WPF 界面与交互代码为实现载体。Hermes Agent Desktop
采用 MIT 许可证，因此已下载其公开桌面端源码进行源码级参考；没有使用其商标、品牌素材、
角色形象、产品文案或专有数据。

## 参考来源

### Tencent Marvis

- 官网：<https://marvis.qq.com/>
- 用途：只参考公开产品画面呈现出的信息层级，例如左侧导航、主任务输入、推荐任务区
  和较克制的留白。
- 边界：按闭源产品处理。没有反编译、抓取或复制客户端资源、图标、吉祥物、文案、
  布局参数与实现代码。

### Hermes Agent Desktop

- 仓库：<https://github.com/NousResearch/hermes-agent>
- 官方桌面截图：
  <https://hermes-agent.nousresearch.com/img/desktop/showcase.webp>
- 桌面端说明：
  <https://github.com/NousResearch/hermes-agent/blob/main/apps/desktop/README.md>
- 公开设计说明：
  <https://github.com/NousResearch/hermes-agent/blob/main/apps/desktop/DESIGN.md>
- 上游许可证：MIT。
- 用途：参考公开设计原则与实际组件结构，包括“对话为主页”“扁平而非层层套卡片”、
  左侧会话栏、文档式消息排版、贴底输入区、模型单入口、思考内容披露行、状态栏、
  主题令牌和窗口缩放优先级。
- 实现：将上述结构与交互关系转换为 WPF/XAML/C#；没有把 React/Electron 运行时、
  Hermes 图标、品牌资源或业务服务直接打进 Velvet Tools。
- 交互边界：服务商、Base URL 和 API Key 仍由 Velvet Tools 设置页管理；对话输入区
  只保留模型切换。思考内容在流式生成时展开、完成后收起，使用项目既有 Fluent 图标。
- 归属：随项目附 `Licenses/Hermes-Agent-MIT.txt`，保留 Copyright © 2025
  Nous Research 与 MIT 许可全文。

### 其他观察对象

- Open Coworker：<https://www.opencoworker.app/>，MIT。只用于确认本地优先、
  BYOK 和任务交付式文案方向。
- OpenLoaf：<https://github.com/OpenLoaf/OpenLoaf>，AGPL-3.0/商业双许可。
  只观察公开产品结构，没有引用任何源码。
- Open WebUI 当前版本包含品牌保护条款，不作为本次代码或视觉资产来源。

## 实际使用的资源

- 界面代码：Velvet Tools 项目原创 WPF/XAML/C#。
- 图标：项目既有 `FluentIcons.Wpf`，对应 Microsoft Fluent UI System Icons；
  许可证与归属信息沿用 `THIRD_PARTY.md`。
- 字体：项目既有未修改 Inter 字体与系统中文字体回退；没有新增字体。
- Logo：项目现有原创 Velvet V 标识；没有使用 Marvis 或 Hermes 品牌素材。

Velvet Tools 自本次切换起采用 GPL-3.0-or-later；Hermes Agent Desktop、Fluent Icons、
Inter 等第三方部分仍分别保留各自原始许可证。
