using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SamplePlugin;

// Solver externo opcional: nenhuma acao automatica, nenhuma gravacao no cache.
public static class RaphaelBridge
{
    public sealed record Request(
        string Executable, int RecipeLevelTableId, int Progress, int Quality,
        int Durability, int CrafterLevel, int Craftsmanship, int Control,
        int Cp, int InitialQuality, bool Expert, bool Manipulation);

    public sealed record Result(bool Success, string Message, string[] Actions,
        int? FinalQuality, int? Steps, string Raw);

    public static async Task<Result> SolveAsync(Request r, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(r.Executable) || !File.Exists(r.Executable))
            return Fail("Arquivo Raphael nao encontrado. Selecione uma copia .exe.", "");
        if (!string.Equals(Path.GetExtension(r.Executable), ".exe", StringComparison.OrdinalIgnoreCase))
            return Fail("Use uma copia do raphael-cli.bin renomeada para .exe.", "");
        if (r.RecipeLevelTableId <= 0 || r.Progress <= 0 || r.Quality <= 0 ||
            r.Durability <= 0 || r.CrafterLevel <= 0 || r.Craftsmanship <= 0 ||
            r.Control <= 0 || r.Cp <= 0 || r.InitialQuality < 0)
            return Fail("Parametros incompletos ou invalidos.", "");

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = r.Executable,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = Path.GetDirectoryName(r.Executable)!
            };
            foreach (var arg in new[] {
                "solve", "--custom-recipe", r.RecipeLevelTableId.ToString(),
                r.Progress.ToString(), r.Quality.ToString(), r.Durability.ToString(),
                r.Expert ? "1" : "0", "--level", r.CrafterLevel.ToString(),
                "--stats", r.Craftsmanship.ToString(), r.Control.ToString(), r.Cp.ToString(),
                "--initial-quality", r.InitialQuality.ToString(),
                "--threads", "2", "--output-variables", "actions", "final_quality", "steps"
            })
                psi.ArgumentList.Add(arg);
            if (r.Manipulation) psi.ArgumentList.Add("--manipulation");

            using var process = new Process { StartInfo = psi };
            if (!process.Start()) return Fail("Falha ao iniciar o Raphael.", "");
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(120));
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            try
            {
                await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
                catch { /* processo ja encerrado */ }
                return Fail(cancellationToken.IsCancellationRequested ? "Calculo cancelado." :
                    "Tempo limite de 120 segundos atingido.", "");
            }
            var output = (await stdout.ConfigureAwait(false)).Trim();
            var error = (await stderr.ConfigureAwait(false)).Trim();
            if (process.ExitCode != 0)
                return Fail($"Raphael encerrou com codigo {process.ExitCode}: {error}", output);
            // Exemplo observado: "[MuscleMemory, StandardTouch]",1398,8
            var match = System.Text.RegularExpressions.Regex.Match(output,
                "^\\s*\"?\\[([^\\]]*)\\]\"?\\s*,\\s*(\\d+)\\s*,\\s*(\\d+)", 
                System.Text.RegularExpressions.RegexOptions.Multiline);
            if (!match.Success)
                return Fail("Saida nao reconhecida. Confira o texto bruto abaixo.", output + "\n" + error);
            var actions = match.Groups[1].Value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            if (actions.Length == 0) return Fail("O Raphael retornou uma lista de acoes vazia.", output);
            int finalQ = int.Parse(match.Groups[2].Value);
            int steps = int.Parse(match.Groups[3].Value);
            return new Result(true, "Rotacao recomendada (nao validada no jogo).", actions, finalQ, steps, output);
        }
        catch (Exception ex)
        {
            return Fail($"{ex.GetType().Name}: {ex.Message}", "");
        }
    }

    private static Result Fail(string message, string raw) =>
        new(false, message, Array.Empty<string>(), null, null, raw);
}
