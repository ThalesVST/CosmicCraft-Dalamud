
using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace SamplePlugin.Windows;

public class ConfigWindow : Window, IDisposable
{
    private readonly Configuration configuration;

    public ConfigWindow(Plugin plugin)
        : base("CosmicCraft - Configuracoes###CosmicCraftConfig")
    {
        Flags = ImGuiWindowFlags.NoCollapse;

        Size = new Vector2(430, 170);
        SizeCondition = ImGuiCond.FirstUseEver;

        configuration = plugin.Configuration;
    }

    public void Dispose() { }

    public override void Draw()
    {
        ImGui.TextDisabled("Cosmic Exploration");
        ImGui.Separator();

        bool autoOpen = configuration.AutoOpenOnMissionStart;

        if (ImGui.Checkbox(
            "Abrir automaticamente ao aceitar missao",
            ref autoOpen))
        {
            configuration.AutoOpenOnMissionStart = autoOpen;
            configuration.Save();
        }

        ImGui.Spacing();

        ImGui.TextDisabled(
            "Abre a janela ao aceitar uma missao Cosmic.");

        ImGui.TextDisabled(
            "Nao inicia a fabricacao automaticamente.");
    }
}
