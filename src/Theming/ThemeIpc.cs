using System;
using System.Collections.Generic;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;

namespace PanacheUI.Theming;

/// <summary>
/// Dalamud IPC surface for the Panache theme registry. Lets other plugins
/// enumerate available themes, read/set the active theme, and pull a single
/// theme's colors as a name→hex dictionary — all without taking an assembly
/// reference to PanacheUI.
///
/// <para>Endpoints (all prefixed <c>PanacheUI.Themes.</c>):</para>
/// <list type="bullet">
///   <item><c>List</c> — <c>() → string[]</c>: names of all registered themes.</item>
///   <item><c>GetActive</c> — <c>() → string</c>: name of the currently active theme.</item>
///   <item><c>SetActive</c> — <c>(string) → bool</c>: swap to the theme by name.
///     Returns false if the name is unknown.</item>
///   <item><c>Get</c> — <c>(string) → Dictionary&lt;string,string&gt;</c>: full color
///     palette for the named theme in the v3 vocabulary (keys camelCase — <c>surface0</c>,
///     <c>primary</c>, <c>secondaryContainer</c>, etc.; values are hex strings like
///     <c>"#E05C9A"</c>). Returns an empty dictionary if the name is unknown.</item>
///   <item><c>ActiveChanged</c> — publishes the new active theme's name whenever it
///     changes. Subscribe via
///     <c>pi.GetIpcSubscriber&lt;string, object&gt;("PanacheUI.Themes.ActiveChanged")</c>.</item>
/// </list>
///
/// <para>Direct in-process consumers should reference <c>PanacheUI.dll</c> and read
/// <see cref="PanacheThemes"/> directly — IPC is here for plugins that want
/// looser coupling.</para>
/// </summary>
public sealed class ThemeIpc : IDisposable
{
    private const string PrefixNs         = "PanacheUI.Themes.";
    private const string EndpointList     = PrefixNs + "List";
    private const string EndpointGetActive= PrefixNs + "GetActive";
    private const string EndpointSetActive= PrefixNs + "SetActive";
    private const string EndpointGet      = PrefixNs + "Get";
    private const string EndpointChanged  = PrefixNs + "ActiveChanged";

    private readonly ICallGateProvider<string[]>                           _list;
    private readonly ICallGateProvider<string>                             _getActive;
    private readonly ICallGateProvider<string, bool>                       _setActive;
    private readonly ICallGateProvider<string, Dictionary<string, string>> _get;
    private readonly ICallGateProvider<string, object>                     _changed;

    private readonly Action<PanacheTheme> _onActiveChanged;

    public ThemeIpc(IDalamudPluginInterface pi)
    {
        _list       = pi.GetIpcProvider<string[]>(EndpointList);
        _getActive  = pi.GetIpcProvider<string>(EndpointGetActive);
        _setActive  = pi.GetIpcProvider<string, bool>(EndpointSetActive);
        _get        = pi.GetIpcProvider<string, Dictionary<string, string>>(EndpointGet);
        _changed    = pi.GetIpcProvider<string, object>(EndpointChanged);

        _list.RegisterFunc(ListThemes);
        _getActive.RegisterFunc(() => PanacheThemes.Active.Name);
        _setActive.RegisterFunc(name =>
        {
            var t = PanacheThemes.Find(name);
            if (t == null) return false;
            PanacheThemes.SetActive(t);
            return true;
        });
        _get.RegisterFunc(GetThemeColors);

        _onActiveChanged = t =>
        {
            try { _changed.SendMessage(t.Name); }
            catch (Exception ex) { Plugin.Log.Warning(ex, "[PanacheUI] Failed to publish theme change via IPC."); }
        };
        PanacheThemes.ActiveChanged += _onActiveChanged;
    }

    private static string[] ListThemes()
    {
        var all    = PanacheThemes.All;
        var result = new string[all.Count];
        for (int i = 0; i < all.Count; i++) result[i] = all[i].Name;
        return result;
    }

    private static Dictionary<string, string> GetThemeColors(string name)
    {
        var t = PanacheThemes.Find(name);
        if (t == null) return new Dictionary<string, string>();
        return new Dictionary<string, string>
        {
            ["name"]                 = t.Name,

            // Surfaces
            ["surface0"]             = ToHex(t.Surface0),
            ["surface1"]             = ToHex(t.Surface1),
            ["surface2"]             = ToHex(t.Surface2),
            ["surface3"]             = ToHex(t.Surface3),
            ["surface4"]             = ToHex(t.Surface4),
            ["surfaceInverse"]       = ToHex(t.SurfaceInverse),

            // On-surface text
            ["onSurfaceHi"]          = ToHex(t.OnSurfaceHi),
            ["onSurfaceMed"]         = ToHex(t.OnSurfaceMed),
            ["onSurfaceLow"]         = ToHex(t.OnSurfaceLow),
            ["onSurfaceDisabled"]    = ToHex(t.OnSurfaceDisabled),

            // Primary
            ["primary"]              = ToHex(t.Primary),
            ["primaryHover"]         = ToHex(t.PrimaryHover),
            ["primaryPressed"]       = ToHex(t.PrimaryPressed),
            ["primaryDisabled"]      = ToHex(t.PrimaryDisabled),
            ["onPrimary"]            = ToHex(t.OnPrimary),
            ["primaryContainer"]     = ToHex(t.PrimaryContainer),
            ["onPrimaryContainer"]   = ToHex(t.OnPrimaryContainer),

            // Secondary
            ["secondary"]            = ToHex(t.Secondary),
            ["secondaryHover"]       = ToHex(t.SecondaryHover),
            ["secondaryPressed"]     = ToHex(t.SecondaryPressed),
            ["secondaryDisabled"]    = ToHex(t.SecondaryDisabled),
            ["onSecondary"]          = ToHex(t.OnSecondary),
            ["secondaryContainer"]   = ToHex(t.SecondaryContainer),
            ["onSecondaryContainer"] = ToHex(t.OnSecondaryContainer),

            // Tertiary
            ["tertiary"]             = ToHex(t.Tertiary),
            ["onTertiary"]           = ToHex(t.OnTertiary),
            ["tertiaryContainer"]    = ToHex(t.TertiaryContainer),
            ["onTertiaryContainer"]  = ToHex(t.OnTertiaryContainer),

            // Semantic status quads
            ["success"]              = ToHex(t.Success),
            ["successContainer"]     = ToHex(t.SuccessContainer),
            ["onSuccess"]            = ToHex(t.OnSuccess),
            ["onSuccessContainer"]   = ToHex(t.OnSuccessContainer),

            ["warning"]              = ToHex(t.Warning),
            ["warningContainer"]     = ToHex(t.WarningContainer),
            ["onWarning"]            = ToHex(t.OnWarning),
            ["onWarningContainer"]   = ToHex(t.OnWarningContainer),

            ["error"]                = ToHex(t.Error),
            ["errorContainer"]       = ToHex(t.ErrorContainer),
            ["onError"]              = ToHex(t.OnError),
            ["onErrorContainer"]     = ToHex(t.OnErrorContainer),

            ["info"]                 = ToHex(t.Info),
            ["infoContainer"]        = ToHex(t.InfoContainer),
            ["onInfo"]               = ToHex(t.OnInfo),
            ["onInfoContainer"]      = ToHex(t.OnInfoContainer),

            // Borders
            ["borderSubtle"]         = ToHex(t.BorderSubtle),
            ["borderDefault"]        = ToHex(t.BorderDefault),
            ["borderStrong"]         = ToHex(t.BorderStrong),
            ["borderFocus"]          = ToHex(t.BorderFocus),

            // State layers
            ["stateHover"]           = ToHex(t.StateHover),
            ["statePressed"]         = ToHex(t.StatePressed),
            ["stateSelected"]        = ToHex(t.StateSelected),
            ["stateFocused"]         = ToHex(t.StateFocused),

            // Rarity ladder
            ["rarityCommon"]         = ToHex(t.RarityCommon),
            ["rarityUncommon"]       = ToHex(t.RarityUncommon),
            ["rarityRare"]           = ToHex(t.RarityRare),
            ["rarityEpic"]           = ToHex(t.RarityEpic),
            ["rarityLegendary"]      = ToHex(t.RarityLegendary),
            ["rarityMythic"]         = ToHex(t.RarityMythic),

            // Glows
            ["glowPrimary"]          = ToHex(t.GlowPrimary),
            ["glowSecondary"]        = ToHex(t.GlowSecondary),

            // Row highlights
            ["rowLocatedBg"]         = ToHex(t.RowLocatedBg),
            ["rowLocatedBd"]         = ToHex(t.RowLocatedBd),
            ["rowOwnedBg"]           = ToHex(t.RowOwnedBg),
            ["rowOwnedBd"]           = ToHex(t.RowOwnedBd),
            ["rowStoredBg"]          = ToHex(t.RowStoredBg),
            ["rowStoredBd"]          = ToHex(t.RowStoredBd),
        };
    }

    private static string ToHex(PanacheUI.Core.PColor c)
        => c.A == 255
            ? $"#{c.R:X2}{c.G:X2}{c.B:X2}"
            : $"#{c.R:X2}{c.G:X2}{c.B:X2}{c.A:X2}";

    public void Dispose()
    {
        PanacheThemes.ActiveChanged -= _onActiveChanged;
        try { _list.UnregisterFunc(); }      catch { }
        try { _getActive.UnregisterFunc(); } catch { }
        try { _setActive.UnregisterFunc(); } catch { }
        try { _get.UnregisterFunc(); }       catch { }
    }
}
