
using System;
using System.Collections.Generic;
using System.Globalization;
using Dalamud.Game.NativeWrapper;

namespace SamplePlugin;

public sealed class SynthesisData
{
    public string RecipeName { get; set; } = "";
    public int Progress { get; set; }
    public int MaxProgress { get; set; }
    public int Durability { get; set; }
    public int MaxDurability { get; set; }
    public int Quality { get; set; }
    public string Condition { get; set; } = "";
}

public static class SynthesisReader
{
    public static string LastStatus { get; private set; }
        = "Aguardando leitura.";

    public static bool TryGetValues(
        AtkUnitBasePtr addon,
        out string[] values)
    {
        values = Array.Empty<string>();

        if (addon.IsNull)
        {
            LastStatus = "Addon nulo.";
            return false;
        }

        if (!addon.IsReady || !addon.IsVisible)
        {
            LastStatus = "Interface indisponivel.";
            return false;
        }

        int count = (int)addon.AtkValuesCount;

        if (count <= 0 || count > 256)
        {
            LastStatus = $"Quantidade invalida: {count}";
            return false;
        }

        var results = new List<string>();

        try
        {
            foreach (var value in addon.AtkValues)
            {
                if (results.Count >= count)
                    break;

                if (value.IsNull)
                {
                    results.Add("null");
                    continue;
                }

                try
                {
                    results.Add(
                        Convert.ToString(
                            value.GetValue(),
                            CultureInfo.InvariantCulture
                        ) ?? ""
                    );
                }
                catch
                {
                    results.Add("");
                }
            }
        }
        catch (Exception ex)
        {
            LastStatus = $"Erro: {ex.GetType().Name}";
            return false;
        }

        values = results.ToArray();

        LastStatus =
            $"Addon pronto. Valores lidos: {values.Length}.";

        return values.Length > 0;
    }

    public static bool TryRead(
        AtkUnitBasePtr addon,
        out SynthesisData data)
    {
        data = new SynthesisData();

        if (!TryGetValues(addon, out var values))
            return false;

        if (values.Length < 14)
        {
            LastStatus = "Dados insuficientes.";
            return false;
        }

        try
        {
            data.RecipeName = values[2];

            data.Progress = ParseInt(values[5]);
            data.MaxProgress = ParseInt(values[6]);

            data.Durability = ParseInt(values[7]);
            data.MaxDurability = ParseInt(values[8]);

            data.Quality = ParseInt(values[9]);

            data.Condition = values[13];

            if (data.MaxProgress <= 0 ||
                data.MaxDurability <= 0)
            {
                LastStatus = "Parametros invalidos.";
                return false;
            }

            return true;
        }
        catch
        {
            LastStatus = "Erro ao interpretar a sintese.";
            return false;
        }
    }

    private static int ParseInt(string value)
    {
        return int.TryParse(
            value,
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out int result
        ) ? result : 0;
    }
}
