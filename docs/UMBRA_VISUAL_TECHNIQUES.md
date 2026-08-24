# Umbra Visual Techniques — Analysis & PanacheUI Implementation

Documented from side-by-side screenshot comparison. These techniques are now applied
to PanacheUI's DemoWindow and should be used as the standard for all PanacheUI UIs.

---

## Technique 1: Gradient Header → Panel Bleed

**What Umbra does:**
The header/title area gradient ends at the same color as the content panel background.
This makes the header "flow into" the content below it with no visible seam.

**PanacheUI implementation:**
```csharp
// Header: BackgroundGradientEnd = Panel color (same as section panels below)
s.BackgroundColor       = PColor.FromHex("#1E1040");
s.BackgroundGradientEnd = Panel;   // Panel = #131328 — matches section bg exactly
```
The header's gradient bottom color and the first section's background are identical.

---

## Technique 2: Left Accent Bar

**What Umbra does:**
Each panel/section has a narrow (2–4px) vertical strip on its left edge in a bright
accent color. This visually anchors the section and communicates its "type" or category
at a glance.

**PanacheUI implementation — `SectionWrap()`:**
```csharp
// Body row: [3px fixed-width accent node | content node]
var body = new Node() { Flow = Horizontal };
body.AppendChild(new Node() {
    Width = 3, HeightMode = Fill,
    BackgroundColor = accent.WithOpacity(0.70f)
});
body.AppendChild(content);
```

---

## Technique 3: Top Highlight Line ("Light From Above")

**What Umbra does:**
Each panel has a 1px bright line along its top edge. The effect simulates a light
source above the window — each panel looks like a shelf catching the light.
This creates depth without shadows.

**PanacheUI implementation — `SectionWrap()`:**
```csharp
// 1px highlight at top of each section
var highlight = new Node() {
    WidthMode = Fill, Height = 1,
    BackgroundColor       = accent.WithOpacity(0.18f),
    BackgroundGradientEnd = Transparent,  // fades right to left
    Flow = Horizontal
};
outer.AppendChild(highlight);   // inserted BEFORE the body row
outer.AppendChild(body);
```
The gradient fade means the highlight is strongest on the left (near the accent bar)
and invisible on the right — matching how Umbra's highlights look.

---

## Technique 4: Section Dividers (Not Borders)

**What Umbra does:**
Between sections there is NO gap. Instead there is a 1px line that blends from
a subtle color to transparent. This is NOT a card border — it's a visual hint that
two surfaces meet, not two floating objects.

**PanacheUI implementation — `SectionDivider()`:**
```csharp
// Between sections: 1px fade line
private static Node SectionDivider(PColor color) =>
    new Node() {
        WidthMode = Fill, Height = 1,
        BackgroundColor       = color,
        BackgroundGradientEnd = Transparent,
        Flow = Horizontal
    };
```
Color values used:
- After header → first section: `#9966FF @ 25%` (accented, prominent)
- Between content sections: `#FFFFFF @ 5%` (almost invisible, just a hint)

---

## Technique 5: Unified Dark Surface (No Floating Cards)

**What Umbra does:**
Sections are NOT floating cards. They are strips of a continuous surface with
slightly varying background darkness. No drop shadows between panels.
Cards only appear INSIDE sections, and even then at low contrast.

**PanacheUI implementation:**
```csharp
static readonly PColor Base   = PColor.FromHex("#0D0D1A");  // root bg
static readonly PColor Panel  = PColor.FromHex("#131328");  // section bg (+6% brightness)
static readonly PColor Panel2 = PColor.FromHex("#0F0F22");  // inner card bg (+2% brightness)
```
- Root: `Base`
- `SectionWrap` outer: `Panel`
- Cards inside sections: `Panel2`
- No `ShadowBlur` on sections; shadows reserved for special accent elements only.

---

## Technique 6: Section Header Labels

**What Umbra does:**
Each section has a small uppercase label ("MAIN TOOLBAR", "LEFT SIDE") in muted
accent color above its content. Provides scannable hierarchy without heavy chrome.

**PanacheUI implementation — `SectionLabel()`:**
```csharp
private static Node SectionLabel(string text, PColor accent) =>
    new Node().WithText(text).WithStyle(s => {
        s.FontSize = 9.5f; s.Bold = true;
        s.Color = accent.WithOpacity(0.65f);
        s.Margin = new EdgeSize(0, 0, 6, 0);  // gap below before content
    });
```
Labels are placed at the top of the `content` node passed to `SectionWrap`.

---

## Technique 7: No BorderRadius on Full-Width Sections

**What Umbra does:**
Full-width panel sections have square edges. Only elements that are explicitly
"floating" or "inset" (like pill badges or mini-cards) have border radius.
Full-width sections touching the window edge are always square.

**PanacheUI rule:**
- `SectionWrap` outer node: `BorderRadius = 0` (default, never set)
- Inset cards (`StatCard`, `FeatureCard`): `BorderRadius = 4`
- Pill buttons: `BorderRadius = 6`
- Window chrome (ImGui title bar): handled by OS/ImGui

---

## Technique 8: Hover Is a Style, Not a Handler

**What Umbra does:**
Every interactive row lightens its background and firms up its border on hover. The cue
is uniform across the whole UI, which is what makes it read as feedback rather than as
decoration — it means the same thing everywhere.

**PanacheUI rule (as of 2026-08-24):**
The renderer paints the hover cue. Do **not** write a hover tracker.

```csharp
row.WithStyle(s =>
{
    s.BackgroundColor      = Theme.Panel2;
    s.BorderColor          = accent.WithOpacity(0.22f);
    s.BorderWidth          = 1;

    s.HoverBackgroundColor = accent.WithOpacity(0.16f);
    s.HoverBorderColor     = accent.WithOpacity(0.70f);
    s.HoverColor           = PColor.White;      // text
});
```

`SkiaRenderer` cross-fades each base→hover pair over `NodeAnimState.HoverT`. No
`IsInteractive` is required — hover state is tracked for every node in the layout.

**The anti-pattern this replaces**, which every consumer had independently arrived at:

```csharp
private string? _hoverId;
private string? _hoverNext;   // applied a frame late, because the tree is rebuilt
                              // before the click that changes it is dispatched
// ...plus an OnMouseEnter on every interactive node and a manual re-style
```

Notes:
- `HoverBorderColor` still needs a non-zero `BorderWidth`. The hover style changes a
  border's color; it does not conjure one out of nothing.
- On a node whose base background is a gradient, set `HoverBackgroundGradientEnd` too, or
  the far stop is carried toward the single hover color and the gradient pinches.
- Opacity convention for the hover pair: background `0.16f`, border `0.70f`. That is a
  deliberate step up from the resting `0.22f` border — visible without being a flash.

---

## Technique 9: Centre on the Cross Axis, Never With a Margin

**PanacheUI rule:**
Use `AlignItems` / `AlignSelf`. Never hand-compute a centring margin.

```csharp
row.WithStyle(s => { s.Flow = Flow.Horizontal; s.AlignItems = AlignItems.Center; });
```

**Banned:**

```csharp
// breaks the instant either size changes — which is what UI scale does to every size at once
mark.WithStyle(s => s.Margin = new EdgeSize((RowH - IconSz) / 2f, 0, 0, 0));
```

Same rule for long labels: set `TextOverflow = TextOverflow.Wrap` (or use `PUI.Paragraph`)
rather than budgeting characters against an invented average glyph width. If a real width
is genuinely needed up front, `PUI.MeasureText` returns the exact one.

---

## Rule for Future PanacheUI Windows

When building any PanacheUI window with a colored background:
1. Use `SectionWrap(accent, content)` for every logical panel section
2. Use `SectionDivider(color)` between sections — never leave a raw gap
3. Header gradient must end at `Panel` color so it bleeds into the first section
4. No `ShadowBlur` on sections — use `Panel2` background for inner card depth
5. Full-width strips have `BorderRadius = 0`; only inset elements have radius
6. Every interactive element (buttons) lives INSIDE the surface — no ImGui widgets outside
7. Hover comes from `Style.Hover*`, not a hand-rolled tracker (Technique 8)
8. Cross-axis centring comes from `AlignItems`, not a computed margin (Technique 9)
9. Long text uses `TextOverflow.Wrap` / `PUI.Paragraph`, not a character budget
10. Decorative icons stay inert — `PUI.Icon` is `PointerEvents.None` by default, so it
    can't swallow the hover its parent button needed
