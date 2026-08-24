# Panache — Deferred Features Living Document

> **Rule:** Every time the thought "this feature won't work" or "this is too complex" arises,
> implement it instead and document it here. This list tracks what was deferred and its current status.

---

## Features Deferred During Initial Build

### 1. Text Word Wrap
- **Status:** ✅ IMPLEMENTED (2026-08-24)
- **Style property:** `s.TextOverflow = TextOverflow.Wrap` (joined Clip and Ellipsis on the
  existing enum rather than adding a second `WhiteSpace` property).
- **What was changed:**
  - New `Core/TextLayout.cs` — greedy whitespace breaking, mid-word hard-break for
    over-long tokens, `\n` as a hard break, and a `[ThreadStatic]` cache keyed on
    (text, font, width, maxLines) so layout and the renderer break the text exactly once.
  - `LayoutEngine` measurement became **width-aware**: `MeasureNode` takes the available
    content width, and subtrees containing wrapping text use a width-keyed memo
    (`_measureWrapped`) instead of the width-independent one. `Node.CachedHasWrap` /
    `HasWrapStamp` keep the fast path free of extra lookups for the ~99% of nodes that
    hold no wrapping text.
  - `SkiaRenderer.DrawWrappedText` — one baseline per line, block vertically centred.
- **Also added:** `s.MaxLines` caps the block and ellipsizes the last kept line.
- **Verified:** growth of a Fit-height card, MaxLines 1 and 2, hard-break of an
  unbreakable token, `TextAlign.Center` per line, and wrapping as a Fill sibling inside a
  Fit-height horizontal row.

### 2. Text Ellipsis (Proper Truncation)
- **Status:** ✅ IMPLEMENTED (2026-03-20)
- **Previously:** The renderer only clipped with `canvas.ClipRect` — text was hard-cut.
- **Fixed:** Binary-search truncation in `SkiaRenderer.DrawText`: shrinks the string until
  `"..."` fits within `contentWidth`. Recalculates textX alignment after truncation.
- **Style property:** `s.TextOverflow = TextOverflow.Ellipsis`

### 3. Multi-Line Text (Line Height / Stacked Rows)
- **Status:** ✅ IMPLEMENTED (2026-08-24) — same change as #1.
- `Style.LineHeight` is now read for real: it sets the per-line advance in both the
  measured height and the painted baselines. It is also folded into the visual
  fingerprint, but only for `Wrap` nodes — for everything else it still reaches the
  renderer solely through the computed box.

### 4. Horizontal Alignment of Children (Justify / Align)
- **Status:** 🟡 PARTIAL (2026-08-24) — `AlignItems` done, `JustifyContent` still open.
- **Done:** `s.AlignItems` (Start / Center / End) plus a per-child `s.AlignSelf` override,
  applied on the **cross** axis in `PlaceHorizontal`, `PlaceVertical` and
  `PlaceHorizontalWrap`. Start is the default and is byte-identical to the old behaviour.
  A Fill child on the cross axis is skipped — it already spans the whole extent — as is a
  scroll container that handed down an unbounded cross extent.
- **Why it mattered:** it deletes the hand-computed centring margins
  (`Margin = new EdgeSize((rowH - iconSz) / 2f, 0, 0, 0)`) that consumers were carrying,
  which are exactly the expressions a UI scale factor breaks.
- **Still needed:** `s.JustifyContent` (Start, Center, End, SpaceBetween, SpaceAround) on
  the **main** axis, applied to the cursor start and inter-child gap in the same three
  placement helpers.

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

### 12a. Surface UI Scale
- **Status:** ✅ IMPLEMENTED (2026-08-24)
- **Property:** `PanacheSurface.Scale` (clamped 0.25–8), plus `LogicalWidth` /
  `LogicalHeight` and `ToLogical` / `ToPhysical` converters.
- **How:** lay out against `physical / S`, `canvas.Scale(S)` before painting, and divide
  the incoming mouse position by `S` before `InteractionManager.Update`. Scaling the
  *layout* rather than the output bitmap is the whole point — Skia then rasterises glyphs
  at their effective size instead of resampling a 1× bitmap, which is blurry at exactly
  the scales anyone actually wants.
- **Caller-visible consequence:** the returned layout dictionary is in **logical** units.
  Code that hit-tests those boxes by hand against a raw ImGui mouse position must run it
  through `ToLogical` first.

### 12b. Framework-Painted Hover
- **Status:** ✅ IMPLEMENTED (2026-08-24)
- **Properties:** `s.HoverBackgroundColor`, `s.HoverBackgroundGradientEnd`,
  `s.HoverBorderColor`, `s.HoverColor`.
- **Why:** every consumer was reimplementing the same hover tracker — an `OnMouseEnter`
  per row, a `_hoverId` field, and a re-style that lands a frame late because the tree is
  rebuilt before the event that changes it is dispatched. `DESIGN_SYSTEM` §7.2 makes the
  hover cue mandatory, so that was boilerplate the design system forced on everyone.
- **How:** `SkiaRenderer` cross-fades each pair over `NodeAnimState.HoverT`;
  `SurfaceFingerprint` hashes `HoverT` **only** for nodes that declare hover colors, so
  the fade repaints at full frame rate and then stops dead, and moving the cursor across
  inert decoration still repaints nothing.
- **Gotcha fixed at the same time:** `NodeAnimState.Update` now snaps `HoverT`/`PressT` to
  their target when `dt <= 0`. `Render`'s `dt` defaults to 0, so a caller that never
  passed one would otherwise have sat at `HoverT = 0` forever and never seen the cue.

### 12c. Text Input Node
- **Status:** 🟡 IMPLEMENTED, KEYBOARD PATH UNVERIFIED (2026-08-24)
- **Components:** `PUI.TextInput` / `PUI.TextInputRow`, plus `PUI.PumpKeyboard`.
- Everything visible is a Node — box, border, text, caret, placeholder. Keyboard focus
  became Id-keyed (`InteractionManager.FocusedId` / `IsFocused` / `ClearFocus`) for the
  same reason pointer capture and scroll offsets already were: the tree is rebuilt every
  frame, so a widget deciding whether to draw itself focused cannot ask an object that
  does not exist yet.
- **Verified:** rendering of the unfocused, placeholder and focused-with-caret states.
- **NOT verified:** live keystroke routing — ImGui's char queue reaching the focused node,
  and Dalamud swallowing those keys so they don't also fire hotbars. The binding surface
  was probed against the real `Dalamud.Bindings.ImGui.dll` (`InputQueueCharacters` is
  `ImVector<ushort>`; `WantTextInput`/`WantCaptureKeyboard` are settable), but no real
  keystroke has been put through it. **Confirm in-game before relying on it.**
- **Not implemented:** text selection, shift-arrow ranges, cut/copy (paste works),
  undo/redo, IME composition. The caret deliberately does not blink — a blink is a repaint
  twice a second forever, which is what this framework's redraw model exists to avoid.

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
