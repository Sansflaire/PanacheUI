# PanacheUI — Feature Implementation Checklist

Each feature gets its own page in HelpWindow's Feature Reference.
Check off each item when: code implemented + HelpWindow page added + build clean.

---

## Generative Textures
- [x] **01 · Perlin Noise Background** — Fractal noise overlay on any node background
- [x] **02 · Plasma / Lava Lamp** — Animated blobs via layered sin/cos math
- [x] **03 · Voronoi Cell Pattern** — Generative tessellated cell background
- [x] **04 · Scanline / CRT Overlay** — 1px-on/off repeating line overlay
- [x] **05 · Dot Matrix / Halftone** — Grid of circles scaling with distance from focal point
- [x] **06 · Waveform Strip** — Procedural audio-style waveform from data or noise

## Animated Effects
- [x] **07 · Shimmer Sweep** — Diagonal highlight stripe slides across node on loop or trigger
- [x] **08 · Breathing Pulse Glow** — Border/shadow slowly brightens and fades
- [x] **09 · Particle Emitter** — Tiny particles spawn and drift from a node
- [x] **10 · Typewriter Text** — Text reveals one character at a time with blinking cursor
- [x] **11 · Rolling Number Counter** — Numeric text animates from old value to new
- [x] **12 · Chase-Light Border** — Bright segment orbits the node perimeter

## Animated Interactions
- [x] **13 · Hover Lift + Glow** — Card scales up, shadow expands on hover
- [x] **14 · Press Depress (Spring Physics)** — Node squishes on mouse down, spring-returns
- [x] **15 · Ripple on Click** — Circular wave expands from click point
- [x] **16 · Hover Tooltip Node** — Floating Panache node appears near hovered element
- [x] **17 · Magnetic Neighbor Pull** — Siblings shift subtly toward hovered node

## Panel Interactions
- [x] **18 · Accordion Expand / Collapse** — Sections animate height open/closed
- [x] **19 · Cross-Panel Spotlight** — Hovered section glows; others dim
- [x] **20 · Shared Value Binding** — Two nodes mirror a PanacheBinding<T> reactively
- [x] **21 · Staggered Entrance** — Sibling nodes slide+fade in with offset delay

## Lighting Effects
- [x] **22 · Specular Light Sweep** — Moving light source casts highlight across all surfaces
- [x] **23 · Rim Lighting** — Thick soft glow on one edge simulating backlight
- [x] **24 · Ambient Occlusion Corners** — Subtle radial darkening where panels meet
- [x] **25 · Bloom / Volumetric Glow** — Bright elements bleed soft light onto neighbors

## Visual Effects
- [x] **26 · Text Wave** — Characters undulate vertically in a sine wave
- [x] **27 · Shake** — Node violently jitters then snaps back (error/warning state)
- [x] **28 · Heat Haze Warp** — Content resampled with scrolling noise UV offset
- [x] **29 · Slide Transition** — Old content slides out, new slides in
- [x] **30 · Card Flip** — Node flips on Y-axis to reveal alternate content

---

## Infrastructure Milestones
- [x] `NodeEffect` enum in Enums.cs
- [x] Effect properties on Style.cs (`Effect`, `EffectColor1/2`, `EffectScale`, `EffectSpeed`, `EffectIntensity`)
- [x] `SkiaRenderer.Render()` accepts `float time`
- [x] `NodeAnimState` on Node (hover T, press T, entrance T, shake state, particle list)
- [x] `InteractionManager` fires hover/click events from mouse pos + layout map
- [x] `PanacheBinding<T>` reactive value container

---

*Last updated: features 03-30 — all 30 features complete*
