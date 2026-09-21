using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;
using FFXIVTurkce.Translation;

namespace FFXIVTurkce.Game;

/// <summary>
/// Fare imlecinin altındaki oyun metnini bulur; kısa bir süre sabit kalırsa çeviri ister.
/// Seçim sırası: (1) doğrudan üstünde olunan en küçük metin, (2) yakındaki metin,
/// (3) farenin bulunduğu pencerenin en uzun metni (görev açıklaması gibi).
/// </summary>
public sealed unsafe class HoverWatcher : IDisposable
{
    private const float NearbyDistancePx = 28f;

    // Bu pencereler zaten ayrı overlay ile çevriliyor ya da çevrilmeye değmez.
    private static readonly HashSet<string> Skip = new(StringComparer.Ordinal)
    {
        "Talk", "BattleTalk", "_FocusTargetInfo", "_TargetInfo", "_TargetInfoMainTarget",
        "_TargetInfoBuffDebuff", "_NaviMap", "_ActionBar", "_ActionBar01", "_ActionBar02", "_ActionBar03",
        "_ActionBar04", "_ActionBar05", "_ActionBar06", "_ActionBar07", "_ActionBar08", "_ActionBar09",
        "_ActionCross", "_ParameterWidget", "_StatusCustom0", "_StatusCustom1", "_StatusCustom2",
        "_PartyList", "_EnemyList", "_Exp", "_Money", "_DTR", "_TextChatLog", "ChatLog", "ChatLogPanel_0",
        "ChatLogPanel_1", "ChatLogPanel_2", "ChatLogPanel_3", "_CastBar", "_TargetCursor", "_ScreenText",
    };

    private readonly IFramework framework;
    private readonly IPluginLog log;
    private readonly Func<Configuration> getConfig;
    private readonly TranslationService translation;
    private readonly Func<string, bool> inPlaceHandles;

    private DateTime lastScan = DateTime.MinValue;
    private DateTime hoverSince = DateTime.MinValue;

    /// <summary>Overlay her frame güncel fare konumunu buraya yazar (ImGui koordinatı = oyun ekran koordinatı).</summary>
    public Vector2 MousePos;

    /// <summary>Shift şartı varsa overlay bunu doldurur.</summary>
    public bool ModifierHeld = true;

    public string HoveredText { get; private set; } = string.Empty;
    public string HoveredAddon { get; private set; } = string.Empty;
    public TranslationResult? Result { get; private set; }

    // Tanı penceresi için
    public int DebugAddonCount;
    public int DebugVisibleAddons;
    public int DebugTextCount;
    public string DebugAddonsUnderMouse = string.Empty;
    public string DebugPick = string.Empty;
    public UiText? DebugBest;

    public HoverWatcher(IFramework framework, IPluginLog log, Func<Configuration> getConfig, TranslationService translation, Func<string, bool> inPlaceHandles)
    {
        this.framework = framework;
        this.log = log;
        this.getConfig = getConfig;
        this.translation = translation;
        this.inPlaceHandles = inPlaceHandles;
        framework.Update += OnUpdate;
    }

    private void OnUpdate(IFramework _)
    {
        var cfg = getConfig();
        if (!cfg.Enabled || !cfg.HoverEnabled || (cfg.HoverOnlyWithShift && !ModifierHeld))
        {
            Reset();
            return;
        }

        var now = DateTime.UtcNow;
        if ((now - lastScan).TotalMilliseconds < 100) return;
        lastScan = now;

        try
        {
            Scan(now, cfg);
        }
        catch (Exception e)
        {
            Reset();
            log.Error(e, "Hover taraması hatası");
        }
    }

    private void Scan(DateTime now, Configuration cfg)
    {
        var mouse = MousePos;
        var manager = RaptureAtkUnitManager.Instance();
        if (manager == null)
        {
            Reset();
            return;
        }

        UiText? direct = null;          // fare tam üstünde, en küçük alan
        UiText? nearby = null;          // en yakın metin
        var nearbyDist = float.MaxValue;
        UiText? longest = null;         // farenin içinde olduğu en küçük pencerenin en uzun metni
        var containerArea = float.MaxValue;

        string directAddon = string.Empty, nearbyAddon = string.Empty, longestAddon = string.Empty;
        var visibleAddons = 0;
        var textCount = 0;
        var underMouse = new StringBuilder();

        var list = manager->AllLoadedUnitsList;
        var count = list.Count;
        for (var i = 0; i < count; i++)
        {
            var addon = list.Entries[i].Value;
            if (addon == null || !addon->IsVisible || addon->RootNode == null) continue;

            var name = addon->NameString;
            if (string.IsNullOrEmpty(name) || Skip.Contains(name) || inPlaceHandles(name)) continue;
            visibleAddons++;

            var hasRoot = UiTextScanner.TryGetRootBounds(addon, out var root);
            var mouseInside = hasRoot && root.Contains(mouse);

            // Kökü makul büyüklükte olan pencerelerde fare, kökün 150 px çevresinde bile değilse
            // metinleri hiç ayrıştırma (her 100 ms'de onlarca pencereyi parse etmek pahalı).
            if (hasRoot && root.W > 64 && root.H > 64 && !mouseInside)
            {
                const float margin = 150f;
                if (mouse.X < root.X - margin || mouse.X > root.X + root.W + margin ||
                    mouse.Y < root.Y - margin || mouse.Y > root.Y + root.H + margin)
                    continue;
            }

            var texts = UiTextScanner.Collect(addon);
            textCount += texts.Count;
            if (texts.Count == 0) continue;

            UiText? addonLongest = null;
            foreach (var t in texts)
            {
                if (t.Contains(mouse))
                {
                    if (direct is null || t.Area < direct.Value.Area)
                    {
                        direct = t;
                        directAddon = name;
                    }
                }
                else
                {
                    var d = t.DistanceTo(mouse);
                    if (d <= NearbyDistancePx && d < nearbyDist)
                    {
                        nearby = t;
                        nearbyDist = d;
                        nearbyAddon = name;
                    }
                }

                if (addonLongest is null || t.Text.Length > addonLongest.Value.Text.Length)
                    addonLongest = t;
            }

            if (mouseInside)
            {
                if (underMouse.Length > 0) underMouse.Append(", ");
                underMouse.Append(name);

                if (root.Area < containerArea && addonLongest is not null)
                {
                    containerArea = root.Area;
                    longest = addonLongest;
                    longestAddon = name;
                }
            }
        }

        UiText? best;
        string bestAddon;
        string pick;
        if (direct is not null) { best = direct; bestAddon = directAddon; pick = "üstünde"; }
        else if (nearby is not null) { best = nearby; bestAddon = nearbyAddon; pick = "yakın"; }
        else if (longest is not null) { best = longest; bestAddon = longestAddon; pick = "pencere"; }
        else { best = null; bestAddon = string.Empty; pick = "-"; }

        DebugAddonCount = (int)count;
        DebugVisibleAddons = visibleAddons;
        DebugTextCount = textCount;
        DebugAddonsUnderMouse = underMouse.ToString();
        DebugPick = pick;
        DebugBest = best;

        if (best is null)
        {
            Reset();
            return;
        }

        var text = best.Value.Text.Trim();
        if (text != HoveredText)
        {
            HoveredText = text;
            HoveredAddon = bestAddon;
            hoverSince = now;
            // Önbellekte varsa beklemeden göster.
            Result = translation.TryGetCached(text);
            return;
        }

        if (Result is null && (now - hoverSince).TotalMilliseconds >= cfg.HoverDelayMs)
            Result = translation.Request(string.Empty, text);
    }

    private void Reset()
    {
        HoveredText = string.Empty;
        HoveredAddon = string.Empty;
        Result = null;
    }

    public void Dispose()
    {
        framework.Update -= OnUpdate;
    }
}
