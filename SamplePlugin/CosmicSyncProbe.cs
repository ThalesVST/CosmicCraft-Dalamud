using System;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using Lumina.Excel.Sheets;

namespace SamplePlugin;

// Somente leitura: replica a selecao de RecipeLevelTable observada no Artisan.
// Nao escolhe automaticamente entre receitas com o mesmo nome.
public static class CosmicSyncProbe
{
    public static string Inspect(string displayedName, int playerLevel,
        int gameProgress, int gameQuality, int gameDurability)
    {
        var report = new StringBuilder();
        report.AppendLine($"Receita exibida: {displayedName} | Artesao nivel {playerLevel}");
        report.AppendLine($"Jogo: P={gameProgress}, Q={gameQuality}, D={gameDurability}");
        try
        {
            var levels = Plugin.DataManager.GetExcelSheet<RecipeLevelTable>();
            var recipes = Plugin.DataManager.GetExcelSheet<Recipe>();
            if (levels == null || recipes == null)
                return report.AppendLine("Tabelas Recipe/RecipeLevelTable indisponiveis.").ToString();

            var synced = levels.FirstOrDefault(x => x.ClassJobLevel == playerLevel);
            bool hasSynced = levels.Any(x => x.ClassJobLevel == playerLevel);
            if (hasSynced)
                report.AppendLine($"Tabela por ClassJobLevel={playerLevel}: RowId={synced.RowId}, Difficulty={synced.Difficulty}, Quality={synced.Quality}, Durability={synced.Durability}, ProgressDivider={synced.ProgressDivider}, ProgressModifier={synced.ProgressModifier}, QualityDivider={synced.QualityDivider}, QualityModifier={synced.QualityModifier}");
            else report.AppendLine("AVISO: nenhuma RecipeLevelTable encontrada para o nivel atual.");

            int count = 0, exact = 0, shown = 0;
            var compatibleRecipes = new List<string>();
            foreach (var recipe in recipes)
            {
                string name;
                try { name = recipe.ItemResult.Value.Name.ToString().Trim(); }
                catch { continue; }
                if (!string.Equals(name, displayedName.Trim(), StringComparison.OrdinalIgnoreCase))
                    continue;
                count++;
                // A regra do Artisan: Number == 0 e nivel < 100 usa a tabela do personagem.
                bool cosmic = recipe.Number == 0;
                var table = cosmic && playerLevel < 100 && hasSynced
                    ? synced : recipe.RecipeLevelTable.Value;
                long progress = (long)table.Difficulty * recipe.DifficultyFactor / 100;
                long quality = (long)table.Quality * recipe.QualityFactor / 100;
                // Artisan calcula durabilidade a partir da tabela ORIGINAL da receita.
                long durability = (long)recipe.RecipeLevelTable.Value.Durability * recipe.DurabilityFactor / 100;
                bool match = progress == gameProgress && durability == gameDurability &&
                    (gameQuality <= 0 || quality == gameQuality);
                if (match)
                {
                    exact++;
                    compatibleRecipes.Add(
                        $"RecipeId={recipe.RowId}, " +
                        $"ItemResultId={recipe.ItemResult.RowId}, " +
                        $"RecipeLevelTableId={recipe.RecipeLevelTable.RowId}, " +
                        $"Number={recipe.Number}");
                }
                if (shown++ >= 20) continue;
                report.AppendLine($"Recipe #{recipe.RowId} | Number={recipe.Number} | Cosmic={cosmic} | Tabela={table.RowId} | Fatores Df={recipe.DifficultyFactor} Qf={recipe.QualityFactor} DurF={recipe.DurabilityFactor}");
                report.AppendLine($"  Calculado P={progress}, Q={quality}, D={durability} | {(match ? "COMPATIVEL" : "DIVERGENTE")}");
            }
            report.AppendLine($"Receitas com nome igual: {count}; compativeis: {exact}.");
            report.AppendLine("=== RECEITAS COMPATIVEIS ===");
            foreach (var candidate in compatibleRecipes)
                report.AppendLine(candidate);
            if (count > 20) report.AppendLine("Exibidas somente as primeiras 20 candidatas.");
            if (exact != 1) report.AppendLine("Nao associar automaticamente uma receita: correspondencia ausente ou ambigua.");
            report.AppendLine("Diagnostico nao altera simulador, rotacoes ou cache.");
        }
        catch (Exception ex)
        {
            report.AppendLine($"Erro: {ex.GetType().Name}: {ex.Message}");
        }
        return report.ToString();
    }
}
