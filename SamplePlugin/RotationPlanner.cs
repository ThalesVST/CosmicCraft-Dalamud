using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace SamplePlugin;

public sealed class CraftingActionEntry
{
    public CraftAction Action { get; init; }
    public CraftingState Result { get; init; } = new();
}

public sealed class RotationPlan
{
    public List<CraftingActionEntry> Actions { get; init; } = new();
    public bool CompletesCraft { get; init; }
    public bool ReachesMaxQuality { get; init; }
    public CraftingState FinalState { get; init; } = new();
    public string Message { get; init; } = "";
}

public static class RotationPlanner
{
    private sealed record Candidate(CraftingState State, List<CraftingActionEntry> Steps);
    private static readonly CraftAction[] Available =
    {
        CraftAction.BasicTouch, CraftAction.BasicSynthesis,
        CraftAction.MastersMend, CraftAction.WasteNot,
        CraftAction.Innovation, CraftAction.GreatStrides,
        CraftAction.MuscleMemory, CraftAction.Veneration, CraftAction.Groundwork,
        CraftAction.StandardTouch, CraftAction.CarefulSynthesis, CraftAction.ByregotsBlessing
    };

    public static RotationPlan Generate(CraftingState initial, CraftingParameters parameters, int crafterLevel = 100)
    {
        if (initial.MaxProgress <= 0 || initial.MaxDurability <= 0 || initial.MaxQuality <= 0 ||
            parameters.SynthesisGain <= 0 || parameters.TouchGain <= 0)
            return new RotationPlan { FinalState = initial.Clone(), Message = "Informe progresso, qualidade maxima e ganhos validos." };

        // Beam search limitada: nao garante encontrar a melhor rotacao global.
        const int maxDepth = 30;
        const int beamWidth = 1800;
        var startingState = initial.Clone();
        startingState.CrafterLevel = crafterLevel;
        var beam = new List<Candidate> { new(startingState, new List<CraftingActionEntry>()) };
        Candidate? bestComplete = null;
        Candidate? bestFull = null;
        for (var depth = 0; depth < maxDepth && beam.Count > 0; depth++)
        {
            var next = new Dictionary<string, Candidate>();
            foreach (var candidate in beam)
            {
                foreach (var action in Available)
                {
                    if (!Unlocked(action, crafterLevel)) continue;
                    // Buffs nao devem ser aplicados quando nao podem trazer beneficio.
                    if (action == CraftAction.GreatStrides && candidate.State.Quality >= candidate.State.MaxQuality) continue;
                    if (action == CraftAction.ByregotsBlessing && candidate.State.InnerQuietStacks < 2) continue;
                    if (action == CraftAction.Veneration && candidate.State.Progress >= candidate.State.MaxProgress) continue;
                    if (action == CraftAction.Innovation && candidate.State.Quality >= candidate.State.MaxQuality) continue;
                    if (action == CraftAction.WasteNot && candidate.State.Durability >= candidate.State.MaxDurability && candidate.State.Progress >= candidate.State.MaxProgress) continue;
                    if (!CraftingSimulator.CanUse(candidate.State, action, parameters)) continue;
                    var state = CraftingSimulator.Apply(candidate.State, action, parameters);
                    var steps = new List<CraftingActionEntry>(candidate.Steps)
                    {
                        new() { Action = action, Result = state.Clone() }
                    };
                    var result = new Candidate(state, steps);
                    if (state.Completed)
                    {
                        if (bestComplete is null || CompareComplete(result, bestComplete) > 0)
                            bestComplete = result;
                        if (state.Quality >= state.MaxQuality &&
                            (bestFull is null || CompareFull(result, bestFull) > 0))
                            bestFull = result;
                        continue;
                    }
                    var key = $"{state.Progress}|{state.Quality}|{state.Durability}|{state.Cp}|{state.WasteNotTurns}|{state.InnovationTurns}|{state.GreatStridesTurns}|{state.MuscleMemoryTurns}|{state.VenerationTurns}|{state.StepsTaken == 0}|{state.InnerQuietStacks}|{state.LastAction}";
                    if (!next.TryGetValue(key, out var previous) || result.Steps.Count < previous.Steps.Count)
                        next[key] = result;
                }
            }
            // A primeira solucao de qualidade maxima nesta profundidade ja tem o
            // menor numero de acoes entre as solucoes exploradas ate aqui.
            if (bestFull is not null) break;
            beam = next.Values.OrderByDescending(x => Score(x.State, parameters))
                .ThenBy(x => x.Steps.Count).Take(beamWidth).ToList();
        }
        var selected = bestFull ?? bestComplete;
        if (selected is null)
            return new RotationPlan { FinalState = initial.Clone(), Message = "Nenhuma conclusao encontrada nos limites da busca. Nao execute uma macro estimada." };
        return new RotationPlan
        {
            Actions = selected.Steps,
            FinalState = selected.State.Clone(),
            CompletesCraft = selected.State.Completed,
            ReachesMaxQuality = selected.State.Quality >= selected.State.MaxQuality,
            Message = bestFull is not null
                ? "Qualidade maxima e conclusao na simulacao EXPERIMENTAL. Inner Quiet, combos e arredondamentos sao estimativas; valide no chat antes de confiar na macro."
                : "Conclusao encontrada, mas a busca nao atingiu a qualidade maxima."
        };
    }

    private static bool Unlocked(CraftAction action, int level) =>
        CraftingActionCatalog.IsAvailable(action, level);

    private static double Score(CraftingState s, CraftingParameters p)
    {
        var progressRemaining = Math.Max(0, s.MaxProgress - s.Progress);
        var progressActions = (int)Math.Ceiling((double)progressRemaining / p.SynthesisGain);
        var durabilityNeeded = progressActions * p.SynthesisDurabilityCost;
        var mendCount = s.Cp / Math.Max(1, p.MastersMendCpCost);
        var completionFeasible = s.Durability + mendCount * 30 >= durabilityNeeded;
        return (completionFeasible ? 10000 : -10000)
            + 1500.0 * s.Quality / s.MaxQuality
            + 300.0 * s.Progress / s.MaxProgress
            + s.Cp * 0.6 + s.Durability * 2.0
            + (s.InnovationTurns > 0 ? 12 : 0)
            + (s.WasteNotTurns > 0 ? 12 : 0)
            + (s.GreatStridesTurns > 0 ? 8 : 0)
            + (s.MuscleMemoryTurns > 0 ? 65 : 0)
            + (s.VenerationTurns > 0 ? 40 : 0)
            + s.InnerQuietStacks * (s.Quality < s.MaxQuality ? 9 : 0);
    }

    private static int CompareComplete(Candidate a, Candidate b)
    {
        var quality = a.State.Quality.CompareTo(b.State.Quality);
        if (quality != 0) return quality;
        var steps = b.Steps.Count.CompareTo(a.Steps.Count);
        return steps != 0 ? steps : a.State.Cp.CompareTo(b.State.Cp);
    }
    private static int CompareFull(Candidate a, Candidate b)
    {
        var steps = b.Steps.Count.CompareTo(a.Steps.Count);
        return steps != 0 ? steps : a.State.Cp.CompareTo(b.State.Cp);
    }

    public static string GenerateMacro(RotationPlan plan)
    {
        var builder = new StringBuilder();
        foreach (var (entry, index) in plan.Actions.Select((item, i) => (item, i)))
        {
            if (index > 0 && index % 15 == 0)
                builder.AppendLine("\n--- PROXIMA MACRO (copie apenas as linhas /ac deste bloco) ---");
            var name = entry.Action switch
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
                CraftAction.StandardTouch => "Standard Touch",
                CraftAction.CarefulSynthesis => "Careful Synthesis",
                CraftAction.ByregotsBlessing => "Byregot's Blessing",
                _ => throw new ArgumentOutOfRangeException()
            };
            builder.Append("/ac \"").Append(name).AppendLine("\" <wait.3>");
        }
        return builder.ToString().TrimEnd();
    }
}
