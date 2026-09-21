using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace FFXIVTurkce.Translation;

/// <summary>Google Gemini (generateContent REST) ile çeviri. API key: https://aistudio.google.com</summary>
public sealed class GeminiTranslator : ITranslator
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };

    private readonly Func<Configuration> getConfig;

    public GeminiTranslator(Func<Configuration> getConfig)
    {
        this.getConfig = getConfig;
    }

    public string Name => "Gemini";

    public async Task<string> TranslateAsync(string speaker, string text, CancellationToken ct)
    {
        var cfg = getConfig();
        if (string.IsNullOrWhiteSpace(cfg.GeminiApiKey))
            throw new InvalidOperationException("Gemini API anahtarı girilmemiş (/trk ayarlar).");

        var model = string.IsNullOrWhiteSpace(cfg.GeminiModel) ? "gemini-2.5-flash" : cfg.GeminiModel.Trim();
        var url = $"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent";

        var body = new JsonObject
        {
            ["system_instruction"] = new JsonObject
            {
                ["parts"] = new JsonArray(new JsonObject { ["text"] = PromptBuilder.BuildSystemPrompt(cfg.ExtraGlossary) }),
            },
            ["contents"] = new JsonArray(
                new JsonObject
                {
                    ["role"] = "user",
                    ["parts"] = new JsonArray(new JsonObject { ["text"] = PromptBuilder.BuildUserMessage(speaker, text) }),
                }),
            ["generationConfig"] = new JsonObject
            {
                ["temperature"] = 0.3,
                ["maxOutputTokens"] = 2048,
            },
        };

        // 2.5 ailesinde düşünmeyi kapatmak gecikmeyi ciddi azaltır; diğer modeller bu alanı reddedebilir.
        if (model.Contains("2.5", StringComparison.Ordinal))
        {
            body["generationConfig"]!["thinkingConfig"] = new JsonObject { ["thinkingBudget"] = 0 };
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.Add("x-goog-api-key", cfg.GeminiApiKey.Trim());
        request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

        using var response = await Http.SendAsync(request, ct).ConfigureAwait(false);
        var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var preview = json.Length > 400 ? json[..400] : json;
            throw new HttpRequestException($"Gemini HTTP {(int)response.StatusCode}: {preview}");
        }

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (!root.TryGetProperty("candidates", out var candidates) || candidates.GetArrayLength() == 0)
        {
            var preview = json.Length > 400 ? json[..400] : json;
            throw new InvalidOperationException($"Gemini boş yanıt döndü: {preview}");
        }

        var sb = new StringBuilder();
        var parts = candidates[0].GetProperty("content").GetProperty("parts");
        foreach (var part in parts.EnumerateArray())
        {
            if (part.TryGetProperty("text", out var t))
                sb.Append(t.GetString());
        }

        return sb.ToString().Trim();
    }
}
