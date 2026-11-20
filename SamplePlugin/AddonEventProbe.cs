using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Plugin.Services;

namespace SamplePlugin;

/// <summary>Diagnostico somente leitura de eventos de addons.</summary>
public sealed class AddonEventProbe : IDisposable
{
    private readonly IAddonLifecycle lifecycle;
    private readonly object gate = new();
    private readonly Dictionary<string, int> counts = new(StringComparer.Ordinal);
    private Dictionary<string, int> baseline = new(StringComparer.Ordinal);
    private readonly List<string> recent = new();
    private bool recording;

    public AddonEventProbe(IAddonLifecycle lifecycle)
    {
        this.lifecycle = lifecycle;
        lifecycle.RegisterListener(AddonEvent.PostSetup, OnAddon);
        lifecycle.RegisterListener(AddonEvent.PostRefresh, OnAddon);
        lifecycle.RegisterListener(AddonEvent.PostUpdate, OnAddon);
    }

    private static bool Relevant(string name) =>
        name.Contains("WKS", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("Mission", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("Recipe", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("Synthesis", StringComparison.OrdinalIgnoreCase);

    private void OnAddon(AddonEvent type, AddonArgs args)
    {
        lock (gate)
        {
            if (!recording) return;
            var name = string.IsNullOrWhiteSpace(args.AddonName) ? "(sem nome)" : args.AddonName;
            // PostUpdate dispara a cada frame; contar apenas addons potencialmente relevantes.
            if (type == AddonEvent.PostUpdate && !Relevant(name)) return;
            string key = $"{type}: {name}";
            counts[key] = counts.GetValueOrDefault(key) + 1;
            // Guardar apenas a primeira ocorrencia e marcos exponenciais de PostUpdate.
            int n = counts[key];
            if (type != AddonEvent.PostUpdate || (n & (n - 1)) == 0)
            {
                recent.Add($"{key} (total {n})");
                if (recent.Count > 120) recent.RemoveAt(0);
            }
        }
    }

    public string[] KnownAddonNames()
    {
        lock (gate)
            return counts.Keys.Select(k => k.Substring(k.IndexOf(": ", StringComparison.Ordinal) + 2))
                .Where(n => !n.StartsWith("(", StringComparison.Ordinal))
                .Distinct(StringComparer.Ordinal).ToArray();
    }

    public void Start()
    {
        lock (gate)
        {
            counts.Clear(); baseline.Clear(); recent.Clear();
            recording = true;
        }
    }

    public void MarkClosed()
    {
        lock (gate) baseline = new Dictionary<string, int>(counts, StringComparer.Ordinal);
    }

    public string Report()
    {
        lock (gate)
        {
            var changes = counts.Where(p => p.Value > baseline.GetValueOrDefault(p.Key))
                .OrderByDescending(p => p.Value - baseline.GetValueOrDefault(p.Key))
                .Select(p => $"{p.Key}: +{p.Value - baseline.GetValueOrDefault(p.Key)} evento(s)")
                .Take(50).ToArray();
            return "Mudancas apos a captura de base:\n" +
                (changes.Length == 0 ? "Nenhum evento novo." : string.Join("\n", changes)) +
                "\n\nEventos recentes:\n" + string.Join("\n", recent.TakeLast(35)) +
                "\n\nPostUpdate indica atualizacao do painel, nao identifica por si so a missao ativa.";
        }
    }

    public void Dispose()
    {
        lifecycle.UnregisterListener(AddonEvent.PostSetup, OnAddon);
        lifecycle.UnregisterListener(AddonEvent.PostRefresh, OnAddon);
        lifecycle.UnregisterListener(AddonEvent.PostUpdate, OnAddon);
    }
}
