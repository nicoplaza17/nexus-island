using System.Diagnostics;
using System.Globalization;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using NexusIsland.Core.Security;

namespace NexusIsland.Services.Windows.Security;

/// <summary>
/// Explica alertas con un modelo local de LM Studio (por defecto Foundation-Sec 1.1 8B de Cisco), por su
/// API compatible con OpenAI en http://127.0.0.1:1234. Si el servidor no está corriendo, lo arranca con
/// "lms server start". LM Studio carga el modelo al recibir el pedido y lo descarga solo tras
/// <see cref="ModelIdleSeconds"/> sin uso, así los ~5 GB de memoria no quedan ocupados.
/// Nada sale de la PC: la conexión es solo a la propia máquina.
/// </summary>
public sealed class LmStudioExplainer(ILogger<LmStudioExplainer> logger) : ISecurityExplainer
{
    private const string BaseUrl = "http://127.0.0.1:1234";
    public const int ModelIdleSeconds = 300;
    private static readonly HttpClient Http = new() { Timeout = Timeout.InfiniteTimeSpan };

    public string ModelName { get; } = Environment.GetEnvironmentVariable("NEXUS_SECURITY_MODEL") ?? "foundation-sec-1.1-8b-instruct";

    private static string LmsPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".lmstudio", "bin", "lms.exe");

    public async Task ExplainAsync(SecurityAlert alert, Action<string> onText, Action<ExplainStage> onStatus, CancellationToken cancellationToken)
    {
        if (!await IsServerUpAsync(cancellationToken))
        {
            onStatus(ExplainStage.StartingServer);
            await StartServerAsync(cancellationToken);
        }

        onStatus(ExplainStage.LoadingModel);
        var spanish = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "es";
        var body = JsonSerializer.Serialize(new
        {
            model = ModelName,
            messages = new object[]
            {
                new { role = "system", content = SecurityPrompt.System(spanish) },
                new { role = "user", content = SecurityPrompt.User(alert) },
            },
            temperature = 0.2,
            max_tokens = 600,
            stream = true,
            ttl = ModelIdleSeconds, // LM Studio descarga el modelo solo después de 5 min sin uso
        });

        var sw = Stopwatch.StartNew();
        using var request = new HttpRequestMessage(HttpMethod.Post, BaseUrl + "/v1/chat/completions")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        using var response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException($"LM Studio respondió {(int)response.StatusCode}: {Trim(error)}");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var first = true;
        var chars = 0;
        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            if (!line.StartsWith("data:", StringComparison.Ordinal)) continue;
            var payload = line[5..].Trim();
            if (payload == "[DONE]") break;
            using var json = JsonDocument.Parse(payload);
            if (!json.RootElement.TryGetProperty("choices", out var choices) || choices.GetArrayLength() == 0) continue;
            if (!choices[0].TryGetProperty("delta", out var delta) || !delta.TryGetProperty("content", out var content)) continue;
            var text = content.GetString();
            if (string.IsNullOrEmpty(text)) continue;
            if (first)
            {
                onStatus(ExplainStage.Writing);
                logger.LogInformation("Seguridad: el modelo empezó a responder a los {Seconds:F0} s", sw.Elapsed.TotalSeconds);
                first = false;
            }
            chars += text.Length;
            onText(text);
        }
        logger.LogInformation("Seguridad: explicación de {Rule} lista en {Seconds:F0} s ({Chars} caracteres)", alert.RuleId, sw.Elapsed.TotalSeconds, chars);
    }

    private static async Task<bool> IsServerUpAsync(CancellationToken ct)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(3));
            using var r = await Http.GetAsync(BaseUrl + "/v1/models", cts.Token);
            return r.IsSuccessStatusCode;
        }
        catch (HttpRequestException) { return false; }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested) { return false; }
    }

    private async Task StartServerAsync(CancellationToken ct)
    {
        if (!File.Exists(LmsPath))
            throw new FileNotFoundException("No está instalado LM Studio (falta lms.exe)", LmsPath);
        logger.LogInformation("Seguridad: arrancando el servidor de LM Studio");
        using var p = Process.Start(new ProcessStartInfo(LmsPath, "server start")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        }) ?? throw new InvalidOperationException("No se pudo ejecutar lms.exe");
        await p.WaitForExitAsync(ct);
        for (var i = 0; i < 20; i++)
        {
            if (await IsServerUpAsync(ct)) return;
            await Task.Delay(500, ct);
        }
        throw new InvalidOperationException("El servidor de LM Studio no respondió");
    }

    private static string Trim(string s) => s.Length > 200 ? s[..200] : s;
}
