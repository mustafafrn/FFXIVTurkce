using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.ManagedFontAtlas;
using FFXIVClientStructs.FFXIV.Component.GUI;
using FFXIVTurkce.Game;

namespace FFXIVTurkce.Windows;

/// <summary>Gizlenen oyun metinlerinin yerine, aynı dikdörtgene Türkçesini çizer.</summary>
public sealed class InPlaceRenderer
{
    private readonly InPlaceWatcher watcher;
    private readonly Func<Configuration> getConfig;
    private readonly Func<IFontHandle?> getFont;
    private readonly List<string> lines = new();

    // (metin, boyut, genişlik) → satırlar. Her kare yüzlerce ölçüm yapmamak için.
    private readonly Dictionary<(string, int, int), string[]> wrapCache = new();
    private const int WrapCacheLimit = 4000;

    public InPlaceRenderer(InPlaceWatcher watcher, Func<Configuration> getConfig, Func<IFontHandle?> getFont)
    {
        this.watcher = watcher;
        this.getConfig = getConfig;
        this.getFont = getFont;
    }

    public void Draw()
    {
        var cfg = getConfig();
        if (!cfg.Enabled || !cfg.InPlaceEnabled) return;

        var font = getFont();
        if (font is not { Available: true }) return;

        font.Push();
        try
        {
            var imFont = ImGui.GetFont();
            var drawList = ImGui.GetBackgroundDrawList();
            foreach (var e in watcher.CurrentEntries())
            {
                DrawEntry(drawList, imFont, e);
                if (cfg.DebugWindow)
                {
                    drawList.AddRect(new Vector2(e.X, e.Y), new Vector2(e.X + Math.Max(e.W, 4), e.Y + Math.Max(e.H, 4)), 0xFF0000FF);
                    drawList.AddText(new Vector2(e.X, e.Y - 14), 0xFF00FFFF, $"{e.Addon} a={e.Alpha}");
                }
            }
        }
        finally
        {
            font.Pop();
        }
    }

    private void DrawEntry(ImDrawListPtr drawList, ImFontPtr font, in InPlaceEntry e)
    {
        var size = e.FontSize;
        var lineHeight = e.LineHeight;
        var wrapWidth = e.W > 8 ? e.W : float.MaxValue;

        // Tek satırlık kutu (buton, menü satırı, başlık): alt satıra sarmak yerine fontu küçültüp sığdır.
        var singleLine = e.H > 0 && e.H < e.LineHeight * 1.6f && !e.Text.Contains('\n');
        if (singleLine && wrapWidth < float.MaxValue)
        {
            var width = Measure(font, size, e.Text);
            if (width > wrapWidth)
            {
                size = Math.Max(e.FontSize * 0.6f, size * wrapWidth / width);
                lineHeight = Math.Min(lineHeight, size * 1.15f);
            }

            wrapWidth = float.MaxValue;
        }

        WrapLines(font, size, wrapWidth, e.Text);
        if (lines.Count == 0) return;

        // Sığmıyorsa önce satır aralığını sıkıştır, sonra fontu kademeli küçült.
        if (e.MaxHeight > 0)
        {
            var minSize = e.FontSize * 0.6f;
            while (lines.Count * lineHeight > e.MaxHeight)
            {
                if (lineHeight > size * 1.05f)
                {
                    lineHeight = Math.Max(size * 1.05f, e.MaxHeight / lines.Count);
                    continue;
                }

                if (size <= minSize) break;
                size = Math.Max(minSize, size * 0.92f);
                lineHeight = size * 1.1f;
                WrapLines(font, size, wrapWidth, e.Text);
            }
        }

        var blockHeight = lines.Count * lineHeight;
        var align = (int)e.Alignment;
        var hAlign = align % 3;        // 0 sol, 1 orta, 2 sağ
        var vAlign = align / 3;        // 0 üst, 1 orta, 2 alt

        var startY = e.Y;
        if (e.H > 0)
        {
            if (vAlign == 1) startY = e.Y + (e.H - blockHeight) / 2f;
            else if (vAlign == 2) startY = e.Y + e.H - blockHeight;
        }

        var alphaScale = e.Alpha / 255f;
        var textCol = WithAlpha(e.TextColor, alphaScale);
        var edgeCol = WithAlpha(e.EdgeColor, alphaScale);
        var drawEdge = e.EdgeStyle != 0 && (e.EdgeColor >> 24) > 0;

        var clipY2 = e.MaxHeight > 0 ? Math.Min(e.ClipY2, e.Y + e.MaxHeight) : e.ClipY2;
        drawList.PushClipRect(new Vector2(e.ClipX1, e.ClipY1), new Vector2(e.ClipX2, clipY2), true);

        var y = startY;
        foreach (var line in lines)
        {
            var x = e.X;
            if (hAlign != 0 && e.W > 0)
            {
                var lineWidth = Measure(font, size, line);
                x = hAlign == 1 ? e.X + (e.W - lineWidth) / 2f : e.X + e.W - lineWidth;
            }

            var pos = new Vector2(x, y);
            if (drawEdge && e.EdgeStyle == 1)
            {
                // Oyunun kenar çizgisine benzer ince bir dış hat.
                drawList.AddText(font, size, pos + new Vector2(-1, 0), edgeCol, line);
                drawList.AddText(font, size, pos + new Vector2(1, 0), edgeCol, line);
                drawList.AddText(font, size, pos + new Vector2(0, -1), edgeCol, line);
                drawList.AddText(font, size, pos + new Vector2(0, 1), edgeCol, line);
            }
            else if (drawEdge)
            {
                // Emboss: sağ alta tek gölge.
                drawList.AddText(font, size, pos + new Vector2(1, 1), edgeCol, line);
            }

            drawList.AddText(font, size, pos, textCol, line);
            y += lineHeight;
        }

        drawList.PopClipRect();
    }

    /// <summary>Kelime bazlı satır kaydırma; açık satır sonlarını korur. Sonuç önbelleklenir.</summary>
    private void WrapLines(ImFontPtr font, float size, float wrapWidth, string text)
    {
        lines.Clear();
        var key = (text, (int)MathF.Round(size * 4), wrapWidth >= float.MaxValue ? -1 : (int)wrapWidth);
        if (wrapCache.TryGetValue(key, out var cachedLines))
        {
            lines.AddRange(cachedLines);
            return;
        }

        WrapLinesUncached(font, size, wrapWidth, text);
        if (wrapCache.Count >= WrapCacheLimit) wrapCache.Clear();
        wrapCache[key] = lines.ToArray();
    }

    private void WrapLinesUncached(ImFontPtr font, float size, float wrapWidth, string text)
    {
        foreach (var paragraph in text.Split('\n'))
        {
            var p = paragraph.TrimEnd('\r');
            if (p.Length == 0)
            {
                lines.Add(string.Empty);
                continue;
            }

            if (wrapWidth >= float.MaxValue || Measure(font, size, p) <= wrapWidth)
            {
                lines.Add(p);
                continue;
            }

            var current = string.Empty;
            foreach (var word in p.Split(' '))
            {
                var candidate = current.Length == 0 ? word : current + " " + word;
                if (Measure(font, size, candidate) <= wrapWidth || current.Length == 0)
                {
                    current = candidate;
                }
                else
                {
                    lines.Add(current);
                    current = word;
                }
            }

            if (current.Length > 0) lines.Add(current);
        }
    }

    private static float Measure(ImFontPtr font, float size, string text)
    {
        return ImGui.CalcTextSizeA(font, size, float.MaxValue, 0f, text, out _).X;
    }

    private static uint WithAlpha(uint abgr, float scale)
    {
        var a = (byte)Math.Clamp((abgr >> 24) * scale, 0, 255);
        return (abgr & 0x00FFFFFF) | ((uint)a << 24);
    }
}
