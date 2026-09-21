using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Dalamud.Plugin.Services;

namespace FFXIVTurkce.Translation;

/// <summary>Çevirileri JSON dosyasında kalıcı önbelleğe alır (aynı cümle bir daha API'ye gitmez).</summary>
public sealed class TranslationCache : IDisposable
{
    private readonly string path;
    private readonly IPluginLog log;
    private readonly ConcurrentDictionary<string, string> entries = new();
    private volatile bool dirty;
    private volatile bool saving;
    private DateTime lastSave = DateTime.UtcNow;

    public TranslationCache(string directory, IPluginLog log)
    {
        this.log = log;
        Directory.CreateDirectory(directory);
        path = Path.Combine(directory, "cache.json");
        Load();
    }

    public int Count => entries.Count;

    public static string MakeKey(string speaker, string text) => speaker + "" + text;

    public bool TryGet(string key, out string translated) => entries.TryGetValue(key, out translated!);

    public void Set(string key, string translated)
    {
        entries[key] = translated;
        dirty = true;
    }

    public void Clear()
    {
        entries.Clear();
        dirty = true;
        Save();
    }

    /// <summary>Her frame çağrılabilir; kirli ise ve son kayıttan 5 sn geçtiyse arka planda diske yazar.</summary>
    public void Tick()
    {
        if (!dirty || saving) return;
        if ((DateTime.UtcNow - lastSave).TotalSeconds < 5) return;
        saving = true;
        _ = Task.Run(() =>
        {
            try { Save(); }
            finally { saving = false; }
        });
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(path)) return;
            var json = File.ReadAllText(path);
            var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
            if (dict is null) return;
            foreach (var kv in dict)
                entries[kv.Key] = kv.Value;
            log.Information($"Çeviri önbelleği yüklendi: {entries.Count} kayıt");
        }
        catch (Exception e)
        {
            log.Error(e, "Çeviri önbelleği okunamadı");
        }
    }

    private void Save()
    {
        // Önce bayrağı indir: yazma sırasında gelen yeni kayıtlar bir sonraki kayda kalsın, kaybolmasın.
        dirty = false;
        try
        {
            var snapshot = new Dictionary<string, string>(entries);
            var json = JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = false });
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, json);
            File.Move(tmp, path, overwrite: true);
            lastSave = DateTime.UtcNow;
        }
        catch (Exception e)
        {
            dirty = true;
            log.Error(e, "Çeviri önbelleği yazılamadı");
        }
    }

    public void Dispose()
    {
        if (dirty) Save();
    }
}
