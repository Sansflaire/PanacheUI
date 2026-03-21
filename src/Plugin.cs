using System;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using PanacheUI.Windows;

namespace PanacheUI;

public sealed class Plugin : IDalamudPlugin
{
    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static IPluginLog              Log             { get; private set; } = null!;
    [PluginService] internal static ICommandManager         CommandManager  { get; private set; } = null!;
    [PluginService] internal static ITextureProvider        TextureProvider { get; private set; } = null!;

    private readonly DemoWindow      _demo;
    private readonly HelpWindow      _help;
    private readonly EffectLabWindow _lab;
    private readonly RenderApi       _api;

    public Plugin()
    {
        _help = new HelpWindow(TextureProvider);
        _demo = new DemoWindow(TextureProvider, _help);
        _lab  = new EffectLabWindow(TextureProvider);
        _api  = new RenderApi();

        PluginInterface.UiBuilder.Draw      += OnDraw;
        PluginInterface.UiBuilder.OpenMainUi += OnOpenMainUi;

        CommandManager.AddHandler("/panacheui", new Dalamud.Game.Command.CommandInfo(OnCommand)
        {
            HelpMessage = "Open the PanacheUI UI framework demo window."
        });
        CommandManager.AddHandler("/panacheui help", new Dalamud.Game.Command.CommandInfo(OnHelpCommand)
        {
            HelpMessage = "Open the PanacheUI feature reference / help window."
        });
        CommandManager.AddHandler("/panacheui lab", new Dalamud.Game.Command.CommandInfo(OnLabCommand)
        {
            HelpMessage = "Open the PanacheUI Effect Lab — live parameter tuning for effects."
        });

        Log.Info("PanacheUI loaded. /panacheui to open the demo, /panacheui help for feature reference.");
    }

    private void OnDraw()
    {
        _demo.Draw();
        _help.Draw();
        _lab.Draw();
    }

    private void OnOpenMainUi()   => _demo.IsVisible = true;

    private void OnCommand(string command, string args)
    {
        if (args.Trim().Equals("help", StringComparison.OrdinalIgnoreCase))
            _help.IsVisible = !_help.IsVisible;
        else
            _demo.IsVisible = !_demo.IsVisible;
    }

    private void OnHelpCommand(string command, string args) => _help.IsVisible = !_help.IsVisible;
    private void OnLabCommand(string command, string args)  => _lab.IsVisible  = !_lab.IsVisible;

    public void Dispose()
    {
        CommandManager.RemoveHandler("/panacheui");
        CommandManager.RemoveHandler("/panacheui help");
        CommandManager.RemoveHandler("/panacheui lab");
        PluginInterface.UiBuilder.Draw      -= OnDraw;
        PluginInterface.UiBuilder.OpenMainUi -= OnOpenMainUi;
        _demo.Dispose();
        _help.Dispose();
        _lab.Dispose();
        _api.Dispose();
        Log.Info("PanacheUI unloaded.");
    }
}
