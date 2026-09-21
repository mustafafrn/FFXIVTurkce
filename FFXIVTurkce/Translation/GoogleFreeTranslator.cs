using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace FFXIVTurkce.Translation;

/// <summary>
/// Google Translate'in tarayıcı istemcilerinin kullandığı ücretsiz uç noktalar (API key gerekmez).
/// Resmi değil; Google bir gün kapatabilir ya da IP'yi geçici olarak sınırlayabilir (429).
/// Önce Chrome sözlük uç noktasını dener, olmazsa gtx uç noktasına düşer.
/// </summary>
public sealed class GoogleFreeTranslator : ITranslator
{
    private const string ChromeEndpoint = "https://clients5.google.com/translate_a/t?client=dict-chrome-ex&sl=en&tl=tr";
    private const string GtxEndpoint = "https://translate.googleapis.com/translate_a/single?client=gtx&sl=en&tl=tr&dt=t&ie=UTF-8&oe=UTF-8";

    private static readonly HttpClient Http = CreateClient();

    public string Name => "Google";

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/128.0 Safari/537.36");
        return client;
    }

    public async Task<string> TranslateAsync(string speaker, string text, CancellationToken ct)
    {
        Exception? first = null;
        try
        {
            return await ViaChromeEndpoint(text, ct).ConfigureAwait(false);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            first = e;
        }

        try
        {
            return await ViaGtxEndpoint(text, ct).ConfigureAwait(false);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            throw new InvalidOperationException($"{first.Message} | yedek: {e.Message}");
        }
    }

    private static async Task<string> PostForm(string url, string text, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new FormUrlEncodedContent(new[] { new KeyValuePair<string, string>("q", text) }),
        };

        using var response = await Http.SendAsync(request, ct).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

        if (response.StatusCode == HttpStatusCode.TooManyRequests)
            throw new HttpRequestException("Google IP'yi geçici olarak sınırladı (429). Biraz bekle.");
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"Google HTTP {(int)response.StatusCode}");
        if (body.StartsWith("<", StringComparison.Ordinal))
            throw new HttpRequestException("Google HTML sayfası döndürdü (engelleme/captcha).");

        return body;
    }

    // Yanıt: ["çeviri"]  ya da sl=auto ise [["çeviri","en"]]
    private static async Task<string> ViaChromeEndpoint(string text, CancellationToken ct)
    {
        var json = await PostForm(ChromeEndpoint, text, ct).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Array || root.GetArrayLength() == 0)
            throw new InvalidOperationException("Google (chrome) beklenmeyen yanıt.");

        var item = root[0];
        var result = item.ValueKind switch
        {
            JsonValueKind.String => item.GetString(),
            JsonValueKind.Array when item.GetArrayLength() > 0 && item[0].ValueKind == JsonValueKind.String => item[0].GetString(),
            _ => null,
        };

        if (string.IsNullOrWhiteSpace(result))
            throw new InvalidOperationException("Google (chrome) boş çeviri döndü.");

        return result.Trim();
    }

    // Yanıt: [[["çeviri parçası","orijinal parça",...],[...]],null,"en",...]
    private static async Task<string> ViaGtxEndpoint(string text, CancellationToken ct)
    {
        var json = await PostForm(GtxEndpoint, text, ct).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Array || root.GetArrayLength() == 0 || root[0].ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException("Google (gtx) beklenmeyen yanıt.");

        var sb = new StringBuilder();
        foreach (var segment in root[0].EnumerateArray())
        {
            if (segment.ValueKind == JsonValueKind.Array && segment.GetArrayLength() > 0 && segment[0].ValueKind == JsonValueKind.String)
                sb.Append(segment[0].GetString());
        }

        var result = sb.ToString().Trim();
        if (result.Length == 0)
            throw new InvalidOperationException("Google (gtx) boş çeviri döndü.");

        return result;
    }
}
