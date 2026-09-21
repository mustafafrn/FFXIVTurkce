using System;
using System.Collections.Generic;
using Dalamud.Game.Text.SeStringHandling;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace FFXIVTurkce.Game;

/// <summary>AtkUnitBase üzerinden metin okuma yardımcıları.</summary>
public static unsafe class AddonText
{
    // Düğüm işaretçisi → (bayt hash'i, uzunluk, çözülmüş metin). SeString ayrıştırma her karede
    // yüzlerce düğüm için pahalı; baytlar değişmediyse önceki sonucu kullan.
    private static readonly Dictionary<nint, (int Hash, int Length, string Text)> ParseCache = new();
    private const int ParseCacheLimit = 20000;

    public static string ReadTextNode(AtkUnitBase* addon, uint nodeId)
    {
        if (addon == null) return string.Empty;
        var node = addon->GetTextNodeById(nodeId);
        if (node == null) return string.Empty;
        return ReadTextNode(node);
    }

    public static string ReadTextNode(AtkTextNode* node)
    {
        if (node == null) return string.Empty;
        var span = node->NodeText.AsSpan();
        if (span.Length == 0) return string.Empty;

        var hash = Fnv1a(span);
        var key = (nint)node;
        if (ParseCache.TryGetValue(key, out var cached) && cached.Hash == hash && cached.Length == span.Length)
            return cached.Text;

        // SeString.Parse payload'ları (renk, italik, satır sonu) ayıklar; TextValue düz metni verir.
        var text = SeString.Parse(span).TextValue.Trim();

        if (ParseCache.Count >= ParseCacheLimit) ParseCache.Clear();
        ParseCache[key] = (hash, span.Length, text);
        return text;
    }

    private static int Fnv1a(ReadOnlySpan<byte> data)
    {
        unchecked
        {
            var h = (int)2166136261;
            foreach (var b in data) h = (h ^ b) * 16777619;
            return h;
        }
    }

    /// <summary>Hata ayıklama: addon içindeki tüm metin düğümlerini (id, metin) listeler.</summary>
    public static List<(uint Id, string Text)> DumpTextNodes(AtkUnitBase* addon)
    {
        var list = new List<(uint, string)>();
        if (addon == null) return list;
        var count = addon->UldManager.NodeListCount;
        for (var i = 0; i < count; i++)
        {
            var node = addon->UldManager.NodeList[i];
            if (node == null || node->Type != NodeType.Text) continue;
            var text = ReadTextNode((AtkTextNode*)node);
            list.Add((node->NodeId, text));
        }

        return list;
    }
}
