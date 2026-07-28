# Velvet Tools brand asset source

`VelvetTools-V-master.png` is a new, project-specific V mark created on
2026-07-27 with OpenAI's built-in image-generation capability. Two images were
used only as visual-direction references:

- the user's `V_logo_square_1024.png`, for a blue/cyan palette and friendly
  desktop-software feel;
- the project's former V icon, for compact small-size proportions.

The generated mark uses a new joined-ribbon geometry and does not trace or
reuse the former skill-icons SVG path. The prior skill-icons-derived raster and
vector logo assets were replaced.

## Generation prompt

Use case: logo-brand. Create a completely original, vector-friendly capital V
symbol for Velvet Tools. Use the user's preferred upper treatment—short
horizontal top edges with diagonal outer cuts—and combine it with one seamless,
cleanly rounded lower contour. Use broad strokes matching the supplied
stroke-weight reference; do not add a protruding tail, folded flap, or
criss-cross. Preserve only the general blue/cyan direction from the references;
do not trace their exact geometry. Center one standalone mark on a flat
`#ff00ff` chroma-key background. Keep it clear at 16 px. No text, watermark,
enclosing tile, cast shadow, or reflection.

The chroma background was removed with the installed OpenAI image-generation
skill helper (`remove_chroma_key.py`, soft matte and despill). The final
packaged PNG and ICO derivatives are generated deterministically by
`tools/generate_brand_assets.py`.

The generated brand assets are distributed as part of Velvet Tools under the
repository's MIT license. They contain no bundled third-party font or icon file.
