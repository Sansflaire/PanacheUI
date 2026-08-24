Panache Color Themes
====================

Panache watches this folder and reloads themes live — no game restart needed.
Schema: Panache v3. Older v2 (flat Base/Panel/Accent) exports are ignored.

── 1. ColorSchemeCreator v3 export (folder per theme) ───────────────────

Drop the whole theme folder in. Panache expects <ThemeName>/theme.json:

  Themes/
    Shadow/
      theme.json          <-- required
      preview.png         <-- ignored (kept for your reference)
      rules_report.txt    <-- ignored

The JSON is the exact output of ColorSchemeCreator v3:

  {
    "meta":  { "name": "Shadow", "schema_version": "3.0", ... },
    "slots": {
      "Surface0": { "hex": "#0F0E12", "rgb": [...], "hsl": [...],
                     "oklch": { "L": 0.20, "C": 0.01, "H": 300 } },
      "Primary":  { "hex": "#E05C9A", ... },
      ...
    }
  }

Slot keys are PascalCase and map 1:1 to Panache's 66 v3 color slots.
Only the "hex" child of each slot is consumed; rgb/hsl/oklch are
informational. If meta.name is missing the folder name is used.

── 2. Flat single-file theme (Panache's own format) ─────────────────────

Drop a *.json directly in this folder. Every field is optional; anything
you leave out inherits from the theme named in `basedOn` (default:
the built-in default). A valid file can be as small as:

  {
    "name": "ShadowRed",
    "basedOn": "Shadow",
    "primary": "#FF3060"
  }

Colors are hex strings: #RGB, #RRGGBB, or #RRGGBBAA.

── The 66 v3 slots ───────────────────────────────────────────────────────

  Surfaces      : Surface0..4, SurfaceInverse
  On-surface    : OnSurfaceHi, OnSurfaceMed, OnSurfaceLow, OnSurfaceDisabled
  Primary       : Primary, PrimaryHover, PrimaryPressed, PrimaryDisabled,
                  OnPrimary, PrimaryContainer, OnPrimaryContainer
  Secondary     : Secondary + Hover/Pressed/Disabled + OnSecondary +
                  SecondaryContainer + OnSecondaryContainer
  Tertiary      : Tertiary, OnTertiary, TertiaryContainer, OnTertiaryContainer
  Success       : Success, SuccessContainer, OnSuccess, OnSuccessContainer
  Warning       : Warning, WarningContainer, OnWarning, OnWarningContainer
  Error         : Error, ErrorContainer, OnError, OnErrorContainer
  Info          : Info, InfoContainer, OnInfo, OnInfoContainer
  Borders       : BorderSubtle, BorderDefault, BorderStrong, BorderFocus
  State layers  : StateHover, StatePressed, StateSelected, StateFocused
  Rarity        : RarityCommon, RarityUncommon, RarityRare, RarityEpic,
                  RarityLegendary, RarityMythic
  Glows         : GlowPrimary, GlowSecondary
  Rows          : RowLocatedBg/Bd, RowOwnedBg/Bd, RowStoredBg/Bd

Full theory + slot semantics + validation rules are documented in
../docs/COLOR_THEORY_AND_THEMES.md — read that first if you plan to
author a theme by hand.

External themes with the same name as the built-in "Shadow" override
it. Other plugins can consume this registry via:
  - HTTP:        GET http://localhost:17779/themes, /themes/{name}, /themes/active
  - Dalamud IPC: PanacheUI.Themes.List / .Get / .GetActive / .SetActive
  - Direct:      reference PanacheUI.dll and read PanacheThemes.All
