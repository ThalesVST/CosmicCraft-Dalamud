using System;
using System.Globalization;
using System.Reflection;
using Lumina.Excel.Sheets;

namespace SamplePlugin;

public sealed record RecipeInfo(uint RowId, int MaxQuality);

public static class RecipeDataReader
{
    // O nome exibido pode nao corresponder ao item de saida em receitas especiais.
    // Nesse caso, o leitor falha de forma segura e mantem a calibracao manual.
    public static bool TryFind(string displayedName, int progress, int durability,
        out RecipeInfo info, out string status)
    {
        info = new RecipeInfo(0, 0);
        status = "Receita nao encontrada nas tabelas padrao.";
        try
        {
            var sheet = Plugin.DataManager.GetExcelSheet<Recipe>();
            if (sheet is null) { status = "Tabela Recipe indisponivel."; return false; }
            int matches = 0;
            RecipeInfo candidate = info;
            foreach (var recipe in sheet)
            {
                object boxed = recipe;
                var item = Get(boxed, "ItemResult");
                var itemRow = Get(item, "Value");
                string name = Get(itemRow, "Name")?.ToString()?.Trim() ?? "";
                if (!string.Equals(name, displayedName.Trim(), StringComparison.OrdinalIgnoreCase)) continue;
                var level = Get(Get(boxed, "RecipeLevelTable"), "Value");
                int baseDifficulty = Number(Get(level, "Difficulty"));
                int baseDurability = Number(Get(level, "Durability"));
                int baseQuality = Number(Get(level, "Quality"));
                int difficultyFactor = Number(Get(boxed, "DifficultyFactor"));
                int durabilityFactor = Number(Get(boxed, "DurabilityFactor"));
                int qualityFactor = Number(Get(boxed, "QualityFactor"));
                int calculatedProgress = baseDifficulty * difficultyFactor / 100;
                int calculatedDurability = baseDurability * durabilityFactor / 100;
                if (calculatedProgress != progress || calculatedDurability != durability) continue;
                int calculatedQuality = baseQuality * qualityFactor / 100;
                if (calculatedQuality <= 0) continue;
                matches++;
                candidate = new RecipeInfo((uint)Number(Get(boxed, "RowId")), calculatedQuality);
            }
            if (matches == 1)
            {
                info = candidate;
                status = $"Recipe #{info.RowId}: qualidade maxima {info.MaxQuality} (calculada da tabela).";
                return true;
            }
            status = matches == 0 ? "Sem correspondencia exata; informe qualidade manualmente." :
                "Mais de uma receita compativel; informe qualidade manualmente.";
            return false;
        }
        catch (Exception ex)
        {
            status = "Leitura Lumina indisponivel: " + ex.GetType().Name + " - " + ex.Message;
            return false;
        }
    }
    private static object? Get(object? obj, string name)
    {
        if (obj is null) return null;
        var t = obj.GetType();
        return t.GetProperty(name, BindingFlags.Public | BindingFlags.Instance)?.GetValue(obj)
            ?? t.GetField(name, BindingFlags.Public | BindingFlags.Instance)?.GetValue(obj);
    }
    private static int Number(object? value)
    {
        if (value is null) return 0;
        return int.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture),
            NumberStyles.Integer, CultureInfo.InvariantCulture, out int result) ? result : 0;
    }
}
