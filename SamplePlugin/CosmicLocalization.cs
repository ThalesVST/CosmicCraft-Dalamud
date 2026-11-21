using System;
using System.Collections.Generic;

namespace SamplePlugin;

// UI only. Never translate Raphael action identifiers or FFXIV macro commands.
internal static class CosmicLocalization
{
    private static readonly Dictionary<string, string> English = new(StringComparer.Ordinal)
    {
        ["Entre na Cosmic Exploration para utilizar o plugin."] = "Enter Cosmic Exploration to use the plugin.",
        ["Nenhuma sintese ativa."] = "No active synthesis.",
        ["Configuracoes avancadas..."] = "Advanced settings...",
        ["Ajustes e diagnosticos - janela independente"] = "Settings and diagnostics - separate window",
        ["Raphael e qualidade"] = "Raphael and quality",
        ["Calibracao e ganhos"] = "Calibration and gains",
        ["Diagnosticos de receita"] = "Recipe diagnostics",
        ["IDs de habilidades"] = "Action IDs",
        ["Acompanhamento detalhado"] = "Detailed tracking",
        ["AUTOMATICO"] = "AUTOMATIC",
        ["MACRO GERADA"] = "GENERATED MACRO",
        ["Anterior"] = "Previous",
        ["Proximo"] = "Next",
        ["Copiar macro"] = "Copy macro",
        ["Calcular rotacao"] = "Calculate rotation",
        ["Cancelar calculo"] = "Cancel calculation",
        ["Raphael calculando..."] = "Raphael is calculating...",
        ["Abra uma sintese no passo 1 para calcular."] = "Open a synthesis at step 1 to calculate.",
        ["Tentar carregar rotacao salva"] = "Try loading saved rotation",
        ["Iniciar automatico"] = "Start automatic",
        ["Pausar"] = "Pause",
        ["Parar"] = "Stop",
        ["Desativar automatico"] = "Disable automatic",
        ["Configuracoes avancadas"] = "Advanced settings",
        ["Idioma e apoio ao projeto"] = "Language and support",
        ["Idioma da interface"] = "Interface language",
        ["Nomes das habilidades e comandos de macro nao sao traduzidos."] = "Action names and macro commands are not translated.",
        ["Apoiar o projeto no Ko-fi"] = "Support on Ko-fi",
        ["Caminho do Raphael"] = "Raphael path",
        ["Manipulation desbloqueada"] = "Manipulation unlocked",
        ["Usar Raphael"] = "Use Raphael",
        ["Qualidade maxima automatica [17]"] = "Automatic maximum quality [17]",
        ["Qualidade maxima manual"] = "Manual maximum quality",
    };

    public static string T(string text, string language) =>
        language == "en" && English.TryGetValue(text, out var translated) ? translated : text;
}
