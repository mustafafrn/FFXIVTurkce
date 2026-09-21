using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Plugin.Services;

namespace FFXIVTurkce.Translation;

public enum TranslationStatus
{
    Pending,
    Done,
    Error,
}

public sealed class TranslationResult
{
    public volatile TranslationStatus Status = TranslationStatus.Pending;
    public volatile string Text = string.Empty;
    public string Speaker = string.Empty;
    public string Original = string.Empty;
}

/// <summary>
/// Önbellek + seçili motor üzerinden asenkron çeviri. Aynı metin için tek istek,
/// istekler tek sırada ve hız sınırlı gider, hata veren metin bir süre yeniden denenmez.
/// </summary>
public sealed class TranslationService : IDisposable
{
    private const int FailureCooldownSeconds = 30;

    private readonly Func<Configuration> getConfig;
    private readonly IPluginLog log;
    private readonly TranslationCache cache;
    private readonly GeminiTranslator gemini;
    private readonly ClaudeTranslator claude;
    private readonly GoogleFreeTranslator google = new();
    private readonly ConcurrentDictionary<string, TranslationResult> inFlight = new();
    private readonly ConcurrentDictionary<string, DateTime> failed = new();
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly CancellationTokenSource cts = new();
    private DateTime lastRequest = DateTime.MinValue;

    public TranslationService(Func<Configuration> getConfig, TranslationCache cache, IPluginLog log)
    {
        this.getConfig = getConfig;
        this.cache = cache;
        this.log = log;
        gemini = new GeminiTranslator(getConfig);
        claude = new ClaudeTranslator(getConfig);
    }

    public TranslationCache Cache => cache;

    public int InFlightCount => inFlight.Count;

    public ITranslator Current => getConfig().Engine switch
    {
        TranslationEngine.Claude => claude,
        TranslationEngine.Gemini => gemini,
        _ => google,
    };

    /// <summary>Önbellekte varsa anında Done döner; yoksa Pending döner ve arka planda çevirir.</summary>
    public TranslationResult Request(string speaker, string text)
    {
        var key = TranslationCache.MakeKey(speaker, text);

        if (cache.TryGet(key, out var cached))
        {
            return new TranslationResult
            {
                Status = TranslationStatus.Done,
                Text = cached,
                Speaker = speaker,
                Original = text,
            };
        }

        // Yakın zamanda hata verdiyse yeniden isteme (her karede tekrar denemek 429'a yol açıyor).
        if (failed.TryGetValue(key, out var failedAt))
        {
            if ((DateTime.UtcNow - failedAt).TotalSeconds < FailureCooldownSeconds)
            {
                return new TranslationResult
                {
                    Status = TranslationStatus.Error,
                    Text = "Çeviri hatası (bekleniyor)",
                    Speaker = speaker,
                    Original = text,
                };
            }

            failed.TryRemove(key, out _);
        }

        return inFlight.GetOrAdd(key, k =>
        {
            var result = new TranslationResult { Speaker = speaker, Original = text };
            _ = RunAsync(k, result);
            return result;
        });
    }

    /// <summary>Önbellekte varsa hazır sonucu döner, yoksa null (istek başlatmaz).</summary>
    public TranslationResult? TryGetCached(string text)
    {
        var key = TranslationCache.MakeKey(string.Empty, text);
        if (!cache.TryGet(key, out var cached)) return null;
        return new TranslationResult
        {
            Status = TranslationStatus.Done,
            Text = cached,
            Original = text,
        };
    }

    /// <summary>Ön-çeviri için: çevirir ve önbelleğe yazar (sonucu döner).</summary>
    public async Task<string> TranslateAndStoreAsync(string text, CancellationToken ct)
    {
        var key = TranslationCache.MakeKey(string.Empty, text);
        var translated = await Throttled(() => Current.TranslateAsync(string.Empty, text, ct), ct).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(translated))
            throw new InvalidOperationException("Boş çeviri döndü.");
        cache.Set(key, translated);
        return translated;
    }

    /// <summary>Ayar penceresindeki "Test" düğmesi için.</summary>
    public Task<string> TestAsync(string text) => Current.TranslateAsync(string.Empty, text, cts.Token);

    /// <summary>Tüm motor çağrıları buradan geçer: tek sıra + istekler arası en az bekleme.</summary>
    private async Task<string> Throttled(Func<Task<string>> call, CancellationToken ct)
    {
        await gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var minInterval = getConfig().Engine == TranslationEngine.GoogleFree ? 120 : 40;
            var wait = minInterval - (int)(DateTime.UtcNow - lastRequest).TotalMilliseconds;
            if (wait > 0) await Task.Delay(wait, ct).ConfigureAwait(false);

            try
            {
                return await call().ConfigureAwait(false);
            }
            finally
            {
                lastRequest = DateTime.UtcNow;
            }
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task RunAsync(string key, TranslationResult result)
    {
        var translator = Current;
        try
        {
            var translated = await Throttled(
                () => translator.TranslateAsync(result.Speaker, result.Original, cts.Token), cts.Token).ConfigureAwait(false);

            if (string.IsNullOrWhiteSpace(translated))
                throw new InvalidOperationException("Boş çeviri döndü.");

            cache.Set(key, translated);
            result.Text = translated;
            result.Status = TranslationStatus.Done;
        }
        catch (OperationCanceledException)
        {
            result.Text = "İptal edildi";
            result.Status = TranslationStatus.Error;
        }
        catch (Exception e)
        {
            log.Warning($"[{translator.Name}] çeviri hatası: {e.Message}");
            failed[key] = DateTime.UtcNow;
            result.Text = $"Çeviri hatası ({translator.Name}): {e.Message}";
            result.Status = TranslationStatus.Error;
        }
        finally
        {
            inFlight.TryRemove(key, out _);
        }
    }

    public void Dispose()
    {
        cts.Cancel();
        cts.Dispose();
        gate.Dispose();
    }
}
