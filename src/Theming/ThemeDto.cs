using Newtonsoft.Json;

namespace PanacheUI.Theming;

/// <summary>
/// JSON-friendly mirror of <see cref="PanacheTheme"/> (v3 schema). Every
/// field is optional; anything left <c>null</c> inherits from the theme
/// named in <see cref="BasedOn"/> (defaults to the built-in default) at
/// load time. This lets a user drop in a theme file that only overrides
/// <see cref="Primary"/> without listing all 66 slots.
/// </summary>
internal sealed class ThemeDto
{
    /// <summary>Display name. If null, the JSON filename (without extension) is used.</summary>
    public string? Name    { get; set; }

    /// <summary>Name of a registered theme (built-in or previously loaded) to
    /// inherit missing fields from. Defaults to the built-in default.</summary>
    [JsonProperty("basedOn")]
    public string? BasedOn { get; set; }

    // ── Surfaces (6) ────────────────────────────────────────────────────────
    public string? Surface0        { get; set; }
    public string? Surface1        { get; set; }
    public string? Surface2        { get; set; }
    public string? Surface3        { get; set; }
    public string? Surface4        { get; set; }
    public string? SurfaceInverse  { get; set; }

    // ── On-surface text (4) ─────────────────────────────────────────────────
    public string? OnSurfaceHi        { get; set; }
    public string? OnSurfaceMed       { get; set; }
    public string? OnSurfaceLow       { get; set; }
    public string? OnSurfaceDisabled  { get; set; }

    // ── Primary accent (7) ──────────────────────────────────────────────────
    public string? Primary             { get; set; }
    public string? PrimaryHover        { get; set; }
    public string? PrimaryPressed      { get; set; }
    public string? PrimaryDisabled     { get; set; }
    public string? OnPrimary           { get; set; }
    public string? PrimaryContainer    { get; set; }
    public string? OnPrimaryContainer  { get; set; }

    // ── Secondary accent (7) ────────────────────────────────────────────────
    public string? Secondary             { get; set; }
    public string? SecondaryHover        { get; set; }
    public string? SecondaryPressed      { get; set; }
    public string? SecondaryDisabled     { get; set; }
    public string? OnSecondary           { get; set; }
    public string? SecondaryContainer    { get; set; }
    public string? OnSecondaryContainer  { get; set; }

    // ── Tertiary signal (4) ─────────────────────────────────────────────────
    public string? Tertiary             { get; set; }
    public string? OnTertiary           { get; set; }
    public string? TertiaryContainer    { get; set; }
    public string? OnTertiaryContainer  { get; set; }

    // ── Semantic status quads (4 × 4 = 16) ──────────────────────────────────
    public string? Success             { get; set; }
    public string? SuccessContainer    { get; set; }
    public string? OnSuccess           { get; set; }
    public string? OnSuccessContainer  { get; set; }

    public string? Warning             { get; set; }
    public string? WarningContainer    { get; set; }
    public string? OnWarning           { get; set; }
    public string? OnWarningContainer  { get; set; }

    public string? Error               { get; set; }
    public string? ErrorContainer      { get; set; }
    public string? OnError             { get; set; }
    public string? OnErrorContainer    { get; set; }

    public string? Info                { get; set; }
    public string? InfoContainer       { get; set; }
    public string? OnInfo              { get; set; }
    public string? OnInfoContainer     { get; set; }

    // ── Borders (4) ─────────────────────────────────────────────────────────
    public string? BorderSubtle   { get; set; }
    public string? BorderDefault  { get; set; }
    public string? BorderStrong   { get; set; }
    public string? BorderFocus    { get; set; }

    // ── State layers (4) ────────────────────────────────────────────────────
    public string? StateHover     { get; set; }
    public string? StatePressed   { get; set; }
    public string? StateSelected  { get; set; }
    public string? StateFocused   { get; set; }

    // ── Rarity ladder (6) ───────────────────────────────────────────────────
    public string? RarityCommon      { get; set; }
    public string? RarityUncommon    { get; set; }
    public string? RarityRare        { get; set; }
    public string? RarityEpic        { get; set; }
    public string? RarityLegendary   { get; set; }
    public string? RarityMythic      { get; set; }

    // ── Glows (2) ───────────────────────────────────────────────────────────
    public string? GlowPrimary    { get; set; }
    public string? GlowSecondary  { get; set; }

    // ── Row highlights (6) ──────────────────────────────────────────────────
    public string? RowLocatedBg   { get; set; }
    public string? RowLocatedBd   { get; set; }
    public string? RowOwnedBg     { get; set; }
    public string? RowOwnedBd     { get; set; }
    public string? RowStoredBg    { get; set; }
    public string? RowStoredBd    { get; set; }
}
