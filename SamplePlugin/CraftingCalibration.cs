using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace SamplePlugin;

/// <summary>
/// Calibrates base actions from two observed synthesis snapshots.
/// Only accept observations made without active buffs, special conditions,
/// or recipe transitions. The UI must explicitly identify the action.
/// </summary>
public sealed class CraftingCalibration
{
    private readonly Dictionary<string, CalibrationResult> learned = new(StringComparer.Ordinal);
    private Observation? pending;
    private readonly string savePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CosmicCraft", "calibrations-v1.json");

    public CraftingCalibration()
    {
        try
        {
            if (!File.Exists(savePath)) return;
            var items = JsonSerializer.Deserialize<List<SavedCalibration>>(File.ReadAllText(savePath));
            if (items is null) return;
            foreach (var item in items)
            {
                if (string.IsNullOrWhiteSpace(item.Recipe) || item.JobId < 8 || item.JobId > 15 ||
                    item.MaxProgress <= 0 || item.MaxDurability <= 0 ||
                    item.SynthesisGain is <= 0 || item.TouchGain is <= 0) continue;
                learned[Key(item.Recipe, item.JobId, item.MaxProgress, item.MaxDurability)] =
                    new CalibrationResult(item.SynthesisGain, item.TouchGain, item.Recipe, item.JobId);
            }
            Status = $"{learned.Count} calibracoes carregadas do disco.";
        }
        catch (Exception ex)
        {
            Status = $"Nao foi possivel carregar calibracoes: {ex.Message}";
        }
    }

    private void Save()
    {
        try
        {
            var items = new List<SavedCalibration>();
            foreach (var pair in learned)
            {
                var parts = pair.Key.Split('|', 4);
                if (parts.Length != 4 || !int.TryParse(parts[0], out var job) ||
                    !int.TryParse(parts[2], out var progress) || !int.TryParse(parts[3], out var durability))
                    continue;
                items.Add(new SavedCalibration(parts[1], job, progress, durability,
                    pair.Value.SynthesisGain, pair.Value.TouchGain));
            }
            Directory.CreateDirectory(Path.GetDirectoryName(savePath)!);
            var temporary = savePath + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(items, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temporary, savePath, true);
        }
        catch (Exception ex)
        {
            Status += $" AVISO: nao foi possivel salvar: {ex.Message}";
        }
    }

    private sealed record SavedCalibration(string Recipe, int JobId, int MaxProgress,
        int MaxDurability, int? SynthesisGain, int? TouchGain);

    public string Status { get; private set; } = "Pronto para calibrar.";
    public bool IsPending => pending is not null;

    public void Begin(string recipe, int jobId, CraftAction action, CraftingState state)
    {
        if (action != CraftAction.BasicSynthesis && action != CraftAction.BasicTouch)
        {
            Status = "Somente Basic Synthesis e Basic Touch podem ser calibrados.";
            return;
        }
        if (string.IsNullOrWhiteSpace(recipe) || state.MaxProgress <= 0 || state.MaxDurability <= 0)
        {
            Status = "Abra uma sintese valida antes de iniciar a calibracao.";
            return;
        }
        if (state.InnovationTurns != 0 || state.GreatStridesTurns != 0 || state.WasteNotTurns != 0)
        {
            Status = "Calibre sem buffs ativos.";
            return;
        }
        pending = new Observation(recipe, jobId, action, state.Clone());
        Status = $"Execute uma unica acao {action} no jogo e clique em Medir resultado.";
    }

    public bool Finish(string recipe, int jobId, CraftingState after, string conditionBefore, string conditionAfter)
    {
        if (pending is null)
        {
            Status = "Nenhuma medicao iniciada.";
            return false;
        }
        var observation = pending;
        pending = null;
        var before = observation.State;
        if (!string.Equals(observation.Recipe, recipe, StringComparison.Ordinal) || observation.JobId != jobId ||
            before.MaxProgress != after.MaxProgress || before.MaxDurability != after.MaxDurability)
        {
            Status = "Receita, profissao ou parametros mudaram. Medicao descartada.";
            return false;
        }
        if (!string.Equals(conditionBefore, "Normal", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(conditionAfter, "Normal", StringComparison.OrdinalIgnoreCase))
        {
            Status = "Condicao diferente de Normal. Medicao descartada.";
            return false;
        }
        var progress = after.Progress - before.Progress;
        var quality = after.Quality - before.Quality;
        var durability = before.Durability - after.Durability;
        if (durability != 10)
        {
            Status = "Durabilidade nao diminuiu exatamente 10. Possivel buff ou acao extra; medicao descartada.";
            return false;
        }
        var key = Key(recipe, jobId, before.MaxProgress, before.MaxDurability);
        learned.TryGetValue(key, out var old);
        if (observation.Action == CraftAction.BasicSynthesis)
        {
            if (progress <= 0 || quality != 0 || before.Progress + progress >= before.MaxProgress)
            {
                Status = "Progresso nao mensuravel com seguranca (pode ter sido limitado pelo maximo).";
                return false;
            }
            learned[key] = new CalibrationResult(progress, old?.TouchGain, recipe, jobId);
            Status = $"Basic Synthesis medido: +{progress} progresso.";
        }
        else
        {
            if (quality <= 0 || progress != 0 || (before.MaxQuality > 0 && after.Quality >= before.MaxQuality))
            {
                Status = "Qualidade nao mensuravel com seguranca (pode ter sido limitada pelo maximo).";
                return false;
            }
            learned[key] = new CalibrationResult(old?.SynthesisGain, quality, recipe, jobId);
            Status = $"Basic Touch medido: +{quality} qualidade.";
        }
        Save();
        return true;
    }

    // O chat fornece o ganho real, mesmo quando o valor exibido na barra foi limitado.
    // A interface exige confirmacao humana de condicao Normal e ausencia de buffs.
    public bool SaveChatGain(string recipe, int jobId, CraftingState state, CraftAction action, int gain)
    {
        if (string.IsNullOrWhiteSpace(recipe) || jobId < 8 || jobId > 15 ||
            state.MaxProgress <= 0 || state.MaxDurability <= 0 || gain <= 0 || gain > 100000 ||
            (action != CraftAction.BasicSynthesis && action != CraftAction.BasicTouch))
        {
            Status = "Leitura do chat invalida; nada foi salvo.";
            return false;
        }
        var key = Key(recipe, jobId, state.MaxProgress, state.MaxDurability);
        learned.TryGetValue(key, out var old);
        learned[key] = action == CraftAction.BasicTouch
            ? new CalibrationResult(old?.SynthesisGain, gain, recipe, jobId)
            : new CalibrationResult(gain, old?.TouchGain, recipe, jobId);
        Status = $"Ganho do chat confirmado: {action} +{gain}. Calibracao atualizada.";
        Save();
        return true;
    }

    public CalibrationResult? Get(string recipe, int jobId, CraftingState state) =>
        learned.TryGetValue(Key(recipe, jobId, state.MaxProgress, state.MaxDurability), out var result) ? result : null;

    public void Cancel()
    {
        pending = null;
        Status = "Medicao cancelada.";
    }

    private static string Key(string recipe, int jobId, int maxProgress, int maxDurability) =>
        $"{jobId}|{recipe}|{maxProgress}|{maxDurability}";

    private sealed record Observation(string Recipe, int JobId, CraftAction Action, CraftingState State);
}

public sealed record CalibrationResult(int? SynthesisGain, int? TouchGain, string Recipe, int JobId)
{
    public bool Complete => SynthesisGain is > 0 && TouchGain is > 0;
}
