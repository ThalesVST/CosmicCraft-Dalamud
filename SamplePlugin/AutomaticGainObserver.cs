using System;

namespace SamplePlugin;

// Observacao experimental: identifica apenas transicoes compativeis.
// Nao confirma ausencia de buffs nem grava calibracoes automaticamente.
public sealed class AutomaticGainObserver
{
    private CraftingState? baseline;
    private string baselineCondition = "";
    private DateTime? pendingSince;
    private DateTime lastChange;
    private CraftingState? latest;
    private string latestCondition = "";
    private static readonly TimeSpan MaxWait = TimeSpan.FromMilliseconds(1500);
    private static readonly TimeSpan QuietTime = TimeSpan.FromMilliseconds(250);

    public int? SynthesisGain { get; private set; }
    public int? TouchGain { get; private set; }
    public int? SynthesisMinimum { get; private set; }
    public int? TouchMinimum { get; private set; }
    public string Status { get; private set; } = "Aguardando uma acao de crafting.";

    public void Reset()
    {
        baseline = null;
        baselineCondition = "";
        pendingSince = null;
        latest = null;
        latestCondition = "";
        SynthesisGain = null;
        TouchGain = null;
        SynthesisMinimum = null;
        TouchMinimum = null;
        Status = "Aguardando uma acao de crafting.";
    }

    public void Observe(CraftingState current, string condition)
    {
        var now = DateTime.UtcNow;
        if (baseline is null)
        {
            baseline = current.Clone();
            baselineCondition = condition;
            Status = $"Referencia: P={current.Progress}, Q={current.Quality}, D={current.Durability}, CP={current.Cp}; condicao={condition}.";
            return;
        }

        var before = baseline;
        int dp = current.Progress - before.Progress;
        int dq = current.Quality - before.Quality;
        int dd = before.Durability - current.Durability;
        int dc = before.Cp - current.Cp;
        bool discontinuity = current.MaxProgress != before.MaxProgress ||
                             current.MaxDurability != before.MaxDurability ||
                             current.MaxQuality != before.MaxQuality ||
                             dp < 0 || dq < 0 || dc < 0 ||
                             current.Durability > current.MaxDurability ||
                             current.Cp < 0;
        if (discontinuity)
        {
            baseline = current.Clone();
            baselineCondition = condition;
            pendingSince = null;
            latest = null;
            Status = "Referencia reiniciada por nova sintese ou estado descontinuo.";
            return;
        }

        bool changed = dp != 0 || dq != 0 || dd != 0 || dc != 0;
        if (pendingSince is null)
        {
            if (!changed)
            {
                baselineCondition = condition;
                return;
            }
            pendingSince = now;
            lastChange = now;
            latest = current.Clone();
            latestCondition = condition;
        }
        else if (latest is not null &&
                 (latest.Progress != current.Progress || latest.Quality != current.Quality ||
                  latest.Durability != current.Durability || latest.Cp != current.Cp))
        {
            lastChange = now;
            latest = current.Clone();
            latestCondition = condition;
        }
        else
        {
            latestCondition = condition;
        }

        // Nao finalize na primeira combinacao plausivel: CP pode mudar antes
        // de qualidade/durabilidade. Espere uma curta janela sem alteracoes.
        bool quiet = now - lastChange >= QuietTime;
        bool expired = now - pendingSince.Value >= MaxWait;
        if (!quiet && !expired)
        {
            Status = $"Aguardando estabilizacao: CP {before.Cp}->{current.Cp}, P +{dp}, Q +{dq}, D -{dd}; " +
                     $"condicao inicial={baselineCondition}, atual={condition}.";
            return;
        }

        bool normal = string.Equals(baselineCondition, "Normal", StringComparison.OrdinalIgnoreCase);
        bool synthesis = dp > 0 && dq == 0 && dd == 10 && dc == 0;
        bool touch = dq > 0 && dp == 0 && dd == 10 && dc == 18;
        string details = $"CP {before.Cp}->{current.Cp} (delta -{dc}), P +{dp}, Q +{dq}, D -{dd}; " +
                         $"condicao inicial={baselineCondition}, final={latestCondition}.";

        if (synthesis)
        {
            bool capped = current.Progress >= current.MaxProgress;
            if (normal)
            {
                if (capped) SynthesisMinimum = dp;
                else SynthesisGain = dp;
            }
            Status = $"Basic Synthesis compativel: progresso +{dp}" +
                     (capped ? " (ganho minimo, limitado). " : " (nao limitado). ") +
                     (normal ? "Confira buffs/traits. " : "Nao calibrado: condicao inicial diferente de Normal. ") + details;
        }
        else if (touch)
        {
            bool capped = current.Quality >= current.MaxQuality;
            if (normal)
            {
                if (capped) TouchMinimum = dq;
                else TouchGain = dq;
            }
            Status = $"Basic Touch compativel: qualidade +{dq}" +
                     (capped ? " (ganho minimo, limitado). " : " (nao limitado). ") +
                     (normal ? "Confira buffs/traits. " : "Nao calibrado: condicao inicial diferente de Normal. ") + details;
        }
        else
        {
            string reason = dc == 0 && dq > 0
                ? "CP nao mudou entre as amostras do observador; a referencia pode ter sido capturada apos o consumo. "
                : "Custos ou deltas nao correspondem a uma acao basica isolada. ";
            Status = "Transicao inconclusiva. " + reason + details + " Nenhum ganho aplicado.";
        }

        baseline = current.Clone();
        baselineCondition = condition;
        pendingSince = null;
        latest = null;
    }
}
