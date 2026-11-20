using System;
using System.Collections.Generic;
using System.Linq;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace SamplePlugin;

/// <summary>Diagnostico somente leitura da arvore de nos, inclusive componentes aninhados.</summary>
public static unsafe class SynthesisNodeProbe
{
    private const int MaxNodes = 4096;
    private const int MaxDepth = 32;
    private static Dictionary<nint, NodeInfo>? baseline;

    private sealed record NodeInfo(string Path, string Text, bool IsText, bool IsComponent);

    private static Dictionary<nint, NodeInfo> Capture(AtkUnitBase* addon)
    {
        var result = new Dictionary<nint, NodeInfo>();
        if (addon == null) return result;
        var visited = new HashSet<nint>();
        WalkManager(&addon->UldManager, "Synthesis", result, visited, 0);
        return result;
    }

    private static void WalkManager(AtkUldManager* manager, string path,
        Dictionary<nint, NodeInfo> result, HashSet<nint> visited, int depth)
    {
        if (manager == null || manager->NodeList == null || depth > MaxDepth) return;
        var count = Math.Min((int)manager->NodeListCount, MaxNodes);
        for (var i = 0; i < count && visited.Count < MaxNodes; i++)
            WalkNode(manager->NodeList[i], path + "/root" + i, result, visited, depth);
    }

    private static void WalkNode(AtkResNode* node, string path,
        Dictionary<nint, NodeInfo> result, HashSet<nint> visited, int depth)
    {
        if (node == null || depth > MaxDepth || visited.Count >= MaxNodes || !visited.Add((nint)node)) return;
        var type = node->Type;
        var nodePath = path + "/id" + node->NodeId + "/type" + (int)type;
        var isText = type == NodeType.Text;
        var isComponent = type >= NodeType.Component;
        var value = isText ? ((AtkTextNode*)node)->NodeText.ToString() : string.Empty;
        result[(nint)node] = new NodeInfo(nodePath, value, isText, isComponent);

        // Componentes possuem seu proprio UldManager: o diagnostico anterior nao os visitava.
        if (isComponent)
        {
            var component = ((AtkComponentNode*)node)->Component;
            if (component != null)
                WalkManager(&component->UldManager, nodePath + "/component", result, visited, depth + 1);
        }
        WalkNode(node->ChildNode, nodePath + "/child", result, visited, depth + 1);
        WalkNode(node->NextSiblingNode, path + "/next", result, visited, depth + 1);
    }

    private static string Counts(Dictionary<nint, NodeInfo> nodes)
        => $"Nos: {nodes.Count} | Textos: {nodes.Values.Count(n => n.IsText)} | Componentes: {nodes.Values.Count(n => n.IsComponent)}";

    public static string MarkClosed(AtkUnitBase* addon)
    {
        baseline = Capture(addon);
        return "Painel fechado capturado. " + Counts(baseline) + "\nAbra Efficiency Calculations e clique em B.";
    }

    public static string Compare(AtkUnitBase* addon)
    {
        if (baseline == null) return "Primeiro clique em A com o painel fechado.";
        var current = Capture(addon);
        var lines = new List<string>();
        foreach (var pair in current)
        {
            if (!baseline.TryGetValue(pair.Key, out var old))
            {
                var n = pair.Value;
                if (n.IsText && !string.IsNullOrWhiteSpace(n.Text))
                    lines.Add("NOVO TEXTO " + n.Path + ": " + Clean(n.Text));
                else if (n.IsComponent) lines.Add("NOVO COMPONENTE " + n.Path);
            }
            else if (pair.Value.IsText && old.Text != pair.Value.Text)
                lines.Add("TEXTO ALTERADO " + pair.Value.Path + ": " + Clean(old.Text) + " => " + Clean(pair.Value.Text));
        }
        foreach (var pair in baseline)
            if (!current.ContainsKey(pair.Key) && (pair.Value.IsComponent || (pair.Value.IsText && !string.IsNullOrWhiteSpace(pair.Value.Text))))
                lines.Add("REMOVIDO " + pair.Value.Path + (pair.Value.IsText ? ": " + Clean(pair.Value.Text) : ""));
        return "Fechado: " + Counts(baseline) + "\nAberto: " + Counts(current) +
               "\nMudancas relevantes: " + lines.Count + "\n" +
               (lines.Count == 0 ? "Nenhuma diferenca detectada na arvore do Synthesis. Investigar outros addons." : string.Join("\n", lines.Take(100)));
    }

    private static string Clean(string text) => text.Replace('\r', ' ').Replace('\n', ' ').Trim();
}
