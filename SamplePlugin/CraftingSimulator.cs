using System;
using System.Collections.Generic;

namespace SamplePlugin;

public enum CraftAction { BasicSynthesis, BasicTouch, MastersMend, WasteNot, Innovation, GreatStrides, MuscleMemory, Veneration, Groundwork, StandardTouch, CarefulSynthesis, ByregotsBlessing }

public sealed class CraftingState
{
    public int Progress { get; set; }
    public int MaxProgress { get; set; }
    public int Quality { get; set; }
    public int MaxQuality { get; set; } = 1060;
    public int Durability { get; set; }
    public int MaxDurability { get; set; }
    public int Cp { get; set; }
    public int MaxCp { get; set; }
    public int WasteNotTurns { get; set; }
    public int InnovationTurns { get; set; }
    public int GreatStridesTurns { get; set; }
    public int MuscleMemoryTurns { get; set; }
    public int VenerationTurns { get; set; }
    public int InnerQuietStacks { get; set; }
    public CraftAction? LastAction { get; set; }
    public int CrafterLevel { get; set; } = 100;
    public int StepsTaken { get; set; }
    public bool Completed => MaxProgress > 0 && Progress >= MaxProgress;
    public CraftingState Clone() => (CraftingState)MemberwiseClone();
}

public sealed class CraftingParameters
{
    public int SynthesisGain { get; set; } = 90;
    public int TouchGain { get; set; } = 130;
    public int SynthesisDurabilityCost { get; set; } = 10;
    public int TouchDurabilityCost { get; set; } = 10;
    public int SynthesisCpCost { get; set; } = 0;
    public int TouchCpCost { get; set; } = 18;
    public int MastersMendCpCost { get; set; } = 88;
    public int WasteNotCpCost { get; set; } = 56;
    public int InnovationCpCost { get; set; } = 18;
    public int GreatStridesCpCost { get; set; } = 32;
    public int MuscleMemoryCpCost { get; set; } = 6;
    public int VenerationCpCost { get; set; } = 18;
    public int GroundworkCpCost { get; set; } = 18;
    public int StandardTouchCpCost { get; set; } = 32;
    public int CarefulSynthesisCpCost { get; set; } = 7;
    public int ByregotsBlessingCpCost { get; set; } = 24;
    // Basic Synthesis at level 31+ has 120% efficiency.
    public int BasicSynthesisEfficiency { get; set; } = 120;
}

public sealed class CraftingStep
{
    public CraftAction Action { get; init; }
    public CraftingState Before { get; init; } = new();
    public CraftingState After { get; init; } = new();
}

public static class CraftingSimulator
{
    public static bool CanUse(CraftingState s, CraftAction a, CraftingParameters p)
    {
        if (s.Completed || s.Durability <= 0 || s.MaxProgress <= 0) return false;
        if (a == CraftAction.ByregotsBlessing && s.InnerQuietStacks == 0) return false;
        if ((a == CraftAction.BasicTouch || a == CraftAction.StandardTouch || a == CraftAction.ByregotsBlessing) && s.Quality >= s.MaxQuality) return false;
        var cp = CpCost(a, p, s);
        if (s.Cp < cp) return false;
        if (a == CraftAction.MastersMend) return s.Durability < s.MaxDurability;
        if (a == CraftAction.WasteNot) return s.WasteNotTurns == 0;
        if (a == CraftAction.Innovation) return s.InnovationTurns == 0;
        if (a == CraftAction.GreatStrides) return s.GreatStridesTurns == 0;
        if (a == CraftAction.Veneration) return s.VenerationTurns == 0;
        if (a == CraftAction.MuscleMemory) return s.StepsTaken == 0 && s.Progress == 0 && s.Quality == 0;
        return s.Durability >= DurabilityCost(s, a, p);
    }

    public static int CpCost(CraftAction a, CraftingParameters p, CraftingState? s = null) => a switch
    {
        CraftAction.BasicSynthesis => p.SynthesisCpCost,
        CraftAction.BasicTouch => p.TouchCpCost,
        CraftAction.MastersMend => p.MastersMendCpCost,
        CraftAction.WasteNot => p.WasteNotCpCost,
        CraftAction.Innovation => p.InnovationCpCost,
        CraftAction.GreatStrides => p.GreatStridesCpCost,
        CraftAction.MuscleMemory => p.MuscleMemoryCpCost,
        CraftAction.Veneration => p.VenerationCpCost,
        CraftAction.Groundwork => p.GroundworkCpCost,
        CraftAction.StandardTouch => s?.LastAction == CraftAction.BasicTouch ? 18 : p.StandardTouchCpCost,
        CraftAction.CarefulSynthesis => p.CarefulSynthesisCpCost,
        CraftAction.ByregotsBlessing => p.ByregotsBlessingCpCost,
        _ => int.MaxValue
    };

    public static int DurabilityCost(CraftingState s, CraftAction a, CraftingParameters p)
    {
        var baseCost = a switch
        {
            CraftAction.BasicSynthesis => p.SynthesisDurabilityCost,
            CraftAction.CarefulSynthesis => 10,
            CraftAction.BasicTouch => p.TouchDurabilityCost,
            CraftAction.StandardTouch => 10,
            CraftAction.ByregotsBlessing => 10,
            CraftAction.MuscleMemory => 10,
            CraftAction.Groundwork => 20,
            _ => 0
        };
        return s.WasteNotTurns > 0 ? (baseCost + 1) / 2 : baseCost;
    }

    public static CraftingState Apply(CraftingState s, CraftAction a, CraftingParameters p)
    {
        if (!CanUse(s, a, p)) throw new InvalidOperationException("Acao indisponivel.");
        var n = s.Clone();
        n.Cp -= CpCost(a, p, s);
        n.Durability -= DurabilityCost(s, a, p);
        // Scaling is experimental until compared with the chat log for each action.
        int ProgressGain(int efficiency, bool consumeMuscleBuff)
        {
            // FFXIV progress buffs add efficiency bonuses rather than multiply them.
            // Example with Basic Synthesis +90: Muscle Memory (+100%) and
            // Veneration (+50%) yield 90 * (1 + 1 + .5) = 225, NOT 270.
            // Experimental approximation: game rounding and level modifiers still
            // require comparison with actual chat output.
            double buff = (s.VenerationTurns > 0 ? 0.5 : 0.0)
                        + (consumeMuscleBuff && s.MuscleMemoryTurns > 0 ? 1.0 : 0.0);
            double factor = (double)efficiency / Math.Max(1, p.BasicSynthesisEfficiency);
            return (int)Math.Floor(p.SynthesisGain * factor * (1.0 + buff));
        }
        switch (a)
        {
            case CraftAction.MuscleMemory:
                n.Progress = Math.Min(n.MaxProgress, n.Progress + ProgressGain(300, false));
                break;
            case CraftAction.Groundwork:
                // Groundwork potency is halved if remaining durability is below 20.
                n.Progress = Math.Min(n.MaxProgress, n.Progress + ProgressGain(s.Durability < 20 ? 150 : 300, true));
                break;
            case CraftAction.CarefulSynthesis:
                // Efficiency is level-dependent. Experimental until verified in chat.
                n.Progress = Math.Min(n.MaxProgress, n.Progress + ProgressGain(s.CrafterLevel >= 82 ? 180 : 150, true));
                break;
            case CraftAction.BasicSynthesis:
                n.Progress = Math.Min(n.MaxProgress, n.Progress + ProgressGain(p.BasicSynthesisEfficiency, true));
                break;
            case CraftAction.BasicTouch:
            case CraftAction.StandardTouch:
            case CraftAction.ByregotsBlessing:
                // Quality is a calibrated Basic Touch gain at 100% potency and zero IQ stacks.
                // Inner Quiet + buffs and integer rounding are approximate until chat-validated.
                var efficiency = a switch
                {
                    CraftAction.StandardTouch => 125,
                    CraftAction.ByregotsBlessing => Math.Min(300, 100 + 20 * s.InnerQuietStacks),
                    _ => 100
                };
                var iqFactor = 1.0 + s.InnerQuietStacks * 0.1;
                var buffFactor = 1.0 + (s.InnovationTurns > 0 ? 0.5 : 0.0)
                                     + (s.GreatStridesTurns > 0 ? 1.0 : 0.0);
                var qualityGain = (int)Math.Floor(p.TouchGain * efficiency / 100.0 * iqFactor * buffFactor);
                n.Quality = Math.Min(n.MaxQuality, n.Quality + qualityGain);
                if (a == CraftAction.ByregotsBlessing) n.InnerQuietStacks = 0;
                else n.InnerQuietStacks = Math.Min(10, s.InnerQuietStacks + 1);
                break;
            case CraftAction.MastersMend:
                n.Durability = Math.Min(n.MaxDurability, n.Durability + 30);
                break;
        }
        n.StepsTaken++;
        n.MuscleMemoryTurns = Math.Max(0, n.MuscleMemoryTurns - 1);
        n.VenerationTurns = Math.Max(0, n.VenerationTurns - 1);
        if (a == CraftAction.BasicSynthesis || a == CraftAction.CarefulSynthesis || a == CraftAction.Groundwork) n.MuscleMemoryTurns = 0;
        n.WasteNotTurns = Math.Max(0, n.WasteNotTurns - 1);
        n.InnovationTurns = Math.Max(0, n.InnovationTurns - 1);
        n.GreatStridesTurns = Math.Max(0, n.GreatStridesTurns - 1);
        if (a == CraftAction.WasteNot) n.WasteNotTurns = 4;
        if (a == CraftAction.Innovation) n.InnovationTurns = 4;
        if (a == CraftAction.GreatStrides) n.GreatStridesTurns = 3;
        if (a == CraftAction.MuscleMemory) n.MuscleMemoryTurns = 5;
        if (a == CraftAction.Veneration) n.VenerationTurns = 4;
        if (a == CraftAction.BasicTouch || a == CraftAction.StandardTouch || a == CraftAction.ByregotsBlessing) n.GreatStridesTurns = 0;
        n.LastAction = a;
        return n;
    }

    public static List<CraftingStep> Simulate(CraftingState initial, IEnumerable<CraftAction> actions, CraftingParameters p)
    {
        var steps = new List<CraftingStep>();
        var current = initial.Clone();
        foreach (var action in actions)
        {
            if (!CanUse(current, action, p)) break;
            var before = current.Clone();
            current = Apply(current, action, p);
            steps.Add(new CraftingStep { Action = action, Before = before, After = current.Clone() });
            if (current.Completed) break;
        }
        return steps;
    }
}
