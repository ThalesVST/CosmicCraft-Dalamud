using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace SamplePlugin;

// Local-only cache. Never treats an old prediction as proof of an in-game success.
public sealed class RotationCache
{
    private const int SchemaVersion = 1;
    private const int MaxPerLevel = 5;
    private const string RulesVersion = "experimental-0.6.18";
    private readonly string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CosmicCraft", "rotations-v1.json");
    private readonly List<Entry> entries = new();
    public string Status { get; private set; } = "Cache local pronto.";
    public int Count => entries.Count;

    public RotationCache() => Load();

    public static string MakeKey(string recipe, int job, int level, int craftsmanship, int control, CraftingState s, CraftingParameters p) =>
        string.Join("|", RulesVersion, job, level, craftsmanship, control,
            recipe.Trim().ToUpperInvariant(), s.MaxProgress, s.MaxQuality, s.MaxDurability,
            s.MaxCp, s.Progress, s.Quality, s.Durability, s.Cp,
            p.SynthesisGain, p.TouchGain, p.BasicSynthesisEfficiency);

    public bool TryGet(string key, CraftingState initial, CraftingParameters parameters, int level, out RotationPlan plan)
    {
        plan = new RotationPlan();
        var entry = entries.FirstOrDefault(x => x.Key == key && x.Rules == RulesVersion);
        if (entry == null || entry.Actions == null || entry.Actions.Count == 0) return false;
        var state = initial.Clone();
        state.CrafterLevel = level;
        var steps = new List<CraftingActionEntry>();
        try
        {
            foreach (var name in entry.Actions)
            {
                if (!Enum.TryParse<CraftAction>(name, out var action) ||
                    !CraftingActionCatalog.IsAvailable(action, level) ||
                    !CraftingSimulator.CanUse(state, action, parameters))
                    return false;
                state = CraftingSimulator.Apply(state, action, parameters);
                steps.Add(new CraftingActionEntry { Action = action, Result = state.Clone() });
            }
            if (!state.Completed || (entry.MaxQuality && state.Quality < state.MaxQuality)) return false;
            plan = new RotationPlan
            {
                Actions = steps,
                FinalState = state.Clone(),
                CompletesCraft = true,
                ReachesMaxQuality = state.Quality >= state.MaxQuality,
                Message = "Rotacao recuperada do cache local e REVALIDADA no simulador experimental. Nao e confirmacao de sucesso no jogo."
            };
            Status = "Cache encontrado; sequencia revalidada.";
            return true;
        }
        catch { return false; }
    }

    public void Save(string key, RotationPlan plan)
    {
        if (!plan.CompletesCraft || !plan.ReachesMaxQuality || plan.Actions.Count == 0) return;
        // Keep at most five rotations per crafter level. A replacement must
        // be strictly better than the weakest saved rotation in that group.
        int level = ExtractLevel(key);
        if (level <= 0) { Status = "Chave de cache sem nivel valido."; return; }
        var candidate = new Entry {
            Key = key, Rules = RulesVersion, CrafterLevel = level,
            MaxQuality = true, Actions = plan.Actions.Select(x => x.Action.ToString()).ToList()
        };
        // Identical sequences should not occupy two of the five level slots.
        var duplicate = entries.FirstOrDefault(x => x.CrafterLevel == level &&
            x.Actions.SequenceEqual(candidate.Actions));
        if (duplicate != null && duplicate.Key != key)
        {
            Status = "Sequencia identica ja existe neste nivel; cache mantido.";
            return;
        }
        var existing = entries.FirstOrDefault(x => x.Key == key);
        if (existing != null)
        {
            if (!Better(candidate, existing))
            {
                Status = "Rotacao existente igual ou mais otimizada; cache mantido.";
                return;
            }
            entries.Remove(existing);
        }
        else
        {
            var group = entries.Where(x => x.CrafterLevel == level).ToList();
            if (group.Count >= MaxPerLevel)
            {
                // Largest action count is least optimized. Ties remain stable.
                var worst = group.OrderByDescending(x => x.Actions.Count).First();
                if (!Better(candidate, worst))
                {
                    Status = $"Cache do nivel {level} cheio (5/5); nova macro nao supera a pior salva.";
                    return;
                }
                entries.Remove(worst);
            }
        }
        entries.Add(candidate);
        try
        {
            Persist();
            Status = $"Rotacao salva; nivel {level}: {entries.Count(x => x.CrafterLevel == level)}/5.";

        }
        catch (Exception e) { Status = "Nao foi possivel salvar cache: " + e.Message; }
    }

    public void Clear()
    {
        entries.Clear();
        try { if (File.Exists(path)) File.Delete(path); Status = "Cache local apagado."; }
        catch (Exception e) { Status = "Falha ao apagar cache: " + e.Message; }
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(path)) return;
            var store = JsonSerializer.Deserialize<Store>(File.ReadAllText(path));
            if (store?.Schema != SchemaVersion || store.Entries == null) return;
            // Migrate older cache entries to the current limit without trusting
            // entries created with a different simulation rules version.
            foreach (var entry in store.Entries.Where(x => x.Rules == RulesVersion &&
                         x.Actions != null && x.Actions.Count > 0))
            {
                entry.CrafterLevel = entry.CrafterLevel > 0 ? entry.CrafterLevel : ExtractLevel(entry.Key);
                if (entry.CrafterLevel <= 0) continue;
                entries.Add(entry);
            }
            var trimmed = entries.GroupBy(x => x.CrafterLevel)
                .SelectMany(g => g.OrderBy(x => x.Actions.Count).ThenBy(x => x.Key, StringComparer.Ordinal)
                    .Take(MaxPerLevel)).ToList();
            if (trimmed.Count != entries.Count)
            {
                entries.Clear();
                entries.AddRange(trimmed);
                Persist();
            }
            Status = $"{entries.Count} rotacoes carregadas do cache local.";
        }
        catch (Exception e) { Status = "Cache indisponivel: " + e.Message; }
    }

    private static int ExtractLevel(string key)
    {
        // MakeKey: rules | job | crafterLevel | craftsmanship | ...
        var parts = key.Split('|');
        return parts.Length > 2 && int.TryParse(parts[2], out int level) ? level : 0;
    }

    private static bool Better(Entry candidate, Entry existing)
    {
        // All stored plans already complete the craft at max quality.
        // Fewer actions wins. Do not replace on ties without duration data.
        return candidate.Actions.Count < existing.Actions.Count;
    }

    private void Persist()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(
            new Store { Schema = SchemaVersion, Entries = entries },
            new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temp, path, true);
    }

    public sealed class Store
    {
        public int Schema { get; set; }
        public List<Entry> Entries { get; set; } = new();
    }
    public sealed class Entry
    {
        public string Key { get; set; } = "";
        public string Rules { get; set; } = "";
        public int CrafterLevel { get; set; }
        public bool MaxQuality { get; set; }
        public List<string> Actions { get; set; } = new();
    }
}
