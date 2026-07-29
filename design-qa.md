# Design QA

## Sources

- Screenshot toolbar reference:
  `C:\Users\15825\AppData\Local\Temp\codex-clipboard-6ff3cb5f-cb76-4c22-9101-818d375154ef.png`
- Logo merge reference:
  `C:\Users\15825\AppData\Local\Temp\codex-clipboard-0f4d27e3-5169-49c4-9e8d-3b3411b94ba6.png`
- Logo stroke-weight reference:
  `C:\Users\15825\AppData\Local\Temp\codex-clipboard-326407d4-91f5-4def-89ce-0b1ca24bb9a3.png`

## Implementation captures

- `out/system-audit/shots-final-actual-search/截图工具栏-画笔参数.png`
- `out/system-audit/shots-final-actual-search/AI 对话.png`
- `out/system-audit/shots-final-actual-search/设置-AI 对话.png`
- `out/system-audit/shots-final-actual-search/设置-快捷键.png`
- `out/system-audit/shots-final-actual-search/控制面板.png`
- `src/VelvetTools/Assets/logo.png`

## Combined comparisons

- `out/system-audit/qa-toolbar-comparison.png`
- `out/system-audit/qa-logo-comparison.png`

## Findings

- Screenshot toolbar uses the reference's compact white two-row structure and
  square controls. The second row appears only for the selected annotation
  tool; the captured pen state exposes stroke width and color without keeping
  irrelevant options visible.
- Tool order and action grouping remain recognizably aligned with the
  reference while using MIT-licensed Microsoft Fluent UI System Icons instead
  of tracing or copying the reference's glyph artwork.
- Final V mark combines the reference's flat, diagonally cut upper arms with
  the smooth rounded lower join. Stroke weight matches the supplied thicker
  direction and there is no folded tail or overlapping lower segment.
- Captured settings, chat, search, knowledge, clipboard, launcher, dashboard,
  and screenshot states have no clipped controls, missing glyphs, broken
  resource references, or unintended private-use characters.

result: passed

---

## AI conversation workspace redesign · 2026-07-29

### Visual target and design references

- Hermes Agent Desktop official showcase:
  `https://hermes-agent.nousresearch.com/img/desktop/showcase.webp`
- Hermes Agent Desktop source and design notes:
  `https://github.com/NousResearch/hermes-agent/tree/main/apps/desktop`
- Source-level reference record and license boundary:
  `docs/AI-CHAT-REDESIGN-REFERENCES.md`

### Implementation captures

- Default 1180 × 780 workspace:
  `out/hermes-chat-qa-2/AI 对话.png`
- Compact 820 × 600 workspace:
  `out/hermes-chat-qa-2/AI 对话-窄窗口.png`
- 1920 × 1100 conversation state:
  `out/hermes-chat-qa-2/AI 对话-示例会话.png`
- Dark workspace:
  `out/hermes-chat-qa-2/AI 对话-深色.png`
- Same-viewport reference/prototype comparison:
  `out/hermes-chat-qa-2/Hermes-Velvet-side-by-side.png`

### Findings

- The final WPF screen follows the official Hermes desktop hierarchy: a single
  flat sidebar, compact title bar, bounded document reading column, lightly
  outlined user rows, unboxed assistant text, a bottom composer and a narrow
  status bar.
- At the 1920-pixel comparison width the sidebar expands to 316 pixels, message
  and composer width caps at 1040 pixels, and the first transcript row starts
  at the same visual depth as the reference. At ordinary window widths these
  values contract without hiding the conversation.
- At 820 × 600, provider/model controls yield space before the input and send
  path. No control is clipped, overlaid or forced outside the window.
- Light and dark modes use dedicated chat surfaces. The dark composer has one
  surface instead of a nested text-box card; session text uses dynamic theme
  resources and remains legible after a live theme change.
- Window edges retain the native resize hit area; minimize, maximize/restore,
  title-bar double-click and normal drag remain functional.
- Hermes is MIT licensed. Its public desktop source was used as a lawful
  implementation reference and its copyright/license text is retained in
  `Licenses/Hermes-Agent-MIT.txt`. Velvet Tools remains a WPF/C# implementation
  and does not ship Hermes React/Electron runtime, branding, icons or copy.
- Existing Microsoft Fluent UI System Icons, Inter font files and original
  Velvet brand assets are reused; no new unlicensed asset was introduced.

final result: passed

---

## AI reasoning, model picker and maximize follow-up · 2026-07-29

### Source visual truth

- User-supplied problem-state crop:
  `C:\Users\15825\AppData\Local\Temp\codex-clipboard-8542c3c7-ab5e-4f59-857a-e42e3072d35c.png`
- Hermes disclosure behavior:
  `tool-cache/hermes-agent-reference/hermes-agent-main/apps/desktop/src/components/assistant-ui/thread/message-parts.tsx`
- Hermes disclosure row and composer model pill:
  `tool-cache/hermes-agent-reference/hermes-agent-main/apps/desktop/src/components/chat/disclosure-row.tsx`
  and
  `tool-cache/hermes-agent-reference/hermes-agent-main/apps/desktop/src/app/chat/composer/model-pill.tsx`

The supplied 135 × 55 image documents the old control that needed replacing;
it is not treated as a pixel-for-pixel target. The accepted target is the
Hermes interaction hierarchy: a text-sized disclosure, right-side chevron,
automatic expansion while streaming, automatic collapse when complete, and a
model-only composer control.

### Rendered implementation evidence

- Full reasoning state:
  `out/chat-fixes-qa-2/AI 对话-思考过程.png`
- Focused reasoning crop:
  `out/chat-fixes-qa-2/AI 对话-思考过程-局部.png`
- Combined problem-state / final-state comparison:
  `out/chat-fixes-qa-2/AI 对话-思考过程-对比.png`
- Open model menu:
  `out/chat-fixes-qa-2/AI 对话-模型选择.png`
- Maximized state:
  `out/chat-fixes-qa-2/AI 对话-最大化.png`

Viewport: WPF desktop window at 1180 × 780 device-independent pixels for the
reasoning and model states; 1920 × 1032 for maximize. The implementation
captures are 1180 × 780 and 1920 × 1032 pixels at 1× density. The source crop
is 135 × 55 pixels and was normalized to 270 × 110 only for the focused
comparison; its aspect ratio was preserved. CSS size is not applicable to this
native WPF screen.

State: light theme, completed assistant reasoning expanded for inspection;
model picker open with the current model selected; maximized window on a
1920 × 1080 display whose Windows work area is 1920 × 1032.

### Full-view and focused comparison

- Full view confirms that the reasoning disclosure remains part of the
  assistant document flow and does not introduce a card, extra column, clipped
  content or horizontal overflow.
- The focused comparison is required because the original issue is a
  text-sized control. It shows removal of the circular leading icon and white
  capsule, a smaller right-side chevron, and a thin left rule connecting the
  expanded reasoning body.
- Fonts and typography: the disclosure uses the existing application font
  stack at 12 px with 19 px body line height. Label, body and assistant answer
  retain distinct optical weight and remain readable in light and dark themes.
- Spacing and layout rhythm: the hover target hugs the label, the body begins
  directly below it, and the model menu aligns with the composer control
  without covering the send action.
- Colors and visual tokens: all foreground, hover, selected, border and popup
  colors use existing dynamic theme resources; no hard-coded light-only
  surface was added.
- Image and icon fidelity: the existing MIT-licensed Fluent UI System Icons
  chevron is reused. No raster substitute, copied mark, custom SVG or new
  third-party asset was introduced.
- Copy: raw model value `auto` is presented to users as `自动选择`; provider,
  Base URL and API-key configuration remain in Settings.

### Comparison history

- Earlier P1: the composer exposed provider and model as two cramped controls,
  and raw `auto` looked like a broken value.
  Fix: removed the provider control, added a model-only ghost selector, moved
  provider/API configuration to Settings, and mapped `auto` to `自动选择`.
  Post-fix evidence:
  `out/chat-fixes-qa-2/AI 对话-模型选择.png`.
- Earlier P2: the reasoning header appeared as a standalone capsule with a
  circled leading arrow, making it visually heavier than the answer.
  Fix: replaced it with the Hermes-style disclosure row, right chevron,
  streaming open state, completed collapsed state and a thin body guide.
  Post-fix evidence:
  `out/chat-fixes-qa-2/AI 对话-思考过程-对比.png`.
- Earlier P1: borderless maximize could consume the monitor bounds and cover
  the taskbar.
  Fix: handled `WM_GETMINMAXINFO` against the active monitor work area.
  Post-fix evidence:
  `out/chat-fixes-qa-2/AI 对话-最大化.png`; automated assertion passed at
  1920 × 1032.

### Findings

No actionable P0, P1 or P2 visual mismatch remains. No focused interaction is
clipped at the tested normal, compact or maximized sizes.

### Follow-up polish

- P3: elapsed reasoning time could be shown when providers expose reliable
  timing metadata. It is intentionally omitted rather than displaying an
  estimated value.

### Implementation checklist

- [x] Completed reasoning expands and collapses from a text-sized disclosure.
- [x] Streaming reasoning opens automatically and collapses on completion.
- [x] Composer exposes only the model selector.
- [x] Model menu open, selected and scrolling states captured.
- [x] `auto` displays as `自动选择`.
- [x] Maximize stays within the Windows work area.
- [x] Debug build and UI smoke suite pass with zero warnings or errors.

final result: passed
