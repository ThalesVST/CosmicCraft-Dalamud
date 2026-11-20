using System;
using System.Globalization;
using System.Reflection;
using Lumina.Excel.Sheets;

namespace SamplePlugin;

// Conservative lookup: no guessed recipe row and no use of gains from another recipe.
// Formula is a starting estimate, not a certified in-game result.
public sealed record AutomaticGainResult(int Synthesis, int Touch, uint RecipeRowId, string Detail);

public static class AutomaticCraftingGain
{
    public static bool TryEstimate(string displayedName, int maxProgress, int maxDurability, int maxQuality,
        int craftsmanship, int control, int crafterLevel, int candidateItemId, out AutomaticGainResult result, out string status)
    {
        result = new AutomaticGainResult(0, 0, 0, "");
        status = "Sem dados suficientes para calcular ganhos.";
        if (maxProgress <= 0 || maxDurability <= 0 || maxQuality <= 0 || craftsmanship <= 0 || control <= 0 || crafterLevel <= 0)
            return false;
        try
        {
            var recipes = Plugin.DataManager.GetExcelSheet<Recipe>();
            if (recipes is null) { status = "Tabela Recipe indisponivel."; return false; }
            int matches = 0;
            int parameterMatches = 0;
            int itemMatches = 0;
            AutomaticGainResult candidate = result;
            foreach (var recipe in recipes)
            {
                object row = recipe;
                var level = Get(Get(row, "RecipeLevelTable"), "Value");
                int difficulty = Number(Get(level, "Difficulty"));
                int durability = Number(Get(level, "Durability"));
                int quality = Number(Get(level, "Quality"));
                int df = Number(Get(row, "DifficultyFactor"));
                int durf = Number(Get(row, "DurabilityFactor"));
                int qf = Number(Get(row, "QualityFactor"));
                if (difficulty <= 0 || durability <= 0 || quality <= 0 || df <= 0 || durf <= 0 || qf <= 0) continue;
                if ((long)difficulty * df / 100 != maxProgress ||
                    (long)durability * durf / 100 != maxDurability ||
                    (long)quality * qf / 100 != maxQuality) continue;

                parameterMatches++;
                // AtkValue[3] is an unverified candidate item ID, not a recipe ID.
                // Require an exact ItemResult ID match; never guess by numbers alone.
                var item = Get(row, "ItemResult");
                int itemId = Number(Get(item, "RowId"));
                if (itemId == 0) itemId = Number(Get(item, "Row"));
                if (candidateItemId <= 0 || itemId != candidateItemId) continue;
                itemMatches++;
                // The displayed recipe name may differ from ItemResult in Cosmic Exploration.
                // Require matching numeric recipe parameters, not a fabricated name match.
                int progressDivider = Number(Get(level, "ProgressDivider"));
                int qualityDivider = Number(Get(level, "QualityDivider"));
                int progressModifier = Number(Get(level, "ProgressModifier"));
                int qualityModifier = Number(Get(level, "QualityModifier"));
                int recipeLevel = Number(Get(level, "ClassJobLevel"));
                if (progressDivider <= 0 || qualityDivider <= 0 || progressModifier <= 0 || qualityModifier <= 0) continue;
                int baseProgress = (int)Math.Floor((double)craftsmanship * 10 / progressDivider + 2);
                int baseQuality = (int)Math.Floor((double)control * 10 / qualityDivider + 35);
                if (crafterLevel <= recipeLevel)
                {
                    baseProgress = (int)Math.Floor(baseProgress * progressModifier / 100.0);
                    baseQuality = (int)Math.Floor(baseQuality * qualityModifier / 100.0);
                }
                int synthesisEfficiency = crafterLevel >= 31 ? 120 : 100;
                int synthesis = (int)Math.Floor(baseProgress * synthesisEfficiency / 100.0);
                int touch = baseQuality;
                if (synthesis <= 0 || touch <= 0) continue;
                matches++;
                candidate = new AutomaticGainResult(synthesis, touch,
                    (uint)Number(Get(row, "RowId")),
                    $"Recipe #{Number(Get(row, "RowId"))}; divisor P={progressDivider}, Q={qualityDivider}; nivel da receita={recipeLevel}.");
                if (matches > 1) break;
            }
            if (matches != 1)
            {
                status = matches == 0
                    ? $"Sem identificacao segura: AtkValue[3]={candidateItemId} (ID candidato, nao confirmado); {parameterMatches} receitas com parametros iguais, {itemMatches} com ItemResult correspondente. Pode ser receita especial fora da tabela Recipe."
                    : "Varias receitas compartilham os mesmos parametros. Nao e seguro escolher uma formula automaticamente.";
                return false;
            }
            result = candidate;
            status = $"Correspondencia por ItemResult #{candidateItemId} e parametros confirmada na tabela Recipe. Estimativa automatica: Synthesis +{result.Synthesis}, Touch +{result.Touch}. {result.Detail} Compare com o chat antes de confiar.";
            return true;
        }
        catch (Exception ex)
        {
            status = "Nao foi possivel calcular automaticamente: " + ex.GetType().Name + " - " + ex.Message;
            return false;
        }
    }

    private static object? Get(object? obj, string name)
    {
        if (obj is null) return null;
        var type = obj.GetType();
        return type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance)?.GetValue(obj)
            ?? type.GetField(name, BindingFlags.Public | BindingFlags.Instance)?.GetValue(obj);
    }
    private static int Number(object? value) => int.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture),
        NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) ? n : 0;
}
