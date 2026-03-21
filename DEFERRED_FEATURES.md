# Panache — Deferred Features Living Document

> **Rule:** Every time the thought "this feature won't work" or "this is too complex" arises,
> implement it instead and document it here. This list tracks what was deferred and its current status.

---

## Features Deferred During Initial Build

### 1. Text Word Wrap
- **Status:** ⏳ NOT YET IMPLEMENTED
- **Why it was deferred:** Multi-line text requires splitting content into lines, measuring each,
  and re-running height calculation in the layout pass — more involved than single-line.
- **What it needs:**
  - In `LayoutEngine.MeasureNode`: break NodeValue into lines that fit within `availWidth`,
    return `(maxLineW, totalLineH * lineCount)` as intrinsic size.
  - In `SkiaRenderer.DrawText`: split into the same lines, render each at successive baseline Y.
  - Style property: `s.WhiteSpace = WhiteSpace.Wrap` (default `NoWrap`).
- **Priority:** High — affects all long-label nodes.

### 2. Text Ellipsis (Proper Truncation)
- **Status:** ✅ IMPLEMENTED (2026-03-20)
- **Previously:** The renderer only clipped with `canvas.ClipRect` — text was hard-cut.
- **Fixed:** Binary-search truncation in `SkiaRenderer.DrawText`: shrinks the string until
  `"..."` fits within `contentWidth`. Recalculates textX alignment after truncation.
- **Style property:** `s.TextOverflow = TextOverflow.Ellipsis`

### 3. Multi-Line Text (Line Height / Stacked Rows)
- **Status:** ⏳ NOT YET IMPLEMENTED
- **Related to:** Word Wrap (above). `Style.LineHeight` property exists but currently unused.
- **What it needs:** Same as word wrap — measure pass + multi-draw-call render pass.

### 4. Horizontal Alignment of Children (Justify / Align)
- **Status:** ⏳ NOT YET IMPLEMENTED
- **Why deferred:** Only left-aligned flow implemented (children pack at start).
- **What it needs:** `s.JustifyContent` (Start, Center, End, SpaceBetween, SpaceAround) and
  `s.AlignItems` (Start, Center, End, Stretch) applied in `PlaceNode`.

### 5. Node Event Handling (Click / Hover)
- **Status:** ⏳ NOT YET IMPLEMENTED (infrastructure exists)
- **Why deferred:** ImGui renders the final image as a single `ImGui.Image()` call.
  Mouse events need hit-testing against `LayoutBox` entries and routing to `Node.OnClick` etc.
- **What it needs:**
  - After `ImGui.Image(...)`, use `ImGui.IsItemHovered()` + `ImGui.GetMousePos()` to get cursor.
  - Walk `_layout` dictionary, find deepest box containing the cursor, fire `OnMouseEnter/Leave/Click`.
  - Track "previous hover" to detect enter/leave transitions.
- **Infrastructure already in Node.cs:** `OnClick`, `OnMouseEnter`, `OnMouseLeave`, `IsInteractive`.

### 6. Image/Texture Nodes (render game icons inside a Panache node)
- **Status:** ⏳ NOT YET IMPLEMENTED
- **Why deferred:** Nodes currently only render background + border + text.
- **What it needs:** `Node.ImagePath` or `Node.TextureId` property + renderer path that
  draws an `SKBitmap` inside the node rect. Dalamud `ITextureProvider` can load game icons
  by icon ID into a `IDalamudTextureWrap`; extract the raw RGBA bytes and load into SkiaSharp.

### 7. Scrollable Containers
- **Status:** ⏳ NOT YET IMPLEMENTED
- **Why deferred:** Requires tracking a scroll offset per node and clipping to the node rect
  while offsetting child positions. Layout must still measure full child height.
- **What it needs:** `s.OverflowY = Overflow.Scroll`, `Node.ScrollOffsetY` state,
  scroll-wheel input via ImGui mouse delta, clip rect set to node bounds before children.

### 8. Absolute / Overlay Positioning
- **Status:** ⏳ NOT YET IMPLEMENTED
- **Why deferred:** All nodes currently participate in flow layout.
- **What it needs:** `s.Position = Position.Absolute`, `s.Top`, `s.Left`, `s.Right`, `s.Bottom`
  style properties. Absolutely-positioned nodes are excluded from parent's flow measurement
  and placed relative to the nearest ancestor with `Position.Relative`.

### 9. Z-Order / Render Layers
- **Status:** ⏳ NOT YET IMPLEMENTED
- **Why deferred:** The tree walk always renders parent-before-child.
- **What it needs:** A `s.ZIndex` integer property; nodes collected into buckets and drawn in
  ascending Z order after the normal tree pass.

### 10. CSS Class Inheritance / Theme System
- **Status:** ⏳ NOT YET IMPLEMENTED
- **Why deferred:** Currently style is set per-node only.
- **What it needs:** A `Dictionary<string, Action<Style>>` theme registry. Nodes with matching
  class names inherit that style before their own overrides are applied.
  `Node.WithClass("card")` + `Theme.Register("card", s => { s.BorderRadius = 8; ... })`.

### 11. Border per-edge (top/right/bottom/left separately)
- **Status:** ⏳ NOT YET IMPLEMENTED
- **Why deferred:** SkiaSharp `DrawRoundRect` strokes all 4 sides equally.
- **What it needs:** Per-side border widths in Style; render as individual `DrawLine` calls
  instead of a stroked rect when sides differ.

### 12. Background Image / Pattern fills
- **Status:** ⏳ NOT YET IMPLEMENTED
- **Why deferred:** Background currently only supports solid color or two-color linear gradient.
- **What it needs:** `s.BackgroundImage` (SKBitmap), `s.BackgroundSize` (Cover, Contain, Tile),
  rendered via `SKShader.CreateBitmap` in the background draw step.

---

## Rule

When any of the above is implemented:
1. Change status from ⏳ to ✅ with the implementation date.
2. Add a one-line note of what file/method was changed.
3. Add it to the HelpWindow feature list if user-visible.
