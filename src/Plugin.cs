using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using PanacheUI.Theming;
using PanacheUI.Windows;

namespace PanacheUI;

public sealed class Plugin : IDalamudPlugin
{
    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static IPluginLog              Log             { get; private set; } = null!;
    [PluginService] internal static ICommandManager         CommandManager  { get; private set; } = null!;
    [PluginService] internal static ITextureProvider        TextureProvider { get; private set; } = null!;
    [PluginService] internal static IChatGui                Chat            { get; private set; } = null!;

    private readonly DemoWindow        _demo;
    private readonly HelpWindow        _help;
    private readonly EffectLabWindow   _lab;
    private readonly IconBrowserWindow _iconBrowser;
    private readonly RenderApi         _api;
    private readonly ThemeLoader       _themes;
    private readonly ThemeIpc          _themeIpc;

    public Plugin()
    {
        _themes   = new ThemeLoader(ResolveThemesFolder());
        _themeIpc = new ThemeIpc(PluginInterface);

        _help        = new HelpWindow(TextureProvider);
        _demo        = new DemoWindow(TextureProvider, _help, OpenIconBrowser);
        _lab         = new EffectLabWindow(TextureProvider);
        _iconBrowser = new IconBrowserWindow(TextureProvider);
        _api         = new RenderApi();

        PluginInterface.UiBuilder.Draw      += OnDraw;
        PluginInterface.UiBuilder.OpenMainUi += OnOpenMainUi;

        CommandManager.AddHandler("/panacheui", new Dalamud.Game.Command.CommandInfo(OnCommand)
        {
            HelpMessage = "Open the PanacheUI demo. Subcommands: help, lab, icons, icons list, stats, themes, theme <name>, refresh."
        });
        CommandManager.AddHandler("/panacheui help", new Dalamud.Game.Command.CommandInfo(OnHelpCommand)
        {
            HelpMessage = "Open the PanacheUI feature reference / help window."
        });
        CommandManager.AddHandler("/panacheui lab", new Dalamud.Game.Command.CommandInfo(OnLabCommand)
        {
            HelpMessage = "Open the PanacheUI Effect Lab — live parameter tuning for effects."
        });
        CommandManager.AddHandler("/panacheui icons", new Dalamud.Game.Command.CommandInfo(OnIconsCommand)
        {
            HelpMessage = "Open the PanacheUI Icon Browser — every bundled icon, scrollable, at a chosen scale."
        });

        Log.Info($"PanacheUI loaded. Themes folder: {_themes.FolderPath}");
    }

    /// <summary>Locate the git-tracked <c>Themes</c> folder, which sits directly beside
    /// the plugin assembly at <c>devPlugins\PanacheUI\Themes\</c>.
    ///
    /// <para>This used to hunt for a sibling <c>devPlugins\Panache\</c> source folder,
    /// back when the repo and the loaded plugin lived in two different directories. They
    /// are now one folder, so the themes are simply next to the DLL. Falls back to the
    /// plugin config directory if the assembly location can't be determined.</para></summary>
    private static string ResolveThemesFolder()
    {
        try
        {
            // Dalamud loads plugins from a shadow-copy location, so
            // Assembly.Location isn't reliable — use PluginInterface.AssemblyLocation
            // (Dalamud's canonical answer for "where does this plugin live").
            var dllDir = PluginInterface.AssemblyLocation.Directory?.FullName;
            if (!string.IsNullOrEmpty(dllDir))
                return Path.Combine(dllDir, "Themes");
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[PanacheUI] Could not resolve themes folder via assembly location; using config fallback.");
        }
        return Path.Combine(PluginInterface.ConfigDirectory.FullName, "Themes");
    }

    private void OnDraw()
    {
        // Host frame timing, sampled on the draw thread — the denominator for
        // "what fraction of the frame is Panache costing?" in /stats.
        var io = Dalamud.Bindings.ImGui.ImGui.GetIO();
        Diagnostics.PanacheStats.ReportFrame(io.Framerate, io.DeltaTime);

        _demo.Draw();
        _help.Draw();
        _lab.Draw();
        _iconBrowser.Draw();
    }

    private void OpenIconBrowser() => _iconBrowser.IsVisible = true;

    private void OnOpenMainUi()   => _demo.IsVisible = true;

    private void OnCommand(string command, string args)
    {
        var trimmed = args.Trim();
        if (trimmed.Length == 0)
        {
            _demo.IsVisible = !_demo.IsVisible;
            return;
        }

        // "help" / "lab" open their respective windows (kept for backward compat).
        if (trimmed.Equals("help", StringComparison.OrdinalIgnoreCase))
        {
            _help.IsVisible = !_help.IsVisible;
            return;
        }
        if (trimmed.Equals("lab", StringComparison.OrdinalIgnoreCase))
        {
            _lab.IsVisible = !_lab.IsVisible;
            return;
        }

        // Live cost readout — what Panache is charging you per frame, across every plugin.
        if (trimmed.Equals("stats", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var line in Diagnostics.PanacheStats.ToChatLines())
                Chat.Print(line);
            return;
        }
        if (trimmed.Equals("stats reset", StringComparison.OrdinalIgnoreCase))
        {
            Diagnostics.PanacheStats.ResetAll();
            Chat.Print("[Panache] Stats counters reset for this plugin's surfaces.");
            return;
        }

        if (trimmed.Equals("icons", StringComparison.OrdinalIgnoreCase))
        {
            _iconBrowser.IsVisible = !_iconBrowser.IsVisible;
            return;
        }
        if (trimmed.Equals("icons list", StringComparison.OrdinalIgnoreCase))
        {
            PrintIconsList();
            return;
        }

        // Theme subcommands: "themes" (list), "theme <name>" (switch), "refresh" (rescan).
        if (trimmed.Equals("themes", StringComparison.OrdinalIgnoreCase))
        {
            PrintThemesList();
            return;
        }
        if (trimmed.Equals("refresh", StringComparison.OrdinalIgnoreCase))
        {
            _themes.Reload();
            Chat.Print($"[Panache] Themes refreshed — {PanacheThemes.All.Count} registered.");
            return;
        }
        if (trimmed.StartsWith("theme ", StringComparison.OrdinalIgnoreCase))
        {
            var name = trimmed.Substring("theme ".Length).Trim();
            var found = PanacheThemes.Find(name);
            if (found == null)
            {
                Chat.PrintError($"[Panache] No theme named \"{name}\". Try /panacheui themes for the list.");
                return;
            }
            PanacheThemes.SetActive(found);
            Chat.Print($"[Panache] Active theme: {found.Name}");
            return;
        }

        _demo.IsVisible = !_demo.IsVisible;
    }

    private void PrintIconsList()
    {
        var ids = Icons.PanacheIcons.AllIds();
        Chat.Print($"[Panache] {ids.Count} icon(s) in {Icons.PanacheIcons.IconsFolder}");
        if (ids.Count > 0)
            Chat.Print("  " + string.Join(", ", ids.Select(i => $"#{i:0000}")));
    }

    private void PrintThemesList()
    {
        var active = PanacheThemes.Active.Name;
        Chat.Print($"[Panache] {PanacheThemes.All.Count} theme(s):");
        foreach (var t in PanacheThemes.All)
        {
            var marker = string.Equals(t.Name, active, StringComparison.OrdinalIgnoreCase) ? "▶ " : "  ";
            var source = PanacheThemes.IsBuiltIn(t) ? "builtin" : "folder";
            Chat.Print($"  {marker}{t.Name}  ({source})");
        }
        Chat.Print($"Themes folder: {_themes.FolderPath}");
    }

    private void OnHelpCommand(string command, string args)  => _help.IsVisible = !_help.IsVisible;
    private void OnLabCommand(string command, string args)   => _lab.IsVisible  = !_lab.IsVisible;
    private void OnIconsCommand(string command, string args) => _iconBrowser.IsVisible = !_iconBrowser.IsVisible;

    public void Dispose()
    {
        CommandManager.RemoveHandler("/panacheui");
        CommandManager.RemoveHandler("/panacheui help");
        CommandManager.RemoveHandler("/panacheui lab");
        CommandManager.RemoveHandler("/panacheui icons");
        PluginInterface.UiBuilder.Draw      -= OnDraw;
        PluginInterface.UiBuilder.OpenMainUi -= OnOpenMainUi;
        _demo.Dispose();
        _help.Dispose();
        _lab.Dispose();
        _iconBrowser.Dispose();
        _api.Dispose();
        _themeIpc.Dispose();
        _themes.Dispose();
        Log.Info("PanacheUI unloaded.");
    }
}
