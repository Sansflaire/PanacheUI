using System;
using System.Collections.Generic;
using PanacheUI.Core;

namespace PanacheUI.Theming;

/// <summary>
/// A full color theme for PanacheUI. Every semantic slot a consumer plugin
/// might need has its own field.
///
/// <para><b>Schema version: v3.</b> Panache adopted the v3 slot vocabulary
/// (Material 3 + Radix + Fluent 2 style — Surface0…4, Primary + variants,
/// Secondary + variants, Tertiary, Success/Warning/Error/Info + Containers,
/// Border*, State*, Rarity*, GlowPrimary/Secondary, RowLocated/Owned/Stored)
/// on 2026-07-10. Panache no longer accepts v2 (legacy Base/Panel/Accent-only)
/// theme files. See <c>docs/COLOR_THEORY_AND_THEMES.md</c> for the full
/// reference.</para>
///
/// <para><b>Legacy V1 aliases.</b> Downstream plugins written before the
/// migration read <c>_theme.Base</c>, <c>_theme.Panel</c>, <c>_theme.Accent</c>,
/// etc. Those names still resolve — every legacy slot is now a computed
/// property forwarding to its v3 equivalent (see the "Legacy aliases" region
/// below). New code should read the v3 slot directly. The aliases will be
/// removed once every consumer is migrated.</para>
///
/// <para>Consumers should NOT read from <see cref="PanacheThemes.Active"/> every
/// frame. Snapshot the active theme once at init (and again in the
/// <see cref="PanacheThemes.ActiveChanged"/> handler) into a local field,
/// and read colors off that.</para>
/// </summary>
public sealed record PanacheTheme
{
    /// <summary>Unique key for this theme. Used by consumers to persist a
    /// user selection and by <see cref="PanacheThemes.SetActive"/> to look
    /// the theme up.</summary>
    public required string Name { get; init; }

    // ── Surfaces (6) — elevation ladder ─────────────────────────────────────
    /// <summary>Root window background — the darkest surface.</summary>
    public required PColor Surface0        { get; init; }
    /// <summary>First elevation above <see cref="Surface0"/>.</summary>
    public required PColor Surface1        { get; init; }
    /// <summary>Second elevation.</summary>
    public required PColor Surface2        { get; init; }
    /// <summary>Third elevation — typical "panel" surface.</summary>
    public required PColor Surface3        { get; init; }
    /// <summary>Highest elevation.</summary>
    public required PColor Surface4        { get; init; }
    /// <summary>Inverted-L surface — tooltips, snackbars, banners.</summary>
    public required PColor SurfaceInverse  { get; init; }

    // ── On-surface text (4) ─────────────────────────────────────────────────
    /// <summary>Primary body text.</summary>
    public required PColor OnSurfaceHi        { get; init; }
    /// <summary>Secondary body text.</summary>
    public required PColor OnSurfaceMed       { get; init; }
    /// <summary>Tertiary text — small labels, badges.</summary>
    public required PColor OnSurfaceLow       { get; init; }
    /// <summary>Placeholder / disabled text.</summary>
    public required PColor OnSurfaceDisabled  { get; init; }

    // ── Primary accent (7) ──────────────────────────────────────────────────
    /// <summary>Primary brand accent.</summary>
    public required PColor Primary               { get; init; }
    /// <summary>Primary in hover state (L ± 0.05).</summary>
    public required PColor PrimaryHover          { get; init; }
    /// <summary>Primary in pressed state (L ± 0.10).</summary>
    public required PColor PrimaryPressed        { get; init; }
    /// <summary>Primary in disabled state (low chroma, mid L).</summary>
    public required PColor PrimaryDisabled       { get; init; }
    /// <summary>Foreground color for icon / label on top of <see cref="Primary"/>.</summary>
    public required PColor OnPrimary             { get; init; }
    /// <summary>Low-chroma container tint of Primary — bg for chips, callouts.</summary>
    public required PColor PrimaryContainer      { get; init; }
    /// <summary>Foreground text on top of <see cref="PrimaryContainer"/>.</summary>
    public required PColor OnPrimaryContainer    { get; init; }

    // ── Secondary accent (7) ────────────────────────────────────────────────
    public required PColor Secondary             { get; init; }
    public required PColor SecondaryHover        { get; init; }
    public required PColor SecondaryPressed      { get; init; }
    public required PColor SecondaryDisabled     { get; init; }
    public required PColor OnSecondary           { get; init; }
    public required PColor SecondaryContainer    { get; init; }
    public required PColor OnSecondaryContainer  { get; init; }

    // ── Tertiary signal (4) ─────────────────────────────────────────────────
    /// <summary>Signal accent — gear names, item names, special-shop labels.</summary>
    public required PColor Tertiary              { get; init; }
    public required PColor OnTertiary            { get; init; }
    public required PColor TertiaryContainer     { get; init; }
    public required PColor OnTertiaryContainer   { get; init; }

    // ── Semantic status quads (4 × 4 = 16) ──────────────────────────────────
    public required PColor Success               { get; init; }
    public required PColor SuccessContainer      { get; init; }
    public required PColor OnSuccess             { get; init; }
    public required PColor OnSuccessContainer    { get; init; }

    public required PColor Warning               { get; init; }
    public required PColor WarningContainer      { get; init; }
    public required PColor OnWarning             { get; init; }
    public required PColor OnWarningContainer    { get; init; }

    public required PColor Error                 { get; init; }
    public required PColor ErrorContainer        { get; init; }
    public required PColor OnError               { get; init; }
    public required PColor OnErrorContainer      { get; init; }

    public required PColor Info                  { get; init; }
    public required PColor InfoContainer         { get; init; }
    public required PColor OnInfo                { get; init; }
    public required PColor OnInfoContainer       { get; init; }

    // ── Borders (4) ─────────────────────────────────────────────────────────
    public required PColor BorderSubtle          { get; init; }
    public required PColor BorderDefault         { get; init; }
    public required PColor BorderStrong          { get; init; }
    public required PColor BorderFocus           { get; init; }

    // ── State layers (4) ────────────────────────────────────────────────────
    public required PColor StateHover            { get; init; }
    public required PColor StatePressed          { get; init; }
    public required PColor StateSelected         { get; init; }
    public required PColor StateFocused          { get; init; }

    // ── Rarity ladder (6) ───────────────────────────────────────────────────
    public required PColor RarityCommon          { get; init; }
    public required PColor RarityUncommon        { get; init; }
    public required PColor RarityRare            { get; init; }
    public required PColor RarityEpic            { get; init; }
    public required PColor RarityLegendary       { get; init; }
    public required PColor RarityMythic          { get; init; }

    // ── Glows (2) ───────────────────────────────────────────────────────────
    /// <summary>Warm signal glow — Located rows.</summary>
    public required PColor GlowPrimary           { get; init; }
    /// <summary>Cool signal glow — Hunt rows.</summary>
    public required PColor GlowSecondary         { get; init; }

    // ── Row highlights (6) ──────────────────────────────────────────────────
    public required PColor RowLocatedBg          { get; init; }
    public required PColor RowLocatedBd          { get; init; }
    public required PColor RowOwnedBg            { get; init; }
    public required PColor RowOwnedBd            { get; init; }
    public required PColor RowStoredBg           { get; init; }
    public required PColor RowStoredBd           { get; init; }

    // ═══════════════════════════════════════════════════════════════════════
    //  Legacy V1 aliases — computed getters forwarding to v3 slots.
    //  These exist so downstream plugins (GlamourDresserHelper etc.) that
    //  still read `_theme.Base`, `_theme.Accent`, etc. keep working. Do not
    //  add new consumers of these; migrate to the v3 slot above instead.
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>Legacy alias for <see cref="Surface0"/>.</summary>
    public PColor Base       => Surface0;
    /// <summary>Legacy alias for <see cref="Surface3"/>.</summary>
    public PColor Panel      => Surface3;
    /// <summary>Legacy alias for <see cref="Surface1"/>.</summary>
    public PColor Panel2     => Surface1;
    /// <summary>Legacy alias for <see cref="Surface3"/>.</summary>
    public PColor CardBg     => Surface3;
    /// <summary>Legacy alias for <see cref="Surface2"/>.</summary>
    public PColor Stripe1    => Surface2;
    /// <summary>Legacy alias for <see cref="Surface1"/>.</summary>
    public PColor Stripe2    => Surface1;

    /// <summary>Legacy alias for <see cref="OnSurfaceHi"/>.</summary>
    public PColor TextHi     => OnSurfaceHi;
    /// <summary>Legacy alias for <see cref="OnSurfaceMed"/>.</summary>
    public PColor TextMed    => OnSurfaceMed;
    /// <summary>Legacy alias for <see cref="OnSurfaceLow"/>.</summary>
    public PColor TextMuted  => OnSurfaceLow;
    /// <summary>Legacy alias for <see cref="OnSurfaceDisabled"/>.</summary>
    public PColor TextDim    => OnSurfaceDisabled;
    /// <summary>Legacy alias for <see cref="OnSurfaceDisabled"/> (V1 had no
    /// distinct TextSubtle in v3; folds to Disabled).</summary>
    public PColor TextSubtle => OnSurfaceDisabled;
    /// <summary>Legacy alias for <see cref="Tertiary"/> (V1 GearName was the
    /// signal-text slot; v3 renames to Tertiary).</summary>
    public PColor GearName   => Tertiary;

    /// <summary>Legacy alias for <see cref="Primary"/>.</summary>
    public PColor Accent     => Primary;
    /// <summary>Legacy alias for <see cref="Secondary"/>.</summary>
    public PColor AccentAlt  => Secondary;

    /// <summary>Legacy alias for <see cref="Success"/>.</summary>
    public PColor StatusOk   => Success;
    /// <summary>Legacy alias for <see cref="Info"/> (V1 had no distinct
    /// "active" slot in v3; folds to Info).</summary>
    public PColor StatusAct  => Info;
    /// <summary>Legacy alias for <see cref="Info"/>.</summary>
    public PColor StatusRet  => Info;
    /// <summary>Legacy alias for <see cref="Warning"/> (V1 "Attention" ≡
    /// v3 semantic Warning).</summary>
    public PColor StatusAtt  => Warning;
    /// <summary>Legacy alias for <see cref="Error"/> (V1 "Miss" ≡
    /// v3 semantic Error).</summary>
    public PColor StatusMiss => Error;

    /// <summary>Legacy alias for <see cref="WarningContainer"/> (V1's muted
    /// warning slot is v3's WarningContainer).</summary>
    public PColor WarningMuted => WarningContainer;

    /// <summary>Legacy alias for <see cref="Error"/> — the bright red close
    /// button top-gradient stop.</summary>
    public PColor CloseFill1  => Error;
    /// <summary>Legacy alias for <see cref="ErrorContainer"/> — the deeper
    /// red close button bottom-gradient stop.</summary>
    public PColor CloseFill2  => ErrorContainer;
    /// <summary>Legacy alias for <see cref="ErrorContainer"/> — the close
    /// button border (near-black red).</summary>
    public PColor CloseBorder => ErrorContainer;

    /// <summary>Legacy alias for <see cref="GlowPrimary"/> (V1 named the warm
    /// signal glow "gold"; v3 names it by role, not hue).</summary>
    public PColor GlowGold   => GlowPrimary;
    /// <summary>Legacy alias for <see cref="GlowSecondary"/> (V1 named the
    /// cool signal glow "cyan"; v3 names it by role, not hue).</summary>
    public PColor GlowCyan   => GlowSecondary;

    /// <summary>Convenience — pure white. Held on the theme so consumers
    /// don't reach past the theme boundary for the common overlay tint.</summary>
    public PColor White => PColor.FromHex("#FFFFFF");
}

/// <summary>
/// Static registry of built-in themes plus the current active-theme pointer.
///
/// <para>Usage:
/// <code>
/// // Consumer plugin init:
/// _theme = PanacheThemes.Active;
/// PanacheThemes.ActiveChanged += t => _theme = t;
///
/// // Elsewhere:
/// style.BackgroundColor = _theme.Surface0;
///
/// // Swap:
/// PanacheThemes.SetActive("Shadow");
/// </code>
/// </para>
/// </summary>
public static class PanacheThemes
{
    // ── Built-in themes ─────────────────────────────────────────────────────

    /// <summary>The Panache-brand built-in default. Palette derived from the
    /// hand-crafted "Shadow" theme (v3 schema, harmony=manual) — rose primary,
    /// purple secondary, warm-gold tertiary. See
    /// <c>docs/COLOR_THEORY_AND_THEMES.md</c> for the full theory.
    ///
    /// <para>Note the field is named <c>Default</c> for legacy API compat
    /// (external plugins reference <c>PanacheThemes.Default</c>). The
    /// <see cref="PanacheTheme.Name"/> displayed to users is "Shadow", so an
    /// external <c>Themes/Shadow/theme.json</c> cleanly overrides this built-in
    /// when present.</para></summary>
    public static readonly PanacheTheme Default = new()
    {
        Name                 = "Shadow",
        Surface0             = PColor.FromHex("#0F0E12"),
        Surface1             = PColor.FromHex("#161519"),
        Surface2             = PColor.FromHex("#1D1C21"),
        Surface3             = PColor.FromHex("#242229"),
        Surface4             = PColor.FromHex("#2B2A31"),
        SurfaceInverse       = PColor.FromHex("#F0EEF2"),
        OnSurfaceHi          = PColor.FromHex("#F0EEF2"),
        OnSurfaceMed         = PColor.FromHex("#C7C3CB"),
        OnSurfaceLow         = PColor.FromHex("#8A8791"),
        OnSurfaceDisabled    = PColor.FromHex("#605C68"),
        Primary              = PColor.FromHex("#E05C9A"),
        PrimaryHover         = PColor.FromHex("#EE6EAA"),
        PrimaryPressed       = PColor.FromHex("#C24A82"),
        PrimaryDisabled      = PColor.FromHex("#5B303F"),
        OnPrimary            = PColor.FromHex("#1F0511"),
        PrimaryContainer     = PColor.FromHex("#3C0F28"),
        OnPrimaryContainer   = PColor.FromHex("#F9C0DA"),
        Secondary            = PColor.FromHex("#B992FF"),
        SecondaryHover       = PColor.FromHex("#C4A3FF"),
        SecondaryPressed     = PColor.FromHex("#A57BEE"),
        SecondaryDisabled    = PColor.FromHex("#3E3055"),
        OnSecondary          = PColor.FromHex("#12081F"),
        SecondaryContainer   = PColor.FromHex("#241640"),
        OnSecondaryContainer = PColor.FromHex("#E4D6FF"),
        Tertiary             = PColor.FromHex("#D9B45F"),
        OnTertiary           = PColor.FromHex("#1E1600"),
        TertiaryContainer    = PColor.FromHex("#3C2F0F"),
        OnTertiaryContainer  = PColor.FromHex("#F7E4AC"),
        Success              = PColor.FromHex("#3CCF7F"),
        SuccessContainer     = PColor.FromHex("#0F3520"),
        OnSuccess            = PColor.FromHex("#03150B"),
        OnSuccessContainer   = PColor.FromHex("#A5EFC3"),
        Warning              = PColor.FromHex("#F5B841"),
        WarningContainer     = PColor.FromHex("#3D2F14"),
        OnWarning            = PColor.FromHex("#1D1403"),
        OnWarningContainer   = PColor.FromHex("#F8DCA0"),
        Error                = PColor.FromHex("#F25757"),
        ErrorContainer       = PColor.FromHex("#421B1E"),
        OnError              = PColor.FromHex("#1C0606"),
        OnErrorContainer     = PColor.FromHex("#F8B3B3"),
        Info                 = PColor.FromHex("#4DA6FF"),
        InfoContainer        = PColor.FromHex("#16304A"),
        OnInfo               = PColor.FromHex("#041220"),
        OnInfoContainer      = PColor.FromHex("#AFD5FB"),
        BorderSubtle         = PColor.FromHex("#252329"),
        BorderDefault        = PColor.FromHex("#38353F"),
        BorderStrong         = PColor.FromHex("#524E5A"),
        BorderFocus          = PColor.FromHex("#B992FF"),
        StateHover           = PColor.FromHex("#25232B"),
        StatePressed         = PColor.FromHex("#2E2B34"),
        StateSelected        = PColor.FromHex("#3C0F28"),
        StateFocused         = PColor.FromHex("#B992FF"),
        RarityCommon         = PColor.FromHex("#A8ADB8"),
        RarityUncommon       = PColor.FromHex("#5BC66E"),
        RarityRare           = PColor.FromHex("#4D9FE8"),
        RarityEpic           = PColor.FromHex("#A66BE8"),
        RarityLegendary      = PColor.FromHex("#E8873C"),
        RarityMythic         = PColor.FromHex("#E8C547"),
        GlowPrimary          = PColor.FromHex("#FFE9A8"),
        GlowSecondary        = PColor.FromHex("#8BE0F7"),
        RowLocatedBg         = PColor.FromHex("#33290F"),
        RowLocatedBd         = PColor.FromHex("#D9B45F"),
        RowOwnedBg           = PColor.FromHex("#14301E"),
        RowOwnedBd           = PColor.FromHex("#3E9B63"),
        RowStoredBg          = PColor.FromHex("#16283C"),
        RowStoredBd          = PColor.FromHex("#4478B8"),
    };

    /// <summary>Built-in themes shipped with Panache. Always present regardless
    /// of what's in the user's Themes folder. Currently a single hand-crafted
    /// palette (Shadow) — additional palettes ship as v3 JSON files under
    /// <c>Themes/&lt;Name&gt;/theme.json</c>.</summary>
    public static IReadOnlyList<PanacheTheme> BuiltIn { get; } = new[] { Default };

    private static IReadOnlyList<PanacheTheme> _external = System.Array.Empty<PanacheTheme>();

    /// <summary>Themes loaded from the on-disk Themes folder (see
    /// <c>PanacheUI.Theming.ThemeLoader</c>). Rebuilt whenever the folder
    /// changes. External themes with a name collision override the built-in
    /// of the same name — this is how a user can "reskin" the default.</summary>
    public static IReadOnlyList<PanacheTheme> External => _external;

    /// <summary>Every registered theme: built-ins first, then externals. If an
    /// external theme has the same <see cref="PanacheTheme.Name"/> as a built-in
    /// it wins — the built-in is not returned.</summary>
    public static IReadOnlyList<PanacheTheme> All
    {
        get
        {
            var extNames = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
            foreach (var t in _external) extNames.Add(t.Name);
            var result = new List<PanacheTheme>(BuiltIn.Count + _external.Count);
            foreach (var t in BuiltIn)  if (!extNames.Contains(t.Name)) result.Add(t);
            foreach (var t in _external)                                 result.Add(t);
            return result;
        }
    }

    /// <summary>True if the given theme instance is one of the built-in
    /// palettes (rather than a folder-loaded external).</summary>
    public static bool IsBuiltIn(PanacheTheme theme)
    {
        foreach (var b in BuiltIn)
            if (ReferenceEquals(b, theme)) return true;
        return false;
    }

    /// <summary>Look up a theme by name (case-insensitive). Returns null if
    /// not registered.</summary>
    public static PanacheTheme? Find(string name)
    {
        foreach (var t in All)
            if (string.Equals(t.Name, name, System.StringComparison.OrdinalIgnoreCase))
                return t;
        return null;
    }

    /// <summary>Replace the external theme set. Called by the ThemeLoader
    /// after it re-scans the on-disk folder. Fires <see cref="ThemesChanged"/>.
    /// If the previously active theme is gone from the new set, the active
    /// theme falls back to <see cref="Default"/>.</summary>
    public static void SetExternal(IReadOnlyList<PanacheTheme> themes)
    {
        _external = themes ?? System.Array.Empty<PanacheTheme>();
        ThemesChanged?.Invoke();

        // If the active theme was replaced by an external of the same name,
        // re-point at the new instance so consumers get the new colors.
        var refreshed = Find(_active.Name);
        if (refreshed != null && !ReferenceEquals(refreshed, _active))
        {
            _active = refreshed;
            ActiveChanged?.Invoke(_active);
        }
        // If the active theme was removed entirely, fall back to Default.
        else if (refreshed == null)
        {
            _active = Default;
            ActiveChanged?.Invoke(_active);
        }
    }

    /// <summary>Fired after the set of registered themes changes (folder rescan).</summary>
    public static event Action? ThemesChanged;

    private static PanacheTheme _active = Default;

    /// <summary>The currently active theme. Consumers should snapshot this
    /// once and subscribe to <see cref="ActiveChanged"/> rather than reading
    /// it per frame.</summary>
    public static PanacheTheme Active => _active;

    /// <summary>Fired after <see cref="SetActive(string)"/> or
    /// <see cref="SetActive(PanacheTheme)"/> changes the active theme. The
    /// new theme is passed as the argument.</summary>
    public static event Action<PanacheTheme>? ActiveChanged;

    /// <summary>Swap the active theme by name. Silently no-ops if the name
    /// doesn't match a registered theme or the theme is already active.</summary>
    public static void SetActive(string name)
    {
        var t = Find(name);
        if (t != null) SetActive(t);
    }

    /// <summary>Swap the active theme by reference. No-ops if already active.</summary>
    public static void SetActive(PanacheTheme theme)
    {
        if (ReferenceEquals(theme, _active)) return;
        _active = theme;
        ActiveChanged?.Invoke(theme);
    }
}
