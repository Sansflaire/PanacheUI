using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using PanacheUI.Core;

namespace PanacheUI.Theming;

/// <summary>
/// Scans a folder for JSON theme files and registers each one with
/// <see cref="PanacheThemes"/>. Users can drop a new <c>*.json</c> file into
/// the folder at any time — a <see cref="FileSystemWatcher"/> picks it up and
/// re-registers the theme set live.
///
/// <para>Each JSON is a <see cref="ThemeDto"/>: every field is optional and any
/// missing color inherits from the theme named in <c>BasedOn</c> (default:
/// the built-in default). That means a valid theme file can be as small as:
/// <code>
/// { "name": "MyOverride", "primary": "#5A8ADB" }
/// </code>
/// and the other ~65 slots are pulled from the base palette.</para>
///
/// <para>Schema: Panache v3. The 66-slot v3 vocabulary (Surface0..4, Primary
/// + variants, Secondary + variants, Tertiary, Success/Warning/Error/Info +
/// containers, Border*, State*, Rarity*, GlowPrimary/Secondary,
/// RowLocated/Owned/Stored) is the only shape accepted. Old v2 exports
/// (flat Base/Panel/Accent slot set) are ignored — the loader will log a
/// warning and skip them.</para>
///
/// <para>Load errors are logged and skipped — one broken file does not prevent
/// the rest from loading.</para>
/// </summary>
public sealed class ThemeLoader : IDisposable
{
    private const string ReadmeFileName = "README.txt";

    /// <summary>Absolute path of the folder this loader watches.</summary>
    public string FolderPath { get; }

    private FileSystemWatcher?      _watcher;
    private System.Threading.Timer? _debounce;
    private readonly object         _debounceGate = new();

    public ThemeLoader(string folderPath)
    {
        FolderPath = folderPath;
        Directory.CreateDirectory(FolderPath);
        WriteReadmeIfMissing();

        Reload();

        try
        {
            _watcher = new FileSystemWatcher(FolderPath, "*.json")
            {
                NotifyFilter = NotifyFilters.LastWrite
                             | NotifyFilters.FileName
                             | NotifyFilters.CreationTime,
                // Watch subfolders too so ColorSchemeCreator's
                // <ThemeName>/theme.json exports are picked up on drop.
                IncludeSubdirectories = true,
                EnableRaisingEvents   = true,
            };
            _watcher.Changed += OnFsEvent;
            _watcher.Created += OnFsEvent;
            _watcher.Deleted += OnFsEvent;
            _watcher.Renamed += OnFsEvent;
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning(ex, $"[PanacheUI] Could not watch themes folder {FolderPath}. Themes will still load on plugin start.");
        }
    }

    private void OnFsEvent(object sender, FileSystemEventArgs e)
    {
        // FileSystemWatcher fires multiple events per save (and editors often
        // write atomically via rename). Debounce by scheduling the reload for
        // 200 ms after the last event so we only re-scan once per quiet burst.
        lock (_debounceGate)
        {
            _debounce ??= new System.Threading.Timer(_ => TryReload(), null, System.Threading.Timeout.Infinite, System.Threading.Timeout.Infinite);
            _debounce.Change(200, System.Threading.Timeout.Infinite);
        }
    }

    private void TryReload()
    {
        try { Reload(); }
        catch (Exception ex) { Plugin.Log.Warning(ex, "[PanacheUI] Theme reload failed"); }
    }

    /// <summary>Re-scan the folder and replace the external theme set on
    /// <see cref="PanacheThemes"/>. Safe to call repeatedly.
    ///
    /// <para>Scans two shapes:</para>
    /// <list type="bullet">
    ///   <item><c>Themes/*.json</c> — flat single-file themes (Panache's own
    ///     format; every field optional, inherits from <c>basedOn</c>).</item>
    ///   <item><c>Themes/*/theme.json</c> — ColorSchemeCreator v3 exports:
    ///     <c>{ "meta": { "name": ... }, "slots": { "Surface0": { "hex": ... }, ... } }</c>.
    ///     The theme is named after <c>meta.name</c> or the containing folder if absent.</item>
    /// </list></summary>
    public void Reload()
    {
        var loaded = new List<PanacheTheme>();
        if (!Directory.Exists(FolderPath))
        {
            PanacheThemes.SetExternal(loaded);
            return;
        }

        // Flat: Themes/*.json (Panache's own partial-override format)
        foreach (var file in Directory.EnumerateFiles(FolderPath, "*.json"))
            TryLoadFile(file, fallbackName: Path.GetFileNameWithoutExtension(file), loaded);

        // Nested: Themes/<ThemeName>/theme.json (ColorSchemeCreator export shape)
        foreach (var sub in Directory.EnumerateDirectories(FolderPath))
        {
            var themeFile = Path.Combine(sub, "theme.json");
            if (File.Exists(themeFile))
                TryLoadFile(themeFile, fallbackName: Path.GetFileName(sub), loaded);
        }

        PanacheThemes.SetExternal(loaded);
        Plugin.Log.Info($"[PanacheUI] Themes reloaded — {loaded.Count} from folder, {PanacheThemes.All.Count} total registered.");
    }

    private static void TryLoadFile(string file, string fallbackName, List<PanacheTheme> loaded)
    {
        try
        {
            var text = File.ReadAllText(file);
            var root = JToken.Parse(text) as JObject;
            if (root == null)
            {
                Plugin.Log.Warning($"[PanacheUI] Theme file did not parse as an object: {file}");
                return;
            }

            // Reject v2 (or older) exports — Panache is v3-only. Detect by
            // schema_version if present; otherwise sniff by the presence of a
            // legacy V1 slot key (Base) with no v3 slot (Surface0).
            var schema = root["meta"]?["schema_version"]?.ToString();
            var slots  = root["slots"] as JObject;
            var isV3   = slots != null && slots["Surface0"] != null;
            var isLegacyV2 = slots != null && slots["Base"] != null && slots["Surface0"] == null;
            if (isLegacyV2 || (schema != null && !string.Equals(schema, "3.0", StringComparison.Ordinal) && !isV3))
            {
                Plugin.Log.Warning(
                    $"[PanacheUI] Skipping legacy (pre-v3) theme file: {file} " +
                    $"(schema_version={schema ?? "unset"}). Re-export from ColorSchemeCreator v2+ with the v3 slot set.");
                return;
            }

            var dto = slots != null
                ? DtoFromSlotted(root, slots)   // ColorSchemeCreator v3 export
                : root.ToObject<ThemeDto>();    // Flat / partial-override

            if (dto == null)
            {
                Plugin.Log.Warning($"[PanacheUI] Theme file produced no data: {file}");
                return;
            }

            var name = string.IsNullOrWhiteSpace(dto.Name) ? fallbackName : dto.Name!;
            var basedOn = ResolveBase(dto.BasedOn);
            loaded.Add(Merge(name, dto, basedOn));
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning(ex, $"[PanacheUI] Failed to load theme file: {file}");
        }
    }

    // ── ColorSchemeCreator v3 format ────────────────────────────────────────
    //
    // The helper program exports themes as a folder per theme with a
    // theme.json inside:
    //
    //   {
    //     "meta":  { "name": "Shadow", "schema_version": "3.0", ... },
    //     "slots": { "Surface0": { "hex": "#0F0E12", "rgb": [...], "hsl": [...],
    //                              "oklch": { "L": ..., "C": ..., "H": ... } },
    //                "Primary":  { ... }, ... }
    //   }
    //
    // Slot keys are PascalCase and map 1:1 to PanacheTheme fields; only the
    // "hex" child is consumed (rgb / hsl / oklch are informational).
    private static ThemeDto DtoFromSlotted(JObject root, JObject slots)
    {
        var dto = new ThemeDto
        {
            Name    = root["meta"]?["name"]?.ToString(),
            BasedOn = root["meta"]?["basedOn"]?.ToString(),
        };

        // Every PanacheTheme v3 field. Assignments are only made when the
        // slot is present with a hex — any missing slot silently inherits
        // from the resolved base theme at merge time.
        Assign(slots, "Surface0",             v => dto.Surface0             = v);
        Assign(slots, "Surface1",             v => dto.Surface1             = v);
        Assign(slots, "Surface2",             v => dto.Surface2             = v);
        Assign(slots, "Surface3",             v => dto.Surface3             = v);
        Assign(slots, "Surface4",             v => dto.Surface4             = v);
        Assign(slots, "SurfaceInverse",       v => dto.SurfaceInverse       = v);

        Assign(slots, "OnSurfaceHi",          v => dto.OnSurfaceHi          = v);
        Assign(slots, "OnSurfaceMed",         v => dto.OnSurfaceMed         = v);
        Assign(slots, "OnSurfaceLow",         v => dto.OnSurfaceLow         = v);
        Assign(slots, "OnSurfaceDisabled",    v => dto.OnSurfaceDisabled    = v);

        Assign(slots, "Primary",              v => dto.Primary              = v);
        Assign(slots, "PrimaryHover",         v => dto.PrimaryHover         = v);
        Assign(slots, "PrimaryPressed",       v => dto.PrimaryPressed       = v);
        Assign(slots, "PrimaryDisabled",      v => dto.PrimaryDisabled      = v);
        Assign(slots, "OnPrimary",            v => dto.OnPrimary            = v);
        Assign(slots, "PrimaryContainer",     v => dto.PrimaryContainer     = v);
        Assign(slots, "OnPrimaryContainer",   v => dto.OnPrimaryContainer   = v);

        Assign(slots, "Secondary",            v => dto.Secondary            = v);
        Assign(slots, "SecondaryHover",       v => dto.SecondaryHover       = v);
        Assign(slots, "SecondaryPressed",     v => dto.SecondaryPressed     = v);
        Assign(slots, "SecondaryDisabled",    v => dto.SecondaryDisabled    = v);
        Assign(slots, "OnSecondary",          v => dto.OnSecondary          = v);
        Assign(slots, "SecondaryContainer",   v => dto.SecondaryContainer   = v);
        Assign(slots, "OnSecondaryContainer", v => dto.OnSecondaryContainer = v);

        Assign(slots, "Tertiary",             v => dto.Tertiary             = v);
        Assign(slots, "OnTertiary",           v => dto.OnTertiary           = v);
        Assign(slots, "TertiaryContainer",    v => dto.TertiaryContainer    = v);
        Assign(slots, "OnTertiaryContainer",  v => dto.OnTertiaryContainer  = v);

        Assign(slots, "Success",              v => dto.Success              = v);
        Assign(slots, "SuccessContainer",     v => dto.SuccessContainer     = v);
        Assign(slots, "OnSuccess",            v => dto.OnSuccess            = v);
        Assign(slots, "OnSuccessContainer",   v => dto.OnSuccessContainer   = v);

        Assign(slots, "Warning",              v => dto.Warning              = v);
        Assign(slots, "WarningContainer",     v => dto.WarningContainer     = v);
        Assign(slots, "OnWarning",            v => dto.OnWarning            = v);
        Assign(slots, "OnWarningContainer",   v => dto.OnWarningContainer   = v);

        Assign(slots, "Error",                v => dto.Error                = v);
        Assign(slots, "ErrorContainer",       v => dto.ErrorContainer       = v);
        Assign(slots, "OnError",              v => dto.OnError              = v);
        Assign(slots, "OnErrorContainer",     v => dto.OnErrorContainer     = v);

        Assign(slots, "Info",                 v => dto.Info                 = v);
        Assign(slots, "InfoContainer",        v => dto.InfoContainer        = v);
        Assign(slots, "OnInfo",               v => dto.OnInfo               = v);
        Assign(slots, "OnInfoContainer",      v => dto.OnInfoContainer      = v);

        Assign(slots, "BorderSubtle",         v => dto.BorderSubtle         = v);
        Assign(slots, "BorderDefault",        v => dto.BorderDefault        = v);
        Assign(slots, "BorderStrong",         v => dto.BorderStrong         = v);
        Assign(slots, "BorderFocus",          v => dto.BorderFocus          = v);

        Assign(slots, "StateHover",           v => dto.StateHover           = v);
        Assign(slots, "StatePressed",         v => dto.StatePressed         = v);
        Assign(slots, "StateSelected",        v => dto.StateSelected        = v);
        Assign(slots, "StateFocused",         v => dto.StateFocused         = v);

        Assign(slots, "RarityCommon",         v => dto.RarityCommon         = v);
        Assign(slots, "RarityUncommon",       v => dto.RarityUncommon       = v);
        Assign(slots, "RarityRare",           v => dto.RarityRare           = v);
        Assign(slots, "RarityEpic",           v => dto.RarityEpic           = v);
        Assign(slots, "RarityLegendary",      v => dto.RarityLegendary      = v);
        Assign(slots, "RarityMythic",         v => dto.RarityMythic         = v);

        Assign(slots, "GlowPrimary",          v => dto.GlowPrimary          = v);
        Assign(slots, "GlowSecondary",        v => dto.GlowSecondary        = v);

        Assign(slots, "RowLocatedBg",         v => dto.RowLocatedBg         = v);
        Assign(slots, "RowLocatedBd",         v => dto.RowLocatedBd         = v);
        Assign(slots, "RowOwnedBg",           v => dto.RowOwnedBg           = v);
        Assign(slots, "RowOwnedBd",           v => dto.RowOwnedBd           = v);
        Assign(slots, "RowStoredBg",          v => dto.RowStoredBg          = v);
        Assign(slots, "RowStoredBd",          v => dto.RowStoredBd          = v);
        return dto;
    }

    private static void Assign(JObject slots, string key, Action<string> setter)
    {
        // The slot may be either { "hex": "#..." } (CSC export) or a bare
        // "#..." string (permissive — accept a flattened form too).
        if (slots[key] is JObject obj && obj["hex"]?.ToString() is { Length: > 0 } h)
            setter(h);
        else if (slots[key]?.Type == JTokenType.String && slots[key]!.ToString() is { Length: > 0 } s)
            setter(s);
    }

    private static PanacheTheme ResolveBase(string? name)
    {
        if (!string.IsNullOrWhiteSpace(name))
        {
            var found = PanacheThemes.Find(name!);
            if (found != null) return found;
        }
        return PanacheThemes.Default;
    }

    private static PanacheTheme Merge(string name, ThemeDto dto, PanacheTheme baseTheme)
    {
        return new PanacheTheme
        {
            Name                 = name,
            Surface0             = Pick(dto.Surface0,             baseTheme.Surface0),
            Surface1             = Pick(dto.Surface1,             baseTheme.Surface1),
            Surface2             = Pick(dto.Surface2,             baseTheme.Surface2),
            Surface3             = Pick(dto.Surface3,             baseTheme.Surface3),
            Surface4             = Pick(dto.Surface4,             baseTheme.Surface4),
            SurfaceInverse       = Pick(dto.SurfaceInverse,       baseTheme.SurfaceInverse),

            OnSurfaceHi          = Pick(dto.OnSurfaceHi,          baseTheme.OnSurfaceHi),
            OnSurfaceMed         = Pick(dto.OnSurfaceMed,         baseTheme.OnSurfaceMed),
            OnSurfaceLow         = Pick(dto.OnSurfaceLow,         baseTheme.OnSurfaceLow),
            OnSurfaceDisabled    = Pick(dto.OnSurfaceDisabled,    baseTheme.OnSurfaceDisabled),

            Primary              = Pick(dto.Primary,              baseTheme.Primary),
            PrimaryHover         = Pick(dto.PrimaryHover,         baseTheme.PrimaryHover),
            PrimaryPressed       = Pick(dto.PrimaryPressed,       baseTheme.PrimaryPressed),
            PrimaryDisabled      = Pick(dto.PrimaryDisabled,      baseTheme.PrimaryDisabled),
            OnPrimary            = Pick(dto.OnPrimary,            baseTheme.OnPrimary),
            PrimaryContainer     = Pick(dto.PrimaryContainer,     baseTheme.PrimaryContainer),
            OnPrimaryContainer   = Pick(dto.OnPrimaryContainer,   baseTheme.OnPrimaryContainer),

            Secondary            = Pick(dto.Secondary,            baseTheme.Secondary),
            SecondaryHover       = Pick(dto.SecondaryHover,       baseTheme.SecondaryHover),
            SecondaryPressed     = Pick(dto.SecondaryPressed,     baseTheme.SecondaryPressed),
            SecondaryDisabled    = Pick(dto.SecondaryDisabled,    baseTheme.SecondaryDisabled),
            OnSecondary          = Pick(dto.OnSecondary,          baseTheme.OnSecondary),
            SecondaryContainer   = Pick(dto.SecondaryContainer,   baseTheme.SecondaryContainer),
            OnSecondaryContainer = Pick(dto.OnSecondaryContainer, baseTheme.OnSecondaryContainer),

            Tertiary             = Pick(dto.Tertiary,             baseTheme.Tertiary),
            OnTertiary           = Pick(dto.OnTertiary,           baseTheme.OnTertiary),
            TertiaryContainer    = Pick(dto.TertiaryContainer,    baseTheme.TertiaryContainer),
            OnTertiaryContainer  = Pick(dto.OnTertiaryContainer,  baseTheme.OnTertiaryContainer),

            Success              = Pick(dto.Success,              baseTheme.Success),
            SuccessContainer     = Pick(dto.SuccessContainer,     baseTheme.SuccessContainer),
            OnSuccess            = Pick(dto.OnSuccess,            baseTheme.OnSuccess),
            OnSuccessContainer   = Pick(dto.OnSuccessContainer,   baseTheme.OnSuccessContainer),

            Warning              = Pick(dto.Warning,              baseTheme.Warning),
            WarningContainer     = Pick(dto.WarningContainer,     baseTheme.WarningContainer),
            OnWarning            = Pick(dto.OnWarning,            baseTheme.OnWarning),
            OnWarningContainer   = Pick(dto.OnWarningContainer,   baseTheme.OnWarningContainer),

            Error                = Pick(dto.Error,                baseTheme.Error),
            ErrorContainer       = Pick(dto.ErrorContainer,       baseTheme.ErrorContainer),
            OnError              = Pick(dto.OnError,              baseTheme.OnError),
            OnErrorContainer     = Pick(dto.OnErrorContainer,     baseTheme.OnErrorContainer),

            Info                 = Pick(dto.Info,                 baseTheme.Info),
            InfoContainer        = Pick(dto.InfoContainer,        baseTheme.InfoContainer),
            OnInfo               = Pick(dto.OnInfo,               baseTheme.OnInfo),
            OnInfoContainer      = Pick(dto.OnInfoContainer,      baseTheme.OnInfoContainer),

            BorderSubtle         = Pick(dto.BorderSubtle,         baseTheme.BorderSubtle),
            BorderDefault        = Pick(dto.BorderDefault,        baseTheme.BorderDefault),
            BorderStrong         = Pick(dto.BorderStrong,         baseTheme.BorderStrong),
            BorderFocus          = Pick(dto.BorderFocus,          baseTheme.BorderFocus),

            StateHover           = Pick(dto.StateHover,           baseTheme.StateHover),
            StatePressed         = Pick(dto.StatePressed,         baseTheme.StatePressed),
            StateSelected        = Pick(dto.StateSelected,        baseTheme.StateSelected),
            StateFocused         = Pick(dto.StateFocused,         baseTheme.StateFocused),

            RarityCommon         = Pick(dto.RarityCommon,         baseTheme.RarityCommon),
            RarityUncommon       = Pick(dto.RarityUncommon,       baseTheme.RarityUncommon),
            RarityRare           = Pick(dto.RarityRare,           baseTheme.RarityRare),
            RarityEpic           = Pick(dto.RarityEpic,           baseTheme.RarityEpic),
            RarityLegendary      = Pick(dto.RarityLegendary,      baseTheme.RarityLegendary),
            RarityMythic         = Pick(dto.RarityMythic,         baseTheme.RarityMythic),

            GlowPrimary          = Pick(dto.GlowPrimary,          baseTheme.GlowPrimary),
            GlowSecondary        = Pick(dto.GlowSecondary,        baseTheme.GlowSecondary),

            RowLocatedBg         = Pick(dto.RowLocatedBg,         baseTheme.RowLocatedBg),
            RowLocatedBd         = Pick(dto.RowLocatedBd,         baseTheme.RowLocatedBd),
            RowOwnedBg           = Pick(dto.RowOwnedBg,           baseTheme.RowOwnedBg),
            RowOwnedBd           = Pick(dto.RowOwnedBd,           baseTheme.RowOwnedBd),
            RowStoredBg          = Pick(dto.RowStoredBg,          baseTheme.RowStoredBg),
            RowStoredBd          = Pick(dto.RowStoredBd,          baseTheme.RowStoredBd),
        };
    }

    private static PColor Pick(string? hex, PColor fallback)
        => string.IsNullOrWhiteSpace(hex) ? fallback : PColor.FromHex(hex!);

    private void WriteReadmeIfMissing()
    {
        var path = Path.Combine(FolderPath, ReadmeFileName);
        if (File.Exists(path)) return;
        try
        {
            File.WriteAllText(path,
                "Panache Color Themes\n" +
                "====================\n\n" +
                "Panache watches this folder and reloads themes live — no game restart needed.\n" +
                "Schema: Panache v3. Older v2 (flat Base/Panel/Accent) exports are ignored.\n\n" +
                "── 1. ColorSchemeCreator v3 export (folder per theme) ───────────────────\n\n" +
                "Drop the whole theme folder in. Panache expects <ThemeName>/theme.json:\n\n" +
                "  Themes/\n" +
                "    Shadow/\n" +
                "      theme.json          <-- required\n" +
                "      preview.png         <-- ignored (kept for your reference)\n" +
                "      rules_report.txt    <-- ignored\n\n" +
                "The JSON is the exact output of ColorSchemeCreator v3:\n\n" +
                "  {\n" +
                "    \"meta\":  { \"name\": \"Shadow\", \"schema_version\": \"3.0\", ... },\n" +
                "    \"slots\": {\n" +
                "      \"Surface0\": { \"hex\": \"#0F0E12\", \"rgb\": [...], \"hsl\": [...],\n" +
                "                     \"oklch\": { \"L\": 0.20, \"C\": 0.01, \"H\": 300 } },\n" +
                "      \"Primary\":  { \"hex\": \"#E05C9A\", ... },\n" +
                "      ...\n" +
                "    }\n" +
                "  }\n\n" +
                "Slot keys are PascalCase and map 1:1 to Panache's 66 v3 color slots.\n" +
                "Only the \"hex\" child of each slot is consumed; rgb/hsl/oklch are\n" +
                "informational. If meta.name is missing the folder name is used.\n\n" +
                "── 2. Flat single-file theme (Panache's own format) ─────────────────────\n\n" +
                "Drop a *.json directly in this folder. Every field is optional; anything\n" +
                "you leave out inherits from the theme named in `basedOn` (default:\n" +
                "the built-in default). A valid file can be as small as:\n\n" +
                "  {\n" +
                "    \"name\": \"ShadowRed\",\n" +
                "    \"basedOn\": \"Shadow\",\n" +
                "    \"primary\": \"#FF3060\"\n" +
                "  }\n\n" +
                "Colors are hex strings: #RGB, #RRGGBB, or #RRGGBBAA.\n\n" +
                "── The 66 v3 slots ───────────────────────────────────────────────────────\n\n" +
                "  Surfaces      : Surface0..4, SurfaceInverse\n" +
                "  On-surface    : OnSurfaceHi, OnSurfaceMed, OnSurfaceLow, OnSurfaceDisabled\n" +
                "  Primary       : Primary, PrimaryHover, PrimaryPressed, PrimaryDisabled,\n" +
                "                  OnPrimary, PrimaryContainer, OnPrimaryContainer\n" +
                "  Secondary     : Secondary + Hover/Pressed/Disabled + OnSecondary +\n" +
                "                  SecondaryContainer + OnSecondaryContainer\n" +
                "  Tertiary      : Tertiary, OnTertiary, TertiaryContainer, OnTertiaryContainer\n" +
                "  Success       : Success, SuccessContainer, OnSuccess, OnSuccessContainer\n" +
                "  Warning       : Warning, WarningContainer, OnWarning, OnWarningContainer\n" +
                "  Error         : Error, ErrorContainer, OnError, OnErrorContainer\n" +
                "  Info          : Info, InfoContainer, OnInfo, OnInfoContainer\n" +
                "  Borders       : BorderSubtle, BorderDefault, BorderStrong, BorderFocus\n" +
                "  State layers  : StateHover, StatePressed, StateSelected, StateFocused\n" +
                "  Rarity        : RarityCommon, RarityUncommon, RarityRare, RarityEpic,\n" +
                "                  RarityLegendary, RarityMythic\n" +
                "  Glows         : GlowPrimary, GlowSecondary\n" +
                "  Rows          : RowLocatedBg/Bd, RowOwnedBg/Bd, RowStoredBg/Bd\n\n" +
                "Full theory + slot semantics + validation rules are documented in\n" +
                "../docs/COLOR_THEORY_AND_THEMES.md — read that first if you plan to\n" +
                "author a theme by hand.\n\n" +
                "External themes with the same name as the built-in \"Shadow\" override\n" +
                "it. Other plugins can consume this registry via:\n" +
                "  - HTTP:        GET http://localhost:17779/themes, /themes/{name}, /themes/active\n" +
                "  - Dalamud IPC: PanacheUI.Themes.List / .Get / .GetActive / .SetActive\n" +
                "  - Direct:      reference PanacheUI.dll and read PanacheThemes.All\n");
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning(ex, "[PanacheUI] Could not write themes README.");
        }
    }

    public void Dispose()
    {
        if (_watcher != null)
        {
            _watcher.EnableRaisingEvents = false;
            _watcher.Changed -= OnFsEvent;
            _watcher.Created -= OnFsEvent;
            _watcher.Deleted -= OnFsEvent;
            _watcher.Renamed -= OnFsEvent;
            _watcher.Dispose();
            _watcher = null;
        }
        lock (_debounceGate)
        {
            _debounce?.Dispose();
            _debounce = null;
        }
    }
}
