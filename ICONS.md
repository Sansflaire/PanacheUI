# PanacheUI Icon Set

78 flat white-on-transparent glyphs, 313×313 RGBA, shipped in [`Icons/`](Icons/) as
zero-padded 4-digit PNGs (`0001.png` … `0078.png`).

```csharp
PUI.Icon(36, 16f, tint: accent)     // decorative by default — PointerEvents.None, no Id
PUI.Icon(36, 16f, nodeId: "row-mark", interactive: true)
```

---

## Contact sheet

![PanacheUI icon contact sheet](docs/icons/contact-sheet.png)

Regenerate after adding icons:

```bash
python - <<'PY'
from PIL import Image, ImageDraw, ImageFont
import glob, os, math
fs = sorted(glob.glob('Icons/*.png'))
CELL, PAD, LBL, COLS = 88, 8, 18, 10
font = ImageFont.truetype('C:/Windows/Fonts/consolab.ttf', 13)
rows = math.ceil(len(fs) / COLS)
sh = Image.new('RGB', (COLS*(CELL+PAD)+PAD, rows*(CELL+LBL+PAD)+PAD), (18, 18, 26))
d = ImageDraw.Draw(sh)
for i, f in enumerate(fs):
    cx = PAD + (i % COLS)*(CELL+PAD); cy = PAD + (i // COLS)*(CELL+LBL+PAD)
    d.rounded_rectangle([cx, cy, cx+CELL, cy+CELL], radius=6, fill=(38, 38, 50))
    im = Image.open(f).convert('RGBA').resize((CELL-22, CELL-22), Image.LANCZOS)
    sh.paste(im, (cx+11, cy+11), im)
    d.text((cx+CELL//2, cy+CELL+2), os.path.basename(f)[:4], fill=(224, 178, 76), font=font, anchor='ma')
sh.save('docs/icons/contact-sheet.png')
PY
```

---

## About the names

**The ID is the address. The name is a label for humans and agents reading this file.**

`PanacheIcons.Get` takes an ID and nothing else, the icon browser shows IDs, and every
call site in every plugin passes an ID. There is no name-based lookup and none is planned
— names drift, IDs don't. The names below exist so that nobody has to build their own
labelled montage to answer "which number is the info circle", which is the exact tax this
file was written to remove.

Names are unique across the set, so near-identical variants (`0005`/`0006`, `0012`/`0048`,
`0034`/`0067`) are distinguished by their differing detail rather than being collapsed
into one name.

Every name below was assigned by looking at the rendered glyph, not inferred from
filename order. Two prior verbal descriptions turned out to be wrong on inspection and are
corrected here: **`0028` is an info circle** (not a question mark) and **`0030` is an info
hexagon** (not an empty box).

---

## Status / locks — 0001–0015

| ID | Name | Glyph |
|----|------|-------|
| 0001 | `lock-closed` | Padlock, closed, tall body |
| 0002 | `lock-closed-rounded` | Padlock, closed, rounded body |
| 0003 | `lock-closed-wide` | Padlock, closed, wide squat body |
| 0004 | `lock-open` | Padlock with the shackle swung open |
| 0005 | `x-mark-heavy` | Bare X, heavy stroke |
| 0006 | `x-mark-light` | Bare X, thin stroke |
| 0007 | `x-circle` | X inside a circle outline |
| 0008 | `x-square` | X inside a rounded-square outline |
| 0009 | `question-circle` | ? inside a circle outline |
| 0010 | `question-square` | ? inside a rounded-square outline |
| 0011 | `question-circle-filled` | ? knocked out of a solid circle |
| 0012 | `question-mark-heavy` | Bare ?, heavy stroke |
| 0013 | `check-mark` | Bare checkmark |
| 0014 | `check-circle` | Check inside a circle outline |
| 0015 | `check-square` | Check inside a rounded-square outline |

## Map pins & routes — 0016–0023

| ID | Name | Glyph |
|----|------|-------|
| 0016 | `pin-map` | Teardrop map pin, hollow centre |
| 0017 | `pin-map-split` | Map pin with a gap in the top of the ring |
| 0018 | `pin-shield` | Pentagon/shield-shaped marker with a centre dot |
| 0019 | `pin-map-grounded` | Map pin over an ellipse "ground shadow" |
| 0020 | `route-solid` | Waypoints 1→2→3 joined by a solid line, arrowhead |
| 0021 | `route-dotted` | Waypoints 1→2→3 joined by a dotted line |
| 0022 | `route-curved` | Waypoints 1→2→3 on a smooth curve |
| 0023 | `route-from-pin` | Dotted route starting at a map pin |

## Targets & info — 0024–0031

| ID | Name | Glyph |
|----|------|-------|
| 0024 | `dot-ring-heavy` | Filled dot inside a thick ring |
| 0025 | `dot-ring-concentric` | Filled dot inside two thin rings |
| 0026 | `crosshair-dot` | Dot in a ring with four crosshair ticks |
| 0027 | `target-ring-dashed` | Dot inside a dashed/segmented ring |
| 0028 | `info-circle` | i inside a circle outline |
| 0029 | `info-square-clipped` | i inside a square with one clipped corner |
| 0030 | `info-hexagon` | i inside a hexagon outline |
| 0031 | `info-circle-dashed` | i inside a segmented circle |

## Verified / achievement marks — 0032–0035

| ID | Name | Glyph |
|----|------|-------|
| 0032 | `check-circle-sparkle` | Check in a broken circle with sparkles |
| 0033 | `check-shield-winged` | Check on a shield with wing flourishes |
| 0034 | `check-circle-compass-rose` | Check in a ring with four leaf-shaped points |
| 0035 | `check-diamond` | Check inside a diamond with arrow points |

## Bare shapes — 0036–0042

| ID | Name | Glyph |
|----|------|-------|
| 0036 | `square-outline` | Empty rounded square |
| 0037 | `circle-outline` | Empty circle |
| 0038 | `octagon-dashed` | Empty octagon, dashed edges |
| 0039 | `diamond-outline` | Empty diamond |
| 0040 | `arrows-circular-dashed` | Two chasing arrows on a dashed circle |
| 0041 | `ring-progress-partial` | Donut ring with a quadrant missing |
| 0042 | `hexagon-ellipsis` | Three dots inside a solid-edged hexagon |

## Negative / alert — 0043–0048

| ID | Name | Glyph |
|----|------|-------|
| 0043 | `x-blades` | X with tapered, blade-like points |
| 0044 | `x-circle-heavy` | X inside a circle, heavy stroke |
| 0045 | `x-diamond-arrows` | X in a diamond with four outward arrows |
| 0046 | `prohibited` | Circle with a diagonal bar (no-entry) |
| 0047 | `lightbulb-idea` | Lightbulb with radiating lines |
| 0048 | `question-mark-light` | Bare ?, lighter stroke than 0012 |

## Actions & navigation — 0049–0054

| ID | Name | Glyph |
|----|------|-------|
| 0049 | `sparkles` | Three four-point sparkles |
| 0050 | `signpost` | Directional signpost on a stake |
| 0051 | `refresh-loop` | Two arrows forming a closed loop |
| 0052 | `refresh-dashed` | Refresh loop with a dashed segment |
| 0053 | `upload-tray` | Up arrow out of an open tray |
| 0054 | `download-tray` | Down arrow into a closed tray |

## Rewards & tools — 0055–0064

| ID | Name | Glyph |
|----|------|-------|
| 0055 | `trophy` | Two-handled trophy cup on a plinth |
| 0056 | `swords-crossed` | Two crossed swords |
| 0057 | `target-arrow` | Dartboard with an arrow in the bullseye |
| 0058 | `medal-star` | Star medal on a ribbon bar |
| 0059 | `user-settings` | Person silhouette with a gear |
| 0060 | `sliders` | Two horizontal slider tracks with knobs |
| 0061 | `wrench-plus` | Wrench with a plus sign |
| 0062 | `gear-pencil` | Gear with a pencil (edit settings) |
| 0063 | `scroll-certificate` | Rolled certificate with a wax seal |
| 0064 | `wax-seal-emblem` | Ornate wax seal with ribbon tails |

## Emblems & badges — 0065–0072

| ID | Name | Glyph |
|----|------|-------|
| 0065 | `crown-laurel-shield` | Crown on a shield framed by laurels |
| 0066 | `winged-emblem` | Winged sword/crest insignia |
| 0067 | `check-circle-cardinal-points` | Check in a broken ring with four diamond points |
| 0068 | `check-shield` | Check on a plain solid shield |
| 0069 | `check-rosette` | Check in an award rosette with ribbon tails |
| 0070 | `check-hexagon-ornate` | Check in a hexagon with decorative points |
| 0071 | `contrast-crosshair` | Half-filled circle with crosshair ticks |
| 0072 | `shield-striped` | Shield with diagonal stripes |

## Progress & world — 0073–0078

| ID | Name | Glyph |
|----|------|-------|
| 0073 | `pie-progress-dashed` | Filled pie wedge in a dashed circle |
| 0074 | `hexagon-ellipsis-dashed` | Three dots inside a broken-edge hexagon |
| 0075 | `globe-grid` | Wireframe globe |
| 0076 | `globe-compass` | Wireframe globe with compass points |
| 0077 | `earth-orbit` | Earth with an orbital ring and satellite dots |
| 0078 | `earth-compass-frame` | Earth inside an ornate compass frame |

---

## Adding an icon

1. Drop a 313×313 white-on-transparent RGBA PNG into `Icons/` with the next free
   4-digit number. No gaps — `PanacheIcons.AllIds()` scans the folder, so a gap is
   legal but shows up as a hole in the browser.
2. Regenerate the contact sheet with the script above.
3. Add a row to the right table here with a name unique across the whole set.
