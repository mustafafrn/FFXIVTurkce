using System;
using System.Collections.Generic;
using Dalamud.Configuration;

namespace FFXIVTurkce;

public enum InPlaceColorMode
{
    White = 0,
    Black = 1,
    Original = 2,
}

public enum TranslationEngine
{
    Gemini = 0,
    Claude = 1,
    GoogleFree = 2,
}

[Serializable]
public class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 6;

    public bool Enabled { get; set; } = true;

    public TranslationEngine Engine { get; set; } = TranslationEngine.GoogleFree;

    // Gemini
    public string GeminiApiKey { get; set; } = string.Empty;
    public string GeminiModel { get; set; } = "gemini-2.5-flash";

    // Claude
    public string ClaudeApiKey { get; set; } = string.Empty;
    public string ClaudeModel { get; set; } = "claude-opus-5";

    // Hangi pencereler çevrilsin
    public bool TranslateTalk { get; set; } = true;
    public bool TranslateBattleTalk { get; set; } = true;

    // Overlay görünümü
    public float FontSize { get; set; } = 22f;
    public float OverlayWidth { get; set; } = 900f;
    public float OverlayOffsetY { get; set; } = -8f;
    public bool ShowOriginal { get; set; } = false;
    public float BackgroundAlpha { get; set; } = 0.85f;

    // Yerinde çizim: oyunun metnini gizleyip aynı yere Türkçesini çiz
    public bool InPlaceEnabled { get; set; } = true;
    public List<string> InPlaceAddons { get; set; } = DefaultInPlace();
    public float InPlaceFontScale { get; set; } = 1.3f;
    public bool InPlaceForceWhite { get; set; } = true; // eski ayar, v4'te InPlaceColor'a taşındı
    public InPlaceColorMode InPlaceColor { get; set; } = InPlaceColorMode.White;
    public bool InPlaceOutline { get; set; } = true;

    /// <summary>Listede olmayan pencereler için elle eklenen addon adları (virgülle ayrılmış).</summary>
    public string ExtraInPlaceAddons { get; set; } = string.Empty;

    /// <summary>true: hariç listesi dışındaki tüm pencereler; false: sadece seçili liste.</summary>
    public bool InPlaceAll { get; set; } = true;

    /// <summary>Kullanıcının hariç tuttuğu pencereler (virgülle ayrılmış, '*' önek eşleşmesi).</summary>
    public string ExcludedInPlaceAddons { get; set; } = string.Empty;

    public static List<string> DefaultInPlace()
    {
        var list = new List<string>();
        foreach (var k in Game.KnownAddons.All)
            if (k.Default) list.Add(k.Addon);
        return list;
    }

    // Genel panel çevirisi (görev günlüğü, seçim menüleri, toast'lar...)
    public bool PanelsEnabled { get; set; } = false;
    public List<string> EnabledPanels { get; set; } = DefaultPanels();
    public float PanelWidth { get; set; } = 460f;

    // Fareyle üstüne gelince çeviri
    public bool HoverEnabled { get; set; } = true;
    public bool DebugWindow { get; set; } = false;
    public bool HoverOnlyWithShift { get; set; } = false;
    public int HoverDelayMs { get; set; } = 250;
    public bool HoverShowOriginal { get; set; } = false;

    // Görev ön-çevirisi (aktif görevlerin metinlerini önceden çevirip önbelleğe yaz)
    public bool PrefetchEnabled { get; set; } = true;
    public int PrefetchDelayMs { get; set; } = 400;

    // Font dosyası (boşsa Segoe UI → Dalamud Noto Sans sırasıyla denenir)
    public string FontPath { get; set; } = string.Empty;

    public static List<string> DefaultPanels()
    {
        var list = new List<string>();
        foreach (var k in Game.KnownAddons.All)
            if (k.Default && k.Addon != "Talk" && k.Addon != "BattleTalk") list.Add(k.Addon);
        return list;
    }

    // Çeviri promptu için ek sözlük (satır başına "İngilizce = Türkçe")
    public string ExtraGlossary { get; set; } = string.Empty;
}
