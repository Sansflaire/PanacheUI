using System;
using System.Collections.Generic;
using PanacheUI.Core;

namespace PanacheUI.Theming;

/// <summary>
/// A full color theme for PanacheUI. Every semantic slot a consumer plugin
/// might need has its own field, so plugins never hard-code hex.
///
/// Consumers should NOT read from <see cref="PanacheThemes.Active"/> every
/// frame. Snapshot the active theme once at init (and again in the
/// <see cref="PanacheThemes.ActiveChanged"/> handler) into a local field,
/// and read colors off that. Property access into the static registry is
/// cheap but the rule keeps consumer code honest about where the palette
/// lives.
/// </summary>
public sealed record PanacheTheme
{
    /// <summary>Unique key for this theme. Used by consumers to persist a
    /// user selection and by <see cref="PanacheThemes.SetActive"/> to look
    /// the theme up.</summary>
    public required string Name { get; init; }

    // ── Backgrounds ─────────────────────────────────────────────────────────
    /// <summary>Root window background — the darkest layer.</summary>
    public required PColor Base       { get; init; }
    /// <summary>Section panel background — one step above <see cref="Base"/>.</summary>
    public required PColor Panel      { get; init; }
    /// <summary>Inner card / inset background — sits between Base and Panel.</summary>
    public required PColor Panel2     { get; init; }
    /// <summary>Small card / inline-row background — lighter than Panel2.
    /// Was hard-coded as <c>#17151B</c> across the plugin.</summary>
    public required PColor CardBg     { get; init; }
    /// <summary>Alternating row stripe top color.</summary>
    public required PColor Stripe1    { get; init; }
    /// <summary>Alternating row stripe bottom color.</summary>
    public required PColor Stripe2    { get; init; }

    // ── Text ────────────────────────────────────────────────────────────────
    /// <summary>Primary body text.</summary>
    public required PColor TextHi     { get; init; }
    /// <summary>Secondary body text.</summary>
    public required PColor TextMed    { get; init; }
    /// <summary>Tertiary text — small labels, badges.</summary>
    public required PColor TextMuted  { get; init; }
    /// <summary>Placeholder / disabled text.</summary>
    public required PColor TextDim    { get; init; }
    /// <summary>Very subtle text — used sparingly.</summary>
    public required PColor TextSubtle { get; init; }
    /// <summary>Special text tint for gear names inside the dresser
    /// (currently a warm gold).</summary>
    public required PColor GearName   { get; init; }

    // ── Accents ─────────────────────────────────────────────────────────────
    /// <summary>Primary brand accent for this theme.</summary>
    public required PColor Accent     { get; init; }
    /// <summary>Secondary accent (Special-Shop pill, purple category glyphs).</summary>
    public required PColor AccentAlt  { get; init; }

    // ── Status palette ──────────────────────────────────────────────────────
    /// <summary>Owned / stored / OK — green.</summary>
    public required PColor StatusOk   { get; init; }
    /// <summary>Active / hunting / in-flight — cyan.</summary>
    public required PColor StatusAct  { get; init; }
    /// <summary>On retainer / offsite — slate-blue.</summary>
    public required PColor StatusRet  { get; init; }
    /// <summary>Attention / bundled / gold — amber.</summary>
    public required PColor StatusAtt  { get; init; }
    /// <summary>Missing / error — soft red.</summary>
    public required PColor StatusMiss { get; init; }

    // ── Row highlight tints (bg + border pairs) ─────────────────────────────
    /// <summary>Background tint for a row flagged as "located this session".</summary>
    public required PColor RowLocatedBg { get; init; }
    /// <summary>Border tint for a row flagged as "located this session".</summary>
    public required PColor RowLocatedBd { get; init; }
    /// <summary>Background tint for a "confirmed owned" row (deep green).</summary>
    public required PColor RowOwnedBg   { get; init; }
    /// <summary>Border tint for a "confirmed owned" row.</summary>
    public required PColor RowOwnedBd   { get; init; }
    /// <summary>Background tint for a "cabinet stored" row (deep teal).</summary>
    public required PColor RowStoredBg  { get; init; }
    /// <summary>Border tint for a "cabinet stored" row.</summary>
    public required PColor RowStoredBd  { get; init; }

    // ── Close button (top-strip red bevel) ──────────────────────────────────
    /// <summary>Close-button top gradient stop (bright red).</summary>
    public required PColor CloseFill1 { get; init; }
    /// <summary>Close-button bottom gradient stop (deep red).</summary>
    public required PColor CloseFill2 { get; init; }
    /// <summary>Close-button border (near-black red).</summary>
    public required PColor CloseBorder { get; init; }

    // ── Effect glows (Located sheen, hunt sheen) ────────────────────────────
    /// <summary>Warm off-white gold sheen — Located rows.</summary>
    public required PColor GlowGold { get; init; }
    /// <summary>Bright cyan glow — Hunt rows.</summary>
    public required PColor GlowCyan { get; init; }

    // ── Warnings (yellow-gold "requires action" text) ───────────────────────
    /// <summary>Bright warning text.</summary>
    public required PColor Warning      { get; init; }
    /// <summary>Muted warning text / warning chip fill base.</summary>
    public required PColor WarningMuted { get; init; }

    /// <summary>Convenience — <see cref="PColor.White"/>. Held on the theme so
    /// consumers don't reach past the theme boundary for a common overlay tint.</summary>
    public required PColor White { get; init; }
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
/// style.BackgroundColor = _theme.Base;
///
/// // Swap:
/// PanacheThemes.SetActive("Charcoal");
/// </code>
/// </para>
/// </summary>
public static class PanacheThemes
{
    // ── Built-in themes ─────────────────────────────────────────────────────

    /// <summary>Cool navy background with soft-rose accent — the palette the
    /// plugin has shipped with since the Panache rewrite.</summary>
    public static readonly PanacheTheme Default = new()
    {
        Name         = "Default",
        Base         = PColor.FromHex("#0D0D1A"),
        Panel        = PColor.FromHex("#131328"),
        Panel2       = PColor.FromHex("#0F0F22"),
        CardBg       = PColor.FromHex("#17151B"),
        Stripe1      = PColor.FromHex("#2A2730"),
        Stripe2      = PColor.FromHex("#242129"),
        TextHi       = PColor.FromHex("#F2EFF6"),
        TextMed      = PColor.FromHex("#C9C4D2"),
        TextMuted    = PColor.FromHex("#8B8794"),
        TextDim      = PColor.FromHex("#6F6A78"),
        TextSubtle   = PColor.FromHex("#666688"),
        GearName     = PColor.FromHex("#D9B45F"),
        Accent       = PColor.FromHex("#E89AC8"),
        AccentAlt    = PColor.FromHex("#B992FF"),
        StatusOk     = PColor.FromHex("#7FD6A9"),
        StatusAct    = PColor.FromHex("#6FBFD6"),
        StatusRet    = PColor.FromHex("#7AA7E0"),
        StatusAtt    = PColor.FromHex("#D9B45F"),
        StatusMiss   = PColor.FromHex("#E57B72"),
        RowLocatedBg = PColor.FromHex("#D9B45F"),
        RowLocatedBd = PColor.FromHex("#D9B45F"),
        RowOwnedBg   = PColor.FromHex("#2D6B3E"),
        RowOwnedBd   = PColor.FromHex("#4C9866"),
        RowStoredBg  = PColor.FromHex("#2D6B6B"),
        RowStoredBd  = PColor.FromHex("#4C9899"),
        CloseFill1   = PColor.FromHex("#E85555"),
        CloseFill2   = PColor.FromHex("#A02222"),
        CloseBorder  = PColor.FromHex("#5A0F0F"),
        GlowGold     = PColor.FromHex("#FFE9A8"),
        GlowCyan     = PColor.FromHex("#8BE0F7"),
        Warning      = PColor.FromHex("#F2D640"),
        WarningMuted = PColor.FromHex("#D9A63F"),
        White        = PColor.FromHex("#FFFFFF"),
    };

    /// <summary>Warmer charcoal background with saturated rose accent —
    /// palette extracted from the original UI-mockup reference in
    /// <c>GlamourDresserHelper/ref/Glamour Layouts.html</c>.</summary>
    public static readonly PanacheTheme Charcoal = new()
    {
        Name         = "Charcoal",
        Base         = PColor.FromHex("#0F0E12"),
        Panel        = PColor.FromHex("#141317"),
        Panel2       = PColor.FromHex("#111015"),
        CardBg       = PColor.FromHex("#1A181D"),
        Stripe1      = PColor.FromHex("#2A2730"),
        Stripe2      = PColor.FromHex("#242129"),
        TextHi       = PColor.FromHex("#F0EEF2"),
        TextMed      = PColor.FromHex("#C7C3CB"),
        TextMuted    = PColor.FromHex("#8A8791"),
        TextDim      = PColor.FromHex("#6E6A75"),
        TextSubtle   = PColor.FromHex("#605C68"),
        GearName     = PColor.FromHex("#D9B45F"),
        Accent       = PColor.FromHex("#E05C9A"),
        AccentAlt    = PColor.FromHex("#B992FF"),
        StatusOk     = PColor.FromHex("#7FD6A9"),
        StatusAct    = PColor.FromHex("#6FBFD6"),
        StatusRet    = PColor.FromHex("#7AA7E0"),
        StatusAtt    = PColor.FromHex("#D9B45F"),
        StatusMiss   = PColor.FromHex("#E57B72"),
        RowLocatedBg = PColor.FromHex("#D9B45F"),
        RowLocatedBd = PColor.FromHex("#D9B45F"),
        RowOwnedBg   = PColor.FromHex("#2D6B3E"),
        RowOwnedBd   = PColor.FromHex("#4C9866"),
        RowStoredBg  = PColor.FromHex("#2D6B6B"),
        RowStoredBd  = PColor.FromHex("#4C9899"),
        CloseFill1   = PColor.FromHex("#E85555"),
        CloseFill2   = PColor.FromHex("#A02222"),
        CloseBorder  = PColor.FromHex("#5A0F0F"),
        GlowGold     = PColor.FromHex("#FFE9A8"),
        GlowCyan     = PColor.FromHex("#8BE0F7"),
        Warning      = PColor.FromHex("#F2D640"),
        WarningMuted = PColor.FromHex("#D9A63F"),
        White        = PColor.FromHex("#FFFFFF"),
    };

    /// <summary>Every built-in theme, in registry order.</summary>
    public static IReadOnlyList<PanacheTheme> All { get; } = new[] { Default, Charcoal };

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
        foreach (var t in All)
        {
            if (t.Name == name) { SetActive(t); return; }
        }
    }

    /// <summary>Swap the active theme by reference. No-ops if already active.</summary>
    public static void SetActive(PanacheTheme theme)
    {
        if (ReferenceEquals(theme, _active)) return;
        _active = theme;
        ActiveChanged?.Invoke(theme);
    }
}
