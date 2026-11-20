using System;
using System.Collections.Generic;
using System.Linq;

namespace SamplePlugin;

// Apenas geracao de texto para o editor de macros. Nao executa comandos.
public static class RaphaelMacroFormatter
{
    public sealed record Result(bool Success, string Error, string[] Parts);

    private static readonly Dictionary<string, (string Name, int Wait)> Skills = new(StringComparer.OrdinalIgnoreCase)
    {
        ["MuscleMemory"] = ("Muscle Memory", 3),
        ["BasicSynthesis"] = ("Basic Synthesis", 3),
        ["RapidSynthesis"] = ("Rapid Synthesis", 3),
        ["CarefulSynthesis"] = ("Careful Synthesis", 3),
        ["PrudentSynthesis"] = ("Prudent Synthesis", 3),
        ["Groundwork"] = ("Groundwork", 3),
        ["DelicateSynthesis"] = ("Delicate Synthesis", 3),
        ["IntensiveSynthesis"] = ("Intensive Synthesis", 3),
        ["BasicTouch"] = ("Basic Touch", 3),
        ["StandardTouch"] = ("Standard Touch", 3),
        ["AdvancedTouch"] = ("Advanced Touch", 3),
        ["PrudentTouch"] = ("Prudent Touch", 3),
        ["PreparatoryTouch"] = ("Preparatory Touch", 3),
        ["PreciseTouch"] = ("Precise Touch", 3),
        ["HastyTouch"] = ("Hasty Touch", 3),
        ["DaringTouch"] = ("Daring Touch", 3),
        ["RefinedTouch"] = ("Refined Touch", 3),
        ["ByregotsBlessing"] = ("Byregot's Blessing", 3),
        ["TrainedFinesse"] = ("Trained Finesse", 3),
        ["Reflect"] = ("Reflect", 3),
        ["TrainedEye"] = ("Trained Eye", 3),
        ["Innovation"] = ("Innovation", 2),
        ["Veneration"] = ("Veneration", 2),
        ["GreatStrides"] = ("Great Strides", 2),
        ["WasteNot"] = ("Waste Not", 2),
        ["WasteNotII"] = ("Waste Not II", 2),
        ["Manipulation"] = ("Manipulation", 2),
        ["MastersMend"] = ("Master's Mend", 2),
        ["MasterMend"] = ("Master's Mend", 2),
        ["Observe"] = ("Observe", 3),
        ["TricksOfTheTrade"] = ("Tricks of the Trade", 2),
        ["FinalAppraisal"] = ("Final Appraisal", 2),
        ["HeartAndSoul"] = ("Heart and Soul", 2),
        ["QuickInnovation"] = ("Quick Innovation", 2),
        ["CarefulObservation"] = ("Careful Observation", 2),
        ["ImmaculateMend"] = ("Immaculate Mend", 2),
        ["TrainedPerfection"] = ("Trained Perfection", 2)
    };

    // Normalizacao compartilhada pelo gerador e pelo resolvedor de IDs.
    // Mantemos aliases explicitos: remover apostrofos nao basta para
    // converter MasterMend em Master's Mend (MASTERSMEND).
    private static string StripActionName(string? value) =>
        new string((value ?? "").Where(char.IsLetterOrDigit)
            .Select(char.ToUpperInvariant).ToArray());

    // Single exact alias catalog for macro, ID resolver and chat tracker.
    private static readonly Dictionary<string, string> CanonicalAliases = BuildAliases();

    private static Dictionary<string, string> BuildAliases()
    {
        var aliases = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var skill in Skills)
        {
            string canonical = StripActionName(skill.Value.Name);
            foreach (var spelling in new[] { skill.Key, skill.Value.Name })
            {
                string alias = StripActionName(spelling);
                if (aliases.TryGetValue(alias, out var previous) && previous != canonical)
                    throw new InvalidOperationException("Alias de habilidade ambiguo: " + alias);
                aliases[alias] = canonical;
            }
        }
        // Aliases exatos de importacao (sem fuzzy matching e sem carregar dados externos).
        // Cada destino deve existir no catalogo; colisao entre habilidades e erro.
        var additionalAliases = new (string Alias, string Target)[]
        {
            ("WasteNot2", "WasteNotII"),
            ("BasicSynth", "BasicSynthesis"),
            ("BasicSynth2", "BasicSynthesis"),
            ("RapidSynthesis3", "RapidSynthesis"),
            ("CarefulSynthesis3", "CarefulSynthesis"),
            ("Manipulation2", "Manipulation")
        };
        foreach (var (aliasName, targetName) in additionalAliases)
        {
            string alias = StripActionName(aliasName);
            string target = StripActionName(targetName);
            if (!aliases.TryGetValue(target, out var canonical))
                throw new InvalidOperationException("Destino de alias desconhecido: " + targetName);
            if (aliases.TryGetValue(alias, out var previous) && previous != canonical)
                throw new InvalidOperationException("Alias de habilidade ambiguo: " + aliasName);
            aliases[alias] = canonical;
        }
        return aliases;
    }

    public static string NormalizeForLookup(string? value)
    {
        string key = StripActionName(value);
        return CanonicalAliases.TryGetValue(key, out var canonical) ? canonical : key;
    }

    public static bool IsKnownAction(string? value) =>
        CanonicalAliases.ContainsKey(StripActionName(value));

    public static Result Build(string[] actions)
    {
        if (actions is null || actions.Length == 0)
            return new(false, "Nenhuma acao recebida do Raphael.", Array.Empty<string>());
        var lines = new List<string>();
        foreach (string action in actions)
        {
            string key = NormalizeForLookup(action);
            var matches = Skills.Values.Where(v => NormalizeForLookup(v.Name) == key).Distinct().ToArray();
            if (matches.Length != 1)
                return new(false, $"Habilidade desconhecida ou ambigua: {action}. Macro nao gerada.", Array.Empty<string>());
            var skill = matches[0];
            lines.Add($"/ac \"{skill.Name}\" <wait.{skill.Wait}>");
        }
        var parts = lines.Chunk(15).Select(chunk => string.Join("\n", chunk)).ToArray();
        return new(true, "", parts);
    }
}
