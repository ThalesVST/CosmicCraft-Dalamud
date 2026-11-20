using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Dalamud.Plugin.Services;

namespace SamplePlugin;

/// <summary>Diagnostico somente leitura. Execute as capturas na thread da UI do plugin.</summary>
public static class LoadedAddonProbe
{
    private static readonly string[] Candidates = {
        "Synthesis", "ActionDetail", "_PopUpText", "ScreenLog", "RecipeNote",
        "RecipeTree", "RecipeMaterialList", "WKSRecipeNotebook", "WKSMission",
        "WKSProgress", "WKSRecipe", "SynthesisSimple", "WKSHud", "WKSAnnounce"
    };

    private sealed record Info(bool Visible, bool Ready, string[] Values);
    private static Dictionary<string, Info>? closed;

    private static Dictionary<string, Info> Capture(IGameGui gui, IEnumerable<string> observedNames)
    {
        var result = new Dictionary<string, Info>(StringComparer.Ordinal);
        foreach (var name in Candidates.Concat(observedNames ?? Array.Empty<string>())
                     .Where(n => !string.IsNullOrWhiteSpace(n)).Distinct(StringComparer.Ordinal))
        {
            try
            {
                var addon = gui.GetAddonByName(name);
                if (addon.IsNull) continue;
                string[] captured = Array.Empty<string>();
                if (addon.IsReady && SynthesisReader.TryGetValues(addon, out var values))
                {
                    captured = values.Take(128).Select(v =>
                    {
                        var s = Convert.ToString(v) ?? "";
                        s = s.Replace('\r', ' ').Replace('\n', ' ');
                        return s.Length > 100 ? s[..100] + "..." : s;
                    }).ToArray();
                }
                result[name] = new Info(addon.IsVisible, addon.IsReady, captured);
            }
            catch { /* Addons podem desaparecer durante a captura. */ }
        }
        return result;
    }

    public static string MarkClosed(IGameGui gui, IEnumerable<string> observedNames)
    {
        closed = Capture(gui, observedNames);
        return $"Captura base: {closed.Count} addons. Selecione uma missao Cosmic e clique em Comparar.";
    }

    public static string Compare(IGameGui gui, IEnumerable<string> observedNames)
    {
        if (closed == null) return "Primeiro faca a captura base.";
        var current = Capture(gui, observedNames);
        var output = new StringBuilder();
        output.AppendLine($"Base: {closed.Count} addons | Atual: {current.Count} addons");
        int changes = 0;
        foreach (var name in closed.Keys.Union(current.Keys).OrderBy(x => x, StringComparer.Ordinal))
        {
            bool had = closed.TryGetValue(name, out var before);
            bool has = current.TryGetValue(name, out var after);
            if (!had || !has)
            {
                output.AppendLine($"{name}: {(has ? "NOVO" : "SUMIU")}");
                changes++;
                continue;
            }
            if (before!.Visible != after!.Visible || before.Ready != after.Ready)
            {
                output.AppendLine($"{name}: visivel {before.Visible}->{after.Visible}; pronto {before.Ready}->{after.Ready}");
                changes++;
            }
            if (before.Values.Length != after.Values.Length)
            {
                output.AppendLine($"{name}: quantidade de valores {before.Values.Length}->{after.Values.Length}");
                changes++;
            }
            int length = Math.Max(before.Values.Length, after.Values.Length);
            int shownForAddon = 0;
            for (int i = 0; i < length; i++)
            {
                string oldValue = i < before.Values.Length ? before.Values[i] : "<ausente>";
                string newValue = i < after.Values.Length ? after.Values[i] : "<ausente>";
                if (oldValue == newValue) continue;
                changes++;
                if (shownForAddon++ >= 24) continue;
                output.AppendLine($"{name} AtkValue[{i}]: {oldValue} -> {newValue}");
            }
            if (shownForAddon > 24) output.AppendLine($"{name}: mais {shownForAddon - 24} indices alterados nao exibidos.");
        }
        output.AppendLine($"Total de alteracoes: {changes}. Mudancas de AtkValues nao provam, por si so, o ID da missao.");
        return output.ToString();
    }
}
