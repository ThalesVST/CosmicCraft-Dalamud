using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace SamplePlugin;

// Cache independente: armazena sugestoes, NUNCA comprova sucesso no jogo.
public sealed class RaphaelSuggestionCache
{
    private const int MaxPerLevel = 5;
    private readonly string path = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CosmicCraft", "raphael-suggestions-v1.json");
    private readonly List<Entry> entries = new();
    public sealed class Entry
    {
        public string Key { get; set; } = "";
        public int Level { get; set; }
        public string[] Actions { get; set; } = Array.Empty<string>();
        public int FinalQuality { get; set; }
    }
    public RaphaelSuggestionCache()
    {
        try
        {
            if (File.Exists(path))
            {
                var loaded = JsonSerializer.Deserialize<List<Entry>>(File.ReadAllText(path));
                if (loaded != null)
                    entries.AddRange(loaded.Where(e => e.Level > 0 && e.Actions?.Length > 0)
                        .GroupBy(e => e.Level)
                        .SelectMany(g => g.OrderBy(e => e.Actions.Length).Take(MaxPerLevel)));
            }
        }
        catch { /* arquivo ausente ou invalido nao impede o plugin */ }
    }
    public static string MakeKey(string recipe, int level, int table, int progress,
        int quality, int durability, int craftsmanship, int control, int cp,
        int initialQuality, bool manipulation) =>
        string.Join("|", "raphael-v1", recipe.Trim().ToUpperInvariant(), level, table,
            progress, quality, durability, craftsmanship, control, cp, initialQuality,
            manipulation);
    public int CountForLevel(int level) => entries.Count(e => e.Level == level);
    public bool TryGet(string key, out Entry entry)
    {
        entry = entries.FirstOrDefault(e => e.Key == key)!;
        return entry != null;
    }
    public void Save(string key, int level, string[] actions, int finalQuality)
    {
        if (level <= 0 || actions.Length == 0) return;
        var existing = entries.FirstOrDefault(e => e.Key == key);
        if (existing != null) entries.Remove(existing);
        // Nao ocupar duas vagas com a mesma sequencia no mesmo nivel.
        var duplicate = entries.FirstOrDefault(e => e.Level == level && e.Actions.SequenceEqual(actions));
        if (duplicate != null) entries.Remove(duplicate);
        var group = entries.Where(e => e.Level == level).ToList();
        if (group.Count >= MaxPerLevel)
        {
            var worst = group.OrderByDescending(e => e.Actions.Length).First();
            if (actions.Length >= worst.Actions.Length)
            {
                if (existing != null) entries.Add(existing);
                if (duplicate != null) entries.Add(duplicate);
                return;
            }
            entries.Remove(worst);
        }
        entries.Add(new Entry { Key = key, Level = level,
            Actions = actions.ToArray(), FinalQuality = finalQuality });
        Persist();
    }
    public void Clear()
    {
        entries.Clear();
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }
    private void Persist()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(entries));
        }
        catch { /* falha de disco nao afeta recomendacoes */ }
    }
}
