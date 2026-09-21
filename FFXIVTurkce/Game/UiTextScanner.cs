using System;
using System.Collections.Generic;
using System.Numerics;
using Bounds = FFXIVClientStructs.FFXIV.Common.Math.Bounds;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace FFXIVTurkce.Game;

/// <summary>Ekranda görünen bir metin düğümü ve piksel sınırları.</summary>
public struct UiText
{
    public string Text;
    public float X;
    public float Y;
    public float W;
    public float H;

    public readonly bool Contains(Vector2 p) => p.X >= X && p.X <= X + W && p.Y >= Y && p.Y <= Y + H;
    public readonly float Area => W * H;

    /// <summary>Noktanın dikdörtgene uzaklığı (içindeyse 0).</summary>
    public readonly float DistanceTo(Vector2 p)
    {
        var dx = Math.Max(Math.Max(X - p.X, 0f), p.X - (X + W));
        var dy = Math.Max(Math.Max(Y - p.Y, 0f), p.Y - (Y + H));
        return MathF.Sqrt(dx * dx + dy * dy);
    }
}

/// <summary>Yerinde çizim için metin düğümü + sınırları.</summary>
public unsafe struct TextNodeInfo
{
    public AtkTextNode* Node;
    public UiText Rect;
    public float Scale;
}

/// <summary>Bir addon'daki (ve içindeki bileşenlerdeki) tüm görünür metin düğümlerini toplar.</summary>
public static unsafe class UiTextScanner
{
    private const int MaxDepth = 6;

    public static List<UiText> Collect(AtkUnitBase* addon)
    {
        var list = new List<UiText>();
        if (addon == null || addon->RootNode == null) return list;
        CollectFromUld(&addon->UldManager, addon->Scale, list, 0);
        return list;
    }

    /// <summary>Yerinde çizim için: düğüm işaretçisiyle birlikte toplar.</summary>
    public static List<TextNodeInfo> CollectNodes(AtkUnitBase* addon, HashSet<nint>? forcedVisible = null)
    {
        var list = new List<TextNodeInfo>();
        if (addon == null || addon->RootNode == null) return list;
        CollectNodesFromUld(&addon->UldManager, addon->Scale, list, 0, forcedVisible);
        return list;
    }

    /// <summary>Verilen düğüm işaretçisi hâlâ bu addon'un ağacında mı? (Dispose'da güvenli geri alma için.)</summary>
    public static bool ContainsNode(AtkUnitBase* addon, AtkResNode* target)
    {
        if (addon == null || target == null) return false;
        return ContainsNode(&addon->UldManager, target, 0);
    }

    private static bool ContainsNode(AtkUldManager* uld, AtkResNode* target, int depth)
    {
        if (uld == null || depth > MaxDepth) return false;
        var count = uld->NodeListCount;
        for (var i = 0; i < count; i++)
        {
            var node = uld->NodeList[i];
            if (node == null) continue;
            if (node == target) return true;
            if ((int)node->Type >= 1000)
            {
                var component = ((AtkComponentNode*)node)->Component;
                if (component != null && ContainsNode(&component->UldManager, target, depth + 1)) return true;
            }
        }

        return false;
    }

    private static void CollectNodesFromUld(AtkUldManager* uld, float scale, List<TextNodeInfo> list, int depth, HashSet<nint>? forcedVisible)
    {
        if (uld == null || depth > MaxDepth) return;
        var count = uld->NodeListCount;
        for (var i = 0; i < count; i++)
        {
            var node = uld->NodeList[i];
            if (node == null) continue;

            if ((int)node->Type >= 1000)
            {
                var component = ((AtkComponentNode*)node)->Component;
                if (component == null || !VisibleChain(node)) continue;
                CollectNodesFromUld(&component->UldManager, scale, list, depth + 1, forcedVisible);
                continue;
            }

            if (node->Type != NodeType.Text) continue;
            var ownFlagIgnored = forcedVisible is not null && forcedVisible.Contains((nint)node);
            if (ownFlagIgnored ? !VisibleChain(node->ParentNode) : !VisibleChain(node)) continue;

            var textNode = (AtkTextNode*)node;
            var text = AddonText.ReadTextNode(textNode);
            if (!LooksTranslatable(text)) continue;

            Bounds b;
            node->GetBounds(&b);
            var rect = new UiText { Text = text, X = b.Pos1.X, Y = b.Pos1.Y, W = b.Width, H = b.Height };
            if (rect.W <= 0 || rect.H <= 0)
            {
                rect.X = node->ScreenX;
                rect.Y = node->ScreenY;
                rect.W = node->Width * scale * node->ScaleX;
                rect.H = node->Height * scale * node->ScaleY;
            }

            list.Add(new TextNodeInfo { Node = textNode, Rect = rect, Scale = scale });
        }
    }

    /// <summary>Addon'un kök düğümünün ekran sınırları.</summary>
    public static bool TryGetRootBounds(AtkUnitBase* addon, out UiText rect)
    {
        rect = default;
        if (addon == null || addon->RootNode == null) return false;
        Bounds b;
        addon->RootNode->GetBounds(&b);
        rect = new UiText { X = b.Pos1.X, Y = b.Pos1.Y, W = b.Width, H = b.Height };
        return rect.W > 0 && rect.H > 0;
    }

    private static void CollectFromUld(AtkUldManager* uld, float scale, List<UiText> list, int depth)
    {
        if (uld == null || depth > MaxDepth) return;
        var count = uld->NodeListCount;
        for (var i = 0; i < count; i++)
        {
            var node = uld->NodeList[i];
            if (node == null) continue;

            if ((int)node->Type >= 1000)
            {
                // Bileşen düğümü (liste, buton...): kendi ULD ağacı var, içine gir.
                var comp = (AtkComponentNode*)node;
                var component = comp->Component;
                if (component == null || !VisibleChain(node)) continue;
                CollectFromUld(&component->UldManager, scale, list, depth + 1);
                continue;
            }

            if (node->Type != NodeType.Text) continue;
            if (!VisibleChain(node)) continue;

            var textNode = (AtkTextNode*)node;
            var text = AddonText.ReadTextNode(textNode);
            if (!LooksTranslatable(text)) continue;

            Bounds b;
            node->GetBounds(&b);
            var rect = new UiText
            {
                Text = text,
                X = b.Pos1.X,
                Y = b.Pos1.Y,
                W = b.Width,
                H = b.Height,
            };

            if (rect.W <= 0 || rect.H <= 0)
            {
                rect.X = node->ScreenX;
                rect.Y = node->ScreenY;
                rect.W = node->Width * scale * node->ScaleX;
                rect.H = node->Height * scale * node->ScaleY;
            }

            // Çok satırlı metinlerde düğüm yüksekliği çoğu zaman tek satır kadar; gerçek yüksekliği tahmin et.
            rect.H = Math.Max(rect.H, EstimateTextHeight(text, rect.W, textNode->FontSize * scale));

            list.Add(rect);
        }
    }

    private static float EstimateTextHeight(string text, float width, float fontSize)
    {
        if (fontSize <= 0) fontSize = 14f;
        if (width <= 0) return fontSize * 1.4f;
        var charWidth = fontSize * 0.5f;
        var charsPerLine = Math.Max(1, (int)(width / charWidth));
        var lines = 0;
        foreach (var line in text.Split('\n'))
            lines += Math.Max(1, (line.Length + charsPerLine - 1) / charsPerLine);
        return lines * fontSize * 1.4f;
    }

    private static bool VisibleChain(AtkResNode* node)
    {
        while (node != null)
        {
            if (!node->IsVisible()) return false;
            node = node->ParentNode;
        }

        return true;
    }

    /// <summary>En az üç harf içeren, sadece sayı/sembol/kısaltma olmayan metinler.</summary>
    public static bool LooksTranslatable(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        var letters = 0;
        foreach (var c in text)
        {
            if (char.IsLetter(c) && ++letters >= 3) return true;
        }

        return false;
    }
}
