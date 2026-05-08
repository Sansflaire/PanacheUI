# PanacheUI — Rendering & Generative Pipeline Research

## Core Directive

**This document is the authoritative reference for every technology, API, concept, and algorithm
used in PanacheUI's rendering and generative pipeline.**

### Purpose
- Before implementing any feature, read the relevant sections here
- Before debugging a rendering issue, check the constraints documented here
- When a new technology enters the pipeline, add a section for it immediately
- When research reveals a gotcha or constraint, write it down here permanently

### Rules for Maintaining This Document
1. **Every concept used in the pipeline must have a section here.** No exceptions.
2. **Sections must be filled with real, actionable information** — not just definitions.
   Write down gotchas, version-specific behavior, performance constraints, and API quirks.
3. **Add "Critical for PanacheUI" notes** wherever a concept directly affects how we build things.
4. **Never remove a section** — mark it deprecated if no longer used, but keep the knowledge.
5. **Update sections when you discover new behavior** through debugging or implementation.

---

## Table of Contents

1. [SkiaSharp](#skiasharp)
2. [SKCanvas & Drawing Primitives](#skcanvas--drawing-primitives)
3. [SKPaint](#skpaint)
4. [SKShader & Gradients](#skshader--gradients)
5. [SKImageFilter (Blur, etc.)](#skimagefilter)
6. [SKSurface & CPU Raster](#sksurface--cpu-raster)
7. [Premultiplied Alpha](#premultiplied-alpha)
8. [SkiaSharp Font & Text](#skiasharp-font--text)
9. [Dear ImGui (via Dalamud)](#dear-imgui-via-dalamud)
10. [ImTextureID & Texture Upload](#imtextureid--texture-upload)
11. [Dalamud Plugin System](#dalamud-plugin-system)
12. [ITextureProvider](#itextureprovider)
13. [CSS Box Model & Layout](#css-box-model--layout)
14. [Flex Layout (Yoga-style)](#flex-layout-yoga-style)
15. [Node Tree / Scene Graph](#node-tree--scene-graph)
16. [Dirty Tracking](#dirty-tracking)
17. [Perlin Noise](#perlin-noise)
18. [Voronoi Diagrams](#voronoi-diagrams)
19. [HSV Color Space](#hsv-color-space)
20. [Easing Functions](#easing-functions)
21. [Premultiplied Alpha Blending](#premultiplied-alpha-blending)
22. [Reactive Data Binding](#reactive-data-binding)
23. [Procedural Texture Generation](#procedural-texture-generation)
24. [Animation Time Integration](#animation-time-integration)
25. [GCHandle & Pinned Memory](#gchandle--pinned-memory)
26. [Hit Testing](#hit-testing)
27. [NodeEffect Pipeline](#nodeeffect-pipeline)
28. [ClipRect & ClipRoundRect](#cliprect--cliproundrect)
29. [Drop Shadows in Skia](#drop-shadows-in-skia)
30. [Linear & Radial Gradients](#linear--radial-gradients)

---

## SkiaSharp

**What it is:** C#/.NET bindings for Google's Skia 2D graphics library. The same engine that powers
Chrome, Android, and Flutter. Version 3.118.0-preview.1.2 in this project.

**Why we use it:** ImGui's native drawing API (ImDrawList) cannot produce anti-aliased curves,
per-pixel gradients, drop shadows, or procedural textures at sufficient quality. Skia gives us a
full GPU-class 2D renderer running on a CPU raster surface, which we then blit into ImGui via a
Dalamud texture.

**Version constraint — CRITICAL:**
We use `SkiaSharp 3.118.0-preview.1.2`. This must match the native `libSkiaSharp.dll` version
that Umbra (another Dalamud plugin) loads into the process. If the managed wrapper version and
native DLL version diverge, the process will crash with a `DllNotFoundException` or ABI mismatch.
**Never downgrade or upgrade without checking Umbra's Skia version first.**

**Native asset path:** `runtimes/win-x64/native/libSkiaSharp.dll`
When packaging for release (not Umbra), this native DLL must be at exactly that path inside the
zip so .NET's native asset resolution finds it.

**Key namespaces:**
- `SkiaSharp` — core types (SKCanvas, SKPaint, SKRect, SKColor, etc.)
- `SkiaSharp.SKShader` — gradient and noise shader factories
- `SkiaSharp.SKImageFilter` — post-process filters (blur, etc.)

**Thread safety:** SKCanvas is NOT thread-safe. All rendering must happen on the same thread that
owns the surface. Dalamud's UiBuilder.Draw fires on the main game thread — stay there.

**Coordinate system:** Top-left origin, Y increases downward. Same as ImGui, same as CSS.

---

## SKCanvas & Drawing Primitives

**What it is:** The main drawing surface. All Skia rendering goes through SKCanvas method calls.

**Draw order matters:** Skia draws in painter's order — later calls draw on top of earlier ones.
PanacheUI rendering order per node:
1. Drop shadow (drawn before background, extends outside bounds)
2. Background fill (solid or gradient)
3. Border stroke
4. Text
5. Children (recursive, clipped to node bounds if ClipContent=true)

**Key primitives used:**
- `DrawRect(SKRect, SKPaint)` — solid/stroke rect
- `DrawRoundRect(SKRoundRect, SKPaint)` — rounded corners with anti-aliasing
- `DrawCircle(x, y, radius, SKPaint)` — circles for particles, ripple
- `DrawText(string, x, y, SKFont, SKPaint)` — baseline-anchored text
- `DrawBitmap(SKBitmap, SKRect, SKPaint)` — rarely used
- `Save()` / `RestoreToCount(count)` — stack-based state management

**Save/Restore pattern:**
```csharp
int save = canvas.Save();
canvas.ClipRoundRect(...);
// draw clipped content
canvas.RestoreToCount(save);
```
Always use `RestoreToCount` with the saved int rather than bare `Restore()` — prevents leaks if
exceptions occur mid-render.

**Coordinate space:** After `Save()`, you can `Translate()` to draw in local node space. Useful
for effects that need coordinates relative to the node origin.

---

## SKPaint

**What it is:** Carries all drawing parameters — color, shader, stroke width, blend mode, filters.

**Critical: SKPaint is stateful and reusable.** In PanacheUI's hot render loop, we create paints
per draw call. This is allocation-heavy but safe. Pooling paints is an optimization opportunity.

**Key properties:**
- `Color` — ARGB color (takes `SKColor`)
- `IsAntialias` — **always set true** for curves; false for pixel-exact fills
- `Style` — Fill, Stroke, or StrokeAndFill
- `StrokeWidth` — border thickness (0 = hairline)
- `Shader` — overrides Color with a gradient or noise shader
- `ImageFilter` — applies post-process (blur, etc.) after draw
- `BlendMode` — compositing mode (default = SrcOver)
- `MaskFilter` — creates blur masks (alternative to ImageFilter blur)

**BlendMode for effects:**
- `SKBlendMode.SrcOver` — normal compositing (default)
- `SKBlendMode.Screen` — brightening blend (good for glow/bloom additive)
- `SKBlendMode.Overlay` — contrast-boosting blend
- `SKBlendMode.Multiply` — darkening (AO corners)

**Alpha pre-multiplication:** When drawing with `SKAlphaType.Premul` surfaces, paint colors are
automatically pre-multiplied. See Premultiplied Alpha section.

---

## SKShader & Gradients

**What it is:** Replaces a paint's solid color with a procedural fill — gradient, noise, image.

**Linear gradient:**
```csharp
SKShader.CreateLinearGradient(
    new SKPoint(x0, y0),          // start
    new SKPoint(x1, y1),          // end
    new[] { colorA, colorB },     // SKColor array
    null,                          // position stops (null = evenly spaced)
    SKShaderTileMode.Clamp        // outside range: clamp to endpoint
)
```

**Radial gradient:**
```csharp
SKShader.CreateRadialGradient(
    new SKPoint(cx, cy),          // center
    radius,
    new[] { inner, outer },
    null,
    SKShaderTileMode.Clamp
)
```

**Perlin noise:**
```csharp
SKShader.CreatePerlinNoiseFractalNoise(freqX, freqY, octaves, seed)
SKShader.CreatePerlinNoiseTurbulence(freqX, freqY, octaves, seed)
```
- `FractalNoise` — smooth, cloud-like. Values from -1 to 1 (mapped to 0–255 in RGBA).
- `Turbulence` — sharper, more dramatic. Better for lava/plasma.
- Frequency: higher = more detail, smaller features. 0.01–0.05 typical for screen effects.
- Octaves: 1–8 layers of detail. More = richer but slower. 4 is a good default.
- Seed: deterministic randomness. Same seed = same noise every time.
- **Time animation:** Animate by composing with `WithLocalMatrix(SKMatrix.CreateTranslation(t, 0))`
  to scroll the noise pattern over time.

**Shader composition (WithLocalMatrix):**
```csharp
shader.WithLocalMatrix(SKMatrix.CreateTranslation(offsetX, offsetY))
```
Translates the shader's sampling UV without moving the drawn rect. Essential for scrolling noise.

**Tile modes:**
- `Clamp` — extends edge color beyond bounds
- `Repeat` — tiles the pattern
- `Mirror` — mirrors tiles

---

## SKImageFilter

**What it is:** Post-process filter applied after a draw call. Unlike shaders (which affect color
at each pixel during fill), image filters process the entire rendered output.

**Gaussian blur:**
```csharp
SKImageFilter.CreateBlur(sigmaX, sigmaY)
```
- `sigma` ≈ blur radius in pixels / 3. A 9px blur = sigma ≈ 3.
- Applying blur via `paint.ImageFilter` blurs the entire paint output.
- For glow/bloom: draw the shape twice — once blurred, once sharp on top.

**Drop shadow:**
```csharp
SKImageFilter.CreateDropShadow(dx, dy, sigmaX, sigmaY, shadowColor)
```
Draws a blurred colored shadow offset by (dx, dy). Applied to the paint, it affects whatever
`DrawRect`/`DrawRoundRect` the paint is applied to.

**Blur for glow effect:**
Draw the element with a large blur and `SKBlendMode.Screen` for additive glow. Then draw the
solid element on top. This is the Bloom effect technique.

**Performance note:** Image filters are expensive. Avoid applying blur to large rects every frame.
Consider rendering blurred elements to a cached surface and only re-rendering on dirty.

---

## SKSurface & CPU Raster

**What it is:** An off-screen rendering target. In PanacheUI, we use CPU-backed raster surfaces
(no GPU required) which produce a byte array we can upload to a Dalamud/D3D11 texture.

**Creation:**
```csharp
SKSurface.Create(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul))
```
- `SKColorType.Rgba8888` — 4 bytes per pixel, R-G-B-A order
- `SKAlphaType.Premul` — alpha pre-multiplied into RGB channels (required for correct compositing)

**Reading pixels back:**
```csharp
var snapshot = surface.Snapshot();        // SKImage
var bitmap   = SKBitmap.FromImage(snapshot);
byte[] pixels = bitmap.Bytes;             // RGBA8888 flat array
```
Or more efficiently:
```csharp
surface.ReadPixels(imageInfo, pinnedPtr, rowBytes, 0, 0)
```
Using a pinned GCHandle to avoid an extra allocation.

**Dispose discipline:** SKSurface, SKImage, SKBitmap all hold unmanaged memory. Always dispose them.
In PanacheUI, `RenderSurface` wraps the lifecycle — dispose it when the window resizes or closes.

**Resize:** Create a new surface when dimensions change. Do not try to resize in-place.

---

## Premultiplied Alpha

**What it is:** A pixel format where RGB channels have already been multiplied by the alpha value.
A 50%-transparent red pixel: RGBA(255, 0, 0, 128) straight → RGBA(128, 0, 0, 128) premul.

**Why it matters:** Most GPU compositing hardware expects premultiplied alpha. Straight alpha
(un-premultiplied) produces fringing artifacts around transparent edges when blended.

**In PanacheUI:**
- SKSurface uses `SKAlphaType.Premul` — all Skia drawing auto-produces premul output
- `ITextureProvider.CreateFromRaw` with `RawImageSpecification.Rgba32` expects premul data
  (Dalamud/D3D11 interprets uploaded data as premul)
- **Never manually premultiply** — Skia does it automatically when you draw with opacity

**Common gotcha:** If you pass straight-alpha data to CreateFromRaw expecting premul, you get
bright halos around transparent UI elements. Always use `SKAlphaType.Premul` on the surface.

---

## SkiaSharp Font & Text

**What it is:** Skia's text rendering pipeline — typeface selection, font metrics, text drawing.

**Typeface:**
```csharp
SKTypeface.FromFamilyName("Arial", SKFontStyleWeight.Bold, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright)
```
Falls back to system default if family not found. Dalamud's game environment has limited fonts —
stick to common system fonts (Arial, Segoe UI, etc.) or embedded resources.

**Font metrics (critical for vertical alignment):**
```csharp
var metrics = font.Metrics;
float ascent  = -metrics.Ascent;   // distance from baseline to top of cap (positive after negation)
float descent =  metrics.Descent;  // distance from baseline to bottom of descenders
float lineH   = ascent + descent;  // total line height
```
`metrics.Ascent` is NEGATIVE in Skia (distance above baseline = negative Y direction).
**Always negate it** when calculating vertical center alignment.

**Vertical centering:**
```csharp
float textY = nodeTop + (nodeHeight / 2f) + (ascent / 2f) - (descent / 2f);
```
This centers the visual cap height within the node, not the full line height.

**Text measurement (for ellipsis):**
```csharp
float textWidth = font.MeasureText(text, out _, paint);
```
Returns the advance width. For ellipsis, binary-search: find the longest prefix that fits, append "…".

**DrawText x,y is the BASELINE position** — not top-left. Y = baseline, not top of glyphs.
Add `ascent` to your top-Y to get the baseline Y:
```csharp
canvas.DrawText(text, x + padding, topY + ascent, font, paint);
```

**Anti-aliasing:** Always enable `paint.IsAntialias = true` for text. Sub-pixel rendering isn't
available on CPU surfaces but AA makes a big difference.

---

## Dear ImGui (via Dalamud)

**What it is:** Immediate Mode GUI — a paradigm where UI is rebuilt every frame from scratch.
No retained widget state, no layout engine. Each `ImGui.Button()` call both draws and tests input
simultaneously. In Dalamud, Dear ImGui (C++) is wrapped as `Dalamud.Bindings.ImGui`.

**Immediate mode paradigm:**
- Every frame: call Begin → draw widgets → call End
- No "create button object" — just call `ImGui.Button()` and check return value
- State that needs to persist between frames must be stored in your own C# fields

**Why PanacheUI exists on top of ImGui:**
ImGui's drawing is intentionally minimal — no anti-aliasing, no gradients, no shadows, no
per-pixel effects. We use ImGui only as the host window shell + input system. The visual output
comes from Skia rendered to a texture, displayed via `ImGui.Image()`.

**Permitted ImGui calls in PanacheUI windows:**
- `ImGui.Begin/End` — window management (always with NoTitleBar)
- `ImGui.Image` — blit the Skia texture
- `ImGui.BeginChild/EndChild` — scrollable viewport around the texture
- `ImGui.SliderFloat/Int`, `ImGui.ColorEdit4` — live controls BELOW the texture
- `ImGui.SetCursorScreenPos + ImGui.Button` — invisible overlay hit regions only
- Input queries: `ImGui.GetMousePos`, `ImGui.GetIO`, `ImGui.IsMouseDragging`, `ImGui.IsMouseClicked`

**BANNED in PanacheUI windows:** Any ImGui widget that produces visible UI (text, colored widgets,
separators, columns) — all visual content must be Panache nodes.

**Window flags pattern:**
```csharp
var flags = ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse;
ImGui.Begin("##unique_id", ref IsVisible, flags);
```

**IsItemHovered() ordering — CRITICAL:**
`ImGui.IsItemHovered()` always refers to the **most recently rendered item**. If you render the
Image and then render a Button overlay, `IsItemHovered()` refers to the Button, not the Image.
**Always capture `bool imageHovered = ImGui.IsItemHovered()` immediately after `ImGui.Image()`**
before rendering any overlay buttons.

**SetCursorScreenPos:** Positions the next widget in absolute screen coordinates (not relative to
window). Use this to place overlay buttons on top of the Skia surface:
```csharp
var btnPos = new Vector2(imagePos.X + offsetX, imagePos.Y + offsetY);
ImGui.SetCursorScreenPos(btnPos);
ImGui.Button("label", size);
```

**Mouse drag detection:**
```csharp
if (ImGui.IsMouseDragging(ImGuiMouseButton.Left))
{
    var delta = ImGui.GetIO().MouseDelta;
    _windowPos += delta;
}
```
`MouseDelta` is the per-frame mouse movement while dragging. Apply it to `_windowPos` and feed
back via `SetNextWindowPos(..., ImGuiCond.Always)`.

**BeginChild for scroll:**
```csharp
ImGui.BeginChild("##scroll_id", new Vector2(width, height), false, ImGuiWindowFlags.HorizontalScrollbar);
ImGui.Image(...);  // taller than the child — creates scroll
ImGui.EndChild();
```
Omit `NoScrollWithMouse` from the BeginChild flags to allow mouse wheel scrolling.

### ImGui ID stack and ## / ### prefixes

Every ImGui widget needs a unique ID within its window for input tracking. ID is derived from the
label string by default. Duplicates cause silent misbehavior (two buttons with same label share state).

**`##` suffix:** everything after `##` is ID, nothing before is displayed.
```csharp
ImGui.Button("X##close_demo")    // displays "X", ID is "X##close_demo"
ImGui.Button("X##close_help")    // displays "X", different ID — no conflict
```

**`###` suffix:** everything after `###` is the entire ID, label can change without changing ID.
```csharp
ImGui.Button($"Frame {frame}###my_button")  // label changes, ID stays "my_button"
```

**Invisible buttons:** `ImGui.InvisibleButton("##id", size)` — no visual, returns true on click.
Used for hit regions over the Panache surface when you need ImGui-managed click detection.

### ImGui item state — what "last item" means

ImGui maintains a single flat struct `g.LastItemData` (not a stack) that is overwritten by every
call to `ItemAdd()` — the internal function every widget calls to register itself. This struct holds:
- The item's ID, bounding rect, `InFlags`, and `StatusFlags`

**`IsItemHovered()`, `IsItemClicked()`, `IsItemActive()`, `IsItemFocused()`** all read from
`g.LastItemData`. There is no history — only the most recently registered item.

```
ImGui.Image(handle, size);          // g.LastItemData = {image rect}
bool imageHovered = ImGui.IsItemHovered();   // ✓ reads image data
ImGui.Button("X##close", btnSize);  // g.LastItemData = {button rect}  ← OVERWRITES
bool imgHov2 = ImGui.IsItemHovered(); // ✗ now reads button data!
```

**`IsItemClicked(button)`** is exactly `IsMouseClicked(button) && IsItemHovered()` — same rule
applies: capture immediately after the target widget.

**Rule:** Always capture `IsItemHovered()` / `IsItemClicked()` in a `bool` on the very next line
after `ImGui.Image()` (or any widget you care about), before any subsequent widget calls.

### ImGui draw list (ImDrawList) — internal structure

Each `ImGui.Begin/End` window owns one `ImDrawList`. `BeginChild` creates its own nested
`ImDrawList` with its own clip rect. The draw list has three parallel buffers:

| Buffer | Type | Contents |
|---|---|---|
| `VtxBuffer` | `ImVector<ImDrawVert>` | 20-byte vertices: `pos` (8), `uv` (8), `col` (4) |
| `IdxBuffer` | `ImVector<ImDrawIdx>` | 16-bit triangle indices |
| `CmdBuffer` | `ImVector<ImDrawCmd>` | Draw call ranges |

**`ImDrawCmd`** fields:
- `ClipRect` — scissor rect for this batch
- `TextureId` / `TexRef` — the texture to bind
- `VtxOffset`, `IdxOffset` — where in the buffers this cmd starts
- `ElemCount` — number of indices to draw

**Batching mechanism:** A new `ImDrawCmd` is created **only when** the `ImDrawCmdHeader`
(a 12-byte struct containing `ClipRect + TextureId`) changes via `memcmp`. All draws that share
the same clip rect and texture go into the same command. This means:
- Mixing textures breaks batching — one `ImGui.Image` per unique texture = one extra draw call
- Changing clip rect (via `PushClipRect`) breaks batching
- For PanacheUI's single-texture approach this is ideal — one `ImGui.Image` = one draw call

**`ImGui.Image()` internals:** Calls `AddImage(texId, p_min, p_max, uv0, uv1, col)` which:
1. Calls `PrimReserve(6, 4)` — reserves 6 indices + 4 vertices
2. Writes 4 `ImDrawVert` into `VtxBuffer` with uv corners
3. Writes 6 indices (two triangles) into `IdxBuffer`
4. Checks if a new `ImDrawCmd` is needed (texture/cliprect changed)

**`ImTextureID` in Dalamud/D3D11:** `ImTextureID` is `ImU64`. The D3D11 backend casts it to
`ID3D11ShaderResourceView*`. Dalamud's `IDalamudTextureWrap.ImGuiHandle` returns this value.
Never store a raw D3D11 pointer manually — always go through `IDalamudTextureWrap`.

**`SetNextWindowPos` mechanism:** Stores the position in `g.NextWindowData.PosVal`. On the next
`Begin()`, if `PosVal` is set, it wins over the window's stored position. After consuming it,
the stored value is cleared. This is why `ImGuiCond.Always` works for every-frame repositioning.

At the end of the frame, Dalamud/ImGui flushes all draw lists to D3D11 in one pass:
- All widget calls during `Draw()` are deferred — no immediate GPU work
- `ImGui.Image()` just enqueues a draw command
- The actual D3D11 draw call happens after your `Draw()` method returns

**Consequence for PanacheUI:** The Skia texture must be fully uploaded to D3D11 **before**
calling `ImGui.Image()`. The upload (via `ITextureProvider.CreateFromRaw`) is the one truly
"immediate" operation — it must complete before ImGui reads the texture handle.

### ImGuiCond values

- `ImGuiCond.Always` — apply the condition every frame (used for programmatic window positioning)
- `ImGuiCond.Once` — apply only the very first time the window is seen in this session
- `ImGuiCond.FirstUseEver` — apply only if no `.ini` position is saved for this window
- `ImGuiCond.Appearing` — apply when window transitions from invisible to visible

For `_windowPos`-driven dragging, use `ImGuiCond.Always` — you want your position to win over
ImGui's internal tracking every frame.

### Mouse event functions — when they fire

- `ImGui.IsMouseClicked(btn)` — fires **exactly one frame** when the button transitions from up to down.
  Returns false every subsequent frame until released and re-pressed. Use for: one-shot actions.
- `ImGui.IsMouseDown(btn)` — true every frame the button is held. Use for: held state checks.
- `ImGui.IsMouseReleased(btn)` — fires **exactly one frame** on release.
- `ImGui.IsMouseDragging(btn)` — true once the mouse has moved beyond the drag threshold (default 6px)
  while held. Use for: window dragging.
- `ImGui.GetMouseDragDelta(btn)` — total delta since drag started (resets on release).
- `ImGui.GetIO().MouseDelta` — per-frame movement delta (only meaningful while moving).

**Critical:** `IsMouseClicked` and `IsMouseReleased` are global — they fire regardless of what's
under the cursor. Always combine with `imageHovered` or a bounds check to ensure the event is
on the right element.

### ImGui window draw list

`ImGui.GetWindowDrawList()` returns the `ImDrawList` for the current window. You can add raw
draw commands (lines, rects, circles, text, images) directly. These render in correct z-order
with the window's other content — unlike `GetBackgroundDrawList()` which draws behind everything.

**Use cases in PanacheUI:** Rarely needed — all visuals are in Skia. But useful for: debug overlays
(draw bounding boxes of layout boxes for debugging), cursor indicators drawn over the surface.

```csharp
var dl = ImGui.GetWindowDrawList();
dl.AddRect(new Vector2(x1, y1), new Vector2(x2, y2), ImGui.ColorConvertFloat4ToU32(color));
dl.AddText(new Vector2(x, y), 0xFFFFFFFF, "debug label");
```

**Note:** `ImGui.ColorConvertFloat4ToU32` converts `Vector4(r,g,b,a)` where each is 0–1 into an
ABGR packed uint32 that ImGui draw list commands expect.

### ImGui font system and Unicode

ImGui uses its own font atlas — a bitmap atlas built at startup from TTF files. By default,
Dalamud uses its own pre-built atlas that includes Latin characters, some symbols, and
FFXIV-specific glyphs.

**Unicode characters not in the atlas render as blank.** This is why `"✕"` (U+2715) was invisible
as a close button — it's not in Dalamud's default atlas. Always use plain ASCII for text in
ImGui widgets. Use Skia (PanacheUI nodes) for any text that requires special characters, symbols,
or custom fonts.

**Font size:** ImGui scales text by multiplying the base font size. Push/pop font to change:
```csharp
ImGui.PushFont(ImGui.GetIO().Fonts.Fonts[fontIndex]);
ImGui.Text("large text");
ImGui.PopFont();
```
In Dalamud this is rarely needed — just use PanacheUI text nodes for styled text.

---

## ImTextureID & Texture Upload

**What it is:** The handle ImGui uses to reference a GPU texture. In Dalamud, this is
`IDalamudTextureWrap` obtained from `ITextureProvider.CreateFromRaw`.

**Upload flow:**
```csharp
// 1. Render to SKSurface
// 2. Read pixels back to byte[]
// 3. Upload to Dalamud texture
var spec = new RawImageSpecification(width, height, (int)DXGI_FORMAT.R8G8B8A8_UNORM);
var wrap = _texProvider.CreateFromRaw(spec, pixels);
// 4. Get ImGui handle
ImTextureID handle = wrap.ImGuiHandle;
// 5. Display
ImGui.Image(handle, new Vector2(width, height));
```

**Format:** `DXGI_FORMAT.R8G8B8A8_UNORM` = `RawImageSpecification.Rgba32` — matches Skia's
`SKColorType.Rgba8888` output. R is first byte, A is last byte.

**Pixel order mismatch gotcha:** Skia's `Rgba8888` is R,G,B,A in memory order. D3D11
`R8G8B8A8_UNORM` expects the same R,G,B,A order. They match — no byte swapping needed.
(If you used `Bgra8888`, you would need to swap or use `B8G8R8A8_UNORM`.)

**Dispose old texture before re-uploading:** Every frame a resize happens, the old `IDalamudTextureWrap`
must be disposed before creating a new one. `TextureManager` in PanacheUI handles this lifecycle.

**Upload cost:** Uploading a 600×500 RGBA8888 image = 1.2MB CPU→GPU transfer per frame. Keep
renders dirty-tracked so you only upload when the content actually changes.

---

## Dalamud Plugin System

**What it is:** The .NET plugin runtime that runs inside the FFXIV game process. Plugins are
.NET assemblies loaded by Dalamud at runtime. No separate process — you run inside the game.

**Plugin entry point:**
```csharp
public class Plugin : IDalamudPlugin
{
    public Plugin([Service] IPluginLog log, [Service] ICommandManager commands, ...) { }
    public void Dispose() { }
}
```
`[Service]` attribute triggers Dalamud's DI container to inject the requested service. Constructor
injection only — no property injection.

**Available services (relevant to PanacheUI):**
- `IPluginLog` — structured logging to Dalamud console
- `ICommandManager` — register `/slash` commands
- `ITextureProvider` — create D3D11 textures from raw data
- `IDalamudPluginInterface` — plugin metadata, config file access, IPC
- `IUiBuilder` — hook into the ImGui render loop

**UiBuilder.Draw:** Called every frame while the game is running with ImGui. This is where all
rendering logic lives. Called on the main game thread.

**UiBuilder.OpenMainUi:** Called when the user clicks the plugin in /xlplugins or uses the
main command. Hook this to set `IsVisible = true` on your main window.

**Hot reload:** In dev mode (`devPlugins/`), Dalamud watches the DLL for changes and reloads the
plugin automatically when the file is updated. No `/xlplugins` disable/re-enable needed.
Build with `-c Debug` — the `CopyToDevPlugins` MSBuild target only runs in Debug config.

**API Level 14:** The current Dalamud API. Different API levels have different service interfaces.
`DalamudApiLevel` in the manifest must match the target API version. Do not mix.

**Manifest (PanacheUI.json):** Must be alongside the DLL in the plugin zip. Key fields:
- `"DalamudApiLevel": 14`
- `"AssemblyVersion"` must match the DLL version
- `"Author": "Sansflaire"` — always

---

## ITextureProvider

**What it is:** Dalamud service for creating and managing GPU textures.

**CreateFromRaw:**
```csharp
IDalamudTextureWrap tex = _texProvider.CreateFromRaw(
    new RawImageSpecification(width, height, (int)DXGI_FORMAT.R8G8B8A8_UNORM),
    pixelBytes
);
```
- Allocates a D3D11 texture, uploads pixel data
- Returns a `IDalamudTextureWrap` — holds the D3D11 resource + ImGui handle
- Call `.Dispose()` to release the GPU resource

**ImGuiHandle:** `tex.ImGuiHandle` returns the `ImTextureID` for passing to `ImGui.Image()`.

**Thread safety:** Must be called from the UI thread (UiBuilder.Draw or similar). Creating textures
from background threads is not supported.

---

## CSS Box Model & Layout

**What it is:** The fundamental model for how boxes (nodes) are sized and positioned.
PanacheUI's LayoutEngine implements a subset of the CSS box model.

**Box model layers (inside → outside):**
```
[margin]           ← gap handles this between siblings; not individual box model
  [border]
    [padding]
      [content]    ← text, children live here
    [/padding]
  [/border]
[/margin]
```

**Content origin calculation:**
```csharp
contentOrigin.X = nodeX + borderLeft + paddingLeft;
contentOrigin.Y = nodeY + borderTop  + paddingTop;
contentWidth    = nodeW - borderLeft - borderRight - paddingLeft - paddingRight;
contentHeight   = nodeH - borderTop  - borderBottom - paddingTop - paddingBottom;
```
**Most common implementation bug:** Placing children at the node's top-left corner instead of
at `contentOrigin`. Always offset child positions by padding before placing them.

**Sizing modes:**
- `SizeMode.Fixed` — exact pixel size; no measurement needed, just assign
- `SizeMode.Fit` — shrinks to wrap content (intrinsic size = smallest box containing content without overflow)
- `SizeMode.Fill` — expands to fill available space from parent

**Box-sizing model:** PanacheUI uses border-box semantics — `Width`/`Height` refer to the full
border box (content + padding + border), not just content. This is more intuitive for UI work.

**Fill distribution:** When multiple children are `Fill`, available space is:
`freeSpace = containerContentSize - sum(Fixed/Fit sizes) - totalGap`
Each Fill child gets `freeSpace / fillCount`. Track `totalGap = gap * (childCount - 1)` separately.

**Padding:** `EdgeSize(vertical, horizontal)` or `EdgeSize(top, right, bottom, left)`. Padding
is inside the border — content starts at `contentOrigin`.

**Gap vs Margin:**
- **Gap** — container property, inserted *between* children, does not affect first/last child outer edges
- **Margin** — child property, affects the child's own claimed space; participates in free-space calculation
- They are not additive in the same dimension — both apply simultaneously if present

**Intrinsic size (Fit mode):**
- For a row container: `intrinsicW = sum(child widths) + totalGap + paddingH; intrinsicH = max(child heights) + paddingV`
- For a column container: `intrinsicH = sum(child heights) + totalGap + paddingV; intrinsicW = max(child widths) + paddingH`
- For text nodes: measured by `font.MeasureText()` for width; `metrics.Descent - metrics.Ascent` for height
- **Gotcha:** When measuring a Fit node whose parent hasn't resolved size yet, pass `float.PositiveInfinity`
  as available width — the text will report single-line width, which is the correct intrinsic value

**Performance:** Cache intrinsic sizes; invalidate only on content/style change. Text measurement
is ~1–5µs per string in SkiaSharp — fine for tens of nodes, not for thousands per frame.

---

## Flex Layout (Yoga-style)

**What it is:** A one-dimensional layout model (one axis at a time — horizontal or vertical).
Similar to CSS Flexbox with `flex-direction: row` or `flex-direction: column`.

**Flow directions:**
- `Flow.Horizontal` — children placed left-to-right (main axis = X)
- `Flow.Vertical` — children placed top-to-bottom (main axis = Y)

**Two-pass algorithm:**

**Pass 1 — Measure (bottom-up):**
1. For Fixed children: size = fixed value (no recursion needed)
2. For Fit children: recurse into their subtree to get intrinsic size
3. Sum all Fixed + Fit sizes along main axis → `usedSpace`
4. `freeSpace = containerContentSize - usedSpace - (gap * (childCount - 1))`
5. Save `freeSpace` for Pass 2

**Pass 2 — Place (top-down):**
1. Distribute `freeSpace` equally among Fill children: `fillChildSize = freeSpace / fillCount`
2. Walk children in order, assign absolute position from accumulated offset:
   ```
   childPos = contentOrigin + offset
   offset  += childSize + gap
   ```
3. Recurse into each child with their resolved size

**Cross-axis sizing:**
- Default: Fill cross-axis — child cross size = container cross content size
- `SizeMode.Fixed` on the cross axis overrides to exact pixel size
- `SizeMode.Fit` on the cross axis = child's intrinsic cross size, aligned within parent
- **Gotcha:** A Fill child on the cross axis with no explicit size collapses to zero if the
  container's cross size is not yet resolved — always ensure containers have a resolved cross size
  before placing Fill cross-axis children

**LayoutBox:** The result of layout — `(X, Y, Width, Height)` in surface coordinate space.
`box.Right = box.X + box.Width`, `box.Bottom = box.Y + box.Height`. Used for hit-testing and rendering.

**Flex collapse edge case:** Fill children should not go below their intrinsic minimum.
Implement `finalSize = max(fillChildSize, intrinsicMin)` after distribution — otherwise a Fill
child with text content can produce a size too small to render the text.

**Why not use Yoga directly?** Yoga is a C library requiring P/Invoke. Our custom implementation
is simpler, handles PanacheUI's specific SizeMode semantics, and avoids the native dependency.

---

## Node Tree / Scene Graph

**What it is:** A hierarchical tree of `Node` objects that describes what to render. Similar to
the DOM in browsers or a scene graph in 3D engines.

**Node properties:**
- `Style` — all visual properties (colors, size, effects, etc.)
- `Text` — optional display text
- `Id` — optional string identifier for FindById()
- `ClassList` — set of string class names
- `Children` — ordered list of child nodes
- `Anim` — animation state (NodeAnimState)
- `IsDirty` — whether this node needs re-render

**Lifecycle per frame:**
1. `BuildTree()` (or `UpdateAnimatedNode()`) — modify node style/text
2. `MarkDirty()` — signal that re-render is needed
3. `LayoutEngine.Compute()` — layout pass, produces `Dictionary<Node, LayoutBox>`
4. `SkiaRenderer.Render()` — draw pass, uses layout dict to position draws
5. `TextureManager.Upload()` — CPU→GPU upload

**Rebuild vs. mutate:** PanacheUI's HelpWindow rebuilds the entire tree from `BuildLiveExample()`
every frame. DemoWindow mutates only the animated banner node. Rebuild is simpler but allocates
more; mutation is efficient but requires careful dirty tracking.

**FindById / FindByClass:** Depth-first search through the tree. Used to grab specific nodes for
per-frame mutation without rebuilding the whole tree.

---

## Dirty Tracking

**What it is:** A flag on each node that indicates whether it needs to be re-rendered.

**IsDirty:** True if this node's style or text changed since the last render. Propagates up the
tree — a dirty child makes its ancestors dirty.

**MarkDirty():** Manually sets the dirty flag. Call this after mutating a node's style directly.

**ClearDirty():** Resets dirty flags after a successful render. Called on the root after upload.

**_needsRender flag:** The DemoWindow's own flag, separate from node dirty. Set when a resize
occurs (requires new surface + full rebuild). Check either: `_needsRender || _root.IsDirty`.

**Optimization:** When nothing is dirty, skip the Compute+Render+Upload steps entirely.
This is critical — rendering Skia + uploading textures every frame for a static UI is wasteful.

---

## Perlin Noise

**What it is:** A gradient noise algorithm (Ken Perlin, 1983) producing smooth pseudo-random
patterns. Nearby points have similar values (unlike white noise). Used for clouds, fire, terrain,
organic textures.

**Mathematical basis (single octave):**
1. Find integer grid cell containing point; compute fractional offsets `(fx, fy)`
2. Look up pseudo-random gradient vectors at 4 corners via permutation table
3. Dot each gradient with the offset vector from that corner
4. Interpolate with Ken Perlin's improved fade curve: `f(t) = 6t⁵ - 15t⁴ + 10t³` (C2 continuous)
5. Result in range approximately `[-1, 1]`

**Fade function:** Original 1983 used cubic smoothstep `3t²−2t³` (C¹). Improved 2002 uses quintic
`6t⁵−15t⁴+10t³` (C²: zero first AND second derivative at 0 and 1). The quintic eliminates shading
discontinuities when computing normals from noise. Skia uses the improved version.

**Permutation table:** 256-entry random permutation, doubled to 512 to avoid modular artifacts
(`perm[i] = perm[i+256]`). Gradient set: 12 unit vectors `{±1,±1,0}, {±1,0,±1}, {0,±1,±1}`
(cube edge midpoints — chosen to be isotropic). Low 4 bits of hash index selects gradient.

**Output range:** approximately `[-0.7, 0.7]` in practice (not full `[-1,1]`). Multiply by `~1.43`
to normalize if a full `[-1,1]` range is required.

### fBm (Fractal Brownian Motion) — multi-octave

```csharp
float fBm(float x, float y, int octaves, float gain=0.5f, float lacunarity=2.0f) {
    float value=0, amplitude=1, frequency=1, norm=0;
    for (int i=0; i<octaves; i++) {
        value    += Perlin(x*frequency, y*frequency) * amplitude;
        norm     += amplitude;
        amplitude *= gain;        // 0.5 = each octave half as strong (pink / 1/f noise)
        frequency *= lacunarity;  // 2.0 = each octave twice as fine
    }
    return value / norm;
}
```

- **Persistence / Gain** (0–1): amplitude decay. `0.5` = classical 1/f spectral rolloff
- **Lacunarity** (>1): frequency multiplier. `2.0` standard
- **Octaves**: 4–6 visually sufficient. Above 8 = sub-pixel detail, pure wasted cost.
- **Warning:** `lacunarity × gain = 1.0` (e.g. 2.0 × 0.5) is the flat-spectrum boundary.
  Use `gain = 0.45–0.48` to ensure coarser octaves dominate and result reads as fractal.

### Turbulence vs FractalNoise — the critical difference

Turbulence applies `abs()` to each octave **before** accumulating — NOT just to the final sum:

```csharp
// FractalNoise: preserve sign
value += Perlin(x*freq, y*freq) * amplitude;

// Turbulence: fold negative lobes upward per octave
value += MathF.Abs(Perlin(x*freq, y*freq)) * amplitude;
```

`abs()` creates **sharp ridges at zero-crossings** (derivative discontinuities → high-frequency
content at creases). FractalNoise = soft clouds. Turbulence = fire, lava, jagged rock.

Skia maps directly to W3C SVG feTurbulence spec:
`CreatePerlinNoiseFractalNoise` = `type="fractalNoise"`,
`CreatePerlinNoiseTurbulence` = `type="turbulence"`

### Animation — two approaches

**Scrolling UV (cheap):** Translates the input coordinates — looks like a texture sheet sliding.
```csharp
shader.WithLocalMatrix(SKMatrix.CreateTranslation(_animTime * speed, 0))
```
Good for: wind on a flag, fast-moving water surface where direction matters.

**Time as 3rd dimension (proper organic animation):**
```csharp
float value = Perlin(x*freq, y*freq, _animTime*speed);
```
The 2D slice through 3D noise evolves organically — no translational artifact.
Correct for: fire that burns, fog that drifts, living organisms.
Cost: full 3D noise evaluation per sample vs 2D.

**Trig wobble (cheap approximation):**
```csharp
float value = Perlin(x + MathF.Sin(_animTime/2.7f), y + MathF.Cos(_animTime/3.6f));
```
Non-harmonic offsets break periodicity without 3D cost.

### Known artifacts

- **Grid-axis bias:** Features cluster at 0°/45°/90° (rectilinear lattice). Subtle grid pattern at low frequencies.
- **Zero at integer coordinates:** Always = 0 at lattice points → "dead zones" at grid intersections.
- **Period 256:** Permutation table wraps → noise tiles with period 256 in axis-aligned directions.

**Simplex noise:** Ken Perlin (2001). Uses simplex lattice (triangles in 2D) → more isotropic,
no grid-axis bias. No Skia built-in — implement in C# if axis artifacts are unacceptable.

### Visual recipes

| Effect | Type | Freq | Octaves | Gain | Animate |
|---|---|---|---|---|---|
| Soft clouds | FractalNoise | 0.01–0.02 | 4–6 | 0.5 | Time-z, slow |
| Fire / lava | Turbulence | 0.03–0.06 | 3–5 | 0.5–0.6 | Scroll Y down |
| Water ripple | Turbulence | 0.04–0.08 | 3–4 | 0.4 | Time-z |
| Stone / rock | Turbulence | 0.05–0.1 | 5–7 | 0.45 | Static |
| Marble veins | FractalNoise | 0.02–0.04 | 5–7 | 0.5 | `sin(x*ringFreq + fBm*8)` |
| Heat shimmer | Turbulence | 0.015–0.03 | 2 | 0.5 | Scroll both axes slowly |

**Practical noise texture for UI:** A 256×256 pre-baked Perlin noise texture (tiled) is often
cheaper than calling `CreatePerlinNoiseTurbulence` per frame on large surfaces. Generate once,
animate by scrolling UV.

**PanacheUI uses Perlin for:** HeatHaze (UV distortion), Plasma/Lava Lamp, Waveform, DotMatrix density.

---

## Voronoi Diagrams

**What it is:** Space partitioned into cells — each cell contains all points closer to one "site"
than to any other site (Worley noise). Produces cellular, stone-tile, or biological appearances.

**Efficient algorithm (grid-accelerated):**
For each sample point `p`:
1. Find grid cell `p` is in: `cell = floor(p / cellSize)`
2. For each of the **9 neighboring cells** (3×3 grid around `cell`):
   - Generate a feature point inside that cell: `fp = cell_corner + hash2D(cell) * cellSize`
   - Compute distance from `p` to `fp`
3. `F1` = minimum distance found
4. `F2` = second minimum distance found

This is O(1) per sample (constant 9-cell search), regardless of total site count.
Only need to check more cells if `cellSize` is very small relative to sample spacing.

**Distance functions:**
```csharp
float Euclidean(float dx, float dy) => MathF.Sqrt(dx*dx + dy*dy); // circular cells
float Manhattan(float dx, float dy) => MathF.Abs(dx) + MathF.Abs(dy); // diamond cells
float Chebyshev(float dx, float dy) => MathF.Max(MathF.Abs(dx), MathF.Abs(dy)); // square cells
```

**F1 vs F2 combinations — key patterns:**
- `F1` alone: standard filled-cell Voronoi — distance field from nearest point
- `F2 - F1`: **edge detection** — peaks exactly at cell boundaries, zero at feature centers.
  Produces crackle, stained glass, ceramic tile, cell membrane patterns.
  Normalize: `(F2 - F1) / F2` for scale invariance.
- `F1 / F2`: smooth 0→1 field, low near feature points, higher near edges

**Edge detection pattern for UI effects:**
```csharp
float edge = F2 - F1;
float normalized = edge / F2; // 0 = feature center, ~1 = edge
// Threshold to draw cell walls:
if (normalized < edgeThickness) DrawEdgePixel(...);
```

**Hash function for feature points:**
```csharp
// Deterministic 2D hash → float2 in [0,1]
float2 Hash2D(int2 cell) {
    float x = MathF.Abs(MathF.Sin(cell.X * 127.1f + cell.Y * 311.7f) * 43758.5453f);
    float y = MathF.Abs(MathF.Sin(cell.X * 269.5f + cell.Y * 183.3f) * 43758.5453f);
    return (x % 1f, y % 1f);
}
```
Not cryptographic — but visually sufficient for noise. Use a fixed seed offset for variation.

**Performance:**
- 9-cell search = O(1) per pixel — fast
- For N=16 sites naive approach: O(W×H×16) — still fast for small preview nodes
- Render at half/quarter resolution + upscale with blur for soft organic look
- Cache the bitmap — only regenerate when `EffectScale` or node size changes

---

## Scanline / CRT Effect

### What a CRT scanline is

A CRT draws one horizontal line at a time via an electron beam. Between scanlines there is a gap of
unlit phosphor. At standard definition (240–480 lines), these gaps are visually prominent — the dark
gap typically occupies 30–50% of each scanline pitch. Simulating this darkens every other row.

### Implementation formulas

**Hard modulo (stylized):**
```csharp
bool isDark = ((int)(y * sourceResolutionY) % 2 == 0);
color *= isDark ? darkenFactor : 1.0f;  // darkenFactor ~0.6–0.75
```

**Soft cosine (more accurate — beam has Gaussian profile):**
```csharp
float scan = 1.0f - MathF.Pow(
    MathF.Cos(y * 2f * MathF.PI * sourceHeight) * 0.5f + 0.5f,
    SCAN_BEAM_WIDTH    // 1.5–3.0: higher = harder edge
) * SCANLINE_STRENGTH; // 0.2–0.5: how dark the gap gets
color *= scan;
```

**Realistic values:** Gap darkening of 25–40% (gap = 60–75% of lit brightness) looks authentic
without being distracting. Pure black gaps are too aggressive on modern monitors.

### RGB phosphor mask

Real CRTs alternate R-G-B sub-pixels horizontally. Add this for sub-pixel texture:
```csharp
int modX = ((int)x) % 3;
float maskR = modX == 0 ? 1.0f : MASK_DARK;  // MASK_DARK = 0.5–0.7
float maskG = modX == 1 ? 1.0f : MASK_DARK;
float maskB = modX == 2 ? 1.0f : MASK_DARK;
```
At low emulated resolutions: visible colored sub-pixels. At high resolution: subtle texture.

### Bloom / phosphor glow additions

1. **Halation** (glass scatters light): large-radius Gaussian blur (5–15% of height) blended at
   5–15% opacity with `Screen` blend → soft halo around all bright areas
2. **Phosphor persistence**: tight blur (1–3% of height) at 20–40% opacity → bright scanlines
   appear to merge at high brightness, widening lines naturally

### Full CRT stack

1. Sample source texture with bilinear filtering
2. Apply RGB phosphor mask
3. Apply cosine scanline darkening
4. Bloom pass: blur full buffer, Screen blend at 15–25%
5. Optional: barrel distortion for curved screen
6. Optional: corner vignette

---

## Plasma Effect (Demoscene)

### What it is

The plasma effect (demoscene circa 1992) sums multiple sine waves evaluated per pixel, producing
interference patterns that look organic when animated. The "secret": use incommensurable speeds and
a radial term to break bilateral symmetry.

### Classic formula (bidouille.org)

```csharp
float Plasma(float x, float y, float t)
{
    // Scale UV to [-5, 5] range
    float cx = x * 10f - 5f;
    float cy = y * 10f - 5f;

    float v = 0f;
    v += MathF.Sin(cx + t);
    v += MathF.Sin((cy + t) / 2f);
    v += MathF.Sin((cx + cy + t) / 2f);
    v += MathF.Sin(MathF.Sqrt(cx*cx + cy*cy + 1f) + t);  // radial ripple
    // v is approximately in [-4, 4]
    return v;
}

// Map v to color (120°-offset sine waves = smooth full color wheel cycling):
SKColor PlasmaColor(float v) {
    float r = MathF.Sin(v * MathF.PI)           * 0.5f + 0.5f;
    float g = MathF.Sin(v * MathF.PI + 2.094f)  * 0.5f + 0.5f;  // + 2π/3
    float b = MathF.Sin(v * MathF.PI + 4.189f)  * 0.5f + 0.5f;  // + 4π/3
    return new SKColor((byte)(r*255), (byte)(g*255), (byte)(b*255));
}
```

**Good frequency/speed values:**
- Use non-integer, non-rational speeds (e.g., 1.0, 0.7, 1.3, 0.8) to prevent obvious looping
- Add a second off-center radial term `sqrt((cx-1.5)²+(cy-0.8)²+1)` to break bilateral symmetry
- `EffectSpeed` multiplies `t` → speed=1 is normal cycling, speed=2 is double

### CPU implementation for PanacheUI

The Plasma effect can be done two ways:
1. **Per-pixel CPU loop** writing to an `SKBitmap` — accurate but expensive at full resolution
2. **Skia shader approximation** — compose two FractalNoise shaders with `CreateCompose` and a
   color matrix mapping, scrolling at different rates. Cheaper but less control over the result.

For preview boxes (small, ~120×80 pixels), per-pixel is fine (~9600 iterations). For full panels,
use the shader approach or pre-render at 1/4 size and upscale with nearest-neighbor for the blocky
retro look, or bilinear for smooth.

---

## Lava Lamp / Metaball Effect

Two implementation approaches:

### Noise-based blobs (simpler)

Use turbulence fBm as a density field, threshold to get blobs:

```csharp
float density = TurbulenceFBm(x*2f, y*2f, _animTime);
// smoothstep threshold creates soft edges:
float blob = SmoothStep(0.35f, 0.65f, density);
// Map: 1 = lava, 0 = fluid. Color from a gradient ramp.
```

Animate by passing `_animTime` as the 3rd noise dimension. Frequency 0.5–1.5 for blob scale,
3–5 octaves, speed 0.2–0.5.

### Metaball approach (more controllable)

Each blob is a point with a "field strength" that decays with distance:

```csharp
// Field contribution from blob i at pixel p:
float field = 0;
foreach (var blob in blobs)
    field += blob.Radius / Vector2.Distance(p, blob.Center);

// If field > threshold → inside lava
bool isLava = field > 1.0f;
```

Blobs merge naturally because fields add — no explicit boolean union needed.

**Animate centers** with incommensurable sine waves:
```csharp
blob.Center.X = baseX + MathF.Sin(_animTime * speedX + phaseX) * amplitude;
blob.Center.Y = baseY + MathF.Cos(_animTime * speedY + phaseY) * amplitude;
```

4–8 blobs, amplitude 20–40% of surface size, speeds 0.3–1.2 different per blob.

**Smooth blending (SmoothMin):** Avoids hard intersections between merging blobs:
```csharp
float SmoothMin(float a, float b, float k) {
    float h = MathF.Max(k - MathF.Abs(a - b), 0f) / k;
    return MathF.Min(a, b) - h*h*k*0.25f;
}
// k = 0.1–0.5, controls blend radius
```

**Color ramp for lava:**
- Field 0.0 → dark background (deep navy, black)
- Field 0.5–0.8 → bright saturated lava (orange-red, purple-pink)
- Field > 1.0 → bright hot center (yellow-white)

Classic: `#FF4500 → #FF8C00 → #FFD700`. Sci-fi: `#AA00FF → #FF00AA → #FFFFFF`.

---

## HSV Color Space

**What it is:** Hue-Saturation-Value — a cylindrical color model more intuitive for artists
than RGB.

**Channels:**
- `Hue` (H) — 0°–360°: color angle on the wheel. 0=Red, 60=Yellow, 120=Green, 240=Blue, 300=Magenta
- `Saturation` (S) — 0–1: 0=gray, 1=full color
- `Value` (V) — 0–1: 0=black, 1=full brightness

**Why it's used in PanacheUI:** Animated gradient colors cycle through hue by incrementing H over
time (H += speed * dt, wrap at 360). This produces rainbow cycling without changing perceived
brightness.

**HSV to RGB conversion (used in DemoWindow.UpdateAnimatedNode):**
```csharp
float c = v * s;
float x = c * (1f - MathF.Abs((h / 60f) % 2f - 1f));
float m = v - c;
// sector-based r,g,b assignment (6 cases by hue sector)
```

**Use cases in PanacheUI:**
- Animated banner gradient (DemoWindow)
- Colorized effect previews in HelpWindow
- Any time you want to cycle through a palette smoothly

---

## Easing Functions and Spring Simulation

**What it is:** Mathematical functions that map linear time [0,1] to a non-linear output [0,1]
for smooth, natural-looking animations.

### Core easing functions

```csharp
float EaseOutCubic(float t)    => 1f - MathF.Pow(1f - t, 3f);      // Fast start, smooth stop
float EaseInCubic(float t)     => t * t * t;                         // Slow start, fast stop
float EaseInOutSine(float t)   => -(MathF.Cos(MathF.PI * t) - 1f) / 2f; // Smooth both ends
float EaseOutElastic(float t)  => t == 0 ? 0 : t == 1 ? 1 :         // Overshoot spring
    MathF.Pow(2f, -10f*t) * MathF.Sin((t*10f - 0.75f) * (2f*MathF.PI/3f)) + 1f;
float EaseOutBounce(float t)   { /* ... piecewise */ }                // Bounce at end
```

**When to use which:**
| Effect | Easing |
|---|---|
| Element sliding in / entrance | `EaseOutCubic` — snappy arrival |
| Hover lift / expand | `EaseOutCubic` |
| Ambient pulse / glow breathe | `EaseInOutSine` — smooth loop |
| Button press spring-back | `EaseOutElastic` or simplified spring |
| Counter reveal, opacity fade | `Linear` |
| Playful bounce | `EaseOutBounce` |

### Frame-rate independent lerp — mathematical derivation

**Naive lerp (WRONG — frame-rate dependent):**
```csharp
value = MathF.Lerp(value, target, 0.1f);  // "10% per frame" — runs 6× faster at 60fps vs 10fps
```

The naive lerp solves `x[n+1] = x[n] + α(target - x[n])` which depends on how many times per
second the update runs.

**The correct ODE:** Exponential decay is the exact solution to `dx/dt = (target - x) * λ`:
```
x(t) = target + (x₀ - target) * e^(-λt)
```
So the per-frame update is:
```csharp
value += (target - value) * (1f - MathF.Exp(-dt * lambda));
```
This is **exact** regardless of dt — two frames of dt=0.008 gives the same result as one frame of
dt=0.016. Frame rate independence falls out of the math automatically.

**Alternative `pow` form** (equivalent, sometimes cleaner):
```csharp
// s = exp(-lambda), so s is "fraction remaining per second"
// s=0.001 → settles in ~1s at 60fps; s=0.0001 → very fast
value = MathF.Lerp(value, target, 1f - MathF.Pow(s, dt));
```

**Practical lambda values:**
| `lambda` | Approximate settle time | Feel |
|---|---|---|
| 2 | ~1.5s | Slow drift |
| 5 | ~0.6s | Smooth |
| 10 | ~0.3s | Fast snap |
| 20 | ~0.15s | Very fast |

### Spring simulation — Euler vs closed-form

**Euler integration (simple, good enough for 60fps UI):**
```csharp
float springK   = 200f;   // stiffness
float dampingC  = 20f;    // damping coefficient
float mass      = 1f;

float force        = -springK * (pos - target);
float damping_f    = -dampingC * velocity;
float acceleration = (force + damping_f) / mass;
velocity          += acceleration * dt;
pos               += velocity * dt;
```

**Critical damping condition:** `dampingC = 2 * sqrt(springK * mass)` → no oscillation, fastest settle.
With K=200, mass=1: critical = `2 * sqrt(200) ≈ 28.3`.
| Condition | Behavior |
|---|---|
| `dampingC < critical` | Oscillates/overshoots — good for elastic feel |
| `dampingC = critical` | No overshoot, fastest settle |
| `dampingC > critical` | Sluggish, no overshoot |

**Ryan Juckett's closed-form critically-damped spring** (frame-rate independent, exact solution):
```csharp
struct SpringCoefs { public float posPosCoef, posVelCoef, velPosCoef, velVelCoef; }

SpringCoefs CalcDampedSpringCoefs(float angularFreq, float dampingRatio, float dt) {
    // angularFreq = sqrt(stiffness/mass), dampingRatio = 1.0 for critical
    float expTerm     = MathF.Exp(-angularFreq * dampingRatio * dt);
    float timeExp     = dt * expTerm;
    float timeExpFreq = timeExp * angularFreq;
    return new SpringCoefs {
        posPosCoef = timeExpFreq + expTerm,
        posVelCoef = timeExp,
        velPosCoef = -(angularFreq * timeExpFreq),
        velVelCoef = -timeExpFreq + expTerm,
    };
}

// Per frame: (for critically-damped, dampingRatio=1.0)
void UpdateSpring(ref float pos, ref float vel, float target, SpringCoefs c) {
    float oldPos = pos - target;
    float oldVel = vel;
    pos = oldPos * c.posPosCoef + oldVel * c.posVelCoef + target;
    vel = oldPos * c.velPosCoef + oldVel * c.velVelCoef;
}
```
Advantage: recalculate `coefs` once per parameter change, not per frame. Exact regardless of dt.

**Apple's intuitive parameters** (Human Interface Guidelines spring model):
```csharp
// duration = time to settle (seconds), bounce = 0..1 (0=no bounce, 1=very bouncy)
float stiffness = MathF.Pow(2f * MathF.PI / duration, 2f);
float damping   = (1f - bounce) * 4f * MathF.PI / duration;
// Then: angularFreq = sqrt(stiffness), dampingRatio = damping / (2 * angularFreq)
```

**Typical values table:**
| Feel | angularFreq | dampingRatio | Result |
|---|---|---|---|
| UI snap (no bounce) | 20 | 1.0 | Settles in ~0.3s, no overshoot |
| Springy button | 25 | 0.6 | Fast with slight overshoot |
| Bouncy entrance | 15 | 0.4 | Pronounced bounce, slow settle |
| Slow float | 8 | 0.8 | Gentle, smooth |

**`EaseOutElastic` vs real spring:** The CSS/easing `EaseOutElastic` is a mathematical approximation
with a fixed oscillation pattern. A real spring simulation responds to velocity — interrupted
transitions blend smoothly (current velocity continues), whereas `EaseOutElastic` would pop.

### Fixed timestep pattern (for physics-heavy effects)

For effects with real spring or rigid-body math, use an accumulator to avoid spiral-of-death:
```csharp
const float FixedDt = 1f / 120f;  // 120Hz simulation
_accumulator += dt;
// Cap to avoid spiral-of-death if a frame takes too long
_accumulator = Math.Min(_accumulator, 0.1f);
while (_accumulator >= FixedDt) {
    UpdatePhysics(FixedDt);  // fixed-step update
    _accumulator -= FixedDt;
}
// Interpolate render state between last and next step
float alpha = _accumulator / FixedDt;
renderPos = Vector2.Lerp(prevPos, curPos, alpha);
```
For 60fps UI animations with small dt (< 0.033s), Euler at variable dt is usually fine.
Fixed timestep is worth it only for multi-body simulations or when accuracy matters.

### Looping animations — absolute time pattern

**Use `_animTime % period` for loops**, never accumulate a separate phase variable:
```csharp
// Correct — always in sync with wall clock, loops cleanly
float phase = (_animTime * speed) % 1f;               // 0→1 loop
float y     = MathF.Sin(phase * MathF.PI * 2f);       // sine wave

// Also correct for staggered: just offset the phase
float phase2 = ((_animTime * speed) + staggerOffset) % 1f;
```

Never `phase += dt * speed` — it drifts over long sessions due to float accumulation error.

---

## Premultiplied Alpha Blending

**What it is:** The compositing operation that determines how a translucent source pixel blends
with the destination.

**Porter-Duff "over" operator (SrcOver):**
```
outRGB = srcRGB + dstRGB * (1 - srcAlpha)
outA   = srcAlpha + dstAlpha * (1 - srcAlpha)
```
With premul: srcRGB already contains srcAlpha, so:
```
outRGB = srcRGB_premul + dstRGB_premul * (1 - srcAlpha)
```

**Why premul:** Straight alpha requires an extra multiply per pixel during compositing. GPU
hardware natively does premul compositing. Additionally, premul avoids color fringing at
transparency edges in filtered images (blur of a transparent element doesn't darken edges).

**SaveLayer (opacity groups):**
```csharp
int save = canvas.SaveLayer(null, new SKPaint { Color = SKColors.White.WithAlpha(opacity) });
// draw children
canvas.RestoreToCount(save);
```
`SaveLayer` composites all children into an offscreen buffer, then applies the layer's opacity
to the group. Essential for node-level opacity without each child independently fading.
Without SaveLayer, overlapping semi-transparent children would double-composite.

---

## Reactive Data Binding

**What it is:** A pattern where a value holder notifies observers when its value changes,
triggering automatic UI updates without polling.

**PanacheBinding<T>:**
```csharp
var binding = new PanacheBinding<float>(0.5f);
binding.OnChanged += (newVal) => node.MarkDirty();
binding.Value = 0.75f;  // triggers OnChanged → MarkDirty → re-render
```

**In HelpWindow:** The Shared Value Binding demo shows `lintensity` (from an ImGui slider) driving
a `PanacheBinding<float>`, whose value feeds directly into a progress-bar node's width.

**Design intent:** Plugins consuming PanacheUI should bind their data model to nodes via
`PanacheBinding<T>` rather than rebuilding the entire tree on data change. The binding fires
MarkDirty only on the affected node, enabling minimal re-renders.

---

## Procedural Texture Generation

**What it is:** Generating pixel content algorithmically rather than loading from an image file.

**CPU procedural generation in PanacheUI:**
All procedural effects (Perlin noise, Voronoi, Plasma, Scanlines, DotMatrix) are generated by
writing pixel values directly to an `SKBitmap` pixel array, then using that bitmap as a paint
shader or drawing it directly.

**Pattern:**
```csharp
var bmp = new SKBitmap(width, height);
var ptr = bmp.GetPixels();  // unsafe pointer to pixel data
// write RGBA8888 values directly via unsafe code or SetPixel
var shader = SKShader.CreateBitmap(bmp, SKShaderTileMode.Repeat, SKShaderTileMode.Repeat);
paint.Shader = shader;
```

**Skia's built-in noise shaders vs. CPU generation:**
- Skia's `CreatePerlinNoiseFractalNoise` is faster (C++ implementation) but gives less control
- CPU generation gives full control over the algorithm but is slow for large surfaces
- For PanacheUI preview boxes (small size), CPU is acceptable

**Voronoi is always CPU-generated** because Skia has no built-in Voronoi shader.

**Scanline effect:** Simple CPU loop — every other row, multiply pixel luminance by a factor:
```csharp
for (int y = 0; y < h; y += 2)
    for (int x = 0; x < w; x++)
        pixels[y * w + x] = DarkenPixel(pixels[y * w + x], 0.4f);
```

---

## Animation Time Integration

**What it is:** Using accumulated elapsed time (in seconds) as the input to all animation
calculations, making animations frame-rate independent.

**Pattern in PanacheUI:**
```csharp
_animTime += ImGui.GetIO().DeltaTime;  // accumulate each frame
```

**Using _animTime for effects:**
- Continuous cycling: `float phase = _animTime * speed % period`
- Sine oscillation: `MathF.Sin(_animTime * speed * MathF.PI * 2f)`
- Hue animation: `float hue = (_animTime * 30f) % 360f`

**Rebuilding trees every frame (HelpWindow):** Since HelpWindow's `BuildLiveExample()` creates
new Node objects every frame, there is no persistent `Anim` state. The workaround for effects
that need persistent state (like StaggeredEntrance) is to compute the animation value from
`_animTime` directly rather than from `node.Anim.*`:
```csharp
float ct = _animTime % period;
node.Anim.EntranceT = Math.Clamp(ct * rate, 0f, 1f);
node.Anim.EntranceStarted = true;
```

**Delta time (dt):** `ImGui.GetIO().DeltaTime` is the time since the last frame in seconds.
Typically 0.016 at 60fps, 0.033 at 30fps.

---

## GCHandle & Pinned Memory

**What it is:** A .NET mechanism to pin a managed array in memory so that unmanaged code (like
Skia's native DLL) can safely read from it without the GC moving it mid-read.

**Pattern:**
```csharp
var pixels = new byte[width * height * 4];
var handle = GCHandle.Alloc(pixels, GCHandleType.Pinned);
try {
    IntPtr ptr = handle.AddrOfPinnedObject();
    surface.ReadPixels(imageInfo, ptr, width * 4, 0, 0);
    _texWrap = _texProvider.CreateFromRaw(spec, pixels);
} finally {
    handle.Free();
}
```

**Why needed:** SKSurface.ReadPixels requires an `IntPtr` to write into. Passing a managed byte[]
pointer is unsafe unless pinned because the GC may relocate the array between the pin and the read.

**Alternative:** `SKBitmap.Bytes` property returns a copy — safe but allocates. For hot paths,
the pinned approach avoids the allocation.

**Dispose discipline:** Always `handle.Free()` in a `finally` block. A leaked `GCHandle.Pinned`
prevents the GC from collecting the array, causing a memory leak.

---

## Hit Testing

**What it is:** Determining which node a mouse click or hover lands on, given that the Panache
surface is a single `ImGui.Image` and ImGui has no awareness of internal node structure.

**Pattern:**
```csharp
// In Draw(), after ImGui.Image():
bool imageHovered = ImGui.IsItemHovered();      // capture BEFORE any other ImGui calls
var mouse   = ImGui.GetMousePos();
float mx    = mouse.X - imagePos.X;            // mouse in node-local coords
float my    = mouse.Y - imagePos.Y;

// Per-node check:
if (_lastLayout.TryGetValue(targetNode, out var box))
{
    bool hit = mx >= box.X && mx <= box.Right && my >= box.Y && my <= box.Bottom;
    if (hit && imageHovered && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
        HandleClick();
}
```

**`_lastLayout`:** `Dictionary<Node, LayoutBox>` produced by `LayoutEngine.Compute()` and cached.
Persists between frames unless the tree is rebuilt. For hot trees (rebuilt every frame), compute
and cache each frame.

**Mouse coordinate transformation:** Subtract `imagePos` (the screen-space top-left of the
`ImGui.Image` call) to get surface-local coordinates matching the LayoutBox values.

**Z-order:** Last rendered node wins for hit tests (paint order = z-order). For overlapping
nodes, check from front-to-back and stop at first hit.

---

## NodeEffect Pipeline

**What it is:** The system by which `Style.Effect` + `Style.EffectColor1/2/Scale/Speed/Intensity`
parameters are interpreted by `SkiaRenderer` to produce animated visual effects on a node.

**Effect application point:** After background and border are drawn, before children. Effects
render into the node's clipped area.

**EffectIntensity:** Always 0–1 for most effects. Exception: Bloom allows up to 3.0 for dramatic
glow. Acts as a blend factor between no-effect and full-effect.

**EffectSpeed:** Multiplier applied to `_animTime` for time-driven effects. 0 = frozen, 1 = normal,
2 = double speed.

**EffectScale:** Spatial scale parameter. For noise effects: controls feature size/frequency.
For geometric effects (Voronoi sites, scanline spacing): controls density or size.

**EffectColor1/Color2:** Primary and secondary colors. Effects that use both: Bloom (glow + tint),
Plasma (two hue endpoints), RimLight (two rim colors), etc.

**Staggered Entrance — special case:**
Effect reads `node.Anim.EntranceT` (0→1, where 1 = fully entered) and `node.Anim.EntranceStarted`.
Since nodes are rebuilt every frame in HelpWindow, EntranceT must be manually set each frame from
`_animTime % period` before rendering, not from a countdown mechanism.

**Renderer pipeline per node:**
```
if (Style.Effect != NodeEffect.None)
    ApplyEffect(canvas, box, style, animTime)
```
Each effect is a separate private method in SkiaRenderer.

---

## Full CPU Raster Pipeline — End to End

The complete path from "node tree exists" to "pixels visible in the game":

```
1. Build / mutate node tree
       │
       ▼
2. MarkDirty() on changed nodes
       │  (propagates up to root)
       ▼
3. Check: _needsRender || _root.IsDirty
       │  (skip everything below if false — no work this frame)
       ▼
4. LayoutEngine.Compute(root, surfaceW, surfaceH)
       │  → Measure pass (bottom-up): compute natural sizes
       │  → Place pass (top-down): assign LayoutBox {X,Y,W,H} to every node
       │  → Returns Dictionary<Node, LayoutBox>
       ▼
5. SkiaRenderer.Render(surface.Canvas, root, layoutDict, animTime)
       │  → Recursive DrawNode(canvas, node, box, animTime):
       │     a. SaveLayer if Opacity < 1.0 (group opacity)
       │     b. Draw drop shadow (SKImageFilter.CreateBlur, offset rect, before fill)
       │     c. Draw background (solid SKColor or gradient SKShader)
       │     d. Draw border (Stroke, SKColor or gradient)
       │     e. ApplyEffect() if Effect != None
       │     f. ClipRoundRect if ClipContent && BorderRadius > 0
       │     g. Draw text (DrawText at computed baseline Y)
       │     h. Recurse into children
       │     i. RestoreToCount()
       ▼
6. TextureManager.Upload(surface)
       │  → surface.Snapshot() → SKImage
       │  → SKBitmap.FromImage(snapshot)
       │  → bitmap.Bytes → byte[] (RGBA8888, premultiplied)
       │  → _texProvider.CreateFromRaw(RawImageSpec(w,h,R8G8B8A8_UNORM), bytes)
       │  → Old IDalamudTextureWrap.Dispose()
       │  → Store new IDalamudTextureWrap
       ▼
7. root.ClearDirty()

8. In ImGui Draw():
       │
       ▼
   ImGui.Image(texWrap.ImGuiHandle, new Vector2(w, h))
       │  → ImGui records a draw command: "blit texture X at screen rect Y"
       │  → At end of ImGui frame, D3D11 renders the quad
       ▼
   Hit-test: mouse pos relative to imagePos → check _lastLayout boxes
```

### Key performance points

- **Step 3 gate:** If nothing is dirty, steps 4–7 are entirely skipped. Static UI costs zero render work.
- **Step 4 (Layout) is cheap** — pure C# arithmetic, no allocations per frame in steady state.
- **Step 5 (Render) is moderate** — Skia CPU raster, ~1–3ms for a 600×500 surface with moderate content.
- **Step 6 (Upload) is expensive** — 600×500 × 4 bytes = 1.2MB CPU→GPU transfer. Minimize by dirty-gating.
- **Step 8 (ImGui.Image) is trivial** — just adds one draw command to ImGui's list.

### Surface resize handling

When `avail.X != _surfaceW || avail.Y != _surfaceH`:
1. `_surface.Dispose()` — frees SKSurface + native Skia memory
2. `_textures.Dispose()` — disposes IDalamudTextureWrap, releases D3D11 texture
3. Re-create both at new size
4. Rebuild node tree (layout is size-dependent for Fill nodes)
5. Set `_needsRender = true`

Never try to resize an SKSurface in-place — create a new one.

---

## ClipRect & ClipRoundRect

**What it is:** Restricting the drawing area so that children cannot paint outside their parent's bounds.

**When used:** Any node with `ClipContent = true` clips its children to its own bounds. Used for
accordion expand animation (height mask), progress bars (fill clipped to track), scrolling text.

**SKCanvas.ClipRoundRect:**
```csharp
int save = canvas.Save();
canvas.ClipRoundRect(new SKRoundRect(rect, radius), SKClipOperation.Intersect, antialias: true);
// draw children here
canvas.RestoreToCount(save);
```
Antialiased clipping produces smooth rounded corners. Without AA, the clip has pixel-jagged edges.

**Clip stack:** `Save()` pushes a new clip context. `RestoreToCount()` pops back. Clips compose —
a child's clip is always the intersection of all ancestor clips.

**ClipRect vs ClipRoundRect:** ClipRect is slightly faster (no curve computation). Only use
ClipRoundRect when the node has `BorderRadius > 0` and `ClipContent = true`.

---

## Drop Shadows in Skia

**What it is:** A blurred colored rectangle drawn behind (and slightly offset from) the main rect.

**Implementation:**
```csharp
var shadowPaint = new SKPaint
{
    Color       = shadowColor,
    IsAntialias = true,
    ImageFilter = SKImageFilter.CreateBlur(blurRadius, blurRadius)
};
canvas.DrawRoundRect(rect.Offset(shadowOffsetX, shadowOffsetY).Inflate(spread), shadowPaint);
```

**PanacheUI shadow conventions:**
- Only inset cards and special elements use shadow — never full-width section strips
- `ShadowBlur` > 0 on full-width strips causes visible blur artifacts at window edges
- Drop shadow is drawn first (before background fill) so it appears behind the element

**Performance:** Shadow blurs are expensive. Large blur radii on many elements per frame will
hurt FPS. Cache shadow-bearing elements on separate dirty-tracked surfaces if needed.

**Alternative (cheaper):** Use a dark background offset without blur — fake "drop shadow" that
is cheaper but less polished. Acceptable for small cards.

---

## Linear & Radial Gradients

**What it is:** Smooth color interpolation between two or more color stops.

**Linear gradient direction:**
PanacheUI uses flow direction to determine gradient axis:
- `Flow.Horizontal` → gradient goes left-to-right
- `Flow.Vertical` → gradient goes top-to-bottom

**`BackgroundColor` → `BackgroundGradientEnd`:** If `BackgroundGradientEnd` is set, a linear
gradient from `BackgroundColor` to `BackgroundGradientEnd` fills the node background.

**Header gradient technique:** Header gradient ends at `Theme.Panel` (the section background color).
This makes the gradient appear to bleed seamlessly into the first section below — no visible seam.

**Multi-stop gradients:**
```csharp
SKShader.CreateLinearGradient(
    start, end,
    new[] { colorA, colorB, colorC },
    new[] { 0f, 0.5f, 1.0f },       // position stops
    SKShaderTileMode.Clamp
)
```

**Radial gradients (glow effects):**
Center-to-edge gradient with transparent end creates a soft radial glow. Used in PulseGlow,
RimLight, and Bloom effects.

---

---

## NodeEffect Full Catalogue

Every effect in the `NodeEffect` enum, what it renders, and which Style properties it reads.

### Generative background effects (replace the node background)

| Effect | What it does | Style properties used |
|---|---|---|
| `PerlinNoise` | Smooth fractal noise fill | Color1 (tint), Scale (frequency), Speed (scroll rate), Intensity (opacity blend) |
| `Plasma` | Lava-lamp sine wave blend | Color1, Color2 (hue endpoints), Scale, Speed |
| `Voronoi` | Cell tessellation | Color1, Scale (cell density), Intensity |
| `Scanline` | CRT scanline overlay | Color1 (line color), Scale (spacing), Intensity |
| `DotMatrix` | Distance-scaled dot grid | Color1, Scale (dot size), Intensity |
| `Waveform` | Audio bar spectrum visualization | Color1, Color2, Speed, Intensity |

### Animated overlay effects (layer on top of the background)

| Effect | What it does | Style properties used |
|---|---|---|
| `Shimmer` | Diagonal stripe sweeping left→right | Color1 (stripe color), Speed, Intensity |
| `PulseGlow` | Border color breathing in/out | Color1 (glow color), Speed (pulse rate), Intensity |
| `ChaseLight` | Bright dot chasing the perimeter | Color1 (light color), Speed, Intensity |

### Lighting effects

| Effect | What it does | Style properties used |
|---|---|---|
| `RimLight` | Backlight edge glow on all four sides | Color1, Color2 (two rim colors), Intensity |
| `AmbientOcclusion` | Corner darkening (2D AO approximation) | Color1 (shadow tint), Scale (corner reach), Intensity |
| `Bloom` | Soft glow bleed around the element | Color1 (glow color), Color2 (tint), Intensity (0–3.0, higher than other effects) |
| `SpecularSweep` | Moving highlight streak across the surface | Color1 (highlight color), Speed, Intensity |

### Distortion / text effects

| Effect | What it does | Style properties used |
|---|---|---|
| `HeatHaze` | Noise-driven UV offset distortion | Color1, Scale (distortion frequency), Speed, Intensity (displacement magnitude) |
| `TextWave` | Per-character Y sine wave undulation | Speed (wave speed), Intensity (wave amplitude) — node must have Text |
| `TypewriterText` | Character-by-character text reveal with cursor | Speed (reveal rate) — node must have Text |
| `RollingCounter` | Numeric value animates from 0 to target | Speed — node must have Text with numeric content |

### Particle effects

| Effect | What it does | Style properties used |
|---|---|---|
| `Particles` | Upward-drifting particles from bottom of node | Color1, Speed (rise speed), Intensity (particle density/opacity) |

### Interaction effects (respond to Anim state)

| Effect | What it does | Style properties / Anim state used |
|---|---|---|
| `HoverLift` | Scale up + brighten on hover | Color1, Intensity — reads `Anim.HoverT` |
| `PressDepress` | Squish on press, spring back on release | Intensity — reads `Anim.PressT` |
| `Ripple` | Circular wave from click point | Color1, Speed — reads `Anim.RippleX/Y/Radius/Alpha` |
| `MagneticPull` | Node pulls toward mouse | Intensity — reads mouse via InteractionManager |

### Layout / transition effects

| Effect | What it does | Style / Anim used |
|---|---|---|
| `Accordion` | Expand/collapse height reveal | Intensity drives Height directly in HelpWindow preview; normally reads `Anim.ExpandT` |
| `StaggeredEntrance` | Sequential fade+slide in from top | reads `Anim.EntranceT`, `Anim.EntranceStarted` — **must be set manually when rebuilding per frame** |
| `SlideTransition` | Looping left→right slide | Speed, Color1 — reads `Anim.SlideT` |
| `CardFlip` | Y-axis perspective flip showing back face | Color1 (back face color) — reads `Anim.FlipT` |

### Effect parameter ranges

| Property | Typical range | Notes |
|---|---|---|
| `EffectIntensity` | 0.0–1.0 | Bloom allows 0–3.0 for strong glow |
| `EffectSpeed` | 0.0–2.0+ | 0=frozen, 1=normal, 2=double speed |
| `EffectScale` | 0.1–3.0 | Context-dependent — noise frequency vs. cell density |
| `EffectColor1/2` | Any PColor | Effect defines what each color means |

---

## Text Effects — Typewriter, Rolling Counter, Text Wave

All three operate by post-processing the node's `Text` string during rendering.

### TypewriterText

Reveals characters one at a time, left to right, with a blinking cursor at the end.

**How it works in SkiaRenderer:**
1. Calculate how many characters to show: `charCount = (int)(_animTime * EffectSpeed * charsPerSecond)`
2. Draw `text.Substring(0, min(charCount, text.Length))` normally
3. If `charCount < text.Length`, append a cursor character (`|`) that blinks at ~1Hz
4. When `charCount >= text.Length`, cursor disappears (reveal complete)

**`EffectSpeed` maps to:** characters per second reveal rate. Speed=1 → ~8 chars/sec typical.

**Node requires:** `Text` must be set. The effect reads `Style.Text` directly.

**Full cycle:** The effect loops — after full reveal it resets. Period is driven by `_animTime % period`
where period = `text.Length / (speed * charsPerSec) + pauseTime`.

### RollingCounter

Animates a numeric value from 0 up to the target number in the text.

**How it works:** Parse `Style.Text` as a float. Compute `displayValue = animFraction * parsedValue`
where `animFraction` goes 0→1 over the animation period. Render `displayValue.ToString("0")`.

**`EffectSpeed` maps to:** how fast the counter rolls. Speed=1 → reaches target in ~1 second.

**Use case:** Counters that "count up" when they appear (score displays, stat popups).

### TextWave

Renders each character individually with a Y offset driven by a sine wave, creating an undulating wave.

**How it works in SkiaRenderer:**
1. Measure each character individually with `font.MeasureText(char)` for advance width
2. For each character at index `i`, compute Y offset:
   ```
   yOffset = sin(i * frequency + _animTime * EffectSpeed * 2π) * EffectIntensity * amplitude
   ```
3. Draw each character at `(baselineX + charAdvance, baselineY + yOffset)`
4. X advances normally; only Y varies per character

**`EffectIntensity` maps to:** wave amplitude (pixels). Intensity=1.0 → ~8px peak displacement typical.
**`EffectSpeed` maps to:** wave animation speed (cycles per second).

**Node requires:** `Text` set. Works best on single-line text.

---

## Entrance Animations — StaggeredEntrance

### How StaggeredEntrance works in SkiaRenderer

The renderer reads `node.Anim.EntranceT` (0→1) and `node.Anim.EntranceStarted` (bool):
- `EntranceStarted = false` → node is invisible (alpha = 0)
- `EntranceStarted = true, EntranceT = 0` → node is at start state (typically Y-offset + alpha=0)
- `EntranceStarted = true, EntranceT = 1` → node fully entered, normal render

**What the entrance does to the node:**
- Translates Y by `(1-EntranceT) * entranceDistance` (slides up from below)
- Scales opacity by `EntranceT` (fades in)
- Applies `EaseOutCubic(EntranceT)` for smooth deceleration

### The per-frame rebuild problem

**Problem:** HelpWindow's `BuildLiveExample()` creates *new* `Node` objects every frame. A new Node
has `Anim.EntranceT = 0` and `Anim.EntranceStarted = false` by default. There is no persistent
NodeAnimState to accumulate `EntranceDelay` countdowns across frames.

**Solution:** Compute `EntranceT` directly from `_animTime` and assign it before rendering:

```csharp
float period = MathF.Max(0.8f, 2.5f / MathF.Max(0.1f, speed));
float ct      = _animTime % period;           // resets every period seconds
float rate    = 3f * MathF.Max(0.1f, intensity * 2.5f);

// Card 0: immediate
node0.Anim.EntranceT       = Math.Clamp(ct * rate, 0f, 1f);
node0.Anim.EntranceStarted = true;

// Card 1: staggered by 0.18s
node1.Anim.EntranceT       = Math.Clamp((ct - 0.18f) * rate, 0f, 1f);
node1.Anim.EntranceStarted = true;

// Card 2: staggered by 0.36s
node2.Anim.EntranceT       = Math.Clamp((ct - 0.36f) * rate, 0f, 1f);
node2.Anim.EntranceStarted = true;
```

**This pattern should be used for any animation state on per-frame-rebuilt nodes.**

### For persistent trees (not rebuilt per frame)

If the node lives across frames (e.g., DemoWindow's tree), `InteractionManager.Update()` ticks
`Anim.EntranceDelay` down with delta time and sets `EntranceStarted = true` when it hits 0.
This is the intended API for production use.

---

## 3D-Style Effects — CardFlip in 2D

### The perspective illusion

A 3D card flip is faked by animating the **horizontal scale** of the node. A card rotating around
its vertical center axis looks, from a straight-on viewer, identical to a card being squashed
horizontally and then expanded. No actual rotation matrix needed.

### Correct transform — use cos(angle), not linear

**Use `cos` for the scale curve**, not linear interpolation. `cos` naturally decelerates near
the face-on position and accelerates near edge-on, matching real physical inertia.

```csharp
float angle = flipT * MathF.PI;                     // 0 → π as flipT goes 0 → 1
float scaleX;
bool  showBack;

if (flipT < 0.5f)
{
    scaleX   =  MathF.Cos(angle);     // 1.0 → 0.0  (front face collapses)
    showBack = false;
}
else
{
    scaleX   = -MathF.Cos(angle);     // 0.0 → 1.0  (back face expands, sign = mirrored)
    showBack = true;
}

float cx = box.MidX, cy = box.MidY;
canvas.Save();
canvas.Translate(cx, cy);
canvas.Scale(scaleX, 1f);             // negative scaleX auto-mirrors back face text
canvas.Translate(-cx, -cy);
DrawCardFace(canvas, box, showBack);  // front or back background + content
canvas.RestoreToCount(save);
```

At `flipT = 0.5` the card is zero-width (edge-on, invisible) — that's the face swap point.
The negative scaleX on the back face means text/art render correctly mirrored.

### Adding perspective foreshortening

For a more convincing 3D look, add a **directional gradient overlay** — slightly brighter on the
leading edge, darker on the trailing edge — shifting as the card rotates. The eye interprets the
shading differential as depth without any actual geometry distortion. Much cheaper than a perspective
matrix and visually convincing for UI cards.

For full perspective: render the card to an `SKBitmap` then blit it with `SKMatrix` that has the
`Persp0`/`Persp1` perspective components set to simulate foreshortening of the near vs far edges.

**EffectColor1** = back face background color. Front face uses normal node background.

---

## Distortion Effects — HeatHaze

### How UV distortion works in real-time 3D

In a GPU fragment shader: sample a noise texture for a (dx, dy) displacement, then re-sample
the scene texture at `uv + noise(uv, time) * amplitude`. One pass, essentially free on GPU.

### CPU raster implementation in PanacheUI

Skia's high-level API has no per-pixel UV offset draw call. Two strategies:

**Strategy A: Full per-pixel resample (accurate, expensive)**

```csharp
using var offscreen = new SKBitmap(width, height);
using var offCanvas  = new SKCanvas(offscreen);
DrawContent(offCanvas);  // render content into offscreen buffer

for (int y = 0; y < height; y++)
for (int x = 0; x < width;  x++)
{
    float nx = Perlin.Noise(x * freq, y * freq, _animTime * speed);
    float ny = Perlin.Noise(x * freq + 100f, y * freq, _animTime * speed);
    // Heat = mostly vertical. Underwater = symmetric.
    int sx = Math.Clamp((int)(x + nx * amplitude),        0, width  - 1);
    int sy = Math.Clamp((int)(y + ny * amplitude * 0.3f), 0, height - 1);
    output.SetPixel(x, y, offscreen.GetPixel(sx, sy));
}
```

Mitigations for cost: work at half resolution (quarters pixel count), precompute the displacement
field and only recompute every 2–3 frames (slow effect, temporal alias invisible), or use a
precomputed scrolling 128×128 tiled noise texture (sample as two channels: R=dx, G=dy).

**Strategy B: Whole-subtree wobble (fast, approximate)**

Noise-driven canvas translate before drawing children. Entire subtree shifts uniformly, no
per-pixel distortion. Nearly zero cost.

```csharp
float noiseX = SamplePerlin(_animTime * speed, 0f, 0f) * amplitude;
float noiseY = SamplePerlin(0f, _animTime * speed, 0f) * amplitude * 0.3f;
canvas.Save();
canvas.Translate(noiseX, noiseY);
DrawChildren(canvas);
canvas.RestoreToCount(save);
```

### Good parameter values

| Look | freqX/Y | amplitude (px) | speed |
|---|---|---|---|
| Subtle heat shimmer | 0.015–0.025 | 2–4 | 0.4–0.6 |
| Strong heat haze | 0.030–0.050 | 6–12 | 0.8–1.2 |
| Underwater warp | 0.008–0.015 | 8–18 | 0.2–0.35 |
| Sci-fi shield ripple | 0.05–0.10 | 3–6 | 1.5–3.0 |

**Y amplitude is ~30% of X** for heat (hot air rises = vertical shimmer). Symmetric for underwater.

---

## Lighting Effects — AO, RimLight, Bloom, SpecularSweep

### Ambient Occlusion (2D corner darkening)

Real SSAO samples a hemisphere of neighbor depth values. The 2D approximation: draw a darkening
radial gradient from each corner inward, composited with `Multiply`.

**Implementation in Skia:**
```csharp
var corners = new[] {
    new SKPoint(box.Left,  box.Top),
    new SKPoint(box.Right, box.Top),
    new SKPoint(box.Left,  box.Bottom),
    new SKPoint(box.Right, box.Bottom),
};
float r = box.Width * Scale * 0.45f;   // corner reach relative to width
foreach (var corner in corners)
{
    var paint = new SKPaint { BlendMode = SKBlendMode.Multiply };
    paint.Shader = SKShader.CreateRadialGradient(
        corner, r,
        new[] { new SKColor(0, 0, 0, intensityByte), SKColors.Transparent },
        null, SKShaderTileMode.Clamp);
    canvas.DrawRect(box, paint);
}
```

**Intensity and radius values:**

| Look | Intensity | Radius (% of short edge) |
|---|---|---|
| Barely there / glass | 0.08–0.12 | 30–40% |
| Subtle but visible | 0.18–0.28 | 40–55% |
| Dramatic / paper on table | 0.35–0.50 | 50–70% |
| Cinematic vignette | 0.55–0.75 | 60–80% |

**Visibility requirement:** AO darkens — invisible on near-black backgrounds.
Use `color.WithOpacity(0.38f)` minimum background so the darkening is perceptible.

**Edge darkening variant:** Add a center-outward radial gradient (transparent center → dark edge)
to handle the spaces between corners.

### Rim Light / Backlight

In 3D: rim lighting illuminates the silhouette edge from behind. In 2D: simulated as a bright
inner stroke along the element border.

**Method 1 — Inner stroke + MaskFilter blur (simplest)**
```csharp
var rimPaint = new SKPaint {
    Style       = SKPaintStyle.Stroke,
    StrokeWidth = strokeWidth,
    Color       = rimColor,
    MaskFilter  = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, blurSigma),
    BlendMode   = SKBlendMode.Screen,
    IsAntialias = true,
};
// Draw slightly inset so stroke stays inside the element
canvas.DrawRoundRect(insetRect, radius, rimPaint);
```

**Method 2 — Directional gradient stroke (most 3D-looking)**
Gradient brighter on top-left (light behind and above), fading to transparent at bottom-right:
```csharp
paint.Shader = SKShader.CreateLinearGradient(
    new SKPoint(box.Left, box.Top), new SKPoint(box.Right, box.Bottom),
    new[] { rimColor.WithAlpha(200), rimColor.WithAlpha(0) },
    null, SKShaderTileMode.Clamp);
paint.BlendMode = SKBlendMode.Screen;
```

**Blend mode guide:**
- `Screen` — safe, never darkens, preserves hue. Best for subtle rim.
- `Plus` / additive — stronger, can blow out. Best for neon/energy rim.
- `Lighten` — sharper edge than Screen.

**Good values:**

| Look | Rim color | Stroke width | Blur sigma |
|---|---|---|---|
| Subtle glass highlight | White at 40–60% alpha | 1–2 px | 2–4 |
| Soft backlight | Color at 70–90% alpha | 2–4 px | 6–12 |
| Neon edge | Color 100% alpha | 3–6 px | 10–20, `Plus` blend |

### Bloom (Soft Glow Bleed)

**Glow** = element surrounded by soft halo; element itself unchanged.
**Bloom** = bright scene areas bleed light into neighbors (needs threshold pass + multi-scale blur).

For PanacheUI, **glow** is the right choice. Bloom is overkill for flat UI panels.

**Simple glow pipeline (single element):**
```csharp
// Pass 1: blurred glow layer (behind the element)
float sigma = 8f + intensity * 20f;
var glowPaint = new SKPaint {
    Color       = glowColor.WithAlpha((byte)(intensity * 200)),
    ImageFilter = SKImageFilter.CreateBlur(sigma, sigma),
    BlendMode   = SKBlendMode.Screen
};
canvas.DrawRoundRect(box, radius, glowPaint);

// Pass 2: crisp element on top (normal render, already done by base renderer)
```

**SaveLayer shortcut (glow the entire subtree):**
```csharp
var filter = SKImageFilter.CreateBlur(sigma, sigma);
using var layerPaint = new SKPaint { ImageFilter = filter, BlendMode = SKBlendMode.Screen };
int save = canvas.SaveLayer(bounds, layerPaint);
DrawContent(canvas);               // goes into offscreen
canvas.RestoreToCount(save);       // composited back blurred + Screen
DrawContent(canvas);               // draw again crisp on top
```

**Intensity > 1.0 (PanacheUI Bloom allows up to 3.0):**
Draw multiple passes with increasing sigma and decreasing alpha. Multi-scale avoids the
halo-ring artifact of single-sigma bloom:
```
Pass 1: sigma=4,  alpha=0.50
Pass 2: sigma=10, alpha=0.35
Pass 3: sigma=22, alpha=0.15
```

**CPU performance:**
- 512×512 at sigma=15: ~3–6ms per pass
- To stay cheap: work on a half-res bloom buffer (quarter the pixel count), blur there, upscale back.
  The bloom radius is perceptually the same; the cost is 4× lower.

**Color tint:** Color1 = glow color. White = neutral light bleed. Colored (cyan, purple) = tech/fantasy.
Cap at 3.0 intensity — beyond that the glow blows out to pure white.

### Specular Sweep

Moving bright highlight streak sweeping across the surface left-to-right.

```csharp
float phase   = (_animTime * speed) % 1f;
float centerX = box.Left + box.Width * phase;
float w       = box.Width * 0.25f;           // streak width = 25% of node
var shader = SKShader.CreateLinearGradient(
    new SKPoint(centerX - w, box.MidY),
    new SKPoint(centerX + w, box.MidY),
    new[] { SKColors.Transparent, color.WithAlpha((byte)(intensity*180)), SKColors.Transparent },
    new[] { 0f, 0.5f, 1f },
    SKShaderTileMode.Clamp);
var paint = new SKPaint { Shader = shader, BlendMode = SKBlendMode.Screen };
canvas.DrawRoundRect(box, radius, paint);
```

Speed = cycles per second across the surface width.

---

## SKBlendMode — All Modes, Formulas, and Visual Use

**What it is:** Controls how a source pixel composites onto the destination. 29 modes total, grouped
into three categories: Porter-Duff (structural compositing), Separable (per-channel math), and
Non-Separable (full-color transforms).

**Default:** `SKBlendMode.SrcOver` — normal "paint on top" compositing. Automatically applied when
you construct `SKPaint` without specifying BlendMode.

### Porter-Duff modes — structural compositing

Sa/Sc = source alpha/color, Da/Dc = destination alpha/color (premultiplied floats 0–1).

| Mode | Output alpha | Output color | Use case |
|---|---|---|---|
| `Clear` | 0 | 0 | Erase to transparent |
| `Src` | Sa | Sc | Replace destination entirely |
| `Dst` | Da | Dc | Keep destination (no-op) |
| **`SrcOver`** | Sa + Da·(1–Sa) | Sc + Dc·(1–Sa) | **Default. Normal paint.** |
| `DstOver` | Da + Sa·(1–Da) | Dc + Sc·(1–Da) | Draw behind destination |
| `SrcIn` | Sa·Da | Sc·Da | Clip source to destination shape |
| `DstIn` | Da·Sa | Dc·Sa | Clip destination to source shape |
| `DstOut` | Da·(1–Sa) | Dc·(1–Sa) | Erase destination with source shape |
| `SrcATop` | Da | Sc·Da + Dc·(1–Sa) | Source atop destination |
| `Xor` | Sa+Da–2·Sa·Da | Sc·(1–Da)+Dc·(1–Sa) | Exclusive regions only |
| `Plus` | Sa+Da (clamped) | Sc+Dc (clamped) | **Additive light — sparks, fire, bloom** |
| `Modulate` | Sa·Da | Sc·Dc | Multiply both alpha and color |

### Separable blend modes — per-channel math

Cb = backdrop channel value (0–1), Cs = source channel value (0–1).

| Mode | Formula | Visual | Neutral source |
|---|---|---|---|
| **`Multiply`** | Cb·Cs | Darkening — AO maps, shadow overlays | White |
| **`Screen`** | 1–(1–Cb)(1–Cs) | Brightening — **glow, bloom, rim light** | Black |
| `Overlay` | HardLight(Cs,Cb) | Contrast boost | 0.5 gray |
| `Darken` | min(Cb,Cs) | Keep darker per channel | White |
| `Lighten` | max(Cb,Cs) | Keep lighter per channel | Black |
| `ColorDodge` | min(1, Cb/(1–Cs)) | Aggressive brightening — lens flare | Black |
| `ColorBurn` | 1–min(1,(1–Cb)/Cs) | Aggressive darkening — shadow vignette | White |
| `HardLight` | Multiply/Screen by Cs | Harsh spotlight | 0.5 gray |
| `Difference` | abs(Cb–Cs) | Inverts where source is white | Black |
| `Exclusion` | Cb+Cs–2·Cb·Cs | Softer Difference | Black |

### Non-separable modes
`Hue`, `Saturation`, `Color`, `Luminosity` — transfer HSL components from source to destination.
Good for colorization.

### Critical PanacheUI usage

- **Bloom / glow:** draw blurred bright shape with `Screen`, then draw sharp shape on top with `SrcOver`
- **Additive particles / sparks:** `Plus` — purely additive, never darkens
- **AO corner darkening:** draw radial gradient with `Multiply` — proportionally darkens content underneath
- **Rim light:** inner stroke/glow drawn with `Screen` — only brightens, preserves hue
- **Shadow overlay:** semi-transparent dark rect with `Multiply`

---

## SKColorFilter — CreateColorMatrix

**What it is:** A per-pixel color transform with no spatial awareness. Transforms each RGBA pixel
through a 4×5 matrix. Runs in a single pass — no intermediate texture. Cheaper than SKImageFilter.

**Cannot look at neighboring pixels.** For spatial effects (blur, glow), use SKImageFilter instead.

**Pipeline position:** After shader generates color, before blending to destination.

### Matrix format — 20 floats, 4 rows × 5 columns, row-major

```
[ R_r  R_g  R_b  R_a  R_off ]   R' = R_r·R + R_g·G + R_b·B + R_a·A + R_off
[ G_r  G_g  G_b  G_a  G_off ]   G' = ...
[ B_r  B_g  B_b  B_a  B_off ]   B' = ...
[ A_r  A_g  A_b  A_a  A_off ]   A' = ...
```

**CRITICAL: R, G, B, A values are 0–255 integers in this formula, NOT 0–1 floats.**
- Matrix coefficient columns (1–4): 0.0–2.0 typical range
- Offset column (col 5): 0–255 range
- Results clamped to 0–255

**Silent bug trap: if A_a (row 4, col 4) = 0, alpha becomes 0 → everything invisible.**

### Concrete matrices

```csharp
// Identity (no change)
new float[] { 1,0,0,0,0,  0,1,0,0,0,  0,0,1,0,0,  0,0,0,1,0 }

// Grayscale (luminance-weighted)
new float[] {
    0.21f,0.72f,0.07f,0,0,
    0.21f,0.72f,0.07f,0,0,
    0.21f,0.72f,0.07f,0,0,
    0,    0,    0,    1,0 }

// Sepia
new float[] {
    0.393f,0.769f,0.189f,0,0,
    0.349f,0.686f,0.168f,0,0,
    0.272f,0.534f,0.131f,0,0,
    0,     0,     0,     1,0 }

// Brightness (+50)
new float[] { 1,0,0,0,50,  0,1,0,0,50,  0,0,1,0,50,  0,0,0,1,0 }

// Contrast (c=1.5, t=128*(1-c))
float c=1.5f, t=128*(1-c);
new float[] { c,0,0,0,t,  0,c,0,0,t,  0,0,c,0,t,  0,0,0,1,0 }

// Invert RGB
new float[] { -1,0,0,0,255,  0,-1,0,0,255,  0,0,-1,0,255,  0,0,0,1,0 }
```

### SKShader vs SKColorFilter vs SKImageFilter

| | SKShader | SKColorFilter | SKImageFilter |
|---|---|---|---|
| Purpose | Generate source color | Transform one pixel | Spatially-aware effect |
| Sees neighbors? | No | No | Yes |
| Intermediate texture? | No | No | Yes |
| Cost | Fast | Fast | Moderate–heavy |
| Use for | Gradients, noise | Grayscale, tint, sepia | Blur, glow, shadow |

**Pipeline order when all three are on one paint:**
`Shader → color → ColorFilter → transforms it → BlendMode → destination → ImageFilter → post-processes layer`

---

## SKMaskFilter vs SKImageFilter for Blur

Two completely different blur APIs.

### SKMaskFilter.CreateBlur — alpha boundary blur only

```csharp
paint.MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, sigma);
```

Blurs **only the alpha channel** (shape boundary). RGB data is untouched — colors stay flat, edges go soft.

`SKBlurStyle` options: `Normal`, `Solid` (opaque interior), `Outer` (outside only), `Inner` (inside only)

**Use for:** Soft-edged shapes, text halos, simple drop shadow silhouettes.
**Cannot:** blur actual image content (colors).

### SKImageFilter.CreateBlur — full RGBA blur

```csharp
paint.ImageFilter = SKImageFilter.CreateBlur(sigmaX, sigmaY);
```

Blurs all four RGBA channels. sigmaX ≠ sigmaY = directional/motion blur.
Requires an intermediate render pass.

**Use for:** Blurring bitmap content, building glow (blur → Screen back), depth of field.

| | MaskFilter | ImageFilter |
|---|---|---|
| Blurs | Alpha only | All RGBA |
| Can blur image content | No | Yes |
| Blur styles (Solid/Outer/Inner) | Yes | No |
| Directional blur | No | Yes |
| Composable with other filters | No | Yes |
| Cost | Very light | Moderate |

---

## SKBitmap / SKImage / SKPixmap — Choosing the Right Container

### SKBitmap — mutable, owned pixel buffer

Read-write. Owns memory. Draw into it with `new SKCanvas(bitmap)`. Access pixels directly.

```csharp
var bmp = new SKBitmap(w, h, SKColorType.Rgba8888, SKAlphaType.Premul);
var canvas = new SKCanvas(bmp);       // draw into it
IntPtr ptr = bmp.GetPixels();         // raw pointer — fast, unsafe
byte[] copy = bmp.Bytes;              // safe copy
```

**PanacheUI uses this for:** Voronoi generation, scanline effect, DotMatrix — anything that writes
pixels in a loop. Build the bitmap, then draw it with `canvas.DrawBitmap()`.

### SKImage — immutable snapshot, optimized for drawing

Read-only. May stay GPU-resident. Fastest for repeatedly drawing the same content.

```csharp
SKImage img = SKImage.FromBitmap(bmp);       // from a bitmap
SKImage img = surface.Snapshot();            // snapshot of rendered surface
canvas.DrawImage(img, destRect, paint);
```

**PanacheUI upload chain:** `SKSurface.Snapshot()` → `SKBitmap.FromImage(snapshot)` → `bitmap.Bytes`
→ `ITextureProvider.CreateFromRaw()`. The Snapshot avoids re-rendering.

### SKPixmap — non-owning pixel view

Lightweight pointer + metadata. No memory ownership. Valid only while backing store is alive.

```csharp
if (surface.PeekPixels(out SKPixmap map)) {
    // Direct pointer to surface pixels — no copy. Read-only.
}
SKImage img = SKImage.FromPixels(info, nativePtr, rowBytes); // zero-copy wrap
```

### Decision guide

| Need | Use |
|---|---|
| Write pixels in a loop (procedural gen) | `SKBitmap` |
| Draw from repeatedly (cached asset) | `SKImage` |
| Snapshot rendered surface | `surface.Snapshot()` → `SKImage` |
| Read pixels back for Dalamud upload | `SKBitmap.FromImage(snapshot).Bytes` |
| Zero-copy wrap of native pixel buffer | `SKPixmap` + `SKImage.FromPixels` |

---

*Last updated: 2026-04-01 — Major expansion via web research: SKBlendMode (all 29 modes + formulas), SKColorFilter/ColorMatrix (concrete matrices for grayscale/sepia/brightness/contrast/invert), MaskFilter vs ImageFilter blur comparison, SKBitmap/Image/Pixmap decision guide, NodeEffect full catalogue (every effect + style props), Text effects (Typewriter/RollingCounter/TextWave rendering internals), StaggeredEntrance per-frame-rebuild pattern, CardFlip cos(angle) technique (replaces linear), HeatHaze CPU strategies + parameter tables, AO (intensity+radius table), RimLight (3 methods), Bloom (multi-scale + SaveLayer + CPU perf), SpecularSweep, Easing + spring simulation (exponential decay lerp exact ODE derivation, pow(s,dt) alternative form, Ryan Juckett's closed-form critically-damped spring, Apple's intuitive stiffness/damping params, typical values table, EaseOutElastic vs real spring comparison, fixed timestep accumulator pattern), Full CPU raster pipeline end-to-end flowchart, ImGui ID stack/#/### prefixes, ImDrawList internal structure (VtxBuffer/IdxBuffer/CmdBuffer, batching via ImDrawCmdHeader memcmp, AddImage internals, SetNextWindowPos mechanism, BeginChild own draw list), g.LastItemData exact mechanism (single flat struct, overwritten by every ItemAdd()), ImGuiCond, mouse event semantics, Unicode atlas gotcha, Perlin technical deep dive (quintic fade, gradient hash, fBm lacunarity×gain warning, turbulence vs fBm distinction, 3D time animation vs UV scroll, known artifacts, visual recipes table), Simplex noise vs Perlin comparison, Voronoi Fortune's algorithm + Worley noise grid, F1/F2/F2-F1 patterns, distance function shapes, seamless tiling, Scanline/CRT (cosine formula + RGB mask + bloom stack), Plasma demoscene effect (sine sum formula + color mapping), Lava Lamp (noise blobs + metaball SDF + SmoothMin)*
*Maintained by: Aria (Claude)*
