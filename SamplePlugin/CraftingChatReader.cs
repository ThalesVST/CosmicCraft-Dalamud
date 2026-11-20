using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Dalamud.Game.Chat;

namespace SamplePlugin;

// Diagnostic reader. Does not modify chat or persist unrelated messages.
public sealed class CraftingChatReader : IDisposable
{
    private readonly object gate = new();
    private readonly Queue<ChatCraftGain> observations = new();
    private readonly Queue<ChatCraftAction> actionObservations = new();
    private CraftAction? pendingAction;
    private DateTime pendingAt;
    private int receivedCount;
    private int relevantCount;
    private string lastRelevant = "(nenhuma mensagem de crafting recebida)";
    private string detail = "Aguardando mensagens de crafting no chat (ingles).";

    // Read from the ImGui thread; the chat callback can run on another thread.
    public string Status
    {
        get
        {
            lock (gate)
                return $"Eventos recebidos: {receivedCount}; candidatos de crafting: {relevantCount}. " +
                       $"Ultima mensagem candidata: {lastRelevant}. Estado: {detail}";
        }
    }

    private static readonly Regex ActionLine = new(
        @"\bYou\s+use\s+(Basic\s+Touch|Basic\s+Synthesis)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex QualityLine = new(
        @"\bQuality\s+increases\s+by\s+([\d,]+)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex ProgressLine = new(
        @"\bProgress\s+increases\s+by\s+([\d,]+)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public CraftingChatReader() => Plugin.ChatGui.ChatMessage += OnChatMessage;
    public void Dispose() => Plugin.ChatGui.ChatMessage -= OnChatMessage;

    private void OnChatMessage(IHandleableChatMessage message)
    {
        var line = message.Message.TextValue ?? string.Empty;
        lock (gate)
        {
            receivedCount++;
            if (pendingAction is not null && DateTime.UtcNow - pendingAt > TimeSpan.FromSeconds(5))
            {
                pendingAction = null;
                detail = "Tempo esgotado aguardando ganho apos habilidade.";
            }

            // Only retain recognized action names from English crafting messages.
            var anyAction = AnyCraftAction.Match(line);
            if (anyAction.Success)
            {
                var actionName = anyAction.Groups["name"].Value.Trim();
                if (KnownCraftActions.Contains(actionName))
                {
                    actionObservations.Enqueue(new ChatCraftAction(actionName, DateTime.UtcNow));
                    while (actionObservations.Count > 20) actionObservations.Dequeue();
                }
            }

            // Do not retain or display unrelated/private chat messages.
            bool relevant = line.Contains("Basic Touch", StringComparison.OrdinalIgnoreCase) ||
                            line.Contains("Basic Synthesis", StringComparison.OrdinalIgnoreCase) ||
                            line.Contains("Quality increases", StringComparison.OrdinalIgnoreCase) ||
                            line.Contains("Progress increases", StringComparison.OrdinalIgnoreCase);
            if (!relevant) return;
            relevantCount++;
            lastRelevant = line.Length > 240 ? line[..240] + "..." : line;
            lastRelevant = lastRelevant.Replace('\r', ' ').Replace('\n', ' ');

            var action = ActionLine.Match(line);
            if (action.Success)
            {
                pendingAction = action.Groups[1].Value.Contains("Touch", StringComparison.OrdinalIgnoreCase)
                    ? CraftAction.BasicTouch : CraftAction.BasicSynthesis;
                pendingAt = DateTime.UtcNow;
                detail = $"Habilidade identificada: {pendingAction}; aguardando ganho.";
            }

            if (pendingAction is null)
            {
                detail = "Mensagem candidata recebida, mas nenhuma habilidade identificada. Confira o texto.";
                return;
            }

            var match = pendingAction == CraftAction.BasicTouch ? QualityLine.Match(line) : ProgressLine.Match(line);
            if (!match.Success) return;
            if (!int.TryParse(match.Groups[1].Value.Replace(",", ""), out int gain) || gain <= 0)
            {
                detail = "Ganho encontrado, mas numero invalido.";
                return;
            }
            observations.Enqueue(new ChatCraftGain(pendingAction.Value, gain, DateTime.UtcNow));
            while (observations.Count > 10) observations.Dequeue();
            detail = $"Ganho detectado: {pendingAction} +{gain} (valor informado pelo chat).";
            pendingAction = null;
        }
    }

    private static readonly HashSet<string> KnownCraftActions = new(StringComparer.OrdinalIgnoreCase)
    {
        "Muscle Memory", "Standard Touch", "Innovation", "Veneration",
        "Basic Synthesis", "Basic Touch", "Byregot's Blessing",
        "Great Strides", "Observe", "Focused Touch", "Focused Synthesis",
        "Prudent Touch", "Prudent Synthesis", "Preparatory Touch",
        "Manipulation", "Waste Not", "Waste Not II", "Master's Mend",
        "Careful Synthesis", "Groundwork", "Delicate Synthesis",
        "Advanced Touch", "Refined Touch", "Trained Finesse",
        "Trained Eye", "Tricks of the Trade", "Reflect", "Final Appraisal",
        "Heart and Soul", "Quick Innovation", "Immaculate Mend",
        "Trained Perfection", "Intensive Synthesis", "Precise Touch"
    };

    // Match the longest known action name after "You use"; do not consume the
    // client's "➤ Success!" suffix. No raw chat text is retained for tracking.
    private static readonly Regex AnyCraftAction = new(
        @"\bYou\s+use\s+(?<name>" +
        string.Join("|", KnownCraftActions.OrderByDescending(x => x.Length).Select(Regex.Escape)) +
        @")(?=\s*(?:$|[^\p{L}\p{N}' ]|Success\b|Failure\b))",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public bool TryDequeueAction(out ChatCraftAction action)
    {
        lock (gate)
        {
            if (actionObservations.Count > 0)
            {
                action = actionObservations.Dequeue();
                return true;
            }
        }
        action = default;
        return false;
    }

    public void ClearActionObservations()
    {
        lock (gate) actionObservations.Clear();
    }

    public bool TryDequeue(out ChatCraftGain gain)
    {
        lock (gate)
        {
            if (observations.Count > 0)
            {
                gain = observations.Dequeue();
                return true;
            }
        }
        gain = default;
        return false;
    }

    public void Clear()
    {
        lock (gate)
        {
            pendingAction = null;
            observations.Clear();
            actionObservations.Clear();
            detail = "Aguardando mensagens de crafting no chat (ingles).";
        }
    }
}

public readonly record struct ChatCraftGain(CraftAction Action, int Gain, DateTime ObservedAt);

public readonly record struct ChatCraftAction(string Name, DateTime ObservedAt);
