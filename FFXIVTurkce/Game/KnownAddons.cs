using System;
namespace FFXIVTurkce.Game;

/// <summary>Çevrilebilecek oyun pencereleri: (addon adı, Türkçe etiket, varsayılan açık mı).</summary>
public static class KnownAddons
{
    public static readonly (string Addon, string Label, bool Default)[] All =
    [
        ("Talk", "NPC diyaloğu", true),
        ("BattleTalk", "Savaş konuşması", true),
        ("TalkSubtitle", "Alt yazı", true),
        ("_MiniTalk", "NPC konuşma balonları (yoldan geçerken)", true),
        ("_LocationTitle", "Bölge adı (giriş afişi)", true),
        ("_LocationTitleShort", "Bölge adı (kısa)", true),
        ("JournalDetail", "Görev günlüğü (açıklama)", true),
        ("Journal", "Görev günlüğü (liste)", true),
        ("_ToDoList", "Görev takipçisi (sağ üst)", true),
        ("ToDo", "Görev hedefleri (instance/FATE)", true),
        ("ScenarioTree", "Ana senaryo takipçisi", true),
        ("JournalAccept", "Görev kabul penceresi", true),
        ("JournalResult", "Görev teslim penceresi", true),
        ("RecommendList", "Önerilen görevler", true),
        ("SelectString", "Seçim menüsü", true),
        ("SelectIconString", "İkonlu seçim menüsü", true),
        ("CutSceneSelectString", "Ara sahne seçimi", true),
        ("SelectYesno", "Evet / Hayır kutusu", true),
        ("SelectOk", "Tamam kutusu", true),
        ("_WideText", "Ekran ortası yazı", true),
        ("_TextError", "Hata mesajı", true),
        ("_TextClassChange", "Sınıf değişimi bildirimi", true),
        ("_AreaText", "Bölge adı (küçük)", true),
        ("_PopUpText", "Ekran üstü bildirimler", true),
        ("ItemDetail", "Eşya açıklaması (tooltip)", false),
        ("ActionDetail", "Yetenek açıklaması (tooltip)", false),
    ];

    /// <summary>
    /// Açık (parşömen/beyaz) zeminli pencereler: oyun buralarda koyu yazı kullanır, rengi olduğu gibi bırak.
    /// Diğer pencereler koyu zeminlidir; koyu gelen yazı beyaza çevrilir.
    /// </summary>
    public static readonly System.Collections.Generic.HashSet<string> LightBackground = new(System.StringComparer.Ordinal)
    {
        "Talk", "BattleTalk", "TalkSubtitle", "CutSceneSelectString", "_MiniTalk",
        "JournalDetail", "JournalAccept", "JournalResult",
    };

    /// <summary>
    /// "Tüm pencereler" modunda çevrilmeyecek pencereler: HUD öğeleri, sohbet, isim etiketleri ve
    /// eşya/oyuncu adlarının bozulmaması gereken listeler. Sonu '*' olanlar önek eşleşmesidir.
    /// </summary>
    public static readonly string[] DefaultExcluded =
    [
        // HUD
        "_ActionBar*", "_ActionCross", "_ActionDoubleCross*", "_ParameterWidget", "_PartyList", "_EnemyList",
        "_TargetInfo*", "_FocusTargetInfo", "_NaviMap", "_Exp", "_Money", "_DTR", "_CastBar", "_TargetCursor",
        "_ScreenText", "_Status*", "_Gauge*", "JobHud*", "_LimitBreak", "_BattleTalk", "_Notification*",
        "_MainCommand", "_MainCross", "_ContentGauge", "_PoisonTargetInfo",
        // Sohbet, isimler
        "_TextChatLog", "ChatLog", "ChatLogPanel_*", "NamePlate", "_NamePlate", "FriendList", "FreeCompany*",
        "LinkShell", "CrossWorldLinkshell", "PartyMemberList", "Social*", "BlackList", "MuteList",
        // Eşya adları / market / envanter
        "Inventory*", "ItemSearch*", "ItemDetail", "ActionDetail", "RetainerList", "RetainerSell*", "Shop*",
        "RecipeNote", "RecipeTree", "RecipeProductList", "Synthesis*", "Gathering*", "Materialize", "Repair",
        "ArmouryBoard", "ChocoboSaddlebag", "MiragePrism*", "Bank", "FreeCompanyChest", "MateriaAttach",
        "ItemInspection", "Macro", "MacroText*", "ConfigKeybind*",
        // Metin girişi olan pencereler (yazdığın şeyi çevirmesin)
        "ChatLogPanel*", "InputString", "LetterEditor", "LetterViewer", "NoteBook*", "LinkShellEditor",
    ];

    public static bool IsExcludedByDefault(string addon)
    {
        foreach (var pattern in DefaultExcluded)
        {
            if (pattern.EndsWith('*'))
            {
                if (addon.StartsWith(pattern.AsSpan(0, pattern.Length - 1), System.StringComparison.Ordinal)) return true;
            }
            else if (string.Equals(addon, pattern, System.StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Bu düğümler çevrilmez (NPC adları vb.).</summary>
    public static bool IsNameNode(string addon, uint nodeId) => addon switch
    {
        "Talk" => nodeId == 2,
        "BattleTalk" => nodeId == 4,
        _ => false,
    };
}
