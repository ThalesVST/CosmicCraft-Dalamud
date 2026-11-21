using System.Linq;
using System;
using System.Numerics;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Lumina.Excel.Sheets;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.Player;
using Dalamud.Interface.Windowing;

namespace SamplePlugin.Windows;

public class MainWindow : Window, IDisposable
{
    private readonly CraftingCalibration calibration = new();
    private readonly RotationCache rotationCache = new();
    private readonly CraftingChatReader chatReader = new();
    private int? chatSynthesisGain;
    private int? chatTouchGain;
    private string chatStatus = "Aguardando mensagens do chat.";
    private bool confirmNoBuffs = false;
    private readonly AutomaticGainObserver gainObserver = new();
    private bool observeGains = true;
    private int synthesisGain = 90;
    private int touchGain = 130;
    private int maxQuality = 1060;
    private string lastRecipeKey = "";
    private string conditionAtStart = "";
    private RotationPlan? currentPlan;
    private bool automaticPlanning = true;
    private string lastAutoPlanKey = "";
    private string currentMacro = "";
    private string plannerStatus = "";
    private string recipeLookupStatus = "Aguardando receita.";
    private string automaticGainStatus = "Aguardando dados da receita.";
    private bool estimatedGains = false;
    private int candidateItemId = 0;
    private string itemIdStatus = "Aguardando AtkValue[3].";
    private string missionProbeStatus = "Clique para investigar tabelas WKSMission locais.";
    private readonly Plugin owner;
    private string T(string value) => CosmicLocalization.T(value, owner.Configuration.Language);

    private string nodeProbeReport = "Capture com Efficiency Calculations fechado.";
    private string loadedAddonReport = "Capture os addons por ID com o painel fechado.";
    private string addonEventReport = "Inicie o monitoramento com o painel fechado.";
    private string efficiencyAddonName = "Synthesis";
    private bool showOnlyNonSynthesis = true;
    private string efficiencyProbeReport = "Abra Efficiency Calculations e clique em Procurar addons.";
    // Candidate names only: GetAddonByName cannot enumerate all loaded addons.
    private static readonly string[] EfficiencyAddonCandidates = {
        "Synthesis", "SynthesisSimple", "SynthesisCondition",
        "SynthesisCalculations", "SynthesisCalculation", "SynthesisEfficiency",
        "SynthesisAction", "SynthesisActionList", "SynthesisActionDetail",
        "SynthesisActionInfo", "SynthesisEfficiencyCalculation",
        "SynthesisEfficiencyCalculations", "SynthesisProgress",
        "SynthesisQuality", "SynthesisStatus", "SynthesisStatusList",
        "RecipeNote", "RecipeNoteBook", "RecipeNoteDetail",
        "RecipeNoteMaterial", "RecipeNoteRecipe", "RecipeNoteCrafting",
        "RecipeNoteSynthesis", "RecipeNoteAction",
        "CraftingLog", "Crafting", "CraftingAction", "CraftingActionList",
        "CraftingCalculations", "CraftingCalculation", "CraftingEfficiency",
        "SynthesisResult", "ActionDetail", "ActionList",
        "ActionMenu", "ActionMenuDetail", "ActionMenuList",
        "ContentsInfo", "ContentsInfoDetail"
    };
    private string recipeLinksStatus = "Clique para analisar WKSMissionRecipe.Recipe.";
    private int observedBasicSynthesis = 90;
    private int observedBasicTouch = 195;
    private string missionNameSearch = "Further Experimental Loporrit Enrichment";
    private string lastGainLookupKey = "";
    private bool automaticQuality = true;
    private readonly Dictionary<string, int> qualityByRecipe = new();
    private string raphaelPath = ResolveRaphaelPath();

    private static string ResolveRaphaelPath()
    {
        var pluginDirectory = Plugin.PluginInterface.AssemblyLocation.DirectoryName!;
        var bundled = System.IO.Path.Combine(pluginDirectory, "raphael-cli.exe");
        if (System.IO.File.Exists(bundled)) return bundled;
        // Compatibility with existing installations that keep Raphael in Downloads.
        return System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "raphael-cli.exe");
    }
    private Task<RaphaelBridge.Result>? raphaelTask;
    private CancellationTokenSource? raphaelCancel;
    private RaphaelBridge.Result? raphaelResult;
    private string raphaelStatus = "Aguardando solicitacao manual.";
    private string raphaelRecipeKey = "";
    private bool raphaelManipulation;
    private bool preferRaphael = true;
    private readonly RaphaelSuggestionCache raphaelCache = new();
    private string raphaelCacheKey = "";
    private string lastAutomaticCacheLookupKey = "";
    private readonly RaphaelStepTracker raphaelTracker = new();
    private bool showAdvancedPanels = false;

    private string missionEventReport = "Aguardando captura de eventos.";
    private int macroPart = 0;
    // Executor: uma habilidade por vez, com confirmacao antes do proximo envio.
    private bool showExecutorDetails;
    private bool automaticMode;
    private bool automaticWasStarted;
    private string automaticLastState = "Aguardando inicio.";
    private string executorStopReason = "Nenhuma parada registrada.";
    private readonly Queue<string> executorTransitionLog = new();
    private void RecordExecutorTransition(string reason)
    {
        string line = $"{DateTime.UtcNow:O} | {reason} | automatic={automaticMode}, " +
            $"monitor={executorMonitoring}, paused={executorPaused}, pending={singleActionPending}, " +
            $"lock={singleActionSafetyLock}, tracker={raphaelTracker.Active}, " +
            $"step={raphaelTracker.Step}, diverged={raphaelTracker.Diverged}, " +
            $"resync={raphaelTracker.NeedsResync}, finished={raphaelTracker.Finished}";
        executorTransitionLog.Enqueue(line);
        while (executorTransitionLog.Count > 30) executorTransitionLog.Dequeue();
        executorStopReason = reason;
    }

    private DateTime automaticNextSendAt;
    private const double AutomaticStepDelaySeconds = 1.2;
    private string actionAttemptDiagnostics = "Nenhuma tentativa registrada.";
    // Read-only evidence; never used for automatic confirmation.
    private HashSet<uint> actionStatusesBefore = new();
    private string actionNameForStatusDiagnostic = "";
    private string DescribeStatusChanges()
    {
        var player = Plugin.ObjectTable.LocalPlayer;
        if (player is null) return "Status: personagem indisponivel.";
        var current = new HashSet<uint>();
        foreach (var effect in player.StatusList)
            if (effect.StatusId != 0) current.Add(effect.StatusId);
        var added = current.Except(actionStatusesBefore).OrderBy(x => x).ToArray();
        var removed = actionStatusesBefore.Except(current).OrderBy(x => x).ToArray();
        return $"Status antes/depois ({actionNameForStatusDiagnostic}): " +
            $"novos=[{string.Join(",", added)}]; removidos=[{string.Join(",", removed)}]. " +
            "Somente diagnostico; nao confirma a habilidade.";
    }

    private uint trainedPerfectionStatusId;
    private bool trainedPerfectionBuffWasAbsent;
    private bool trainedPerfectionConfirmationPending;
    private DateTime trainedPerfectionObservedAt;
    private int trainedPerfectionObservedFrames;
    private bool singleActionPending;
    private bool singleActionSafetyLock;
    private int singleActionConfirmedCount;
    private int singleActionStep = -1;
    private DateTime singleActionSentAt;
    private string singleActionReport = "Nenhuma habilidade enviada pelo teste.";
    private bool executorMonitoring;
    private bool executorPaused;
    private string executorRecipeKey = "";
    private string executorMessage = "Aguardando uma rotacao do Raphael.";


    public MainWindow(Plugin plugin) : base("CosmicCraft v0.9.42.0###CosmicCraftMain")
    {
        owner = plugin;
       Size = new Vector2(720, 365);
        SizeCondition = ImGuiCond.FirstUseEver;
    }

    public void Dispose()
    {
        raphaelCancel?.Cancel();
        executorMonitoring = false;
        automaticMode = false;
        singleActionPending = false;
        chatReader.Dispose();
    }

    private void DrawBrandIcon(float size)
    {
        try
        {
            string path = System.IO.Path.Combine(
                Plugin.PluginInterface.AssemblyLocation.DirectoryName!, "Data", "icon.png");
            if (!System.IO.File.Exists(path)) return;
            var wrap = Plugin.TextureProvider.GetFromFile(path).GetWrapOrDefault();
            if (wrap is not null)
            {
                ImGui.Image(wrap.Handle, new Vector2(size, size));
                ImGui.SameLine();
            }
        }
        catch (Exception) { /* Optional decoration must never break crafting UI. */ }
    }

    public override unsafe void Draw()
    {
        DrawBrandIcon(36f);

ImGui.TextColored(
    new Vector4(0.95f, 0.82f, 0.55f, 1f),
    "COSMICCRAFT");

ImGui.SameLine();
ImGui.TextDisabled("  |  Cosmic Exploration");

ImGui.Spacing();
ImGui.Separator();
ImGui.Spacing();
        bool inCosmic = Plugin.ClientState.TerritoryType == 1319;
        if (!inCosmic)
            ImGui.TextDisabled(T("Entre na Cosmic Exploration para utilizar o plugin."));

        var jobId = (int)Plugin.PlayerState.ClassJob.RowId;
        string jobName = jobId switch
        {
            8 => "CRP", 9 => "BSM", 10 => "ARM", 11 => "GSM",
            12 => "LTW", 13 => "WVR", 14 => "ALC", 15 => "CUL", _ => $"ID {jobId}"
        };
        ImGui.TextColored(
    new Vector4(0.95f, 0.82f, 0.55f, 1f),
    $"{jobName}  |  Nivel {Plugin.PlayerState.Level}");

ImGui.Spacing();

int craftsmanship =
    (int)Plugin.PlayerState.GetAttribute(PlayerAttribute.Craftsmanship);

int control =
    (int)Plugin.PlayerState.GetAttribute(PlayerAttribute.Control);

var player = Plugin.ObjectTable.LocalPlayer;
int cp = player is null ? 0 : (int)player.CurrentCp;
int maxCp = player is null ? 0 : (int)player.MaxCp;

ImGui.TextDisabled("Craftsmanship");
ImGui.SameLine(160);
ImGui.Text($"{craftsmanship}");

ImGui.TextDisabled("Control");
ImGui.SameLine(160);
ImGui.Text($"{control}");

ImGui.TextDisabled("CP");
ImGui.SameLine(160);
ImGui.Text($"{cp} / {maxCp}");

ImGui.Spacing();
ImGui.Separator();
ImGui.Spacing();

        if (!inCosmic)
        {
            ClearActiveState();
            return;
        }
        var addon = Plugin.GameGui.GetAddonByName("Synthesis");
        if (addon.IsNull || !addon.IsReady || !addon.IsVisible)
        {
            ImGui.TextDisabled(T("Nenhuma sintese ativa."));
            ClearActiveState();
            return;
        }
        if (!SynthesisReader.TryRead(addon, out var data))
        {
            ImGui.TextDisabled(SynthesisReader.LastStatus);
            return;
        }

        var recipeKey = $"{jobId}|{data.RecipeName}|{data.MaxProgress}|{data.MaxDurability}";
        if (recipeKey != lastRecipeKey)
        {
            raphaelCancel?.Cancel();
            raphaelResult = null;
            raphaelRecipeKey = "";
            lastAutomaticCacheLookupKey = "";
            raphaelTracker.Reset();
            lastRecipeKey = recipeKey;
            lastAutoPlanKey = "";
            lastGainLookupKey = "";
            estimatedGains = false;
            calibration.Cancel();
            gainObserver.Reset();
            chatReader.Clear();
            chatSynthesisGain = null;
            chatTouchGain = null;
            conditionAtStart = "";
            InvalidatePlan();
            // Valores desconhecidos nao devem herdar a calibracao da receita anterior.
            var known = calibration.Get(data.RecipeName, jobId, new CraftingState
            {
                MaxProgress = data.MaxProgress, MaxDurability = data.MaxDurability
            });
            synthesisGain = known?.SynthesisGain ?? 0;
            touchGain = known?.TouchGain ?? 0;
            maxQuality = qualityByRecipe.TryGetValue(recipeKey, out int savedQuality) ? savedQuality : 0;
            if (RecipeDataReader.TryFind(data.RecipeName, data.MaxProgress, data.MaxDurability,
                    out var recipeInfo, out var diagnostic))
            {
                recipeLookupStatus = diagnostic;
                if (maxQuality == 0 && recipeInfo.MaxQuality > 0)
                {
                    maxQuality = recipeInfo.MaxQuality;
                    qualityByRecipe[recipeKey] = maxQuality;
                }
            }
            else recipeLookupStatus = diagnostic;
        }

        // Probe read-only: [3] may be the result item ID. Do not assume it is a Recipe row ID.
        candidateItemId = 0;
        if (SynthesisReader.TryGetValues(addon, out var probeValues) &&
            probeValues.Length > 3 && int.TryParse(probeValues[3],
                NumberStyles.Integer, CultureInfo.InvariantCulture, out int itemCandidate) && itemCandidate > 0)
        {
            candidateItemId = itemCandidate;
            itemIdStatus = $"AtkValue[3] candidato a ItemResult: {candidateItemId}.";
        }
        else itemIdStatus = "AtkValue[3] nao disponivel; nao e seguro associar a receita.";

        // O indice 17 foi confirmado como qualidade maxima em duas receitas
        // de Cosmic Exploration. Mantemos validacao e alternativa manual.
        int detectedQuality = 0;
        if (SynthesisReader.TryGetValues(addon, out var synthesisValues) &&
            synthesisValues.Length > 17 &&
            int.TryParse(synthesisValues[17], NumberStyles.Integer,
                CultureInfo.InvariantCulture, out var parsedQuality) &&
            parsedQuality > 0 && parsedQuality >= data.Quality)
        {
            detectedQuality = parsedQuality;
        }

        if (automaticQuality && detectedQuality > 0)
        {
            if (maxQuality != detectedQuality)
            {
                maxQuality = detectedQuality;
                InvalidatePlan();
            }
        }

        ImGui.Spacing();
ImGui.TextDisabled("RECEITA ATUAL");

ImGui.TextColored(
    new Vector4(0.95f, 0.82f, 0.55f, 1f),
    data.RecipeName);

ImGui.Spacing();
        // Only look up after MaxQuality has been read from the active synthesis window.
        // Recalculate if stats, recipe, or quality change; never copy previous recipe gains.
        string gainLookupKey = $"{recipeKey}|{candidateItemId}|{maxQuality}|{Plugin.PlayerState.Level}|{Plugin.PlayerState.GetAttribute(PlayerAttribute.Craftsmanship)}|{Plugin.PlayerState.GetAttribute(PlayerAttribute.Control)}";
        if (gainLookupKey != lastGainLookupKey)
        {
            lastGainLookupKey = gainLookupKey;
            if (AutomaticCraftingGain.TryEstimate(data.RecipeName, data.MaxProgress, data.MaxDurability,
                    maxQuality, (int)Plugin.PlayerState.GetAttribute(PlayerAttribute.Craftsmanship),
                    (int)Plugin.PlayerState.GetAttribute(PlayerAttribute.Control),
                    (int)Plugin.PlayerState.Level, candidateItemId, out var estimate, out automaticGainStatus))
            {
                // A chat-confirmed calibration always takes precedence over an estimate.
                var confirmed = calibration.Get(data.RecipeName, jobId, new CraftingState
                {
                    MaxProgress = data.MaxProgress, MaxDurability = data.MaxDurability
                });
                if (confirmed?.SynthesisGain is not > 0) { synthesisGain = estimate.Synthesis; estimatedGains = true; }
                if (confirmed?.TouchGain is not > 0) { touchGain = estimate.Touch; estimatedGains = true; }
                InvalidatePlan();
            }
            else
            {
                estimatedGains = false;
            }
        }

        var state = new CraftingState
        {
            Progress = data.Progress, MaxProgress = data.MaxProgress,
            Quality = data.Quality, MaxQuality = maxQuality,
            Durability = data.Durability, MaxDurability = data.MaxDurability,
            Cp = cp, MaxCp = maxCp
        };
        // Atualize o acompanhamento em cada Draw com sintese valida,
        // independentemente de o painel Raphael estar expandido.
        // A chave de acompanhamento identifica a receita, nao o cache do solver:
        // a qualidade atual muda durante a fabricacao e nao pode reiniciar o tracker.
        // O acompanhamento e atualizado pelo IFramework.Update, mesmo com a janela fechada.
        ImGui.Separator();
        if (preferRaphael) DrawRaphael(state, data.Condition, (int)Plugin.PlayerState.Level,
            (int)Plugin.PlayerState.GetAttribute(PlayerAttribute.Craftsmanship),
            (int)Plugin.PlayerState.GetAttribute(PlayerAttribute.Control));
        ImGui.Separator();
        if (ImGui.Button(T("Configuracoes avancadas...")))
            showAdvancedPanels = true;
        if (showAdvancedPanels)
        {
        // Separate ImGui window, independent of the main crafting layout.
        ImGui.SetNextWindowSize(new Vector2(700, 580), ImGuiCond.FirstUseEver);
        bool opened = showAdvancedPanels;
        bool visible = ImGui.Begin("CosmicCraft - Configuracoes avancadas###CosmicCraftAdvanced", ref opened);
        showAdvancedPanels = opened;
        if (!visible)
        {
            ImGui.End();
            return;
        }
        ImGui.TextDisabled(T("Ajustes e diagnosticos - janela independente"));
        ImGui.Separator();
        if (ImGui.CollapsingHeader(T("Idioma e apoio ao projeto") + "###CosmicLanguageSupport", ImGuiTreeNodeFlags.DefaultOpen))
        {
            ImGui.Text(T("Idioma da interface"));
            string language = owner.Configuration.Language;
            if (ImGui.RadioButton("Português (Brasil)##CosmicPT", language != "en"))
            {
                owner.Configuration.Language = "pt-BR";
                owner.Configuration.Save();
            }
            ImGui.SameLine();
            if (ImGui.RadioButton("English##CosmicEN", language == "en"))
            {
                owner.Configuration.Language = "en";
                owner.Configuration.Save();
            }
            ImGui.TextDisabled(T("Nomes das habilidades e comandos de macro nao sao traduzidos."));
            if (ImGui.Button(T("Apoiar o projeto no Ko-fi") + "###CosmicDonate"))
            {
                try
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "https://ko-fi.com/tchulon",
                        UseShellExecute = true
                    });
                }
                catch (Exception ex)
                {
                    Plugin.Log.Warning($"Could not open Ko-fi URL: {ex.Message}");
                }
            }
            ImGui.SameLine();
            ImGui.TextDisabled("CosmicCraft v0.9.42.0");
        }
        ImGui.Separator();
        if (ImGui.CollapsingHeader(T("Raphael e qualidade")))
        {
            ImGui.InputText(T("Caminho do Raphael"), ref raphaelPath, 512);
            ImGui.Checkbox(T("Manipulation desbloqueada"), ref raphaelManipulation);
            ImGui.Checkbox(T("Usar Raphael"), ref preferRaphael);
            if (ImGui.Checkbox(T("Qualidade maxima automatica [17]"), ref automaticQuality))
                InvalidatePlan();
            if (!automaticQuality || detectedQuality <= 0)
            {
                if (ImGui.InputInt(T("Qualidade maxima manual"), ref maxQuality))
                {
                    maxQuality = Math.Max(0, maxQuality);
                    if (lastRecipeKey.Length > 0) qualityByRecipe[lastRecipeKey] = maxQuality;
                    InvalidatePlan();
                }
            }
            ImGui.TextWrapped("Status Raphael: " + raphaelStatus);
            ImGui.TextWrapped("Dados da receita: " + recipeLookupStatus);
        }
        ImGui.Separator();
        if (ImGui.CollapsingHeader(T("Calibracao e ganhos")))
        {
            DrawChatGains(data.RecipeName, jobId, data.Condition, state);
            DrawAutomaticGains(data.Condition, state);
            DrawCalibration(data.RecipeName, jobId, data.Condition, state);
        }
        if (!preferRaphael) DrawPlanner(state, (int)Plugin.PlayerState.Level, data.RecipeName, jobId,
            (int)Plugin.PlayerState.GetAttribute(PlayerAttribute.Craftsmanship),
            (int)Plugin.PlayerState.GetAttribute(PlayerAttribute.Control));
        if (ImGui.CollapsingHeader(T("Diagnosticos de receita")))
        {
            ImGui.TextWrapped(itemIdStatus);
            ImGui.TextWrapped("Identificacao: " + automaticGainStatus);
            ImGui.TextWrapped("[3] ainda e apenas um candidato; [16] nao e usado como ID de receita.");
            if (ImGui.Button("Investigar tabelas Cosmic (somente leitura)"))
                missionProbeStatus = CosmicMissionProbe.Inspect(data.RecipeName, candidateItemId);
            ImGui.TextWrapped(missionProbeStatus);
            if (ImGui.Button("Verificar IDs reais e referencias vazias"))
                recipeLinksStatus = CosmicMissionProbe.InspectRecipeCollection(data.RecipeName, candidateItemId, data.MaxProgress, data.MaxDurability, maxQuality);
            if (ImGui.Button("Comparar progresso, qualidade e durabilidade WKS"))
                recipeLinksStatus = CosmicMissionProbe.MatchByParameters(
                    data.RecipeName, candidateItemId, data.MaxProgress,
                    data.MaxDurability, maxQuality, jobId);
            if (ImGui.Button("Investigar missoes, profissoes e receitas leves"))
                recipeLinksStatus = CosmicMissionProbe.InspectMissionAndLowRecipes(
                    data.RecipeName, candidateItemId, data.MaxProgress,
                    data.MaxDurability, maxQuality, jobId);
            if (ImGui.Button("Rastrear missao por nome e CraftType"))
                recipeLinksStatus = CosmicMissionProbe.TraceMissionByNameAndJob(
                    data.RecipeName, candidateItemId, data.MaxProgress,
                    data.MaxDurability, maxQuality, jobId);
            ImGui.InputText("Nome da missao##CosmicMissionName", ref missionNameSearch, 160);
            if (ImGui.Button("Buscar receitas da missao WKS"))
                recipeLinksStatus = CosmicMissionProbe.TraceByMissionName(
                    missionNameSearch, data.RecipeName, candidateItemId,
                    data.MaxProgress, data.MaxDurability, maxQuality, jobId);
            if (ImGui.Button("Verificar formula de ganhos WKS"))
                recipeLinksStatus = CosmicMissionProbe.InspectGainFormula(
                    missionNameSearch, data.RecipeName, jobId,
                    (int)Plugin.PlayerState.Level,
                    (int)Plugin.PlayerState.GetAttribute(PlayerAttribute.Craftsmanship),
                    (int)Plugin.PlayerState.GetAttribute(PlayerAttribute.Control));
            ImGui.InputInt("Basic Synthesis real##ObservedSynthesis", ref observedBasicSynthesis);
            ImGui.InputInt("Basic Touch real##ObservedTouch", ref observedBasicTouch);
            if (ImGui.Button("Comparar ganhos reais com RecipeLevelTable"))
                recipeLinksStatus = CosmicMissionProbe.CompareObservedGains(
                    (int)Plugin.PlayerState.Level,
                    (int)Plugin.PlayerState.GetAttribute(PlayerAttribute.Craftsmanship),
                    (int)Plugin.PlayerState.GetAttribute(PlayerAttribute.Control),
                    observedBasicSynthesis, observedBasicTouch);
            ImGui.Separator();
            ImGui.Separator();
            ImGui.TextWrapped("v0.6.37 - Addons carregados por ID (somente leitura)");
            if (ImGui.Button("C. Capturar addons com painel fechado"))
                loadedAddonReport = LoadedAddonProbe.MarkClosed(Plugin.GameGui, owner.AddonProbe.KnownAddonNames());
            if (ImGui.Button("D. Comparar addons com painel aberto"))
                loadedAddonReport = LoadedAddonProbe.Compare(Plugin.GameGui, owner.AddonProbe.KnownAddonNames());
            ImGui.BeginChild("LoadedAddonProbeOutput", new Vector2(0, 220), true);
            ImGui.TextWrapped(loadedAddonReport);
            ImGui.EndChild();
            ImGui.TextWrapped("v0.6.36 - Arvore completa do Synthesis (somente leitura)");
            if (ImGui.Button("A. Capturar arvore com painel fechado"))
                nodeProbeReport = SynthesisNodeProbe.MarkClosed((FFXIVClientStructs.FFXIV.Component.GUI.AtkUnitBase*)addon.Address);
            if (ImGui.Button("B. Comparar arvore com painel aberto"))
                nodeProbeReport = SynthesisNodeProbe.Compare((FFXIVClientStructs.FFXIV.Component.GUI.AtkUnitBase*)addon.Address);
            ImGui.BeginChild("NodeProbeOutput", new Vector2(0, 210), true);
            ImGui.TextWrapped(nodeProbeReport);
            ImGui.EndChild();
            ImGui.TextWrapped("Efficiency Calculations (diagnostico somente leitura)");
            ImGui.TextWrapped("v0.6.34 - Monitor de eventos: mantenha este monitor ativo, capture com o painel fechado, depois abra o painel e compare.");
            if (ImGui.Button("1. Iniciar monitor (painel fechado)"))
            {
                owner.AddonProbe.Start();
                addonEventReport = "Monitor iniciado. Aguarde um instante e clique em Capturar painel fechado.";
            }
            if (ImGui.Button("2. Capturar painel fechado"))
            {
                owner.AddonProbe.MarkClosed();
                addonEventReport = "Base registrada. Agora abra Efficiency Calculations e clique em Comparar.";
            }
            if (ImGui.Button("3. Comparar com painel aberto"))
                addonEventReport = owner.AddonProbe.Report();
            ImGui.BeginChild("AddonEventOutput", new Vector2(0, 180), true);
            ImGui.TextWrapped(addonEventReport);
            ImGui.EndChild();

            ImGui.InputText("Addon##EfficiencyAddonName", ref efficiencyAddonName, 80);
            ImGui.Checkbox("Ocultar Synthesis principal na busca", ref showOnlyNonSynthesis);
            if (ImGui.Button("Procurar addons de calculo"))
            {
                var lines = new System.Collections.Generic.List<string>();
                int available = 0;
                foreach (var name in EfficiencyAddonCandidates)
                {
                    if (showOnlyNonSynthesis && name == "Synthesis") continue;
                    var candidate = Plugin.GameGui.GetAddonByName(name);
                    if (candidate.IsNull) continue;
                    available++;
                    if (SynthesisReader.TryGetValues(candidate, out var candidateValues))
                        lines.Add($"{name}: {candidateValues.Length} valores; " +
                            string.Join(" | ", candidateValues.Take(30).Select((v, i) => $"[{i}] {v}")));
                    else
                        lines.Add($"{name}: ponteiro encontrado, sem AtkValues legiveis ({SynthesisReader.LastStatus}).");
                }
                efficiencyProbeReport = $"Nomes candidatos testados: {EfficiencyAddonCandidates.Length}; encontrados: {available}.\\n" +
                    (lines.Count == 0
                        ? "Nenhum addon candidato encontrado. O painel pode usar nome nao listado ou ser desenhado por outro addon. Esta busca NAO enumera todos os addons."
                        : string.Join("\\n", lines));
            }
            if (ImGui.Button("Ler valores do addon escolhido"))
            {
                var candidate = Plugin.GameGui.GetAddonByName(efficiencyAddonName);
                if (SynthesisReader.TryGetValues(candidate, out var candidateValues))
                    efficiencyProbeReport = $"Addon {efficiencyAddonName}: {candidateValues.Length} valores\\n" +
                        string.Join("\\n", candidateValues.Select((v, i) => $"[{i}] {v}"));
                else
                    efficiencyProbeReport = $"Nao foi possivel ler {efficiencyAddonName}: {SynthesisReader.LastStatus}";
            }
            ImGui.BeginChild("EfficiencyProbeOutput", new Vector2(0, 180), true);
            ImGui.TextWrapped(efficiencyProbeReport);
            ImGui.EndChild();
            ImGui.TextWrapped(recipeLinksStatus);
            if (SynthesisReader.TryGetValues(addon, out var values))
            {
                ImGui.BeginChild("SynthesisDiagnostics", new Vector2(0, 180), true);
                for (int i = 0; i < values.Length; i++) ImGui.Text($"[{i}] {values[i]}");
                ImGui.EndChild();
            }
        }
        if (raphaelResult is { Success: true } current && raphaelRecipeKey == lastRecipeKey)
        {
            if (ImGui.CollapsingHeader(T("IDs de habilidades")))
                DrawActionIdDiagnostics(current.Actions);
            if (ImGui.CollapsingHeader(T("Acompanhamento detalhado")))
                DrawRaphaelTracking(state, current, data.Condition);
        }
        ImGui.End();
        }
    }

    private void DrawRaphael(CraftingState state, string condition, int level, int craftsmanship, int control)
    {
        // A tabela por ClassJobLevel replica a regra Cosmic observada no Artisan.
        var levels = Plugin.DataManager.GetExcelSheet<RecipeLevelTable>();
        var table = levels?.FirstOrDefault(x => x.ClassJobLevel == level);
        bool hasTable = levels?.Any(x => x.ClassJobLevel == level) == true;
        int tableId = table.HasValue ? (int)table.Value.RowId : 0;
        bool atStart = state.Progress == 0 && state.Quality == 0 &&
                       state.Durability == state.MaxDurability && state.Cp == state.MaxCp;
        bool valid = atStart && hasTable && state.MaxProgress > 0 && state.MaxQuality > 0 &&
                     state.MaxDurability > 0 && state.MaxCp > 0 && craftsmanship > 0 && control > 0;
        string cacheKey = RaphaelSuggestionCache.MakeKey(lastRecipeKey, level, tableId,
            state.MaxProgress, state.MaxQuality, state.MaxDurability,
            craftsmanship, control, state.MaxCp, state.Quality, raphaelManipulation);
        // Uma unica consulta por combinacao exata de receita, atributos e configuracao.
        // Nunca carregar uma rotacao enquanto a sintese ja esta em andamento.
        if (valid && !executorMonitoring && raphaelTask is null &&
            (raphaelResult is not { Success: true } || raphaelRecipeKey != lastRecipeKey) &&
            cacheKey != lastAutomaticCacheLookupKey)
        {
            lastAutomaticCacheLookupKey = cacheKey;
            if (raphaelCache.TryGet(cacheKey, out var automaticSaved) &&
                automaticSaved.Actions is { Length: > 0 })
            {
                raphaelResult = new RaphaelBridge.Result(true,
                    "Rotacao carregada automaticamente do cache (experimental).",
                    automaticSaved.Actions, automaticSaved.FinalQuality,
                    automaticSaved.Actions.Length, "cache");
                raphaelRecipeKey = lastRecipeKey;
                raphaelCacheKey = cacheKey;
                raphaelStatus = "Rotacao conhecida carregada automaticamente. Confira antes de executar.";
            }
        }
        if (valid && !executorMonitoring &&
            (raphaelResult is not { Success: true } || raphaelRecipeKey != lastRecipeKey) &&
            ImGui.Button(T("Tentar carregar rotacao salva")))
        {
            if (raphaelCache.TryGet(cacheKey, out var saved))
            {
                raphaelResult = new RaphaelBridge.Result(true,
                    "Recomendacao recuperada do cache (NAO validada no jogo).",
                    saved.Actions, saved.FinalQuality, saved.Actions.Length, "cache");
                raphaelRecipeKey = lastRecipeKey;
                raphaelCacheKey = cacheKey;
                raphaelStatus = "Recomendacao recuperada do cache (experimental).";
            }
            else raphaelStatus = "Nenhuma recomendacao salva para estes parametros.";
        }
        bool busy = raphaelTask is { IsCompleted: false };
        if (!valid && raphaelResult is not { Success: true })
            ImGui.TextDisabled(T("Abra uma sintese no passo 1 para calcular."));
        if (busy) ImGui.Text(T("Raphael calculando..."));
        if (!busy && valid && !executorMonitoring &&
            ImGui.Button(T("Calcular rotacao"), new Vector2(-1f, 30f)))
        {
            raphaelCancel?.Dispose();
            raphaelCancel = new CancellationTokenSource();
            raphaelResult = null;
            raphaelTracker.Reset();
            raphaelRecipeKey = lastRecipeKey;
            raphaelCacheKey = cacheKey;
            raphaelStatus = "Calculando...";
            var request = new RaphaelBridge.Request(raphaelPath, tableId,
                state.MaxProgress, state.MaxQuality, state.MaxDurability,
                level, craftsmanship, control, state.MaxCp, state.Quality,
                false, raphaelManipulation);
            raphaelTask = RaphaelBridge.SolveAsync(request, raphaelCancel.Token);
        }
        if (busy && ImGui.Button(T("Cancelar calculo"), new Vector2(-1f, 30f)))
            raphaelCancel?.Cancel();

        if (raphaelTask is { IsCompleted: true } completed)
        {
            try
            {
                raphaelResult = completed.GetAwaiter().GetResult();
                raphaelStatus = raphaelResult.Message;
                if (raphaelResult.Success && raphaelRecipeKey == lastRecipeKey &&
                    raphaelCacheKey == cacheKey && raphaelResult.FinalQuality.HasValue)
                {
                    raphaelCache.Save(raphaelCacheKey, level, raphaelResult.Actions,
                        raphaelResult.FinalQuality.Value);
                    raphaelStatus += " Recomendacao registrada no cache experimental.";
                }
            }
            catch (Exception ex) { raphaelStatus = ex.Message; raphaelResult = null; }
            raphaelTask = null;
        }
        if (raphaelResult is { Success: true } result && raphaelRecipeKey == lastRecipeKey)
        {
            DrawRaphaelMacros(result.Actions, state);
        }
        else if (raphaelResult is { Success: false } failed &&
                 !string.IsNullOrWhiteSpace(failed.Raw))
            ImGui.TextWrapped(failed.Raw);
    }


    private void DrawRaphaelMacros(string[] actions, CraftingState state)
    {
        // Layout visual: duas colunas alinhadas; nenhuma alteracao na logica.
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        ImGui.Columns(2, "##CosmicCraftCompactColumns", false);
        float availableWidth = ImGui.GetContentRegionAvail().X;
        ImGui.SetColumnWidth(0, Math.Max(240f, availableWidth * 0.43f));

        ImGui.TextColored(
            new Vector4(0.95f, 0.82f, 0.55f, 1f),
            T("AUTOMATICO"));
        // Folga vertical apos o titulo: os controles nao encostam na divisoria.
        ImGui.Dummy(new Vector2(0f, 14f));
        DrawExecutorMonitor(actions, state);

        ImGui.NextColumn();
        ImGui.TextColored(
            new Vector4(0.95f, 0.82f, 0.55f, 1f),
            T("MACRO GERADA"));
        ImGui.Separator();
        ImGui.Spacing();

        var generated = RaphaelMacroFormatter.Build(actions);
        if (!generated.Success)
        {
            ImGui.TextWrapped(generated.Error);
        }
        else
        {
            macroPart = Math.Clamp(macroPart, 0, generated.Parts.Length - 1);
            if (generated.Parts.Length > 1)
            {
                if (ImGui.SmallButton(T("Anterior")) && macroPart > 0) macroPart--;
                ImGui.SameLine();
                ImGui.Text($"{macroPart + 1}/{generated.Parts.Length}");
                ImGui.SameLine();
                if (ImGui.SmallButton(T("Proximo")) && macroPart + 1 < generated.Parts.Length) macroPart++;
            }
            string macroText = generated.Parts[macroPart];
            ImGui.InputTextMultiline("##CosmicMacroOutput", ref macroText, 4096,
                new Vector2(-1, 150), ImGuiInputTextFlags.ReadOnly);
            ImGui.Spacing();
            if (ImGui.Button(T("Copiar macro"), new Vector2(-1f, 30f)))
                ImGui.SetClipboardText(macroText);
        }
        ImGui.Columns(1);
    }

    // Atualizacao independente da janela ImGui. Somente leitura do estado do jogo.
    // Nenhuma habilidade e enviada nesta etapa.
    public void UpdateMonitoringFrame()
    {
        if (!executorMonitoring && !raphaelTracker.Active) return;
        if (Plugin.ClientState.TerritoryType != 1319)
        {
            StopMonitoring("Monitor interrompido: fora da Cosmic Exploration.");
            return;
        }
        var addon = Plugin.GameGui.GetAddonByName("Synthesis");
        if (addon.IsNull || !addon.IsReady || !addon.IsVisible ||
            !SynthesisReader.TryRead(addon, out var data))
        {
            StopMonitoring("Monitor interrompido: sintese indisponivel.");
            return;
        }
        int jobId = (int)Plugin.PlayerState.ClassJob.RowId;
        string key = $"{jobId}|{data.RecipeName}|{data.MaxProgress}|{data.MaxDurability}";
        if (key != lastRecipeKey || (executorMonitoring && key != executorRecipeKey))
        {
            StopMonitoring("Monitor interrompido: receita ou profissao alterada.");
            return;
        }
        var player = Plugin.ObjectTable.LocalPlayer;
        int cp = player is null ? 0 : (int)player.CurrentCp;
        int maxCp = player is null ? 0 : (int)player.MaxCp;
        var state = new CraftingState
        {
            Progress = data.Progress, MaxProgress = data.MaxProgress,
            Quality = data.Quality, MaxQuality = maxQuality,
            Durability = data.Durability, MaxDurability = data.MaxDurability,
            Cp = cp, MaxCp = maxCp
        };
        UpdateRaphaelTracking(state);
        CheckTrainedPerfectionConfirmation(state);
        UpdateExecutorMonitor();
        if (singleActionPending)
        {
            if (raphaelTracker.Active && raphaelTracker.Step > singleActionStep)
            {
                actionAttemptDiagnostics += $"\nConfirmacao: rastreador avancou de {singleActionStep} para {raphaelTracker.Step}.";
                actionAttemptDiagnostics += "\n" + DescribeStatusChanges();
                singleActionPending = false;
                trainedPerfectionConfirmationPending = false;
                singleActionConfirmedCount++;
                automaticLastState = $"Confirmado passo {raphaelTracker.Step}.";
                singleActionReport = automaticMode
                    ? $"Passo {raphaelTracker.Step} confirmado; aguardando intervalo para a proxima acao."
                    : $"Passo {raphaelTracker.Step} confirmado. Clique novamente para solicitar a proxima habilidade.";
                automaticNextSendAt = DateTime.UtcNow.AddSeconds(AutomaticStepDelaySeconds);
            }
            else if (!executorMonitoring || raphaelTracker.Diverged || raphaelTracker.NeedsResync)
            {
                actionAttemptDiagnostics += $"\nBloqueio: perda de sincronizacao. Diverged={raphaelTracker.Diverged}; NeedsResync={raphaelTracker.NeedsResync}.";
                RecordExecutorTransition("Bloqueio: perda de sincronizacao.");
                singleActionPending = false;
                singleActionSafetyLock = true;
                automaticMode = false;
                automaticWasStarted = false;
                automaticLastState = "Bloqueado: perda de sincronizacao.";
                executorPaused = true;
                singleActionReport = "[BLOQUEADO] Perda de sincronizacao. Pare o monitor e reinicie uma nova fabricacao.";
            }
            else if ((DateTime.UtcNow - singleActionSentAt).TotalSeconds > 12)
            {
                actionAttemptDiagnostics += $"\nBloqueio: timeout de confirmacao apos {(DateTime.UtcNow - singleActionSentAt).TotalSeconds:F1}s.";
                actionAttemptDiagnostics += "\n" + DescribeStatusChanges();
                actionAttemptDiagnostics +=
                    $"\nRastreador: {raphaelTracker.DetectionStatus}; " +
                    $"evento recente: {raphaelTracker.LastDetected}; " +
                    $"mudancas UI={raphaelTracker.StateTransitions}; " +
                    $"eventos chat={raphaelTracker.ChatEvents}.";
                RecordExecutorTransition("Bloqueio: timeout de confirmacao.");
                singleActionPending = false;
                executorPaused = true;
                singleActionSafetyLock = true;
                automaticMode = false;
                automaticWasStarted = false;
                automaticLastState = "Bloqueado: tempo limite.";
                singleActionReport = "[BLOQUEADO] Tempo limite sem confirmacao. Nao sera permitido reenviar nesta fabricacao.";
            }
        }
        if (automaticMode && executorMonitoring && !executorPaused && !singleActionPending &&
            !singleActionSafetyLock && raphaelTracker.Active && !raphaelTracker.Finished &&
            !raphaelTracker.Diverged && !raphaelTracker.NeedsResync &&
            DateTime.UtcNow >= automaticNextSendAt)
        {
            if (raphaelResult is { Success: true } activeResult &&
                raphaelRecipeKey == lastRecipeKey && executorRecipeKey == lastRecipeKey)
            {
                // Usa exatamente o mesmo caminho validado na v0.9.20.
                TrySendSingleAction(activeResult.Actions);
                // Uma falha de envio nunca provoca repeticao automatica.
                if (!singleActionPending)
                {
                    RecordExecutorTransition("Bloqueio: TrySendSingleAction nao iniciou uma acao. " + singleActionReport);
                    automaticMode = false;
                    executorPaused = true;
                    singleActionSafetyLock = true;
                    actionAttemptDiagnostics += "\nExecutor interrompido sem novo envio. Motivo: " + singleActionReport;
                    singleActionReport = "[BLOQUEADO] Envio automatico nao confirmado/recusado: " + singleActionReport;
                }
            }
            else
            {
                RecordExecutorTransition("Bloqueio: rotacao Raphael mudou durante a execucao.");
                automaticMode = false;
                executorPaused = true;
                singleActionSafetyLock = true;
                singleActionReport = "[BLOQUEADO] Rotacao Raphael mudou durante a execucao.";
            }
        }
        if (raphaelTracker.Finished && automaticWasStarted)
        {
            RecordExecutorTransition("Rotacao confirmada.");
            automaticMode = false;
            automaticWasStarted = false;
            automaticLastState = "Rotacao confirmada. Verifique a conclusao da sintese no jogo.";
            singleActionReport = automaticLastState;
        }
        else if (automaticMode && !executorMonitoring)
        {
            RecordExecutorTransition("Automatico interrompido: monitor indisponivel.");
            automaticMode = false;
            automaticLastState = "Interrompido: monitoramento indisponivel.";
        }
    }

    private void StopMonitoring(string reason)
    {
        RecordExecutorTransition(reason);
        executorMonitoring = false;
        automaticMode = false;
        automaticWasStarted = false;
        automaticLastState = reason;
        singleActionPending = false;
        executorPaused = false;
        executorMessage = reason;
        raphaelTracker.Reset();
        chatReader.ClearActionObservations();
    }

    // v0.9.13: consultas pesadas executadas somente sob demanda, nunca a cada frame.
    private readonly List<string> actionIdReport = new();
    private string actionIdReportKey = "";
    private bool actionIdReportReady;

    private void DrawActionIdDiagnostics(string[] actions)
    {
        if (!ImGui.CollapsingHeader("Resolvedor de habilidades (v0.9.40)")) return;
        int jobId = (int)Plugin.PlayerState.ClassJob.RowId;
        string key = jobId + "|" + string.Join("|", actions);
        ImGui.TextDisabled("Consulta manual: nenhuma varredura de tabelas durante o redesenho.");
        if (actionIdReportReady && key != actionIdReportKey)
            ImGui.TextColored(new Vector4(1f, .7f, .3f, 1f), "Rotacao/profissao alterada. Clique para atualizar.");
        if (ImGui.Button("Resolver IDs (uma vez)"))
        {
            actionIdReport.Clear();
            actionIdReportKey = key;
            actionIdReportReady = true;
            try
            {
                var general = Plugin.DataManager.GetExcelSheet<Lumina.Excel.Sheets.Action>();
                var crafting = Plugin.DataManager.GetExcelSheet<Lumina.Excel.Sheets.CraftAction>();
                int level = (int)Plugin.PlayerState.Level;
                actionIdReport.Add($"Profissao {jobId} | Nivel {level}");
                var wanted = new HashSet<string>(actions.Select(NormalizeActionName), StringComparer.Ordinal);
                var matches = wanted.ToDictionary(x => x, _ => new List<(uint Id, string Table, int Level)>());
                // Uma passagem por tabela por clique; nenhum processamento caro no Draw seguinte.
                if (general is not null)
                    foreach (var row in general)
                    {
                        string name = NormalizeActionName(row.Name.ToString());
                        if (!matches.TryGetValue(name, out var list)) continue;
                        if (TryGetJobId(row, out int rowJob) && rowJob == jobId)
                            list.Add((row.RowId, "Action", ReadRequiredLevel(row)));
                    }
                if (crafting is not null)
                    foreach (var row in crafting)
                    {
                        string name = NormalizeActionName(row.Name.ToString());
                        if (!matches.TryGetValue(name, out var list)) continue;
                        if (TryGetJobId(row, out int rowJob) && rowJob == jobId)
                            list.Add((row.RowId, "CraftAction", ReadRequiredLevel(row)));
                    }
                foreach (string action in actions.Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    var candidates = matches[NormalizeActionName(action)];
                    if (candidates.Count != 1)
                    {
                        actionIdReport.Add($"[BLOQUEADO] {action}: {candidates.Count} candidatos para a profissao {jobId}.");
                        continue;
                    }
                    var match = candidates[0];
                    if (match.Level < 1 || level < match.Level)
                        actionIdReport.Add($"[BLOQUEADO] {action}: {match.Table} ID {match.Id}, nivel exigido {match.Level} (atual {level}).");
                    else
                        actionIdReport.Add($"[CANDIDATO] {action}: {match.Table} ID {match.Id}, nivel exigido {match.Level}.");
                }
            }
            catch (Exception ex) { actionIdReport.Add("Falha na consulta: " + ex.Message); }
        }
        if (!actionIdReportReady)
            ImGui.TextDisabled("Clique em Resolver IDs para identificar os candidatos da profissao.");
        else
            foreach (var line in actionIdReport) ImGui.TextWrapped(line);
        ImGui.TextDisabled("IDs sao candidatos filtrados; a API de execucao ainda nao foi validada.");
        ImGui.TextDisabled("Somente leitura: nenhuma habilidade e executada.");
        DrawActionTypeProbe();
    }

    // v0.9.40: identifica os tipos de acao conhecidos pelo assembly instalado.
    // Nao acessa ponteiros nem invoca ActionManager.
    private readonly List<string> actionTypeProbeLines = new();
    private bool actionTypeProbeReady;

    private void DrawActionTypeProbe()
    {
        ImGui.Separator();
        ImGui.Text("DIAGNOSTICO DE COMPATIBILIDADE (v0.9.40)");
        ImGui.TextDisabled("Somente leitura. Nenhuma chamada a ActionManager e realizada.");
        if (ImGui.Button("Inspecionar tipos de acao (uma vez)"))
        {
            actionTypeProbeReady = true;
            actionTypeProbeLines.Clear();
            try
            {
                var assembly = typeof(FFXIVClientStructs.FFXIV.Component.GUI.AtkUnitBase).Assembly;
                var type = assembly.GetType("FFXIVClientStructs.FFXIV.Client.Game.ActionType");
                actionTypeProbeLines.Add("Assembly: " + assembly.GetName().Name +
                    " | versao: " + assembly.GetName().Version);
                if (type is null)
                    actionTypeProbeLines.Add("[PENDENTE] Enum ActionType nao encontrado neste assembly.");
                else if (!type.IsEnum)
                    actionTypeProbeLines.Add("[PENDENTE] ActionType encontrado, mas nao e enum.");
                else
                {
                    foreach (string name in Enum.GetNames(type))
                    {
                        if (name.Contains("Craft", StringComparison.OrdinalIgnoreCase) ||
                            name.Contains("Action", StringComparison.OrdinalIgnoreCase) ||
                            name.Contains("General", StringComparison.OrdinalIgnoreCase))
                        {
                            object value = Enum.Parse(type, name);
                            actionTypeProbeLines.Add(name + " = " + Convert.ToUInt64(value));
                        }
                    }
                    if (actionTypeProbeLines.Count == 1)
                        actionTypeProbeLines.Add("Nenhum nome relacionado a crafting/acao encontrado.");
                }
                actionTypeProbeLines.Add("Os valores sao apenas informativos; ainda nao validam o envio.");
            }
            catch (Exception ex)
            {
                actionTypeProbeLines.Add("[ERRO] " + ex.GetType().Name + ": " + ex.Message);
            }
        }
        if (actionTypeProbeReady)
            foreach (string line in actionTypeProbeLines) ImGui.TextWrapped(line);
        DrawActionManagerSignatureProbe();
        DrawActionAvailabilityProbe();
    }

    // v0.9.40: reflection read-only; never instantiates or calls ActionManager.
    private readonly List<string> actionManagerSignatureLines = new();
    private bool actionManagerSignatureReady;

    private void DrawActionManagerSignatureProbe()
    {
        ImGui.Separator();
        ImGui.Text("ACTIONMANAGER - ASSINATURAS (v0.9.40)");
        ImGui.TextDisabled("Somente metadados. Nenhum ponteiro e acessado; nenhum metodo e invocado.");
        if (ImGui.Button("Inspecionar API ActionManager (uma vez)"))
        {
            actionManagerSignatureReady = true;
            actionManagerSignatureLines.Clear();
            try
            {
                var assembly = typeof(FFXIVClientStructs.FFXIV.Component.GUI.AtkUnitBase).Assembly;
                var manager = assembly.GetType("FFXIVClientStructs.FFXIV.Client.Game.ActionManager");
                if (manager is null)
                {
                    actionManagerSignatureLines.Add("[PENDENTE] Tipo ActionManager nao encontrado.");
                }
                else
                {
                    var methods = manager.GetMethods(System.Reflection.BindingFlags.Public |
                        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static |
                        System.Reflection.BindingFlags.DeclaredOnly);
                    var selected = methods.Where(m =>
                        m.Name.Contains("UseAction", StringComparison.OrdinalIgnoreCase) ||
                        m.Name.Contains("GetActionStatus", StringComparison.OrdinalIgnoreCase) ||
                        m.Name.Contains("GetRecast", StringComparison.OrdinalIgnoreCase) ||
                        m.Name.Contains("GetAdjustedActionId", StringComparison.OrdinalIgnoreCase) ||
                        m.Name.Contains("IsAction", StringComparison.OrdinalIgnoreCase))
                        .OrderBy(m => m.Name).Take(40).ToArray();
                    actionManagerSignatureLines.Add("Tipo: " + manager.FullName);
                    actionManagerSignatureLines.Add("Metodos relevantes encontrados: " + selected.Length);
                    foreach (var method in selected)
                    {
                        string parameters = string.Join(", ", method.GetParameters()
                            .Select(p => p.ParameterType.Name + " " + p.Name));
                        actionManagerSignatureLines.Add(method.ReturnType.Name + " " +
                            method.Name + "(" + parameters + ")");
                    }
                    if (selected.Length == 0)
                        actionManagerSignatureLines.Add("Nenhuma assinatura conhecida encontrada: verificar SDK.");
                }
                actionManagerSignatureLines.Add("A existencia do metodo nao comprova que ele seja seguro ou valido para executar crafting.");
            }
            catch (Exception ex)
            {
                actionManagerSignatureLines.Add("[ERRO] " + ex.GetType().Name + ": " + ex.Message);
            }
        }
        if (actionManagerSignatureReady)
            foreach (var line in actionManagerSignatureLines) ImGui.TextWrapped(line);
    }

    // v0.9.40: consulta pontual do status bruto. Nao chama UseAction.
    private readonly List<string> availabilityReport = new();

    private unsafe void DrawActionAvailabilityProbe()
    {
        ImGui.Separator();
        if (ImGui.CollapsingHeader("Historico de paradas do executor (v0.9.40)"))
        {
            ImGui.TextWrapped("Ultima parada: " + executorStopReason);
            ImGui.TextWrapped("Evento que sugeriu ressincronizacao: " + raphaelTracker.ResyncEventDiagnostic);
            if (ImGui.TreeNode("Eventos recentes do chat"))
            {
                ImGui.TextWrapped(raphaelTracker.RecentChatDiagnostic);
                ImGui.TreePop();
            }
            string history = string.Join("\\n", executorTransitionLog);
            ImGui.TextWrapped(history.Length == 0 ? "Nenhuma transicao registrada." : history);
            if (ImGui.Button("Copiar historico de paradas"))
                ImGui.SetClipboardText("Ultima parada: " + executorStopReason + "\n" + history +
                    "\nEvento de resync: " + raphaelTracker.ResyncEventDiagnostic +
                    "\nEventos recentes: " + raphaelTracker.RecentChatDiagnostic);
        }
        if (ImGui.CollapsingHeader("Ultima tentativa do executor (v0.9.40)"))
        {
            ImGui.TextWrapped(actionAttemptDiagnostics);
            if (ImGui.Button("Copiar diagnostico da tentativa"))
                ImGui.SetClipboardText(actionAttemptDiagnostics);
        }
        ImGui.Text("ACTIONMANAGER - STATUS BRUTO (v0.9.40)");
        ImGui.TextDisabled("Consulta somente ao clicar. Nenhuma habilidade e executada.");
        if (ImGui.Button("Consultar status das habilidades (uma vez)"))
        {
            availabilityReport.Clear();
            if (Plugin.ClientState.TerritoryType != 1319)
            {
                availabilityReport.Add("[BLOQUEADO] Fora da Cosmic Exploration.");
            }
            else
            {
                var addon = Plugin.GameGui.GetAddonByName("Synthesis");
                if (addon.IsNull || !addon.IsReady || !addon.IsVisible)
                    availabilityReport.Add("[BLOQUEADO] Nenhuma sintese ativa.");
                else if (raphaelResult is not { Success: true } result ||
                         raphaelRecipeKey != lastRecipeKey)
                    availabilityReport.Add("[BLOQUEADO] Calcule uma rotacao Raphael para a receita atual.");
                else
                {
                    var manager = FFXIVClientStructs.FFXIV.Client.Game.ActionManager.Instance();
                    if (manager == null)
                        availabilityReport.Add("[BLOQUEADO] ActionManager.Instance indisponivel.");
                    else
                    {
                        try
                        {
                            int jobId = (int)Plugin.PlayerState.ClassJob.RowId;
                            int level = (int)Plugin.PlayerState.Level;
                            var wanted = new HashSet<string>(result.Actions.Select(NormalizeActionName));
                            var matches = wanted.ToDictionary(x => x,
                                _ => new List<(uint Id, FFXIVClientStructs.FFXIV.Client.Game.ActionType Type, int Level)>());
                            var general = Plugin.DataManager.GetExcelSheet<Lumina.Excel.Sheets.Action>();
                            var crafting = Plugin.DataManager.GetExcelSheet<Lumina.Excel.Sheets.CraftAction>();
                            if (general is not null)
                                foreach (var row in general)
                                {
                                    string name = NormalizeActionName(row.Name.ToString());
                                    if (matches.TryGetValue(name, out var list) &&
                                        TryGetJobId(row, out int rowJob) && rowJob == jobId)
                                        list.Add((row.RowId, FFXIVClientStructs.FFXIV.Client.Game.ActionType.Action,
                                            ReadRequiredLevel(row)));
                                }
                            if (crafting is not null)
                                foreach (var row in crafting)
                                {
                                    string name = NormalizeActionName(row.Name.ToString());
                                    if (matches.TryGetValue(name, out var list) &&
                                        TryGetJobId(row, out int rowJob) && rowJob == jobId)
                                        list.Add((row.RowId, FFXIVClientStructs.FFXIV.Client.Game.ActionType.CraftAction,
                                            ReadRequiredLevel(row)));
                                }
                            availabilityReport.Add($"Profissao {jobId}, nivel {level}; status bruto do cliente:");
                            foreach (string action in result.Actions.Distinct(StringComparer.OrdinalIgnoreCase))
                            {
                                var candidates = matches[NormalizeActionName(action)];
                                if (candidates.Count != 1 || candidates[0].Level < 1 ||
                                    candidates[0].Level > level)
                                {
                                    availabilityReport.Add($"[BLOQUEADO] {action}: ID ausente, ambiguo ou nivel insuficiente.");
                                    continue;
                                }
                                var match = candidates[0];
                                uint status = manager->GetActionStatus(match.Type, match.Id);
                                availabilityReport.Add($"{action}: {match.Type} ID {match.Id}, status bruto {status}");
                            }
                            availabilityReport.Add("Nao interprete status 0 ou outros valores como garantia de execucao.");
                        }
                        catch (Exception ex)
                        {
                            availabilityReport.Add("[ERRO] " + ex.GetType().Name + ": " + ex.Message);
                        }
                    }
                }
            }
        }
        foreach (string line in availabilityReport) ImGui.TextWrapped(line);
    }

    private static bool TryGetJobId<T>(T row, out int jobId)
    {
        jobId = 0;
        try
        {
            var value = row!.GetType().GetProperty("ClassJob")?.GetValue(row);
            if (value is null) return false;
            var rowId = value.GetType().GetProperty("RowId")?.GetValue(value);
            return int.TryParse((rowId ?? value).ToString(), out jobId) && jobId > 0;
        }
        catch { return false; }
    }

    private static int ReadRequiredLevel<T>(T row)
    {
        try
        {
            var value = row!.GetType().GetProperty("ClassJobLevel")?.GetValue(row);
            return value is not null && int.TryParse(value.ToString(), out int level) ? level : 0;
        }
        catch { return 0; }
    }

    // Inspecao defensiva: os nomes e tipos dos campos variam entre versoes da Lumina.
    // Nao assumimos que a ordem dos IDs equivale a ordem das profissoes.
    private static string DescribeJobMetadata<T>(T row)
    {
        try
        {
            object boxed = row!;
            var properties = boxed.GetType().GetProperties(System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.Instance);
            var found = new List<string>();
            foreach (var prop in properties)
            {
                string n = prop.Name;
                if (!n.Contains("ClassJob", StringComparison.OrdinalIgnoreCase) &&
                    !n.Contains("CraftType", StringComparison.OrdinalIgnoreCase) &&
                    !n.Contains("Job", StringComparison.OrdinalIgnoreCase)) continue;
                object? value = prop.GetValue(boxed);
                if (value is null) continue;
                var rowId = value.GetType().GetProperty("RowId");
                var id = rowId?.GetValue(value);
                found.Add(n + "=" + (id ?? value));
            }
            return found.Count == 0 ? "profissao nao exposta" : string.Join("; ", found);
        }
        catch (Exception ex) { return "metadados indisponiveis: " + ex.GetType().Name; }
    }

    // A mesma chave canonica e usada na busca de IDs, no status bruto e
    // na comparacao da rotacao. Alias explicitos evitam correspondencias vagas.
    private static string NormalizeActionName(string value) =>
        RaphaelMacroFormatter.NormalizeForLookup(value);

    private void UpdateExecutorMonitor()
    {
        if (!executorMonitoring) return;
        if (Plugin.ClientState.TerritoryType != 1319 ||
            executorRecipeKey != lastRecipeKey || !raphaelTracker.Active)
        {
            RecordExecutorTransition("Monitor interrompido: sintese ou rastreador indisponivel.");
            executorMonitoring = false;
            automaticMode = false;
            automaticWasStarted = false;
            executorMessage = "Monitor interrompido: sintese ou rastreador indisponivel.";
            return;
        }
        if (raphaelTracker.Diverged || raphaelTracker.NeedsResync)
        {
            RecordExecutorTransition("Monitor interrompido: possivel divergencia. " +
                raphaelTracker.ResyncEventDiagnostic);
            executorMonitoring = false;
            automaticMode = false;
            automaticWasStarted = false;
            executorMessage = "Monitor interrompido: possivel divergencia. Verifique o jogo.";
        }
        else if (raphaelTracker.Finished)
        {
            RecordExecutorTransition("Sequencia observada como concluida.");
            executorMonitoring = false;
            automaticMode = false;
            executorMessage = "Sequencia observada como concluida. Confirme o resultado no jogo.";
        }
    }

    private void DrawExecutorMonitor(string[] actions, CraftingState state)
    {
        if (showAdvancedPanels)
            ImGui.TextDisabled("Execucao experimental: uma habilidade por confirmacao.");
        bool atStart = state.Progress == 0 && state.Quality == 0 &&
            state.Durability == state.MaxDurability && state.Cp == state.MaxCp;
        bool canStart = atStart && actions.Length > 0 && !executorMonitoring &&
            !raphaelTracker.Active && lastRecipeKey.Length > 0 &&
            raphaelRecipeKey == lastRecipeKey && raphaelResult is { Success: true } &&
            Plugin.ClientState.TerritoryType == 1319;
        if (!canStart) ImGui.BeginDisabled();
        if (ImGui.Button(T("Iniciar automatico"), new Vector2(-1, 32)))
        {
            chatReader.ClearActionObservations();
            raphaelTracker.Start(lastRecipeKey, actions, state);
            executorRecipeKey = lastRecipeKey;
            executorMonitoring = true;
            executorPaused = false;
            executorTransitionLog.Clear();
            executorStopReason = "Execucao iniciada; nenhuma parada registrada.";
            singleActionSafetyLock = false;
            singleActionPending = false;
            trainedPerfectionConfirmationPending = false;
            trainedPerfectionObservedFrames = 0;
            singleActionConfirmedCount = 0;
            automaticMode = true;
            automaticWasStarted = true;
            automaticNextSendAt = DateTime.UtcNow.AddSeconds(AutomaticStepDelaySeconds);
            automaticLastState = "Monitor e automatico iniciados em um clique.";
            singleActionReport = "Aguardando intervalo antes da primeira habilidade.";
            actionAttemptDiagnostics = "Nova execucao iniciada; nenhuma habilidade enviada ainda.";
            executorMessage = "Execucao automatica ativa: cada acao requer confirmacao.";
        }
        if (!canStart) ImGui.EndDisabled();
        // Apenas espacamento entre Iniciar e Pausar/Parar.
        // Evita uma linha horizontal atravessando os botoes.
        ImGui.Spacing();
        if (!canStart && !executorMonitoring && !raphaelTracker.Finished && showAdvancedPanels)
            ImGui.TextDisabled("Disponivel apenas no passo 1, com sintese aberta e rotacao Raphael valida.");
        if (executorMonitoring)
        {
            if (singleActionSafetyLock) ImGui.BeginDisabled();
            // Divide a largura da coluna igualmente entre Pausar e Parar.
            float controlWidth = Math.Max(80f,
                (ImGui.GetContentRegionAvail().X - ImGui.GetStyle().ItemSpacing.X) / 2f);
            if (ImGui.Button(
                executorPaused ? "Continuar" : "Pausar",
                new Vector2(controlWidth, 30f)))
            {
                executorPaused = !executorPaused;
                executorMessage = executorPaused ? "Monitor pausado." : "Monitor retomado.";
                automaticLastState = executorMessage;
                if (!executorPaused && automaticMode)
                    automaticNextSendAt = DateTime.UtcNow.AddSeconds(AutomaticStepDelaySeconds);
            }
            if (singleActionSafetyLock) ImGui.EndDisabled();
            ImGui.SameLine();
            if (ImGui.Button(T("Parar"), new Vector2(controlWidth, 30f)))
            {
                RecordExecutorTransition("Parada solicitada pelo usuario.");
                executorMonitoring = false;
                automaticMode = false;
                automaticWasStarted = false;
                automaticLastState = "Interrompido pelo usuario.";
                singleActionPending = false;
                raphaelTracker.Reset();
                chatReader.ClearActionObservations();
                executorMessage = "Monitor encerrado pelo usuario.";
            }
        }
        if (raphaelTracker.Active && executorRecipeKey == lastRecipeKey)
        {
            int step = Math.Clamp(raphaelTracker.Step, 0, actions.Length);
            ImGui.Text($"{step}/{actions.Length} habilidades confirmadas");
            if (!raphaelTracker.Finished)
                ImGui.TextWrapped($"Proxima habilidade: {raphaelTracker.NextAction}");
        }
        string executionState = singleActionSafetyLock ? "BLOQUEADO - requer nova sintese"
            : singleActionPending ? "AGUARDANDO CONFIRMACAO"
            : executorPaused ? "PAUSADO"
            : automaticMode ? "AUTOMATICO - aguardando proximo envio"
            : raphaelTracker.Finished ? "SEQUENCIA CONFIRMADA"
            : executorMonitoring ? "MONITOR ATIVO - automatico desligado"
            : "PARADO";
        ImGui.TextWrapped("Estado: " + executionState);
        if (!automaticMode && !singleActionPending && raphaelTracker.Active && !raphaelTracker.Finished)
            ImGui.TextWrapped("Ultima parada: " + executorStopReason);
        if (raphaelTracker.Finished && showAdvancedPanels)
            ImGui.TextWrapped("Ultima fabricacao: rotacao confirmada. Confira o resultado no jogo.");
        if (showAdvancedPanels) ImGui.Checkbox("Mostrar detalhes do executor", ref showExecutorDetails);
        if (showAdvancedPanels && showExecutorDetails)
        {
            ImGui.TextWrapped("Ultimo evento: " + automaticLastState);
            ImGui.TextWrapped("Monitor: " + executorMessage);
            ImGui.TextDisabled("Pausar ou parar impede novos envios, mas nao cancela uma acao ja solicitada.");
            ImGui.TextDisabled($"Acoes confirmadas pelo teste: {singleActionConfirmedCount}");
            ImGui.TextWrapped(singleActionReport);
        }
        if (automaticMode)
        {
            ImGui.Spacing();

if (ImGui.Button(
    T("Desativar automatico"),
    new Vector2(-1f, 30f)))
            {
                RecordExecutorTransition("Automatico desativado pelo usuario.");
                automaticMode = false;
                automaticWasStarted = false;
                automaticLastState = "Automatico desativado manualmente.";
                singleActionReport = "Automatico desativado: nenhum novo envio sera iniciado.";
            }
        }
        if (showAdvancedPanels && ImGui.CollapsingHeader("Controles avancados de teste"))
        {
            bool canSend = executorMonitoring && !executorPaused && !automaticMode &&
                !singleActionPending && !singleActionSafetyLock && raphaelTracker.Active &&
                !raphaelTracker.Finished && !raphaelTracker.Diverged &&
                !raphaelTracker.NeedsResync && executorRecipeKey == lastRecipeKey;
            if (!canSend) ImGui.BeginDisabled();
            if (ImGui.Button("Executar proxima habilidade (TESTE)"))
                TrySendSingleAction(actions);
            if (!canSend) ImGui.EndDisabled();
            ImGui.TextDisabled($"Acoes confirmadas nesta execucao: {singleActionConfirmedCount}");
        }
        if (singleActionSafetyLock) ImGui.TextColored(new Vector4(1f, .4f, .3f, 1f), "BLOQUEIO DE SEGURANCA: pare o monitor e inicie uma nova fabricacao.");
    }

    // Resolve the effect from Lumina rather than hardcoding an unverified status ID.
    // A unique status ID is required. Failure means the ordinary timeout lock remains.
    private uint ResolveTrainedPerfectionStatusId()
    {
        var sheet = Plugin.DataManager.GetExcelSheet<Lumina.Excel.Sheets.Status>();
        if (sheet is null) return 0;
        uint found = 0;
        foreach (var row in sheet)
        {
            if (NormalizeActionName(row.Name.ToString()) != "TRAINEDPERFECTION") continue;
            if (found != 0) return 0;
            found = row.RowId;
        }
        return found;
    }

    private bool HasCraftingStatus(uint statusId)
    {
        if (statusId == 0) return false;
        var player = Plugin.ObjectTable.LocalPlayer;
        if (player is null) return false;
        foreach (var status in player.StatusList)
            if (status.StatusId == statusId) return true;
        return false;
    }

    private void CheckTrainedPerfectionConfirmation(CraftingState state)
    {
        if (!singleActionPending || !trainedPerfectionConfirmationPending ||
            !executorMonitoring || executorPaused || singleActionSafetyLock ||
            !raphaelTracker.Active || raphaelTracker.Diverged || raphaelTracker.NeedsResync ||
            raphaelTracker.Step != singleActionStep ||
            trainedPerfectionStatusId == 0 || !trainedPerfectionBuffWasAbsent)
            return;
        var player = Plugin.ObjectTable.LocalPlayer;
        if (player is null || !HasCraftingStatus(trainedPerfectionStatusId))
        {
            trainedPerfectionObservedFrames = 0;
            return;
        }
        // Trained Perfection costs 0 CP. Confirm only a newly observed,
        // persistent buff following our own accepted UseAction request.
        // The tracker still rejects divergent or resync-required states.
        if (trainedPerfectionObservedFrames++ == 0)
        {
            trainedPerfectionObservedAt = DateTime.UtcNow;
            return;
        }
        if ((DateTime.UtcNow - trainedPerfectionObservedAt).TotalMilliseconds < 250)
            return;
        // A unique newly applied status is action-specific evidence for this
        // one zero-CP action. Never apply this rule to other actions.
        if (raphaelTracker.ConfirmVerifiedStatus(state, "Trained Perfection"))
        {
            trainedPerfectionConfirmationPending = false;
            actionAttemptDiagnostics +=
                $"\nConfirmacao complementar: status ID {trainedPerfectionStatusId} apareceu " +
                "apos o envio e permaneceu estavel (habilidade de 0 CP).";
        }
    }

    private unsafe void TrySendSingleAction(string[] actions)
    {
        if (!executorMonitoring || executorPaused || singleActionPending || singleActionSafetyLock ||
            !raphaelTracker.Active || raphaelTracker.Finished ||
            raphaelTracker.Diverged || raphaelTracker.NeedsResync ||
            Plugin.ClientState.TerritoryType != 1319 ||
            executorRecipeKey != lastRecipeKey ||
            raphaelRecipeKey != lastRecipeKey ||
            raphaelResult is not { Success: true })
        {
            singleActionReport = "[BLOQUEADO] Monitor, rotacao ou estado invalido.";
            return;
        }
        var addon = Plugin.GameGui.GetAddonByName("Synthesis");
        if (addon.IsNull || !addon.IsReady || !addon.IsVisible ||
            !SynthesisReader.TryRead(addon, out var current))
        {
            singleActionReport = "[BLOQUEADO] Sintese nao esta ativa.";
            return;
        }
        int step = raphaelTracker.Step;
        if (step < 0 || step >= actions.Length ||
            NormalizeActionName(raphaelTracker.NextAction) != NormalizeActionName(actions[step]))
        {
            singleActionReport = "[BLOQUEADO] Proximo passo inconsistente.";
            return;
        }
        try
        {
            string wanted = NormalizeActionName(actions[step]);
            if (!RaphaelMacroFormatter.IsKnownAction(actions[step]))
            {
                singleActionReport = $"[BLOQUEADO] Habilidade fora do catalogo: {actions[step]}.";
                actionAttemptDiagnostics = singleActionReport + " Nenhuma acao enviada.";
                return;
            }
            actionAttemptDiagnostics = $"Instante UTC: {DateTime.UtcNow:O}\n" +
                $"Profissao ID: {Plugin.PlayerState.ClassJob.RowId}; nivel: {Plugin.PlayerState.Level}\n" +
                $"Passo: {step + 1}/{actions.Length}; habilidade: {actions[step]}\n" +
                $"Sintese: progresso={current.Progress}/{current.MaxProgress}; " +
                $"qualidade={current.Quality}; durabilidade={current.Durability}/{current.MaxDurability}\n" +
                $"Chave de busca: {wanted}\nAcao ainda nao enviada.";
            int jobId = (int)Plugin.PlayerState.ClassJob.RowId;
            int level = (int)Plugin.PlayerState.Level;
            var candidates = new List<(uint Id, FFXIVClientStructs.FFXIV.Client.Game.ActionType Type, int Level)>();
            var general = Plugin.DataManager.GetExcelSheet<Lumina.Excel.Sheets.Action>();
            var crafting = Plugin.DataManager.GetExcelSheet<Lumina.Excel.Sheets.CraftAction>();
            if (general is not null)
                foreach (var row in general)
                    if (NormalizeActionName(row.Name.ToString()) == wanted &&
                        TryGetJobId(row, out int rowJob) && rowJob == jobId)
                        candidates.Add((row.RowId, FFXIVClientStructs.FFXIV.Client.Game.ActionType.Action, ReadRequiredLevel(row)));
            if (crafting is not null)
                foreach (var row in crafting)
                    if (NormalizeActionName(row.Name.ToString()) == wanted &&
                        TryGetJobId(row, out int rowJob) && rowJob == jobId)
                        candidates.Add((row.RowId, FFXIVClientStructs.FFXIV.Client.Game.ActionType.CraftAction, ReadRequiredLevel(row)));
            actionAttemptDiagnostics += $"\nCandidatos de ID: {candidates.Count}.";
            if (candidates.Count == 1)
                actionAttemptDiagnostics += $" Tipo={candidates[0].Type}; ID={candidates[0].Id}; nivel exigido={candidates[0].Level}.";
            if (candidates.Count != 1 || candidates[0].Level < 1 || candidates[0].Level > level)
            {
                actionAttemptDiagnostics += "\nBloqueio: ID ausente/ambiguo ou nivel insuficiente; UseAction NAO chamado.";
                singleActionReport = "[BLOQUEADO] ID ambiguo, ausente ou nivel insuficiente.";
                return;
            }
            var candidate = candidates[0];
            var manager = FFXIVClientStructs.FFXIV.Client.Game.ActionManager.Instance();
            if (manager == null)
            {
                actionAttemptDiagnostics += "\nBloqueio: ActionManager nulo; UseAction NAO chamado.";
                singleActionReport = "[BLOQUEADO] ActionManager indisponivel.";
                return;
            }
            uint status = manager->GetActionStatus(candidate.Type, candidate.Id);
            actionAttemptDiagnostics += $"\nGetActionStatus antes do envio: {status}.";
            if (status != 0)
            {
                actionAttemptDiagnostics += "\nBloqueio: status diferente de zero; UseAction NAO chamado.";
                singleActionReport = $"[BLOQUEADO] {actions[step]} status {status}; nenhuma acao enviada.";
                return;
            }
            // Only Trained Perfection uses a second, independently observed signal.
            trainedPerfectionConfirmationPending = false;
            trainedPerfectionObservedFrames = 0;
            if (wanted == "TRAINEDPERFECTION")
            {
                trainedPerfectionStatusId = ResolveTrainedPerfectionStatusId();
                trainedPerfectionBuffWasAbsent = trainedPerfectionStatusId != 0 &&
                    Plugin.ObjectTable.LocalPlayer is not null &&
                    !HasCraftingStatus(trainedPerfectionStatusId);
                actionAttemptDiagnostics +=
                    $"\nEfeito Trained Perfection: status ID={trainedPerfectionStatusId}; " +
                    $"ausente antes={trainedPerfectionBuffWasAbsent}; habilidade de 0 CP.";
            }
            actionStatusesBefore = new HashSet<uint>();
            var statusPlayer = Plugin.ObjectTable.LocalPlayer;
            if (statusPlayer is not null)
                foreach (var effect in statusPlayer.StatusList)
                    if (effect.StatusId != 0) actionStatusesBefore.Add(effect.StatusId);
            actionNameForStatusDiagnostic = actions[step];
            actionAttemptDiagnostics +=
                $"\nSnapshot de status anterior: {actionStatusesBefore.Count} efeito(s) ativo(s).";
            // UseAction retorna se o cliente aceitou a solicitacao; nao prova a execucao.
            bool accepted = manager->UseAction(candidate.Type, candidate.Id);
            actionAttemptDiagnostics += $"\nUseAction chamado uma vez. Retorno: {accepted}.";
            if (!accepted)
            {
                singleActionReport = $"[FALHOU] UseAction recusou {actions[step]}; nenhuma repeticao automatica.";
                executorPaused = true;
                singleActionSafetyLock = true;
                return;
            }
            actionAttemptDiagnostics += "\nAguardando confirmacao do rastreador (sem reenvio automatico).";
            singleActionPending = true;
            trainedPerfectionConfirmationPending = wanted == "TRAINEDPERFECTION" &&
                trainedPerfectionStatusId != 0 && trainedPerfectionBuffWasAbsent;
            automaticLastState = $"Habilidade solicitada: {actions[step]}. Aguardando confirmacao.";
            singleActionStep = step;
            singleActionSentAt = DateTime.UtcNow;
            singleActionReport = $"[ENVIADO] {actions[step]} ({candidate.Type}, {candidate.Id}). Aguardando confirmacao pelo chat.";
        }
        catch (Exception ex)
        {
            actionAttemptDiagnostics += $"\nExcecao: {ex.GetType().Name}: {ex.Message}.";
            executorPaused = true;
            singleActionSafetyLock = true;
            automaticMode = false;
            singleActionReport = "[ERRO] " + ex.GetType().Name + ": " + ex.Message;
        }
    }

    private void UpdateRaphaelTracking(CraftingState state)
    {
        if (!raphaelTracker.Active) return;
        if (raphaelTracker.Key != lastRecipeKey)
        {
            raphaelTracker.Reset();
            chatReader.ClearActionObservations();
            return;
        }
        raphaelTracker.Observe(state);
        while (chatReader.TryDequeueAction(out var action))
            raphaelTracker.ObserveAction(action, state);
    }

    private void DrawRaphaelTracking(CraftingState state, RaphaelBridge.Result result, string condition)
    {
        ImGui.Separator();
        ImGui.Text("ACOMPANHAMENTO ASSISTIDO (v0.9.4)");
        ImGui.TextDisabled("Mudancas de P/Q/D/CP nao contam como passos; sem evento de chat, confirme manualmente.");
        if (!raphaelTracker.Active)
        {
            bool atStart = state.Progress == 0 && state.Quality == 0 &&
                state.Durability == state.MaxDurability && state.Cp == state.MaxCp;
            if (!atStart) ImGui.TextDisabled("Inicie o acompanhamento no primeiro passo da sintese.");
            if (atStart && ImGui.Button("Iniciar acompanhamento"))
            {
                chatReader.ClearActionObservations();
                raphaelTracker.Start(lastRecipeKey, result.Actions, state);
            }
            return;
        }
        // A observacao acontece em UpdateRaphaelTracking, fora do painel.
        ImGui.TextWrapped("Deteccao experimental pelo chat de crafting em ingles; se o jogo nao emitir o evento, use confirmacao manual.");
        if (raphaelTracker.NeedsResync || raphaelTracker.Diverged)
            ImGui.TextColored(new Vector4(1f, .45f, .3f, 1f), raphaelTracker.DetectionStatus);
        else
            ImGui.TextWrapped(raphaelTracker.DetectionStatus);
        int totalSteps = result.Actions.Length;
        int confirmedSteps = Math.Clamp(raphaelTracker.Step, 0, totalSteps);
        ImGui.ProgressBar(totalSteps > 0 ? (float)confirmedSteps / totalSteps : 0f,
            new Vector2(-1, 20), $"{confirmedSteps}/{totalSteps} passos confirmados");
        if (!raphaelTracker.Finished && !raphaelTracker.Diverged)
        {
            ImGui.TextColored(new Vector4(.3f, .9f, 1f, 1f),
                $"PROXIMA HABILIDADE: {raphaelTracker.NextAction}");
            ImGui.TextDisabled($"Passo sugerido: {confirmedSteps + 1} de {totalSteps}");
        }
        // Exibicao somente leitura: nao altera a confirmacao dos passos.
        ImGui.Separator();
        ImGui.Text("SEQUENCIA DA ROTACAO");
        for (int i = 0; i < result.Actions.Length; i++)
        {
            string actionLabel = $"{i + 1}. {result.Actions[i]}";
            if (i < confirmedSteps)
                ImGui.TextColored(new Vector4(.35f, .85f, .45f, 1f), $"[OK] {actionLabel}");
            else if (i == confirmedSteps && !raphaelTracker.Diverged)
                ImGui.TextColored(new Vector4(.3f, .9f, 1f, 1f), $">> {actionLabel}  (PROXIMA)");
            else
                ImGui.TextDisabled(actionLabel);
        }
        ImGui.Separator();
        ImGui.Text($"Recursos atuais: CP {state.Cp}/{state.MaxCp} | Durabilidade {state.Durability}/{state.MaxDurability}");
        if (!raphaelTracker.Finished && !raphaelTracker.Diverged && state.Cp <= 0)
            ImGui.TextColored(new Vector4(1f, .75f, .25f, 1f),
                "Aviso: CP esgotado. Verifique o custo da proxima habilidade no jogo.");
        if (!raphaelTracker.Finished && !raphaelTracker.Diverged && state.Durability <= 0)
            ImGui.TextColored(new Vector4(1f, .45f, .3f, 1f),
                "Aviso: durabilidade zerada. Verifique se a sintese ainda esta ativa.");
        // Validacao informativa: nunca altera a rotacao nem o rastreador.
        if (!raphaelTracker.Finished && !raphaelTracker.Diverged)
            DrawNextActionValidation(raphaelTracker.NextAction, state, condition, raphaelTracker.Step, result.Actions);
        DrawRealCraftingStatuses();
        ImGui.TextDisabled("Status reais: lista de efeitos do personagem; estimativas da rotacao sao separadas.");
        if (ImGui.CollapsingHeader("Diagnostico de acompanhamento##RaphaelTracking"))
        {
            ImGui.Text($"Ultima habilidade detectada: {(string.IsNullOrEmpty(raphaelTracker.LastDetected) ? "nenhuma" : raphaelTracker.LastDetected)}");
            ImGui.Text($"Estado: {(raphaelTracker.Diverged ? "divergencia manual" : raphaelTracker.Finished ? "sequencia concluida" : raphaelTracker.Paused ? "aguardando confirmacao" : "aguardando evento")}");
            ImGui.Text($"Progresso real: {state.Progress}/{state.MaxProgress}");
            ImGui.Text($"Qualidade real: {state.Quality}/{state.MaxQuality}");
            ImGui.Text($"Durabilidade: {state.Durability}/{state.MaxDurability} | CP: {state.Cp}/{state.MaxCp}");
            ImGui.TextDisabled("Contador de passos confirmados; nao e o passo oficial do jogo.");
            ImGui.TextWrapped(raphaelTracker.SyncDiagnostic);
            ImGui.TextDisabled("Atualizacao independe da secao Raphael expandida; a janela principal ainda precisa estar aberta.");
            if (raphaelTracker.MultipleTransitions)
                ImGui.TextColored(new Vector4(1f, .75f, .25f, 1f),
                    "Mudancas separadas sem confirmacao: sincronizacao manual necessaria.");
        }
        if (!string.IsNullOrEmpty(raphaelTracker.SuspectedAction))
            ImGui.TextWrapped($"Evento nao confirmado: {raphaelTracker.SuspectedAction}. Confira a sequencia antes de prosseguir.");
        if (raphaelTracker.NeedsResync)
        {
            ImGui.Separator();
            ImGui.TextColored(new Vector4(1f, .75f, .25f, 1f), "POSSIVEL PERDA DE SINCRONIZACAO");
            ImGui.TextWrapped($"O chat detectou {raphaelTracker.ResyncAction} (passo {raphaelTracker.ResyncStep + 1}), " +
                $"mas o rastreador ainda espera {raphaelTracker.NextAction}. Nao avance sem verificar a sequencia real.");
            if (ImGui.Button("Confirmar que executei ate essa habilidade"))
            {
                raphaelTracker.ConfirmResync(state);
                chatReader.ClearActionObservations();
            }
            ImGui.SameLine();
            if (ImGui.Button("Ignorar sugestao de realinhamento")) raphaelTracker.DismissResync();
        }
        if (raphaelTracker.Finished)
        {
            ImGui.TextColored(new Vector4(.2f, 1f, .4f, 1f), "Sequencia marcada como concluida (verifique no jogo).");
        }
        else if (raphaelTracker.Paused)
        {
            ImGui.TextWrapped("Mudanca observada. Confirme a habilidade usada; se foi diferente, marque divergencia.");
            ImGui.Text($"Esperada: {raphaelTracker.NextAction}");
            if (ImGui.Button("Confirmei a acao esperada")) raphaelTracker.ConfirmExpected(state);
            ImGui.SameLine();
            if (ImGui.Button("Acao diferente / divergencia")) raphaelTracker.MarkDivergence();
        }
        else if (raphaelTracker.Diverged)
        {
            ImGui.TextColored(new Vector4(1f, .45f, .3f, 1f), "Rotacao interrompida por divergencia. Recalcule antes de continuar.");
        }
        else
        {
            ImGui.Text($"Proximo passo {raphaelTracker.Step + 1}/{result.Actions.Length}: {raphaelTracker.NextAction}");
            ImGui.TextDisabled("Aguardando mudanca no estado da sintese...");
            if (ImGui.Button("Marcar divergencia")) raphaelTracker.MarkDivergence();
        }
        if (ImGui.Button("Encerrar acompanhamento")) raphaelTracker.Reset();
    }

    // Custos nominais conhecidos. Valores informativos, nao substituem validacao do jogo.
    // Acoes nao mapeadas sao explicitamente marcadas como desconhecidas.
    private static (int Cp, int Durability)? KnownActionCost(string action)
    {
        return action switch
        {
            "MuscleMemory" => (6, 10),
            "BasicSynthesis" => (0, 10),
            "CarefulSynthesis" => (7, 10),
            "PrudentSynthesis" => (18, 5),
            "Groundwork" => (18, 20),
            "BasicTouch" => (18, 10),
            "StandardTouch" => (32, 10),
            "AdvancedTouch" => (46, 10),
            "PrudentTouch" => (25, 5),
            "PreparatoryTouch" => (40, 20),
            "ByregotsBlessing" => (24, 10),
            "Innovation" => (18, 0),
            "Veneration" => (18, 0),
            "GreatStrides" => (32, 0),
            "WasteNot" => (56, 0),
            "WasteNotII" => (98, 0),
            "Manipulation" => (96, 0),
            "MastersMend" => (88, 0),
            "Observe" => (7, 0),
            "TricksOfTheTrade" => (0, 0),
            "FinalAppraisal" => (1, 0),
            _ => null
        };
    }

    private static void DrawNextActionValidation(string action, CraftingState state, string condition, int step, string[] actions)
    {
        string normalized = new string(action.Where(char.IsLetterOrDigit).ToArray());
        var cost = KnownActionCost(normalized);
        ImGui.Text("VERIFICACAO DA PROXIMA HABILIDADE (informativa)");
        if (!cost.HasValue)
        {
            ImGui.TextColored(new Vector4(1f, .75f, .25f, 1f),
                $"Custo de {action} desconhecido. Verifique no jogo.");
            return;
        }
        // Condicao vem do SynthesisReader (AtkValue[13]); nao depende do rastreador.
        string currentCondition = (condition ?? "").Trim();
        ImGui.Text($"Condicao lida: {(currentCondition.Length == 0 ? "desconhecida" : currentCondition)}");
        bool good = currentCondition.Equals("Good", StringComparison.OrdinalIgnoreCase);
        bool excellent = currentCondition.Equals("Excellent", StringComparison.OrdinalIgnoreCase);
        bool poor = currentCondition.Equals("Poor", StringComparison.OrdinalIgnoreCase);
        if (normalized == "TricksOfTheTrade" || normalized == "PreciseTouch" || normalized == "IntensiveSynthesis")
        {
            if (!good && !excellent)
                ImGui.TextColored(new Vector4(1f, .45f, .3f, 1f),
                    $"ATENCAO: {action} requer Good ou Excellent; condicao atual: {currentCondition}.");
            else
                ImGui.TextColored(new Vector4(.35f, .9f, .5f, 1f),
                    "Condicao especial exigida identificada (Good/Excellent).");
        }
        if (poor && (normalized.Contains("Touch", StringComparison.OrdinalIgnoreCase) ||
                     normalized == "ByregotsBlessing"))
            ImGui.TextColored(new Vector4(1f, .75f, .25f, 1f),
                "Condicao Poor: ganhos de qualidade podem ser reduzidos.");
        if (excellent)
            ImGui.TextColored(new Vector4(.35f, .9f, .5f, 1f),
                "Condicao Excellent: confira se a rotacao aproveita o bonus de qualidade.");
        // Snapshot somente leitura. A ausencia de status com personagem indisponivel
        // nao deve ser interpretada como buff ausente.
        var player = Plugin.ObjectTable.LocalPlayer;
        var activeStatuses = new Dictionary<uint, int>();
        if (player is not null)
        {
            foreach (var effect in player.StatusList)
                if (effect.StatusId != 0) activeStatuses[effect.StatusId] = effect.Param;
        }
        bool HasStatus(uint id) => activeStatuses.ContainsKey(id);
        bool wasteNot = HasStatus(463);
        bool innovation = HasStatus(2189);
        bool veneration = HasStatus(2226);
        bool greatStrides = HasStatus(254);
        bool innerQuiet = HasStatus(251);
        ImGui.Text("VALIDACAO COM STATUS REAIS (somente leitura)");
        if (player is null)
            ImGui.TextDisabled("Status indisponiveis; nenhuma conclusao sobre buffs.");
        else
        {
            ImGui.Text($"Innovation: {(innovation ? "ativo" : "ausente")} | Veneration: {(veneration ? "ativo" : "ausente")} | Great Strides: {(greatStrides ? "ativo" : "ausente")}");
            ImGui.Text($"Waste Not: {(wasteNot ? "ativo" : "ausente")} | Inner Quiet: {(innerQuiet ? "ativo" : "ausente")}");
            if ((normalized == "PrudentTouch" || normalized == "PrudentSynthesis") && wasteNot)
                ImGui.TextColored(new Vector4(1f, .4f, .3f, 1f),
                    "INCOMPATIVEL: Prudent nao pode ser usado com Waste Not ativo.");
            if (normalized == "ByregotsBlessing")
            {
                if (!innerQuiet)
                    ImGui.TextColored(new Vector4(1f, .4f, .3f, 1f),
                        "ATENCAO: Inner Quiet nao aparece nos status ativos.");
                else
                    ImGui.Text($"Inner Quiet confirmado; parametro bruto: {activeStatuses[251]} (validar stacks no jogo).");
            }
            if (normalized.Contains("Touch", StringComparison.OrdinalIgnoreCase) || normalized == "ByregotsBlessing")
                ImGui.TextDisabled(innovation ? "Innovation confirmado: bonus de qualidade ativo." : "Innovation nao detectado; nao aplicar bonus de Innovation.");
            if (normalized.Contains("Synthesis", StringComparison.OrdinalIgnoreCase) || normalized == "Groundwork")
                ImGui.TextDisabled(veneration ? "Veneration confirmado: bonus de progresso ativo." : "Veneration nao detectado.");
        }
        // Estimativa de buffs a partir de passos confirmados, independente dos status reais.
        // Nunca deduzimos que um buff esta realmente ativo: falhas, efeitos especiais
        // e realinhamentos manuais podem invalidar a estimativa.
        var buffDurations = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["Innovation"] = 4, ["Veneration"] = 4, ["GreatStrides"] = 3,
            ["WasteNot"] = 4, ["WasteNotII"] = 8, ["Manipulation"] = 8,
            ["MuscleMemory"] = 5
        };
        var possibleBuffs = new List<string>();
        for (int i = 0; i < Math.Min(step, actions.Length); i++)
        {
            string prior = new string(actions[i].Where(char.IsLetterOrDigit).ToArray());
            if (buffDurations.TryGetValue(prior, out int duration))
            {
                int remaining = duration - (step - i - 1);
                if (remaining > 0) possibleBuffs.Add($"{actions[i]} (~{remaining} acoes)");
            }
        }
        ImGui.TextWrapped(possibleBuffs.Count > 0
            ? "Buffs possiveis pela rotacao (NAO confirmados): " + string.Join(", ", possibleBuffs)
            : "Nenhum buff inferido dos passos confirmados.");
        if (normalized == "PrudentTouch" || normalized == "PrudentSynthesis")
            ImGui.TextColored(new Vector4(1f, .75f, .25f, 1f),
                "Prudent nao pode ser usado sob Waste Not; confira buffs reais.");
        var required = cost.Value;
        ImGui.Text($"Custo nominal de {action}: CP {required.Cp} | Durabilidade {required.Durability}");
        if (state.Cp < required.Cp)
            ImGui.TextColored(new Vector4(1f, .4f, .3f, 1f),
                $"CP insuficiente: {state.Cp} disponivel, {required.Cp} necessario.");
        else
            ImGui.TextColored(new Vector4(.4f, .9f, .5f, 1f), "CP nominal suficiente.");
        // Waste Not reduz o consumo nominal pela metade, arredondando para cima.
        // Outras reducoes e particularidades da acao nao sao calculadas aqui.
        int effectiveDurability = required.Durability;
        if (player is not null && wasteNot && effectiveDurability > 0)
        {
            effectiveDurability = (effectiveDurability + 1) / 2;
            ImGui.Text($"Waste Not confirmado: durabilidade estimada {effectiveDurability} (base {required.Durability}).");
        }
        if (required.Durability > 0 && state.Durability < effectiveDurability)
            ImGui.TextColored(new Vector4(1f, .75f, .25f, 1f),
                $"Durabilidade abaixo do consumo estimado ({state.Durability}/{effectiveDurability}). Confira no jogo.");
        else if (required.Durability > 0)
            ImGui.TextDisabled("Durabilidade suficiente pela estimativa (confira efeitos especiais).");
        else
            ImGui.TextDisabled("Esta habilidade nao consome durabilidade nominalmente.");
        if (normalized == "MuscleMemory" && state.Progress > 0)
            ImGui.TextColored(new Vector4(1f, .75f, .25f, 1f),
                "Muscle Memory exige o primeiro passo da sintese.");
        if (normalized == "ByregotsBlessing")
            ImGui.TextColored(new Vector4(1f, .75f, .25f, 1f),
                "Confira stacks de Inner Quiet antes de usar Byregot's Blessing.");
        if (normalized == "StandardTouch" || normalized == "AdvancedTouch")
            ImGui.TextDisabled("Combos podem reduzir o custo de CP; o valor acima e o custo base.");
        if (normalized == "TricksOfTheTrade")
            ImGui.TextDisabled("Tricks of the Trade depende da condicao da sintese.");
    }

    // Leitura somente leitura dos status aplicados ao personagem. IDs sao uma
    // tabela inicial de crafting: confira os IDs no diagnostico apos patches.
    // Nao altera o tracker, nao confirma passos e nao executa habilidades.
    private static readonly Dictionary<uint, string> CraftingStatusNames = new()
    {
        [2189] = "Innovation", [2226] = "Veneration", [254] = "Great Strides",
        [463] = "Waste Not", [1164] = "Manipulation", [251] = "Inner Quiet",
        [2191] = "Muscle Memory"
    };

    private static void DrawRealCraftingStatuses()
    {
        ImGui.Separator();
        ImGui.Text("BUFFS REAIS DO PERSONAGEM (v0.9.4)");
        var player = Plugin.ObjectTable.LocalPlayer;
        if (player is null)
        {
            ImGui.TextDisabled("Personagem indisponivel; leitura de buffs nao realizada.");
            return;
        }

        int known = 0;
        var raw = new List<string>();
        foreach (var status in player.StatusList)
        {
            uint id = status.StatusId;
            if (id == 0) continue;
            raw.Add($"{id} (param={status.Param}, tempo={status.RemainingTime:0.0}s)");
            if (!CraftingStatusNames.TryGetValue(id, out var name)) continue;
            known++;
            ImGui.TextColored(new Vector4(.35f, .9f, .5f, 1f),
                $"{name} [ID {id}] | param {status.Param} | tempo {status.RemainingTime:0.0}s");
        }
        if (known == 0)
            ImGui.TextDisabled("Nenhum status com ID de crafting reconhecido neste instante.");
        ImGui.TextDisabled("Param e um valor bruto: pode representar stacks ou turnos, conforme o status. Tempo 0,0s nao indica expiracao em crafting.");
        if (ImGui.CollapsingHeader("Diagnostico de IDs dos status##CosmicBuffIds"))
        {
            ImGui.TextWrapped(raw.Count == 0
                ? "O personagem nao apresenta status na lista."
                : string.Join(" | ", raw));
            ImGui.TextDisabled("Compare os IDs com os buffs exibidos no jogo; mapeamento pode mudar.");
        }
    }

    private void DrawChatGains(string recipe, int jobId, string condition, CraftingState state)
    {
        ImGui.Separator();
        ImGui.Text("GANHOS LIDOS DO CHAT (EXPERIMENTAL)");
        ImGui.TextWrapped("O chat informa ganhos reais, inclusive quando a qualidade atinge o limite. A leitura nao prova ausencia de buffs.");
        while (chatReader.TryDequeue(out var result))
        {
            if (result.Action == CraftAction.BasicTouch) chatTouchGain = result.Gain;
            else if (result.Action == CraftAction.BasicSynthesis) chatSynthesisGain = result.Gain;
            confirmNoBuffs = false;
            chatStatus = $"{result.Action}: +{result.Gain} informado no chat. Confirme as condicoes antes de salvar.";
        }
        ImGui.TextWrapped(chatStatus);
        ImGui.TextDisabled(chatReader.Status);
        if (chatTouchGain is int touch) ImGui.Text($"Basic Touch pelo chat: +{touch}");
        if (chatSynthesisGain is int synthesis) ImGui.Text($"Basic Synthesis pelo chat: +{synthesis}");

        ImGui.Checkbox("Confirmo: acao em Normal, sem buffs ou efeitos especiais", ref confirmNoBuffs);
        bool canSave = confirmNoBuffs && string.Equals(condition, "Normal", StringComparison.OrdinalIgnoreCase);
        if (!string.Equals(condition, "Normal", StringComparison.OrdinalIgnoreCase))
            ImGui.TextDisabled("Condicao atual nao e Normal; aguarde uma receita/acao apropriada.");
        if (chatTouchGain is int t && ImGui.Button("Aplicar e salvar Basic Touch"))
        {
            if (canSave && calibration.SaveChatGain(recipe, jobId, state, CraftAction.BasicTouch, t))
            {
                touchGain = t;
                estimatedGains = !(synthesisGain > 0 && calibration.Get(recipe, jobId, state)?.SynthesisGain > 0);
                chatTouchGain = null;
                confirmNoBuffs = false;
                chatStatus = calibration.Status;
                InvalidatePlan();
            }
            else chatStatus = "Nao salvo: confirme ausencia de buffs e condicao Normal. " + calibration.Status;
        }
        if (chatSynthesisGain is int s && ImGui.Button("Aplicar e salvar Basic Synthesis"))
        {
            if (canSave && calibration.SaveChatGain(recipe, jobId, state, CraftAction.BasicSynthesis, s))
            {
                synthesisGain = s;
                estimatedGains = !(touchGain > 0 && calibration.Get(recipe, jobId, state)?.TouchGain > 0);
                chatSynthesisGain = null;
                confirmNoBuffs = false;
                chatStatus = calibration.Status;
                InvalidatePlan();
            }
            else chatStatus = "Nao salvo: confirme ausencia de buffs e condicao Normal. " + calibration.Status;
        }
        ImGui.TextDisabled("Calibracoes confirmadas sao salvas em AppData\\Local\\CosmicCraft. Nao confirme ganhos com buffs.");
    }

    private void DrawAutomaticGains(string condition, CraftingState state)
    {
        ImGui.Separator();
        ImGui.Text("OBSERVACAO AUTOMATICA (EXPERIMENTAL)");
        if (ImGui.Checkbox("Observar ganhos sem cliques", ref observeGains))
            gainObserver.Reset();
        if (observeGains && !calibration.IsPending)
            gainObserver.Observe(state, condition);
        else
            gainObserver.Reset();
        ImGui.TextWrapped(gainObserver.Status);
        ImGui.TextDisabled("Somente condicao Normal e custos padrao; buffs e outros efeitos nao podem ser descartados automaticamente.");
        if (gainObserver.SynthesisGain is int sg)
        {
            ImGui.Text($"Basic Synthesis observado: {sg}");
            if (ImGui.Button("Usar ganho de Synthesis observado"))
            {
                synthesisGain = sg;
                InvalidatePlan();
            }
        }
        if (gainObserver.TouchGain is int tg)
        {
            ImGui.Text($"Basic Touch observado: {tg}");
            if (ImGui.Button("Usar ganho de Touch observado"))
            {
                touchGain = tg;
                InvalidatePlan();
            }
        }
        if (gainObserver.SynthesisMinimum is int sm)
            ImGui.TextWrapped($"Basic Synthesis: aumento minimo observado +{sm} (limitado; nao aplicar como ganho exato).");
        if (gainObserver.TouchMinimum is int tm)
            ImGui.TextWrapped($"Basic Touch: aumento minimo observado +{tm} (limitado; nao aplicar como ganho exato).");
        ImGui.TextDisabled("Ganhos limitados nao sao aplicados automaticamente. Observacoes nao sao persistidas.");
    }

    private void DrawCalibration(string recipe, int jobId, string condition, CraftingState state)
    {
        ImGui.Separator();
        ImGui.Text("CALIBRACAO POR MEDICAO");
        ImGui.TextWrapped("Use somente em condicao Normal, sem buffs ativos. Clique antes de executar UMA acao no jogo; depois clique em Medir resultado. Nao use macros durante a medicao.");
        if (!calibration.IsPending)
        {
            if (ImGui.Button("Medir Basic Synthesis"))
            {
                if (string.Equals(condition, "Normal", StringComparison.OrdinalIgnoreCase))
                {
                    calibration.Begin(recipe, jobId, CraftAction.BasicSynthesis, state);
                    conditionAtStart = condition;
                }
            }
            ImGui.SameLine();
            if (ImGui.Button("Medir Basic Touch"))
            {
                if (string.Equals(condition, "Normal", StringComparison.OrdinalIgnoreCase))
                {
                    calibration.Begin(recipe, jobId, CraftAction.BasicTouch, state);
                    conditionAtStart = condition;
                }
            }
            if (!string.Equals(condition, "Normal", StringComparison.OrdinalIgnoreCase))
                ImGui.TextDisabled("Aguardando condicao Normal para iniciar medicao.");
        }
        else
        {
            if (ImGui.Button("Medir resultado"))
            {
                if (calibration.Finish(recipe, jobId, state, conditionAtStart, condition))
                {
                    var result = calibration.Get(recipe, jobId, state);
                    if (result?.SynthesisGain is int sg) synthesisGain = sg;
                    if (result?.TouchGain is int tg) touchGain = tg;
                    InvalidatePlan();
                }
            }
            ImGui.SameLine();
            if (ImGui.Button("Cancelar medicao")) calibration.Cancel();
        }
        ImGui.TextWrapped(calibration.Status);
        var learned = calibration.Get(recipe, jobId, state);
        ImGui.Text($"Progresso aprendido: {(learned?.SynthesisGain is int a ? a.ToString() : "nao medido")}");
        ImGui.Text($"Qualidade aprendida: {(learned?.TouchGain is int b ? b.ToString() : "nao medido")}");
        ImGui.TextDisabled("Medicoes validas sao salvas em AppData\\Local\\CosmicCraft.");
    }

    private void DrawPlanner(CraftingState state, int crafterLevel, string recipe, int jobId, int craftsmanship, int control)
    {
        ImGui.Separator();
        ImGui.Text("PLANEJADOR EXPERIMENTAL");
        ImGui.TextWrapped("Calibracoes da receita sao recuperadas automaticamente. Para receitas novas, calibre os ganhos. Buffs e condicoes ainda exigem verificacao.");
        ImGui.TextDisabled("v0.6.19: cache local + Muscle Memory + Veneration agora usam bonus aditivos. Habilidades nao validadas continuam experimentais.");
        bool changed = false;
        if (ImGui.InputInt("Ganho Basic Synthesis", ref synthesisGain))
        {
            estimatedGains = false;
            synthesisGain = Math.Max(1, synthesisGain);
            changed = true;
        }
        if (ImGui.InputInt("Ganho Basic Touch", ref touchGain))
        {
            estimatedGains = false;
            touchGain = Math.Max(0, touchGain);
            changed = true;
        }
        if (changed) InvalidatePlan();
        bool ready = synthesisGain > 0 && touchGain > 0 && state.MaxQuality > 0;
        if (!ready)
        {
            ImGui.TextWrapped("Para gerar a macro, preencha os campos pendentes:");
            if (state.MaxQuality <= 0) ImGui.BulletText("Qualidade maxima da receita (acima)");
            if (synthesisGain <= 0) ImGui.BulletText("Ganho Basic Synthesis: calibre ou informe manualmente");
            if (touchGain <= 0) ImGui.BulletText("Ganho Basic Touch: calibre ou informe manualmente");
        }
        ImGui.Text($"Cache local: {rotationCache.Count} rotacoes");
        ImGui.TextWrapped(rotationCache.Status);
        if (ImGui.Button("Limpar cache de rotacoes"))
        {
            rotationCache.Clear();
            lastAutoPlanKey = "";
            InvalidatePlan();
        }
        ImGui.Checkbox("Gerar macro automaticamente ao abrir receita conhecida", ref automaticPlanning);
        var autoKey = $"{lastRecipeKey}|{synthesisGain}|{touchGain}|{state.MaxQuality}|{state.MaxCp}|{crafterLevel}|{craftsmanship}|{control}";
        // Somente inicio de sintese: nao recalcular a cada frame nem no meio de uma macro.
        bool atStart = state.Progress == 0 && state.Quality == 0 && state.Durability == state.MaxDurability && state.Cp == state.MaxCp;
        bool shouldAutoGenerate = automaticPlanning && ready && atStart && currentPlan is null && lastAutoPlanKey != autoKey;
        if (ImGui.Button("Gerar rotacao estimada / macro") || shouldAutoGenerate)
        {
            InvalidatePlan();
            if (shouldAutoGenerate) lastAutoPlanKey = autoKey;
            if (!ready)
            {
                plannerStatus = "Calculo nao iniciado: informe qualidade maxima e os dois ganhos acima.";
            }
            else if (state.MaxProgress <= 0 || state.Durability <= 0 || state.Cp < 0)
            {
                plannerStatus = "Estado da sintese invalido: confira progresso, durabilidade e CP.";
            }
            else
            {
                try
                {
                    var parameters = new CraftingParameters
                    {
                        SynthesisGain = synthesisGain,
                        BasicSynthesisEfficiency = crafterLevel >= 31 ? 120 : 100,
                        TouchGain = touchGain
                    };
                    var cacheKey = RotationCache.MakeKey(recipe, jobId, crafterLevel, craftsmanship, control, state, parameters);
                    bool fromCache = rotationCache.TryGet(cacheKey, state, parameters, crafterLevel, out var cached);
                    var generated = fromCache ? cached : RotationPlanner.Generate(state, parameters, crafterLevel);
                    if (!fromCache && !estimatedGains && generated.CompletesCraft && generated.ReachesMaxQuality)
                        rotationCache.Save(cacheKey, generated);
                    if (generated is null)
                    {
                        plannerStatus = "O planejador retornou um resultado nulo.";
                    }
                    else
                    {
                        currentPlan = generated;
                        currentMacro = RotationPlanner.GenerateMacro(generated) ?? "";
                        plannerStatus = generated.Actions.Count == 0
                            ? "O planejador nao encontrou acoes validas. Consulte a mensagem abaixo."
                            : $"Rotacao calculada: {generated.Actions.Count} acoes.";
                    }
                }
                catch (Exception ex)
                {
                    plannerStatus = $"Erro ao calcular: {ex.GetType().Name}: {ex.Message}";
                }
            }
        }
        if (!string.IsNullOrWhiteSpace(plannerStatus))
            ImGui.TextWrapped(plannerStatus);
        if (currentPlan is null)
        {
            ImGui.TextDisabled("Nenhuma rotacao calculada.");
            return;
        }
        ImGui.TextWrapped(currentPlan.Message);
        if (estimatedGains) ImGui.TextWrapped("Esta macro usa estimativas automaticas. NAO e garantida; nao sera salva no cache ate os ganhos serem confirmados.");
        ImGui.TextDisabled("Previsao baseada no estado capturado ao gerar.");
        ImGui.Text($"Progresso previsto: {currentPlan.FinalState.Progress} / {currentPlan.FinalState.MaxProgress}");
        ImGui.Text($"Qualidade prevista: {currentPlan.FinalState.Quality} / {currentPlan.FinalState.MaxQuality}");
        ImGui.Text($"Durabilidade prevista: {currentPlan.FinalState.Durability}");
        ImGui.Text($"CP previsto: {currentPlan.FinalState.Cp}");
        ImGui.Separator();
        ImGui.Text("SEQUENCIA SUGERIDA");
        int stepNumber = 1;
        foreach (var entry in currentPlan.Actions)
        {
            string name = entry.Action switch
            {
                CraftAction.BasicSynthesis => "Basic Synthesis",
                CraftAction.BasicTouch => "Basic Touch",
                CraftAction.MastersMend => "Master's Mend",
                CraftAction.WasteNot => "Waste Not",
                CraftAction.Innovation => "Innovation",
                CraftAction.GreatStrides => "Great Strides",
                CraftAction.MuscleMemory => "Muscle Memory",
                CraftAction.Veneration => "Veneration",
                CraftAction.Groundwork => "Groundwork",
                _ => "Desconhecida"
            };
            ImGui.Text($"{stepNumber++}. {name} | P: {entry.Result.Progress} | Q: {entry.Result.Quality} | D: {entry.Result.Durability} | CP: {entry.Result.Cp}");
        }
        ImGui.Separator();
        ImGui.Text(T("MACRO GERADA"));
        ImGui.TextWrapped(currentMacro);
        if (!string.IsNullOrWhiteSpace(currentMacro) && ImGui.Button(T("Copiar macro")))
            ImGui.SetClipboardText(currentMacro);
        ImGui.TextDisabled("Confira habilidades disponiveis e a rotacao antes de utilizar.");
    }

    private void ClearActiveState()
    {
        if (lastRecipeKey.Length == 0) return;
        raphaelCancel?.Cancel();
        raphaelResult = null;
        raphaelRecipeKey = "";
        raphaelCacheKey = "";
        lastAutomaticCacheLookupKey = "";
        raphaelTracker.Reset();
        executorMonitoring = false;
        executorPaused = false;
        lastRecipeKey = "";
        lastAutoPlanKey = "";
        lastGainLookupKey = "";
        estimatedGains = false;
        calibration.Cancel();
        gainObserver.Reset();
        chatReader.Clear();
        chatSynthesisGain = null;
        chatTouchGain = null;
        InvalidatePlan();
    }

    private void InvalidatePlan()
    {
        currentPlan = null;
        currentMacro = "";
        plannerStatus = "";
    }

    private static void DrawProgress(string label, int current, int maximum)
    {
        ImGui.Text($"{label}: {current} / {maximum}");
        float fraction = maximum > 0 ? Math.Clamp((float)current / maximum, 0f, 1f) : 0f;
        ImGui.ProgressBar(fraction, new Vector2(-1, 18));
    }
}
