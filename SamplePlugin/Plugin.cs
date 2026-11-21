
using Dalamud.Game.Command;
using Dalamud.IoC;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using SamplePlugin.Windows;

namespace SamplePlugin;

public sealed class Plugin : IDalamudPlugin
{
    [PluginService]
    internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;

    [PluginService]
    internal static ICommandManager CommandManager { get; private set; } = null!;

    [PluginService]
    internal static IClientState ClientState { get; private set; } = null!;

    [PluginService]
    internal static IPlayerState PlayerState { get; private set; } = null!;

    [PluginService]
    internal static IPluginLog Log { get; private set; } = null!;

    [PluginService]
    internal static IObjectTable ObjectTable { get; private set; } = null!;

    [PluginService]
    internal static IGameGui GameGui { get; private set; } = null!;

    [PluginService]
    internal static IDataManager DataManager { get; private set; } = null!;

    [PluginService]
    internal static IFramework Framework { get; private set; } = null!;

    [PluginService]
    internal static ITextureProvider TextureProvider { get; private set; } = null!;

    [PluginService]
    internal static IChatGui ChatGui { get; private set; } = null!;

    [PluginService]
    internal static IAddonLifecycle AddonLifecycle { get; private set; } = null!;

    internal AddonEventProbe AddonProbe { get; }

    private const string CommandName = "/cosmiccraft";

    public Configuration Configuration { get; }

    public readonly WindowSystem WindowSystem = new("CosmicCraft");

    private readonly MainWindow mainWindow;
    private readonly ConfigWindow configWindow;

    public Plugin()
    {
        AddonProbe = new AddonEventProbe(AddonLifecycle);

        Configuration =
            PluginInterface.GetPluginConfig() as Configuration
            ?? new Configuration();

        mainWindow = new MainWindow(this);
        configWindow = new ConfigWindow(this);

        WindowSystem.AddWindow(mainWindow);
        WindowSystem.AddWindow(configWindow);

        CommandManager.AddHandler(
            CommandName,
            new CommandInfo(OnCommand)
            {
                HelpMessage = "Abre o assistente CosmicCraft."
            });

        AddonProbe.CosmicMissionOpened += OnCosmicMissionOpened;
        Framework.Update += OnFrameworkUpdate;
        PluginInterface.UiBuilder.Draw += WindowSystem.Draw;
        PluginInterface.UiBuilder.OpenMainUi += ToggleMainUi;
        PluginInterface.UiBuilder.OpenConfigUi += ToggleConfigUi;

        Log.Information("CosmicCraft inicializado.");
    }

    private void OnFrameworkUpdate(IFramework framework) => mainWindow.UpdateMonitoringFrame();

    private void OnCommand(string command, string args)
    {
        ToggleMainUi();
    }

    public void ToggleMainUi() => mainWindow.Toggle();

    public void ToggleConfigUi() => configWindow.Toggle();

    private void OnCosmicMissionOpened()
{
    Log.Information(
        $"[CosmicCraft] Callback recebido. " +
        $"AutoOpen={Configuration.AutoOpenOnMissionStart}, " +
        $"Territorio={ClientState.TerritoryType}, " +
        $"JanelaAntes={mainWindow.IsOpen}");

    if (!Configuration.AutoOpenOnMissionStart)
        return;

    if (ClientState.TerritoryType != 1319)
        return;

    mainWindow.IsOpen = true;

    Log.Information(
        $"[CosmicCraft] Abertura solicitada. " +
        $"JanelaDepois={mainWindow.IsOpen}");
}


    public void Dispose()
    {

        Framework.Update -= OnFrameworkUpdate;
        PluginInterface.UiBuilder.Draw -= WindowSystem.Draw;
        PluginInterface.UiBuilder.OpenMainUi -= ToggleMainUi;
        PluginInterface.UiBuilder.OpenConfigUi -= ToggleConfigUi;

        CommandManager.RemoveHandler(CommandName);

        WindowSystem.RemoveAllWindows();

        mainWindow.Dispose();
        AddonProbe.CosmicMissionOpened -= OnCosmicMissionOpened;
        AddonProbe.Dispose();
        configWindow.Dispose();
    }
}
