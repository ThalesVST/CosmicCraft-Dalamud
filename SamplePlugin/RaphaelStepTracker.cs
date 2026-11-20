using System;
using System.Linq;

namespace SamplePlugin;

// Assistente conservador: detecta mudanca de estado, mas nunca deduz a acao executada.
// O usuario confirma cada passo. Nao interage com o jogo nem com o cache.
public sealed class RaphaelStepTracker
{
    public bool Active { get; private set; }
    public bool Paused { get; private set; }
    public bool Diverged { get; private set; }
    public bool Finished => Active && !Diverged && Step >= actions.Length;
    public int Step { get; private set; }
    public string LastDetected { get; private set; } = "";
    public string DetectionStatus { get; private set; } = "Aguardando evento de habilidade.";
    private DateTime startedAt;
    private ChatCraftAction? candidate;
    private DateTime lastAcceptedAt;
    private string lastConfirmedAction = "";
    private DateTime lastConfirmedAt;
    private static readonly TimeSpan DuplicateGrace = TimeSpan.FromSeconds(2);
    private DateTime changedAt;
    private DateTime candidateAt;
    private bool transitionPending;
    private (int Progress, int Quality, int Durability, int Cp) lastSeen;
    private bool multipleTransitions;
    private DateTime lastStateChangeAt;
    // A mesma habilidade pode atualizar CP, D, P e Q em frames distintos.
    private static readonly TimeSpan StateSettle = TimeSpan.FromMilliseconds(750);

    public int StateTransitions { get; private set; }
    public int ChatEvents { get; private set; }
    public int AutoConfirmed { get; private set; }
    public int EstimatedSteps { get; private set; }
    public bool MultipleTransitions => multipleTransitions;
    public string SyncDiagnostic => $"Atualizacoes da UI: {StateTransitions} (nao sao passos) | Eventos do chat: {ChatEvents} | Confirmacoes pelo chat: {AutoConfirmed} | Passos estimados: {EstimatedSteps}";
    private static readonly TimeSpan MatchWindow = TimeSpan.FromSeconds(8);
    public string SuspectedAction { get; private set; } = "";
    public int ResyncStep { get; private set; } = -1;
    public bool NeedsResync => ResyncStep >= 0 && ResyncStep > Step;
    public string ResyncAction => NeedsResync ? actions[ResyncStep] : "";
    public string ResyncEventDiagnostic { get; private set; } = "Nenhum evento de ressincronizacao registrado.";
    public string RecentChatDiagnostic { get; private set; } = "Nenhum evento registrado.";
    private void LogChatDiagnostic(string message)
    {
        var line = $"{DateTime.UtcNow:O} | {message}";
        RecentChatDiagnostic = line + "\n" + RecentChatDiagnostic;
        if (RecentChatDiagnostic.Length > 4500)
            RecentChatDiagnostic = RecentChatDiagnostic.Substring(0, 4500);
    }

    public string Key { get; private set; } = "";
    public string NextAction => Step < actions.Length ? actions[Step] : "Concluido";
    private string[] actions = Array.Empty<string>();
    private (int Progress, int Quality, int Durability, int Cp) baseline;

    public void Start(string key, string[] rotation, CraftingState state)
    {
        Reset();
        if (string.IsNullOrEmpty(key) || rotation.Length == 0) return;
        Key = key;
        actions = (string[])rotation.Clone();
        baseline = Snapshot(state);
        lastSeen = baseline;
        lastStateChangeAt = DateTime.UtcNow;
        Active = true;
        startedAt = DateTime.UtcNow;
    }

    // O chat e o estado podem chegar em qualquer ordem. Mantemos uma transicao
    // pendente ate a mensagem correspondente chegar, sem avancar duas vezes.
    // Agrupa as atualizacoes de P/Q/D/CP de uma mesma acao manual.
    // Nao identifica a habilidade: o contador resultante e estimado.
    public void Observe(CraftingState state)
    {
        if (!Active || Diverged || Finished) return;
        var current = Snapshot(state);
        var now = DateTime.UtcNow;
        if (current != lastSeen)
        {
            lastSeen = current;
            StateTransitions++;
            if (!transitionPending)
            {
                transitionPending = true;
                changedAt = now;
            }
            lastStateChangeAt = now;
            Paused = true;
        }
        TryConfirm(state);
    }

    public void ObserveAction(ChatCraftAction action, CraftingState state)
    {
        if (!Active || Diverged || Finished || action.ObservedAt < startedAt ||
            action.ObservedAt <= lastAcceptedAt ||
            DateTime.UtcNow - action.ObservedAt > MatchWindow) return;
        ChatEvents++;
        LastDetected = action.Name;
        LogChatDiagnostic($"Evento={action.Name}; observado={action.ObservedAt:O}; " +
            $"esperado={NextAction}; passo={Step + 1}/{actions.Length}; " +
            $"ultimoAceito={lastAcceptedAt:O}; transicaoPendente={transitionPending}; " +
            $"variasTransicoes={multipleTransitions}; resyncAntes={ResyncStep}");
        if (Normalize(NextAction) != Normalize(action.Name))
        {
            // A chat event can arrive again just after the action was confirmed.
            // Ignore only the same last-confirmed action, very shortly afterward,
            // with no unaccounted crafting-state transition. Otherwise retain
            // the existing resynchronization safety behavior.
            // A duplicate of the immediately confirmed skill must never be
            // interpreted as a later occurrence of that skill in the rotation.
            // If state changed in the meantime, stop safely for review instead
            // of discarding the event or guessing a later step.
            // The chat timestamp is the authoritative reference for a previously
            // accepted event. lastConfirmedAt is a processing timestamp and may
            // be later than an observed event delivered out of order.
            bool repeatsLast = Normalize(action.Name) == Normalize(lastConfirmedAction);
            bool shortlyAfterLast = lastAcceptedAt != default &&
                action.ObservedAt > lastAcceptedAt &&
                action.ObservedAt - lastAcceptedAt <= DuplicateGrace;
            if (repeatsLast && shortlyAfterLast && !transitionPending && !multipleTransitions)
            {
                bool stateUnchanged = Snapshot(state) == baseline;
                LogChatDiagnostic($"DUPLICATA IGNORADA/REVISAO: {action.Name}; " +
                    $"ultimoConfirmado={lastConfirmedAction}; ultimoEvento={lastAcceptedAt:O}; " +
                    $"observado={action.ObservedAt:O}; estadoIgualAoBaseline={stateUnchanged}; " +
                    $"candidatoPendente={candidate.HasValue}; resyncNaoSugerido=True");
                if (stateUnchanged && !candidate.HasValue)
                {
                    DetectionStatus = $"DUPLICATA IGNORADA: {action.Name}; aguardando {NextAction}.";
                }
                else
                {
                    Paused = true;
                    DetectionStatus = $"Evento repetido {action.Name} com estado ou candidato pendente; " +
                        "verifique manualmente. Nenhuma ressincronizacao automatica.";
                }
                return;
            }
            SuspectedAction = action.Name;
            int later = Array.FindIndex(actions, Step + 1, a => Normalize(a) == Normalize(action.Name));
            if (later >= 0)
            {
                ResyncStep = later;
                ResyncEventDiagnostic = $"{DateTime.UtcNow:O} | evento={action.Name}; " +
                    $"observado={action.ObservedAt:O}; esperado={NextAction}; " +
                    $"passoAtual={Step + 1}; passoSugerido={later + 1}; " +
                    $"ultimoAceito={lastAcceptedAt:O}; " +
                    $"transicaoPendente={transitionPending}; variasTransicoes={multipleTransitions}; " +
                    $"status={DetectionStatus}";
                LogChatDiagnostic("RESYNC SUGERIDO: " + ResyncEventDiagnostic);
                DetectionStatus = $"Possivel perda de sincronizacao: {action.Name} no passo {later + 1}. Confirme a sequencia no jogo.";
            }
            else
                DetectionStatus = $"Evento diferente do esperado ({NextAction}): {action.Name}. Nao foi contabilizado.";
            return;
        }
        if (multipleTransitions)
        {
            DetectionStatus = "Evento correto, mas houve varias mudancas de estado. Confirme manualmente para evitar pular passos.";
            return;
        }
        if (!transitionPending)
            LogChatDiagnostic($"EVENTO CORRETO SEM TRANSICAO DE P/Q/D/CP: {action.Name}; " +
                "pode ser buff ou acao sem alteracao imediata. Aguardando confirmacao segura.");
        candidate = action;
        candidateAt = DateTime.UtcNow;
        SuspectedAction = "";
        ResyncStep = -1;
        DetectionStatus = $"Evento {action.Name} recebido; aguardando estado correspondente.";
        Observe(state);
    }

    private void TryConfirm(CraftingState state)
    {
        if (!transitionPending) return;
        if (DateTime.UtcNow - lastStateChangeAt < StateSettle) return;
        // Nao confundir atualizacoes de P/Q/D/CP com passos oficiais.
        // So avancar automaticamente quando a habilidade esperada for identificada no chat.
        bool verified = candidate.HasValue &&
            Normalize(NextAction) == Normalize(candidate.Value.Name) &&
            (candidateAt - changedAt).Duration() <= MatchWindow;
        if (!verified)
        {
            DetectionStatus = $"Mudanca de estado observada, mas {NextAction} nao foi confirmada pelo chat. Confirme manualmente; nenhum passo foi estimado.";
            Paused = true;
            return;
        }
        string confirmedAction = candidate!.Value.Name;
        Step++;
        AutoConfirmed++;
        lastAcceptedAt = candidate!.Value.ObservedAt;
        lastConfirmedAction = confirmedAction;
        lastConfirmedAt = DateTime.UtcNow;
        DetectionStatus = $"Passo {Step} confirmado pelo chat: {candidate.Value.Name}.";
        baseline = Snapshot(state);
        lastSeen = baseline;
        candidate = null;
        transitionPending = false;
        multipleTransitions = false;
        SuspectedAction = "";
        ResyncStep = -1;
        Paused = false;
    }

    // Same alias catalog as macro generation and ActionManager ID lookup.
    private static string Normalize(string value) =>
        RaphaelMacroFormatter.NormalizeForLookup(value);

    // Realinhamento explicito: o usuario confirma que todas as acoes ate a
    // habilidade indicada ja foram executadas. Nao deduzir isso do chat.
    public void ConfirmResync(CraftingState state)
    {
        if (!Active || Diverged || Finished || !NeedsResync) return;
        Step = ResyncStep + 1;
        baseline = Snapshot(state);
        lastSeen = baseline;
        lastStateChangeAt = DateTime.UtcNow;
        multipleTransitions = false;
        lastAcceptedAt = DateTime.UtcNow;
        lastConfirmedAction = actions[Step - 1];
        lastConfirmedAt = DateTime.UtcNow;
        candidate = null;
        transitionPending = false;
        Paused = false;
        SuspectedAction = "";
        ResyncStep = -1;
        DetectionStatus = $"Realinhado manualmente apos passo {Step}. Verifique a proxima habilidade.";
    }

    public void DismissResync()
    {
        ResyncStep = -1;
        SuspectedAction = "";
        DetectionStatus = "Sugestao de realinhamento descartada. Confirme o passo atual manualmente se necessario.";
    }

    // Used only after the executor independently verifies a new, persistent
    // Trained Perfection status following an accepted request. No generic inference.
    public bool ConfirmVerifiedStatus(CraftingState state, string actionName)
    {
        if (!Active || Diverged || Finished || NeedsResync ||
            Normalize(NextAction) != Normalize(actionName))
            return false;
        Step++;
        baseline = Snapshot(state);
        lastSeen = baseline;
        lastStateChangeAt = DateTime.UtcNow;
        lastAcceptedAt = DateTime.UtcNow;
        lastConfirmedAction = actionName;
        lastConfirmedAt = DateTime.UtcNow;
        candidate = null;
        transitionPending = false;
        multipleTransitions = false;
        Paused = false;
        SuspectedAction = "";
        ResyncStep = -1;
        DetectionStatus = $"Passo {Step} confirmado por novo status aplicado: {actionName}.";
        LogChatDiagnostic($"Confirmacao complementar: {actionName}; passo confirmado={Step}; " +
            $"ultimoAceito atualizado={lastAcceptedAt:O}");
        return true;
    }

    public void ConfirmExpected(CraftingState state)
    {
        if (!Active || !Paused || Diverged || Finished) return;
        Step++;
        baseline = Snapshot(state);
        lastSeen = baseline;
        lastStateChangeAt = DateTime.UtcNow;
        multipleTransitions = false;
        Paused = false;
        candidate = null;
        transitionPending = false;
        lastAcceptedAt = DateTime.UtcNow;
        lastConfirmedAction = actions[Step - 1];
        lastConfirmedAt = DateTime.UtcNow;
        SuspectedAction = "";
        ResyncStep = -1;
        DetectionStatus = "Passo confirmado manualmente; eventos anteriores ignorados.";
    }

    public void MarkDivergence()
    {
        if (Active) { Diverged = true; Paused = false; }
    }

    public void Reset()
    {
        Active = false;
        Paused = false;
        Diverged = false;
        Step = 0;
        Key = "";
        LastDetected = "";
        DetectionStatus = "Aguardando evento de habilidade.";
        startedAt = default;
        lastAcceptedAt = default;
        lastConfirmedAction = "";
        lastConfirmedAt = default;
        candidate = null;
        changedAt = default;
        candidateAt = default;
        transitionPending = false;
        multipleTransitions = false;
        StateTransitions = 0;
        ChatEvents = 0;
        AutoConfirmed = 0;
        EstimatedSteps = 0;
        lastSeen = default;
        lastStateChangeAt = default;
        SuspectedAction = "";
        ResyncStep = -1;
        ResyncEventDiagnostic = "Nenhum evento de ressincronizacao registrado.";
        RecentChatDiagnostic = "Nenhum evento registrado.";
        actions = Array.Empty<string>();
        baseline = default;
    }

    private static (int, int, int, int) Snapshot(CraftingState s) =>
        (s.Progress, s.Quality, s.Durability, s.Cp);
}
