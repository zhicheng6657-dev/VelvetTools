# Design QA

This file records release-facing visual verification. It is not a list of
third-party dependencies or attributions.

## Areas checked

- screenshot and annotation toolbar;
- Velvet V logo at application, tray and small-icon sizes;
- AI conversation workspace, model picker and reasoning disclosure;
- settings, provider configuration, search, knowledge base, clipboard,
  launcher and dashboard;
- normal, compact, maximized, light and dark window states.

## Results

- Controls remain visible and usable at the tested normal and compact sizes.
- Maximizing the borderless main window uses the active monitor work area and
  does not cover the Windows taskbar.
- The AI composer shows only the model selector; provider, Base URL and API key
  configuration remain in Settings.
- Completed reasoning can be expanded or collapsed from a lightweight text
  disclosure. Streaming reasoning opens while content is being generated and
  collapses after completion.
- The screenshot toolbar exposes options only for the selected annotation tool.
- No Unicode private-use glyphs, unlicensed raster substitutes or copied brand
  marks are used for built-in controls.
- Built-in function icons come from the licensed Fluent icon packages listed in
  `THIRD_PARTY.md`; the Velvet V artwork is original project material.
- Debug and Release builds, window construction smoke tests and focused UI
  regression checks pass without actionable P0, P1 or P2 visual defects.

User-supplied screenshots and public products were used only to communicate
general layout and interaction requirements. No code, icons, fonts, logos,
screenshots or brand assets from those products are distributed by Velvet Tools.

result: passed
