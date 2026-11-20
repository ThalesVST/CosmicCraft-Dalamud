using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;

namespace SamplePlugin;

// Read-only investigation of Cosmic Exploration Excel sheets.
// No network requests, no unsafe pointers, no assumptions about recipe IDs.
public static class CosmicMissionProbe
{
    private static readonly string[] Names = {
        "WKSMission", "WKSMissionUnit", "WKSMissionRecipe",
        "WKSMissionToDo", "WKSMissionReward", "WKSRecipe",
        "WKSCraft", "WKSMissionClass", "WKSMissionLevel"
    };

    public static string Inspect(string displayedName, int candidateId)
    {
        var report = new StringBuilder();
        report.AppendLine($"Pesquisa local: {displayedName}; AtkValue[3]={candidateId} (nao confirmado).");
        try
        {
            var assembly = typeof(Lumina.Excel.Sheets.Recipe).Assembly;
            var manager = Plugin.DataManager;
            var methods = manager.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance);
            var method = methods.FirstOrDefault(m => m.Name == "GetExcelSheet" &&
                m.IsGenericMethodDefinition && m.GetGenericArguments().Length == 1);
            if (method == null) return report + "GetExcelSheet<T> nao encontrado no DataManager.";
            foreach (var name in Names)
            {
                var type = assembly.GetType("Lumina.Excel.Sheets." + name);
                if (type == null) { report.AppendLine(name + ": tipo ausente nesta versao Lumina."); continue; }
                try
                {
                    var generic = method.MakeGenericMethod(type);
                    var args = generic.GetParameters().Select(x =>
                        x.HasDefaultValue ? x.DefaultValue :
                        x.ParameterType.IsValueType ? Activator.CreateInstance(x.ParameterType) : null).ToArray();
                    var sheet = generic.Invoke(manager, args) as IEnumerable;
                    if (sheet == null) { report.AppendLine(name + ": tabela indisponivel."); continue; }
                    int scanned = 0, matches = 0;
                    string schema = "";
                    foreach (var row in sheet)
                    {
                        if (row == null) continue;
                        scanned++;
                        if (scanned == 1)
                        {
                            schema = string.Join(", ", row.GetType()
                                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                                .Where(x => x.GetIndexParameters().Length == 0)
                                .Select(x => x.Name).Take(24));
                        }
                        foreach (var prop in row.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
                        {
                            if (prop.GetIndexParameters().Length != 0) continue;
                            if (prop.PropertyType != typeof(string) && !prop.PropertyType.IsPrimitive &&
                                prop.PropertyType != typeof(uint)) continue;
                            object? value;
                            try { value = prop.GetValue(row); } catch { continue; }
                            var s = Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";
                            if ((candidateId > 0 && s == candidateId.ToString(CultureInfo.InvariantCulture)) ||
                                (s.Length > 3 && s.Contains(displayedName, StringComparison.OrdinalIgnoreCase)))
                            {
                                if (matches++ < 5)
                                    report.AppendLine($"  Possivel referencia: linha {GetRowId(row)}, {prop.Name}={s}");
                            }
                        }
                        if (scanned >= 30000) break;
                    }
                    report.AppendLine($"{name}: {scanned} linhas examinadas; {matches} referencias diretas. Campos: {schema}");
                }
                catch (Exception ex)
                {
                    report.AppendLine($"{name}: erro {ex.GetBaseException().GetType().Name}: {ex.GetBaseException().Message}");
                }
            }
        }
        catch (Exception ex) { report.AppendLine("Falha: " + ex.GetType().Name + ": " + ex.Message); }
        report.AppendLine("Nenhuma referencia encontrada nao prova ausencia: IDs podem estar em campos relacionais.");
        return report.ToString();
    }

    // Count raw RowRef IDs first. Access Value only after the reference is nonzero.
    // This is diagnostic only; no crafting parameters are modified.
    public static string InspectRecipeCollection(string displayedName, int candidateId,
        int maxProgress, int maxDurability, int maxQuality)
    {
        var report = new StringBuilder();
        report.AppendLine($"Receita: {displayedName}; candidato UI={candidateId}; progress={maxProgress}; durability={maxDurability}; quality={maxQuality}");
        try
        {
            var wksType = typeof(Lumina.Excel.Sheets.Recipe).Assembly.GetType("Lumina.Excel.Sheets.WKSMissionRecipe");
            if (wksType == null) return report + "WKSMissionRecipe indisponivel.";
            var property = wksType.GetProperty("Recipe");
            if (property == null) return report + "WKSMissionRecipe.Recipe indisponivel.";
            var method = Plugin.DataManager.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .FirstOrDefault(m => m.Name == "GetExcelSheet" && m.IsGenericMethodDefinition && m.GetGenericArguments().Length == 1);
            if (method == null) return report + "GetExcelSheet<T> indisponivel.";
            var generic = method.MakeGenericMethod(wksType);
            var args = generic.GetParameters().Select(p => p.HasDefaultValue ? p.DefaultValue :
                p.ParameterType.IsValueType ? Activator.CreateInstance(p.ParameterType) : null).ToArray();
            if (generic.Invoke(Plugin.DataManager, args) is not IEnumerable sheet)
                return report + "Tabela indisponivel.";
            int rows = 0, slots = 0, zeros = 0, nonzero = 0, unresolved = 0, resolved = 0, matches = 0;
            var samples = new List<string>();
            var hits = new List<string>();
            var firstTypes = new List<string>();
            foreach (var wksRow in sheet)
            {
                if (wksRow == null) continue;
                rows++;
                if (property.GetValue(wksRow) is not IEnumerable collection) continue;
                int index = 0;
                foreach (var reference in collection)
                {
                    int slot = index++;
                    slots++;
                    if (reference == null) { zeros++; continue; }
                    if (firstTypes.Count < 2)
                    {
                        var t = reference.GetType();
                        firstTypes.Add("RowRef type=" + t.FullName + "; fields=" +
                            string.Join(",", t.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance).Select(f => f.Name)) +
                            "; properties=" + string.Join(",", t.GetProperties().Select(p => p.Name)));
                    }
                    var rawId = GetMember(reference, "RowId") ?? GetField(reference, "RowId");
                    if (!uint.TryParse(Convert.ToString(rawId, CultureInfo.InvariantCulture), out var id) || id == 0)
                    {
                        zeros++;
                        continue;
                    }
                    nonzero++;
                    object? recipe = null;
                    try { recipe = GetMember(reference, "Value"); } catch { }
                    var rowId = recipe == null ? "?" : Convert.ToString(GetRowId(recipe), CultureInfo.InvariantCulture);
                    if (recipe == null) unresolved++; else resolved++;
                    var item = recipe == null ? null : GetMember(recipe, "ItemResult");
                    var itemId = GetMember(item, "RowId") ?? GetField(item, "RowId");
                    var itemValue = GetMember(item, "Value");
                    var itemName = Convert.ToString(GetMember(itemValue, "Name"), CultureInfo.InvariantCulture) ?? "";
                    var line = $"WKS={GetRowId(wksRow)} slot={slot} RefID={id} RecipeRow={rowId} ItemID={itemId ?? "?"} Nome={itemName}";
                    if (samples.Count < 12) samples.Add(line);
                    if ((candidateId > 0 && Convert.ToString(itemId, CultureInfo.InvariantCulture) == candidateId.ToString(CultureInfo.InvariantCulture)) ||
                        (!string.IsNullOrWhiteSpace(itemName) && itemName.Contains(displayedName, StringComparison.OrdinalIgnoreCase)))
                    {
                        matches++;
                        if (hits.Count < 10) hits.Add(line + " / " + FormatRecipe(recipe));
                    }
                }
            }
            report.AppendLine($"Linhas={rows}; slots={slots}; vazios/ID0={zeros}; IDs>0={nonzero}; resolvidos={resolved}; nao resolvidos={unresolved}; correspondencias={matches}");
            foreach (var t in firstTypes) report.AppendLine(t);
            report.AppendLine("Primeiras referencias com ID>0:");
            foreach (var x in samples) report.AppendLine("  " + x);
            if (samples.Count == 0) report.AppendLine("  Nenhuma referencia com RowId numerico >0. Investigar estrutura bruta do RowRef.");
            report.AppendLine("Correspondencias:");
            foreach (var x in hits) report.AppendLine("  " + x);
            if (hits.Count == 0) report.AppendLine("  Nenhuma correspondencia por ItemResult/nome.");
            report.AppendLine("Somente leitura; cache e simulador inalterados.");
        }
        catch (Exception ex) { report.AppendLine("Erro: " + ex.GetBaseException().GetType().Name + ": " + ex.GetBaseException().Message); }
        return report.ToString();
    }


    // Compare UI parameters with resolved WKS recipe values.
    // Read-only: candidate ranking is not a verified identification.
    public static string MatchByParameters(string displayedName, int candidateId,
        int maxProgress, int maxDurability, int maxQuality, int jobId)
    {
        var report = new StringBuilder();
        report.AppendLine($"UI: {displayedName}; candidato={candidateId}; progresso={maxProgress}; durabilidade={maxDurability}; qualidade={maxQuality}; job={jobId}.");
        try
        {
            var wksType = typeof(Lumina.Excel.Sheets.Recipe).Assembly.GetType("Lumina.Excel.Sheets.WKSMissionRecipe");
            if (wksType == null) return report + "Tabela WKSMissionRecipe indisponivel.";
            var method = Plugin.DataManager.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .FirstOrDefault(m => m.Name == "GetExcelSheet" && m.IsGenericMethodDefinition &&
                    m.GetGenericArguments().Length == 1);
            if (method == null) return report + "GetExcelSheet<T> indisponivel.";
            var generic = method.MakeGenericMethod(wksType);
            var args = generic.GetParameters().Select(p => p.HasDefaultValue ? p.DefaultValue :
                p.ParameterType.IsValueType ? Activator.CreateInstance(p.ParameterType) : null).ToArray();
            if (generic.Invoke(Plugin.DataManager, args) is not IEnumerable sheet)
                return report + "Tabela indisponivel.";

            var candidates = new List<(int score, string info)>();
            int examined = 0, calculated = 0, fullMatches = 0;
            var samples = new List<string>();
            foreach (var wksRow in sheet)
            {
                if (wksRow == null) continue;
                if (GetMember(wksRow, "Recipe") is not IEnumerable refs) continue;
                int slot = 0;
                foreach (var reference in refs)
                {
                    int currentSlot = slot++;
                    if (!TryPositiveInt(GetMember(reference, "RowId"), out var refId)) continue;
                    var recipe = GetMember(reference, "Value");
                    if (recipe == null) continue;
                    examined++;
                    var levelRef = GetMember(recipe, "RecipeLevelTable");
                    var level = GetMember(levelRef, "Value");
                    if (level == null) continue;
                    // These are base RecipeLevelTable values; Recipe factors are percentages.
                    int baseDifficulty = ReadInt(level, "Difficulty");
                    int baseQuality = ReadInt(level, "Quality");
                    int baseDurability = ReadInt(level, "Durability");
                    int diffFactor = ReadInt(recipe, "DifficultyFactor");
                    int qualFactor = ReadInt(recipe, "QualityFactor");
                    int duraFactor = ReadInt(recipe, "DurabilityFactor");
                    int difficulty = baseDifficulty * diffFactor / 100;
                    int quality = baseQuality * qualFactor / 100;
                    int durability = baseDurability * duraFactor / 100;
                    if (difficulty <= 0 || quality <= 0 || durability <= 0) continue;
                    calculated++;
                    var itemRef = GetMember(recipe, "ItemResult");
                    int itemId = ReadInt(itemRef, "RowId");
                    var item = GetMember(itemRef, "Value");
                    var itemName = Convert.ToString(GetMember(item, "Name"), CultureInfo.InvariantCulture) ?? "";
                    int levelId = ReadInt(levelRef, "RowId");
                    var craftType = GetMember(recipe, "CraftType");
                    int craftTypeId = ReadInt(craftType, "RowId");
                    int score = (difficulty == maxProgress ? 4 : 0) +
                        (durability == maxDurability ? 3 : 0) +
                        (quality == maxQuality ? 4 : 0) +
                        (itemId == candidateId && candidateId > 0 ? 5 : 0) +
                        (!string.IsNullOrEmpty(itemName) &&
                         itemName.Contains(displayedName, StringComparison.OrdinalIgnoreCase) ? 5 : 0);
                    bool full = difficulty == maxProgress && durability == maxDurability && quality == maxQuality;
                    if (full) fullMatches++;
                    string line = $"WKS={GetRowId(wksRow)} slot={currentSlot} RecipeID={refId} ItemID={itemId} " +
                        $"Nome={itemName} LevelTable={levelId} CraftType={craftTypeId} " +
                        $"P={difficulty} Q={quality} D={durability} " +
                        $"bases=({baseDifficulty},{baseQuality},{baseDurability}) fatores=({diffFactor},{qualFactor},{duraFactor})";
                    if (samples.Count < 3) samples.Add(line);
                    if (score >= 4) candidates.Add((score, line));
                }
            }
            report.AppendLine($"Receitas resolvidas={examined}; calculos completos={calculated}; correspondencias exatas P/Q/D={fullMatches}.");
            report.AppendLine("Amostras dos calculos:");
            foreach (var s in samples) report.AppendLine("  " + s);
            report.AppendLine("Candidatas (pontuacao por igualdade P/Q/D, ID e nome):");
            foreach (var entry in candidates.OrderByDescending(x => x.score).Take(15))
                report.AppendLine($"  score={entry.score}: {entry.info}");
            if (candidates.Count == 0) report.AppendLine("  Nenhuma candidata. Conferir fatores e campos da RecipeLevelTable.");
            if (fullMatches != 1)
                report.AppendLine("Identificacao NAO confirmada: coincidencia de parametros pode ser ambigua.");
            else
                report.AppendLine("Uma correspondencia P/Q/D encontrada; validar profissao e identificadores antes de usar no solver.");
            report.AppendLine("Somente diagnostico: nenhum ganho, macro ou cache alterado.");
        }
        catch (Exception ex) { report.AppendLine("Erro: " + ex.GetBaseException().GetType().Name + ": " + ex.GetBaseException().Message); }
        return report.ToString();
    }


    // Investigate mission linkage, profession and low-difficulty recipes.
    // Reflection is used to keep the diagnostic compatible with varying Lumina schemas.
    public static string InspectMissionAndLowRecipes(
        string displayedName, int candidateId, int maxProgress, int maxDurability,
        int maxQuality, int jobId)
    {
        var report = new StringBuilder();
        report.AppendLine($"UI: {displayedName}; candidato={candidateId}; P={maxProgress}; Q={maxQuality}; D={maxDurability}; job={jobId}.");
        try
        {
            var asm = typeof(Lumina.Excel.Sheets.Recipe).Assembly;
            var manager = Plugin.DataManager;
            var method = manager.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .FirstOrDefault(m => m.Name == "GetExcelSheet" && m.IsGenericMethodDefinition &&
                    m.GetGenericArguments().Length == 1);
            if (method == null) return report + "GetExcelSheet<T> indisponivel.";

            IEnumerable? Sheet(string name)
            {
                var t = asm.GetType("Lumina.Excel.Sheets." + name);
                if (t == null) return null;
                var g = method.MakeGenericMethod(t);
                var args = g.GetParameters().Select(p => p.HasDefaultValue ? p.DefaultValue :
                    p.ParameterType.IsValueType ? Activator.CreateInstance(p.ParameterType) : null).ToArray();
                return g.Invoke(manager, args) as IEnumerable;
            }

            var unitSheet = Sheet("WKSMissionUnit");
            var unitExamples = new List<string>();
            int unitCount = 0, unitRefs = 0;
            if (unitSheet != null)
            {
                foreach (var row in unitSheet)
                {
                    if (row == null) continue;
                    unitCount++;
                    var recipeLink = GetMember(row, "WKSMissionRecipe");
                    int linkId = ReadInt(recipeLink, "RowId");
                    if (linkId > 0) unitRefs++;
                    if (unitExamples.Count < 8 && (linkId > 0 || unitCount <= 3))
                    {
                        var unitName = Convert.ToString(GetMember(row, "Name"), CultureInfo.InvariantCulture) ?? "";
                        var classValue = GetMember(row, "ClassJobCategory");
                        unitExamples.Add($"Unit={GetRowId(row)} Name={unitName} WKSMissionRecipe={linkId} " +
                            $"ClassJobCategory={ReadInt(classValue, "RowId")} LevelGroup={GetMember(row, "LevelGroup") ?? "?"} " +
                            $"rawLinkType={recipeLink?.GetType().Name ?? "null"}");
                    }
                }
            }
            report.AppendLine($"WKSMissionUnit: {unitCount} linhas; links WKSMissionRecipe com RowId>0: {unitRefs}.");
            foreach (var s in unitExamples) report.AppendLine("  " + s);

            var wksSheet = Sheet("WKSMissionRecipe");
            if (wksSheet == null) return report + "WKSMissionRecipe indisponivel.";
            var recipes = new List<(int p, int q, int d, int level, int job, string line)>();
            var craftTypeExamples = new List<string>();
            int validRefs = 0, computed = 0, zeroFactors = 0;
            foreach (var wks in wksSheet)
            {
                if (wks == null || GetMember(wks, "Recipe") is not IEnumerable refs) continue;
                int slot = 0;
                foreach (var r in refs)
                {
                    int thisSlot = slot++;
                    if (!TryPositiveInt(GetMember(r, "RowId"), out var recipeId)) continue;
                    var recipe = GetMember(r, "Value");
                    if (recipe == null) continue;
                    validRefs++;
                    var lvlRef = GetMember(recipe, "RecipeLevelTable");
                    var lvl = GetMember(lvlRef, "Value");
                    if (lvl == null) continue;
                    int baseP = ReadInt(lvl, "Difficulty"), baseQ = ReadInt(lvl, "Quality"),
                        baseD = ReadInt(lvl, "Durability");
                    int factorP = ReadInt(recipe, "DifficultyFactor"),
                        factorQ = ReadInt(recipe, "QualityFactor"),
                        factorD = ReadInt(recipe, "DurabilityFactor");
                    if (factorP == 0 || factorQ == 0 || factorD == 0) { zeroFactors++; continue; }
                    int p = baseP * factorP / 100, q = baseQ * factorQ / 100,
                        d = baseD * factorD / 100;
                    if (p <= 0 || q <= 0 || d <= 0) continue;
                    computed++;
                    var itemRef = GetMember(recipe, "ItemResult");
                    var item = GetMember(itemRef, "Value");
                    var craftType = GetMember(recipe, "CraftType");
                    int craftId = ReadInt(craftType, "RowId");
                    if (craftTypeExamples.Count < 4)
                        craftTypeExamples.Add($"Recipe={recipeId}; CraftType raw={craftType ?? "null"}; " +
                            $"type={craftType?.GetType().FullName ?? "null"}; RowId={craftId}; " +
                            $"properties={string.Join(",", craftType?.GetType().GetProperties().Select(x => x.Name).Take(8) ?? Array.Empty<string>())}");
                    var name = Convert.ToString(GetMember(item, "Name"), CultureInfo.InvariantCulture) ?? "";
                    var line = $"WKS={GetRowId(wks)} slot={thisSlot} Recipe={recipeId} " +
                        $"Item={ReadInt(itemRef, "RowId")} Name={name} Level={ReadInt(lvlRef, "RowId")} " +
                        $"CraftTypeRaw={craftType ?? "?"} P={p} Q={q} D={d}";
                    recipes.Add((p, q, d, ReadInt(lvlRef, "RowId"), craftId, line));
                }
            }
            report.AppendLine($"Receitas WKS: refs={validRefs}; calculadas={computed}; fatores zero={zeroFactors}.");
            report.AppendLine("Inspecao de CraftType:");
            foreach (var s in craftTypeExamples) report.AppendLine("  " + s);
            report.AppendLine("Menores progressos encontrados:");
            foreach (var x in recipes.OrderBy(x => x.p).ThenBy(x => x.q).Take(12))
                report.AppendLine("  " + x.line);
            report.AppendLine("Mais proximas da UI (distancia relativa P/Q/D; NAO identificacao):");
            double Distance((int p, int q, int d, int level, int job, string line) x) =>
                Math.Abs(x.p - maxProgress) / (double)Math.Max(1, maxProgress) +
                Math.Abs(x.q - maxQuality) / (double)Math.Max(1, maxQuality) +
                Math.Abs(x.d - maxDurability) / (double)Math.Max(1, maxDurability);
            foreach (var x in recipes.OrderBy(Distance).Take(12))
                report.AppendLine($"  delta={Distance(x):F3} {x.line}");
            report.AppendLine("Distribuicao por durabilidade: " +
                string.Join("; ", recipes.GroupBy(x => x.d).OrderBy(x => x.Key)
                    .Select(g => $"{g.Key}:{g.Count()}")));
            report.AppendLine("Atenção: CraftType pode ser escalar (byte), nao RowRef; confirmar mapeamento job.");
            report.AppendLine("Somente leitura: nao altera cache, solver ou ganhos.");
        }
        catch (Exception ex)
        {
            report.AppendLine("Erro: " + ex.GetBaseException().GetType().Name + ": " +
                ex.GetBaseException().Message);
        }
        return report.ToString();
    }


    // Trace mission names to WKS recipe groups and inspect actual CraftType values.
    // Diagnostic only: no candidate is automatically trusted.
    public static string TraceMissionByNameAndJob(string displayedName, int candidateId,
        int maxProgress, int maxDurability, int maxQuality, int jobId)
    {
        var output = new StringBuilder();
        output.AppendLine($"UI: {displayedName}; candidato={candidateId}; P={maxProgress}; Q={maxQuality}; D={maxDurability}; job={jobId}.");
        try
        {
            var assembly = typeof(Lumina.Excel.Sheets.Recipe).Assembly;
            var getSheet = Plugin.DataManager.GetType()
                .GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .FirstOrDefault(m => m.Name == "GetExcelSheet" &&
                    m.IsGenericMethodDefinition && m.GetGenericArguments().Length == 1);
            if (getSheet == null) return output + "GetExcelSheet<T> indisponivel.";
            IEnumerable? Load(string typeName)
            {
                var type = assembly.GetType("Lumina.Excel.Sheets." + typeName);
                if (type == null) return null;
                var method = getSheet.MakeGenericMethod(type);
                var args = method.GetParameters().Select(p => p.HasDefaultValue ? p.DefaultValue :
                    p.ParameterType.IsValueType ? Activator.CreateInstance(p.ParameterType) : null).ToArray();
                return method.Invoke(Plugin.DataManager, args) as IEnumerable;
            }
            var units = Load("WKSMissionUnit");
            if (units == null) return output + "WKSMissionUnit indisponivel.";
            var matches = new List<string>();
            var near = new List<string>();
            int examined = 0, linked = 0;
            string Normalize(string s) => new string(s.ToLowerInvariant()
                .Where(char.IsLetterOrDigit).ToArray());
            var target = Normalize(displayedName);
            foreach (var unit in units)
            {
                if (unit == null) continue;
                examined++;
                string name = Convert.ToString(GetMember(unit, "Name"), CultureInfo.InvariantCulture) ?? "";
                int wksRecipeId = ReadInt(GetMember(unit, "WKSMissionRecipe"), "RowId");
                if (wksRecipeId > 0) linked++;
                var normalized = Normalize(name);
                if (target.Length < 3 || normalized.Length < 3) continue;
                bool exact = normalized == target;
                bool similar = exact || normalized.Contains(target, StringComparison.Ordinal) ||
                    target.Contains(normalized, StringComparison.Ordinal);
                if (!similar) continue;
                var classRef = GetMember(unit, "ClassJobCategory");
                var classValue = GetMember(classRef, "Value");
                string line = $"Unit={GetRowId(unit)} Name={name}; WKSRecipe={wksRecipeId}; " +
                    $"ClassJobCategory={GetMember(classRef, "RowId") ?? "?"}; " +
                    $"ClassCategoryName={GetMember(classValue, "Name") ?? "?"}; " +
                    $"LevelGroup={GetMember(unit, "LevelGroup") ?? "?"}";
                if (exact) matches.Add(line);
                else if (near.Count < 15) near.Add(line);
            }
            output.AppendLine($"Missoes={examined}; com referencia WKSRecipe={linked}; nome exato={matches.Count}; nome parcial={near.Count} (max 15 exibidas).");
            output.AppendLine("Missoes com nome exato:");
            foreach (var line in matches.Take(15)) output.AppendLine("  " + line);
            output.AppendLine("Missoes com nome parcial:");
            foreach (var line in near) output.AppendLine("  " + line);

            var wksRecipes = Load("WKSMissionRecipe");
            if (wksRecipes == null) return output + "WKSMissionRecipe indisponivel.";
            int examinedRecipes = 0, craftTypeZero = 0;
            var types = new Dictionary<int, int>();
            var craftExamples = new List<string>();
            var targetWords = displayedName.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Where(x => x.Length >= 4).ToArray();
            var nameHits = new List<string>();
            foreach (var group in wksRecipes)
            {
                if (group == null || GetMember(group, "Recipe") is not IEnumerable references) continue;
                foreach (var reference in references)
                {
                    if (!TryPositiveInt(GetMember(reference, "RowId"), out int recipeId)) continue;
                    var recipe = GetMember(reference, "Value");
                    if (recipe == null) continue;
                    examinedRecipes++;
                    var craftRef = GetMember(recipe, "CraftType");
                    // RowId=0 can be a valid CraftType; do not treat it as missing.
                    var craftIdObject = GetMember(craftRef, "RowId");
                    int craftId = -1;
                    if (craftIdObject != null)
                        int.TryParse(Convert.ToString(craftIdObject, CultureInfo.InvariantCulture), out craftId);
                    if (craftId == 0) craftTypeZero++;
                    types[craftId] = types.TryGetValue(craftId, out int count) ? count + 1 : 1;
                    var craftValue = GetMember(craftRef, "Value");
                    var craftName = Convert.ToString(GetMember(craftValue, "Name"), CultureInfo.InvariantCulture) ?? "";
                    if (craftExamples.Count < 12 && !craftExamples.Any(x => x.Contains($"CraftTypeId={craftId};")))
                        craftExamples.Add($"Recipe={recipeId}; CraftTypeId={craftId}; CraftTypeName={craftName}; " +
                            $"RefIsValid={GetMember(craftRef, "IsValid") ?? "?"}");
                    var itemRef = GetMember(recipe, "ItemResult");
                    var itemName = Convert.ToString(GetMember(GetMember(itemRef, "Value"), "Name"),
                        CultureInfo.InvariantCulture) ?? "";
                    if (targetWords.Any(w => itemName.Contains(w, StringComparison.OrdinalIgnoreCase)) &&
                        nameHits.Count < 20)
                        nameHits.Add($"Recipe={recipeId}; Item={GetMember(itemRef, "RowId") ?? "?"}; " +
                            $"Name={itemName}; CraftTypeId={craftId}; WKSRecipe={GetRowId(group)}");
                }
            }
            output.AppendLine($"Receitas resolvidas={examinedRecipes}; CraftTypeId=0 em {craftTypeZero}.");
            output.AppendLine("Distribuicao de CraftTypeId: " +
                string.Join(", ", types.OrderBy(x => x.Key).Select(x => $"{x.Key}:{x.Value}")));
            output.AppendLine("Exemplos de profissao (ID 0 e permitido):");
            foreach (var line in craftExamples) output.AppendLine("  " + line);
            output.AppendLine("Receitas com palavras do nome do item:");
            foreach (var line in nameHits) output.AppendLine("  " + line);
            if (matches.Count == 0 && near.Count == 0 && nameHits.Count == 0)
                output.AppendLine("Sem ligacao por nome. Nomes de missoes e itens podem diferir.");
            output.AppendLine("Nao ha mapeamento confirmado CraftTypeId -> ClassJob.RowId. Nenhuma identificacao automatica.");
            output.AppendLine("Somente diagnostico; cache, macros e ganhos inalterados.");
        }
        catch (Exception ex)
        {
            output.AppendLine("Erro: " + ex.GetBaseException().GetType().Name + ": " +
                ex.GetBaseException().Message);
        }
        return output.ToString();
    }


    // Find a WKS mission by its mission name, not by the crafted item's name.
    // Read-only investigation; does not modify crafting parameters.
    public static string TraceByMissionName(string missionName, string displayedItem,
        int candidateItemId, int maxProgress, int maxDurability, int maxQuality, int jobId)
    {
        var output = new StringBuilder();
        if (string.IsNullOrWhiteSpace(missionName)) return "Digite o nome da missao.";
        output.AppendLine($"Missao buscada: {missionName}; item exibido: {displayedItem}; UI P={maxProgress} Q={maxQuality} D={maxDurability}; job={jobId}; candidato={candidateItemId}.");
        try
        {
            var assembly = typeof(Lumina.Excel.Sheets.Recipe).Assembly;
            var getSheet = Plugin.DataManager.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .FirstOrDefault(m => m.Name == "GetExcelSheet" && m.IsGenericMethodDefinition &&
                    m.GetGenericArguments().Length == 1);
            if (getSheet == null) return output + "GetExcelSheet<T> indisponivel.";
            IEnumerable? Load(string name)
            {
                var type = assembly.GetType("Lumina.Excel.Sheets." + name);
                if (type == null) return null;
                var method = getSheet.MakeGenericMethod(type);
                var args = method.GetParameters().Select(p => p.HasDefaultValue ? p.DefaultValue :
                    p.ParameterType.IsValueType ? Activator.CreateInstance(p.ParameterType) : null).ToArray();
                return method.Invoke(Plugin.DataManager, args) as IEnumerable;
            }
            var units = Load("WKSMissionUnit");
            var groups = Load("WKSMissionRecipe");
            if (units == null || groups == null) return output + "Tabela WKS indisponivel.";
            var groupsById = new Dictionary<int, object>();
            foreach (var group in groups)
            {
                if (group == null) continue;
                if (int.TryParse(Convert.ToString(GetRowId(group), CultureInfo.InvariantCulture), out int id))
                    groupsById[id] = group;
            }
            string Normalize(string s) => new string(s.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());
            string target = Normalize(missionName);
            int matches = 0, exact = 0;
            foreach (var unit in units)
            {
                if (unit == null) continue;
                var name = Convert.ToString(GetMember(unit, "Name"), CultureInfo.InvariantCulture) ?? "";
                string normalized = Normalize(name);
                if (normalized.Length == 0 ||
                    !(normalized == target || normalized.Contains(target, StringComparison.Ordinal) ||
                      target.Contains(normalized, StringComparison.Ordinal))) continue;
                if (normalized == target) exact++;
                matches++;
                if (matches > 12) continue;
                int groupId = ReadInt(GetMember(unit, "WKSMissionRecipe"), "RowId");
                output.AppendLine($"Missao Unit={GetRowId(unit)} Nome={name}; WKSMissionRecipe={groupId}; " +
                    $"ClassJobCategory={GetMember(GetMember(unit, "ClassJobCategory"), "RowId") ?? "?"}; " +
                    $"LevelGroup={GetMember(unit, "LevelGroup") ?? "?"}");
                if (!groupsById.TryGetValue(groupId, out var group))
                {
                    output.AppendLine("  Grupo nao encontrado ou referencia vazia.");
                    continue;
                }
                if (GetMember(group, "Recipe") is not IEnumerable refs)
                {
                    output.AppendLine("  Grupo sem colecao Recipe.");
                    continue;
                }
                int slot = 0, found = 0;
                foreach (var reference in refs)
                {
                    int currentSlot = slot++;
                    if (!TryPositiveInt(GetMember(reference, "RowId"), out int recipeId)) continue;
                    var recipe = GetMember(reference, "Value");
                    if (recipe == null) continue;
                    found++;
                    var itemRef = GetMember(recipe, "ItemResult");
                    var item = GetMember(itemRef, "Value");
                    string itemName = Convert.ToString(GetMember(item, "Name"), CultureInfo.InvariantCulture) ?? "";
                    var craftRef = GetMember(recipe, "CraftType");
                    string craftTypeId = Convert.ToString(GetMember(craftRef, "RowId"), CultureInfo.InvariantCulture) ?? "?";
                    var levelRef = GetMember(recipe, "RecipeLevelTable");
                    var level = GetMember(levelRef, "Value");
                    int baseP = ReadInt(level, "Difficulty"), baseQ = ReadInt(level, "Quality"), baseD = ReadInt(level, "Durability");
                    int fp = ReadInt(recipe, "DifficultyFactor"), fq = ReadInt(recipe, "QualityFactor"), fd = ReadInt(recipe, "DurabilityFactor");
                    int p = baseP * fp / 100, q = baseQ * fq / 100, d = baseD * fd / 100;
                    bool same = p == maxProgress && q == maxQuality && d == maxDurability;
                    output.AppendLine($"  slot={currentSlot}; RecipeID={recipeId}; ItemID={GetMember(itemRef, "RowId") ?? "?"}; " +
                        $"Nome={itemName}; CraftType={craftTypeId}; LevelTable={GetMember(levelRef, "RowId") ?? "?"}; " +
                        $"P={p} Q={q} D={d}; parametrosIguais={same}");
                }
                if (found == 0) output.AppendLine("  Nenhuma referencia de receita preenchida.");
            }
            output.AppendLine($"Missoes correspondentes={matches}; nome exato={exact}.");
            if (matches == 0) output.AppendLine("Nenhuma missao encontrada por nome; verificar texto exato no jogo.");
            output.AppendLine("Diagnostico somente leitura. Nenhuma receita foi ativada no solver ou cache.");
        }
        catch (Exception ex)
        {
            output.AppendLine("Erro: " + ex.GetBaseException().GetType().Name + ": " + ex.GetBaseException().Message);
        }
        return output.ToString();
    }


    // Read-only comparison of crafting gain formulas against RecipeLevelTable fields.
    // RecipeLevelTable=690 is not automatically assumed to be the effective sync level.
    public static string InspectGainFormula(string missionName, string displayedItem,
        int jobId, int playerLevel, int craftsmanship, int control)
    {
        var result = new StringBuilder();
        result.AppendLine($"Mission={missionName}; Item={displayedItem}; Job={jobId}; PlayerLevel={playerLevel}; Craftsmanship={craftsmanship}; Control={control}");
        try
        {
            var asm = typeof(Lumina.Excel.Sheets.Recipe).Assembly;
            var method = Plugin.DataManager.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .FirstOrDefault(m => m.Name == "GetExcelSheet" && m.IsGenericMethodDefinition && m.GetGenericArguments().Length == 1);
            if (method == null) return result + "GetExcelSheet<T> indisponivel.";
            IEnumerable? Load(string sheetName)
            {
                var type = asm.GetType("Lumina.Excel.Sheets." + sheetName);
                if (type == null) return null;
                var generic = method.MakeGenericMethod(type);
                var args = generic.GetParameters().Select(x => x.HasDefaultValue ? x.DefaultValue :
                    x.ParameterType.IsValueType ? Activator.CreateInstance(x.ParameterType) : null).ToArray();
                return generic.Invoke(Plugin.DataManager, args) as IEnumerable;
            }
            var units = Load("WKSMissionUnit");
            var groups = Load("WKSMissionRecipe");
            if (units == null || groups == null) return result + "Tabelas WKS indisponiveis.";
            var groupMap = new Dictionary<int, object>();
            foreach (var row in groups)
            {
                if (row != null && int.TryParse(Convert.ToString(GetRowId(row), CultureInfo.InvariantCulture), out var id))
                    groupMap[id] = row;
            }
            string Normalize(string text) => new string(text.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());
            int matches = 0;
            foreach (var unit in units)
            {
                if (unit == null) continue;
                string name = Convert.ToString(GetMember(unit, "Name"), CultureInfo.InvariantCulture) ?? "";
                if (Normalize(name) != Normalize(missionName)) continue;
                int groupId = ReadInt(GetMember(unit, "WKSMissionRecipe"), "RowId");
                if (!groupMap.TryGetValue(groupId, out var group) || GetMember(group, "Recipe") is not IEnumerable refs) continue;
                foreach (var reference in refs)
                {
                    if (!TryPositiveInt(GetMember(reference, "RowId"), out var recipeId)) continue;
                    var recipe = GetMember(reference, "Value");
                    if (recipe == null) continue;
                    var item = GetMember(GetMember(recipe, "ItemResult"), "Value");
                    string itemName = Convert.ToString(GetMember(item, "Name"), CultureInfo.InvariantCulture) ?? "";
                    if (Normalize(itemName) != Normalize(displayedItem)) continue;
                    int craftType = ReadInt(GetMember(recipe, "CraftType"), "RowId");
                    if (craftType != jobId - 8) continue;
                    matches++;
                    var levelRef = GetMember(recipe, "RecipeLevelTable");
                    var level = GetMember(levelRef, "Value");
                    result.AppendLine($"Unit={GetRowId(unit)} Group={groupId} Recipe={recipeId} LevelTable={GetMember(levelRef, "RowId") ?? "?"} LevelGroup={GetMember(unit, "LevelGroup") ?? "?"}");
                    if (level == null) continue;
                    foreach (var field in new[] { "ClassJobLevel", "Difficulty", "Quality", "Durability",
                        "ProgressDivider", "QualityDivider", "ProgressModifier", "QualityModifier",
                        "ProgressDivisor", "QualityDivisor", "ProgressMultiplier", "QualityMultiplier" })
                        result.AppendLine($"  {field}={GetMember(level, field) ?? "<ausente>"}");
                    int pd = ReadInt(level, "ProgressDivider"), qd = ReadInt(level, "QualityDivider");
                    int pm = ReadInt(level, "ProgressModifier"), qm = ReadInt(level, "QualityModifier");
                    if (pd > 0 && qd > 0 && pm > 0 && qm > 0)
                    {
                        // Candidate formula only. Cast to long to avoid overflow.
                        long baseP = ((long)craftsmanship * 10 / pd + 2) * pm / 100;
                        long baseQ = ((long)control * 10 / qd + 35) * qm / 100;
                        result.AppendLine($"  Formula candidata: BasicSynthesis100%={baseP}; BasicTouch100%={baseQ}");
                        result.AppendLine($"  BasicSynthesis120% (se nivel>=31)={baseP * 120 / 100}");
                    }
                    else result.AppendLine("  Formula nao calculada: campos ausentes ou zerados.");
                }
            }
            result.AppendLine($"Receitas com missao, item e profissao coincidentes={matches}.");
            result.AppendLine("Os ganhos calculados sao HIPOTESES. Validar com chat sem buffs/condicoes especiais.");
            result.AppendLine("Nenhum ganho foi aplicado ao simulador ou salvo no cache.");
        }
        catch (Exception ex) { result.AppendLine("Erro: " + ex.GetBaseException().Message); }
        return result.ToString();
    }


    // Reverse-search local RecipeLevelTable for observed action gains.
    // Results are hypotheses, not a confirmed quest-sync formula.
    public static string CompareObservedGains(int playerLevel, int craftsmanship,
        int control, int observedSynthesis, int observedTouch)
    {
        var report = new StringBuilder();
        report.AppendLine($"Nivel={playerLevel}; Craftsmanship={craftsmanship}; Control={control}; " +
            $"Basic Synthesis observado={observedSynthesis}; Basic Touch observado={observedTouch}.");
        report.AppendLine("Assumimos condicao Normal, sem buffs e sem bonus de qualidade. Confirmar no jogo.");
        if (observedSynthesis <= 0 || observedTouch <= 0)
            return report + "Informe os dois ganhos reais maiores que zero.";
        try
        {
            var sheetType = typeof(Lumina.Excel.Sheets.RecipeLevelTable);
            var method = Plugin.DataManager.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .FirstOrDefault(m => m.Name == "GetExcelSheet" && m.IsGenericMethodDefinition &&
                    m.GetGenericArguments().Length == 1);
            if (method == null) return report + "GetExcelSheet<T> indisponivel.";
            var generic = method.MakeGenericMethod(sheetType);
            var args = generic.GetParameters().Select(p => p.HasDefaultValue ? p.DefaultValue :
                p.ParameterType.IsValueType ? Activator.CreateInstance(p.ParameterType) : null).ToArray();
            if (generic.Invoke(Plugin.DataManager, args) is not IEnumerable rows)
                return report + "RecipeLevelTable indisponivel.";
            var candidates = new List<(int rowId, int level, int pd, int qd, int pm, int qm,
                int p100, int p120, int q100, int error)>();
            foreach (var row in rows)
            {
                if (row == null) continue;
                int pd = ReadInt(row, "ProgressDivider"), qd = ReadInt(row, "QualityDivider");
                int pm = ReadInt(row, "ProgressModifier"), qm = ReadInt(row, "QualityModifier");
                if (pd <= 0 || qd <= 0 || pm <= 0 || qm <= 0) continue;
                int p100 = (int)(((long)craftsmanship * 10 / pd + 2) * pm / 100);
                int p120 = p100 * 120 / 100;
                int q100 = (int)(((long)control * 10 / qd + 35) * qm / 100);
                int error = Math.Min(Math.Abs(p100 - observedSynthesis), Math.Abs(p120 - observedSynthesis))
                    + Math.Abs(q100 - observedTouch);
                candidates.Add((Convert.ToInt32(GetRowId(row), CultureInfo.InvariantCulture),
                    ReadInt(row, "ClassJobLevel"), pd, qd, pm, qm, p100, p120, q100, error));
            }
            report.AppendLine($"Linhas com divisores validos: {candidates.Count}.");
            report.AppendLine("Melhores correspondencias (erro absoluto combinado):");
            foreach (var x in candidates.OrderBy(x => x.error)
                .ThenBy(x => Math.Abs(x.level - playerLevel)).Take(12))
                report.AppendLine($"  Row={x.rowId} ClassJobLevel={x.level} " +
                    $"P100={x.p100} P120={x.p120} Q100={x.q100} erro={x.error} " +
                    $"PD={x.pd} QD={x.qd} PM={x.pm} QM={x.qm}");
            report.AppendLine("Linhas proximas do nivel atual (ClassJobLevel +/- 3):");
            foreach (var x in candidates.Where(x => Math.Abs(x.level - playerLevel) <= 3)
                .OrderBy(x => Math.Abs(x.level - playerLevel)).ThenBy(x => x.rowId).Take(18))
                report.AppendLine($"  Row={x.rowId} ClassJobLevel={x.level} " +
                    $"P100={x.p100} P120={x.p120} Q100={x.q100} erro={x.error} " +
                    $"PD={x.pd} QD={x.qd} PM={x.pm} QM={x.qm}");
            int exact = candidates.Count(x => x.error == 0);
            report.AppendLine($"Linhas com correspondencia numerica exata: {exact}.");
            report.AppendLine("ATENCAO: igualdade numerica nao comprova sincronizacao nem identifica uma receita.");
            report.AppendLine("Basic Synthesis 120% usa eficiencia nominal; arredondamentos do jogo podem diferir.");
            report.AppendLine("Somente diagnostico. Nenhuma alteracao no simulador ou cache.");
        }
        catch (Exception ex)
        {
            report.AppendLine("Erro: " + ex.GetBaseException().GetType().Name + ": " + ex.GetBaseException().Message);
        }
        return report.ToString();
    }

    private static int ReadInt(object? obj, string member)
    {
        var value = GetMember(obj, member) ?? GetField(obj, member);
        return TryPositiveInt(value, out var result) ? result : 0;
    }

    private static bool TryPositiveInt(object? obj, out int result)
    {
        result = 0;
        return obj != null && int.TryParse(Convert.ToString(obj, CultureInfo.InvariantCulture),
            NumberStyles.Integer, CultureInfo.InvariantCulture, out result) && result > 0;
    }

    private static object? GetField(object? value, string name)
    {
        try { return value?.GetType().GetField(name)?.GetValue(value); }
        catch { return null; }
    }

    private static object? GetMember(object? value, string name)
    {
        try { return value?.GetType().GetProperty(name)?.GetValue(value); }
        catch { return null; }
    }

    private static string FormatRecipe(object? value)
    {
        if (value == null) return "indisponivel";
        var result = new List<string>();
        foreach (var field in new[] { "RowId", "ItemResult", "RecipeLevelTable", "DifficultyFactor", "QualityFactor", "DurabilityFactor" })
        {
            var v = GetMember(value, field);
            if (v != null) result.Add(field + "=" + (GetMember(v, "RowId") ?? v));
        }
        return string.Join("; ", result);
    }

    private static object? GetRowId(object row)
    {
        return row.GetType().GetProperty("RowId")?.GetValue(row) ?? "?";
    }
}
