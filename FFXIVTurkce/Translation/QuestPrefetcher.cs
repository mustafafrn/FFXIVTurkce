using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using Lumina.Excel;
using Lumina.Excel.Sheets;

namespace FFXIVTurkce.Translation;

/// <summary>
/// Aktif görevlerin tüm diyalog ve günlük metinlerini oyun verisinden okuyup arka planda
/// yavaş yavaş çevirir ve önbelleğe yazar; oyunda karşına çıktığında beklemeden gösterilir.
/// </summary>
public sealed class QuestPrefetcher : IDisposable
{
    private readonly IFramework framework;
    private readonly IDataManager data;
    private readonly IClientState clientState;
    private readonly IPluginLog log;
    private readonly Func<Configuration> getConfig;
    private readonly TranslationService translation;

    private readonly HashSet<ushort> knownQuests = new();
    private readonly ConcurrentQueue<string> queue = new();
    private readonly HashSet<string> queued = new(StringComparer.Ordinal);
    private readonly CancellationTokenSource cts = new();
    private readonly Task worker;
    private DateTime lastScan = DateTime.MinValue;
    private int consecutiveErrors;

    public int QueueCount => queue.Count;
    public int DoneCount { get; private set; }
    public string LastQuestName { get; private set; } = string.Empty;

    public QuestPrefetcher(
        IFramework framework,
        IDataManager data,
        IClientState clientState,
        IPluginLog log,
        Func<Configuration> getConfig,
        TranslationService translation)
    {
        this.framework = framework;
        this.data = data;
        this.clientState = clientState;
        this.log = log;
        this.getConfig = getConfig;
        this.translation = translation;

        framework.Update += OnUpdate;
        worker = Task.Run(WorkerLoop);
    }

    private void OnUpdate(IFramework _)
    {
        var cfg = getConfig();
        if (!cfg.Enabled || !cfg.PrefetchEnabled || !clientState.IsLoggedIn) return;

        var now = DateTime.UtcNow;
        if ((now - lastScan).TotalSeconds < 3) return;
        lastScan = now;

        try
        {
            ScanActiveQuests();
        }
        catch (Exception e)
        {
            log.Error(e, "Görev taraması hatası");
        }
    }

    private unsafe void ScanActiveQuests()
    {
        var qm = QuestManager.Instance();
        if (qm == null) return;

        var quests = qm->NormalQuests;
        for (var i = 0; i < quests.Length; i++)
        {
            var id = quests[i].QuestId;
            if (id == 0 || !knownQuests.Add(id)) continue;
            var questId = id;
            // Sheet okuma disk erişimi içerir; ana thread'i (oyunu) bekletme.
            _ = Task.Run(() =>
            {
                try { EnqueueQuest(questId); }
                catch (Exception e) { log.Warning($"Görev {questId} ön-çeviri hazırlığı: {e.Message}"); }
            });
        }
    }

    /// <summary>Bir görevin tüm metinlerini kuyruğa alır (önbellekte olanlar atlanır).</summary>
    public void EnqueueQuest(ushort questId)
    {
        var sheet = data.GetExcelSheet<Quest>();
        if (sheet is null || !sheet.TryGetRow(questId + 65536u, out var quest)) return;

        var idStr = quest.Id.ExtractText();
        if (idStr.Length < 5) return;

        var folder = idStr[^5..][..3];
        var sheetName = $"quest/{folder}/{idStr}";
        LastQuestName = quest.Name.ExtractText();

        ExcelSheet<RawRow>? textSheet;
        try
        {
            textSheet = data.GetExcelSheet<RawRow>(name: sheetName);
        }
        catch (Exception e)
        {
            log.Warning($"Görev metni okunamadı ({sheetName}): {e.Message}");
            return;
        }

        if (textSheet is null) return;

        var added = 0;
        Enqueue(LastQuestName, ref added);
        foreach (var row in textSheet)
        {
            string text;
            try
            {
                text = row.ReadStringColumn(1).ExtractText();
            }
            catch
            {
                continue;
            }

            Enqueue(text, ref added);
        }

        if (added > 0)
            log.Information($"Ön-çeviri kuyruğuna alındı: \"{LastQuestName}\" ({added} metin)");
    }

    private void Enqueue(string raw, ref int added)
    {
        var text = raw.Trim();
        if (text.Length < 2) return;
        // Oyuncu adı / koşullu dallar gibi makro içeren satırlar oyunda farklı görünür; atla.
        if (text.Contains('<') && text.Contains('>')) return;
        if (translation.TryGetCached(text) is not null) return;

        lock (queued)
        {
            if (!queued.Add(text)) return;
        }

        queue.Enqueue(text);
        added++;
    }

    private async Task WorkerLoop()
    {
        var ct = cts.Token;
        while (!ct.IsCancellationRequested)
        {
            var cfg = getConfig();
            if (!cfg.Enabled || !cfg.PrefetchEnabled || !queue.TryDequeue(out var text))
            {
                await Task.Delay(500, ct).ConfigureAwait(false);
                continue;
            }

            try
            {
                if (translation.TryGetCached(text) is null)
                {
                    await translation.TranslateAndStoreAsync(text, ct).ConfigureAwait(false);
                    DoneCount++;
                }

                consecutiveErrors = 0;
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception e)
            {
                consecutiveErrors++;
                log.Warning($"Ön-çeviri hatası ({consecutiveErrors}): {e.Message}");
                queue.Enqueue(text); // sonra tekrar dene
                // Art arda hata (ör. 429) → giderek uzun bekle, en fazla 1 dk.
                var backoff = Math.Min(60000, 2000 * (1 << Math.Min(consecutiveErrors, 5)));
                await Task.Delay(backoff, ct).ConfigureAwait(false);
                continue;
            }

            lock (queued) queued.Remove(text);

            // Ücretsiz uç noktaları kızdırmamak için sakin bir tempo.
            await Task.Delay(Math.Max(100, cfg.PrefetchDelayMs), ct).ConfigureAwait(false);
        }
    }

    public void Dispose()
    {
        framework.Update -= OnUpdate;
        cts.Cancel();
        try { worker.Wait(1000); } catch { /* iptal */ }
        cts.Dispose();
    }
}
