using System;
using System.Numerics;
using System.Threading.Tasks;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using FFXIVTurkce.Game;
using FFXIVTurkce.Translation;

namespace FFXIVTurkce.Windows;

public sealed class ConfigWindow : Window
{
    private readonly Plugin plugin;
    private string testInput = "Pray, adventurer, lend me your ears. The Scions have need of you in Ul'dah.";
    private string testOutput = string.Empty;
    private bool testRunning;

    public ConfigWindow(Plugin plugin) : base("FFXIV Türkçe — Ayarlar##trk_config")
    {
        this.plugin = plugin;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(520, 420),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue),
        };
    }

    private bool uiFontPushed;

    public override void PreDraw()
    {
        var font = plugin.UiFont;
        uiFontPushed = font is { Available: true };
        if (uiFontPushed) font!.Push();
    }

    public override void PostDraw()
    {
        if (uiFontPushed) plugin.UiFont!.Pop();
        uiFontPushed = false;
    }

    public override void Draw()
    {
        var cfg = plugin.Configuration;
        var changed = false;

        var enabled = cfg.Enabled;
        if (ImGui.Checkbox("Çeviri açık", ref enabled)) { cfg.Enabled = enabled; changed = true; }

        ImGui.Separator();
        ImGui.TextUnformatted("Çeviri motoru");

        var engine = (int)cfg.Engine;
        if (ImGui.RadioButton("Google Translate (key gerekmez)", ref engine, (int)TranslationEngine.GoogleFree)) { cfg.Engine = TranslationEngine.GoogleFree; changed = true; }
        ImGui.SameLine();
        if (ImGui.RadioButton("Gemini", ref engine, (int)TranslationEngine.Gemini)) { cfg.Engine = TranslationEngine.Gemini; changed = true; }
        ImGui.SameLine();
        if (ImGui.RadioButton("Claude", ref engine, (int)TranslationEngine.Claude)) { cfg.Engine = TranslationEngine.Claude; changed = true; }

        if (cfg.Engine == TranslationEngine.GoogleFree)
        {
            ImGui.TextDisabled("Makine çevirisi; isimleri de çevirebilir. Daha doğal çeviri için Gemini/Claude seç.");
        }
        else if (cfg.Engine == TranslationEngine.Gemini)
        {
            var key = cfg.GeminiApiKey;
            if (ImGui.InputText("Gemini API key", ref key, 256, ImGuiInputTextFlags.Password)) { cfg.GeminiApiKey = key; changed = true; }
            var model = cfg.GeminiModel;
            if (ImGui.InputText("Gemini model", ref model, 64)) { cfg.GeminiModel = model; changed = true; }
            ImGui.TextDisabled("Key: aistudio.google.com → Get API key");
        }
        else
        {
            var key = cfg.ClaudeApiKey;
            if (ImGui.InputText("Claude API key", ref key, 256, ImGuiInputTextFlags.Password)) { cfg.ClaudeApiKey = key; changed = true; }
            var model = cfg.ClaudeModel;
            if (ImGui.InputText("Claude model", ref model, 64)) { cfg.ClaudeModel = model; changed = true; }
            ImGui.TextDisabled("Key: console.anthropic.com");
        }

        ImGui.Separator();
        var inPlaceOn = cfg.InPlaceEnabled;
        if (ImGui.Checkbox("Yerinde çeviri (oyunun kendi kutusunun içinde Türkçe göster)", ref inPlaceOn)) { cfg.InPlaceEnabled = inPlaceOn; changed = true; }
        if (cfg.InPlaceEnabled)
        {
            ImGui.Indent();
            var scope = cfg.InPlaceAll ? 0 : 1;
            if (ImGui.RadioButton("Tüm pencereler (HUD, sohbet, envanter hariç)", ref scope, 0)) { cfg.InPlaceAll = true; changed = true; plugin.RefreshInPlaceListeners(); }
            ImGui.SameLine();
            if (ImGui.RadioButton("Sadece seçili pencereler", ref scope, 1)) { cfg.InPlaceAll = false; changed = true; plugin.RefreshInPlaceListeners(); }

            if (cfg.InPlaceAll)
            {
                var excl = cfg.ExcludedInPlaceAddons;
                if (ImGui.InputText("Hariç tut (addon adı, virgülle, * önek)", ref excl, 512)) { cfg.ExcludedInPlaceAddons = excl; changed = true; }
                ImGui.TextDisabled("Bir pencerede çeviri sorun çıkarıyorsa adını buraya yaz.");
            }

            var half = (KnownAddons.All.Length + 1) / 2;
            if (!cfg.InPlaceAll && ImGui.BeginTable("##inplace", 2))
            {
                for (var i = 0; i < half; i++)
                {
                    ImGui.TableNextRow();
                    for (var col = 0; col < 2; col++)
                    {
                        ImGui.TableSetColumnIndex(col);
                        var idx = i + col * half;
                        if (idx >= KnownAddons.All.Length) continue;
                        var (addon, label, _) = KnownAddons.All[idx];
                        var on = cfg.InPlaceAddons.Contains(addon);
                        if (ImGui.Checkbox($"{label}##inplace_{addon}", ref on))
                        {
                            if (on) cfg.InPlaceAddons.Add(addon); else cfg.InPlaceAddons.Remove(addon);
                            changed = true;
                        }
                    }
                }
                ImGui.EndTable();
            }
            if (!cfg.InPlaceAll)
            {
                var extra = cfg.ExtraInPlaceAddons;
                if (ImGui.InputText("Ek pencereler (addon adı, virgülle)", ref extra, 512)) { cfg.ExtraInPlaceAddons = extra; changed = true; }
                if (ImGui.IsItemDeactivatedAfterEdit()) plugin.RefreshInPlaceListeners();
                ImGui.TextDisabled("Pencere adını /trk debug tanı penceresindeki \"Fare altındaki addonlar\" satırından öğrenebilirsin.");
            }

            var fs = cfg.InPlaceFontScale;
            if (ImGui.SliderFloat("Yazı boyutu çarpanı", ref fs, 0.7f, 2.0f, "%.2f")) { cfg.InPlaceFontScale = fs; changed = true; }
            ImGui.TextUnformatted("Yazı rengi:");
            ImGui.SameLine();
            var cm = (int)cfg.InPlaceColor;
            if (ImGui.RadioButton("Beyaz", ref cm, 0)) { cfg.InPlaceColor = InPlaceColorMode.White; changed = true; }
            ImGui.SameLine();
            if (ImGui.RadioButton("Siyah", ref cm, 1)) { cfg.InPlaceColor = InPlaceColorMode.Black; changed = true; }
            ImGui.SameLine();
            if (ImGui.RadioButton("Oyunun rengi", ref cm, 2)) { cfg.InPlaceColor = InPlaceColorMode.Original; changed = true; }
            var ol = cfg.InPlaceOutline;
            if (ImGui.Checkbox("Kenar çizgisi (dış hat)", ref ol)) { cfg.InPlaceOutline = ol; changed = true; }
            ImGui.Unindent();
        }

        ImGui.Separator();
        ImGui.TextUnformatted("Diyalog overlay'i (yerinde çeviri kapalıysa)");
        var talk = cfg.TranslateTalk;
        if (ImGui.Checkbox("NPC diyalogları (Talk)", ref talk)) { cfg.TranslateTalk = talk; changed = true; }
        var battle = cfg.TranslateBattleTalk;
        if (ImGui.Checkbox("Savaş konuşmaları (BattleTalk)", ref battle)) { cfg.TranslateBattleTalk = battle; changed = true; }

        ImGui.Separator();
        var panelsOn = cfg.PanelsEnabled;
        if (ImGui.Checkbox("Pencere panelleri (pencerenin yanında Türkçe kutu)", ref panelsOn)) { cfg.PanelsEnabled = panelsOn; changed = true; }
        if (cfg.PanelsEnabled)
        {
            ImGui.Indent();
            var half = (PanelWatcher.Known.Length + 1) / 2;
            if (ImGui.BeginTable("##panels", 2))
            {
                for (var i = 0; i < half; i++)
                {
                    ImGui.TableNextRow();
                    for (var col = 0; col < 2; col++)
                    {
                        ImGui.TableSetColumnIndex(col);
                        var idx = i + col * half;
                        if (idx >= PanelWatcher.Known.Length) continue;
                        var (addon, label, _) = PanelWatcher.Known[idx];
                        var on = cfg.EnabledPanels.Contains(addon);
                        if (ImGui.Checkbox($"{label}##panel_{addon}", ref on))
                        {
                            if (on) cfg.EnabledPanels.Add(addon); else cfg.EnabledPanels.Remove(addon);
                            changed = true;
                        }
                    }
                }
                ImGui.EndTable();
            }
            var pw = cfg.PanelWidth;
            if (ImGui.SliderFloat("Panel genişliği", ref pw, 250f, 900f, "%.0f")) { cfg.PanelWidth = pw; changed = true; }
            ImGui.Unindent();
        }

        ImGui.Separator();
        var hoverOn = cfg.HoverEnabled;
        if (ImGui.Checkbox("Fareyle üstüne gelince çevir (her yazı için baloncuk)", ref hoverOn)) { cfg.HoverEnabled = hoverOn; changed = true; }
        if (cfg.HoverEnabled)
        {
            ImGui.Indent();
            var shift = cfg.HoverOnlyWithShift;
            if (ImGui.Checkbox("Sadece Shift basılıyken", ref shift)) { cfg.HoverOnlyWithShift = shift; changed = true; }
            var delay = cfg.HoverDelayMs;
            if (ImGui.SliderInt("Bekleme (ms)", ref delay, 0, 1500)) { cfg.HoverDelayMs = delay; changed = true; }
            var hso = cfg.HoverShowOriginal;
            if (ImGui.Checkbox("Baloncukta İngilizcesini de göster", ref hso)) { cfg.HoverShowOriginal = hso; changed = true; }
            var dbg = cfg.DebugWindow;
            if (ImGui.Checkbox("Tanı penceresi (hover neden çıkmıyor?)", ref dbg)) { cfg.DebugWindow = dbg; changed = true; }
            ImGui.Unindent();
        }

        ImGui.Separator();
        ImGui.TextUnformatted("Görünüm");
        var fontSize = cfg.FontSize;
        if (ImGui.SliderFloat("Yazı boyutu", ref fontSize, 14f, 40f, "%.0f"))
        {
            cfg.FontSize = fontSize;
            changed = true;
        }
        if (ImGui.IsItemDeactivatedAfterEdit()) plugin.RebuildFont();

        var width = cfg.OverlayWidth;
        if (ImGui.SliderFloat("Kutu genişliği", ref width, 400f, 1600f, "%.0f")) { cfg.OverlayWidth = width; changed = true; }
        var offset = cfg.OverlayOffsetY;
        if (ImGui.SliderFloat("Dikey kaydırma", ref offset, -200f, 200f, "%.0f")) { cfg.OverlayOffsetY = offset; changed = true; }
        var alpha = cfg.BackgroundAlpha;
        if (ImGui.SliderFloat("Arka plan opaklığı", ref alpha, 0.2f, 1f, "%.2f")) { cfg.BackgroundAlpha = alpha; changed = true; }
        var showOrig = cfg.ShowOriginal;
        if (ImGui.Checkbox("Orijinal İngilizceyi de göster", ref showOrig)) { cfg.ShowOriginal = showOrig; changed = true; }

        // Font seçimi: kurulu Windows fontlarından liste + serbest yol
        var currentFile = System.IO.Path.GetFileName(plugin.ActiveFontPath);
        var currentLabel = "Özel / otomatik";
        foreach (var (label, file) in Plugin.FontChoices)
            if (string.Equals(file, currentFile, StringComparison.OrdinalIgnoreCase)) { currentLabel = label; break; }

        if (ImGui.BeginCombo("Font", currentLabel))
        {
            foreach (var (label, file) in Plugin.FontChoices)
            {
                var full = System.IO.Path.Combine(Plugin.WindowsFontsDir, file);
                if (!System.IO.File.Exists(full)) continue;
                var selected = string.Equals(file, currentFile, StringComparison.OrdinalIgnoreCase);
                if (ImGui.Selectable(label, selected))
                {
                    cfg.FontPath = full;
                    changed = true;
                    plugin.RebuildFont();
                }
            }
            ImGui.EndCombo();
        }

        var fontPath = cfg.FontPath;
        if (ImGui.InputText("Font dosyası (.ttf, boş = otomatik)", ref fontPath, 512)) { cfg.FontPath = fontPath; changed = true; }
        if (ImGui.IsItemDeactivatedAfterEdit()) plugin.RebuildFont();
        ImGui.TextDisabled($"Kullanılan: {(plugin.ActiveFontPath.Length > 0 ? plugin.ActiveFontPath : "Dalamud Noto Sans")}");

        ImGui.Separator();
        ImGui.TextUnformatted("Ek sözlük (satır başına: İngilizce = Türkçe)");
        var glossary = cfg.ExtraGlossary;
        if (ImGui.InputTextMultiline("##glossary", ref glossary, 4000, new Vector2(-1, 80))) { cfg.ExtraGlossary = glossary; changed = true; }

        ImGui.Separator();
        ImGui.TextUnformatted($"Çeviri veritabanı: {plugin.Translation.Cache.Count} kayıt");
        ImGui.SameLine();
        if (ImGui.Button("Temizle")) plugin.Translation.Cache.Clear();

        var pf = cfg.PrefetchEnabled;
        if (ImGui.Checkbox("Görev ön-çevirisi (aktif görevlerin tüm metinlerini arka planda önceden çevir)", ref pf)) { cfg.PrefetchEnabled = pf; changed = true; }
        if (cfg.PrefetchEnabled)
        {
            ImGui.Indent();
            var pd = cfg.PrefetchDelayMs;
            if (ImGui.SliderInt("İstekler arası bekleme (ms)", ref pd, 100, 2000)) { cfg.PrefetchDelayMs = pd; changed = true; }
            var q = plugin.Prefetcher.QueueCount;
            ImGui.TextDisabled(q > 0
                ? $"Kuyrukta {q} metin — son görev: {plugin.Prefetcher.LastQuestName}"
                : $"Kuyruk boş — bu oturumda {plugin.Prefetcher.DoneCount} metin önceden çevrildi");
            ImGui.Unindent();
        }

        ImGui.Separator();
        ImGui.TextUnformatted("Test");
        ImGui.InputTextMultiline("##testin", ref testInput, 2000, new Vector2(-1, 60));
        if (testRunning) ImGui.BeginDisabled();
        if (ImGui.Button("Çevir"))
        {
            testRunning = true;
            testOutput = "Çevriliyor...";
            _ = Task.Run(async () =>
            {
                try
                {
                    testOutput = await plugin.Translation.TestAsync(testInput);
                }
                catch (Exception e)
                {
                    testOutput = "Hata: " + e.Message;
                }
                finally
                {
                    testRunning = false;
                }
            });
        }
        if (testRunning) ImGui.EndDisabled();
        if (!string.IsNullOrEmpty(testOutput))
        {
            var font = plugin.OverlayFont;
            if (font is { Available: true }) font.Push();
            ImGui.TextWrapped(testOutput);
            if (font is { Available: true }) font.Pop();
        }

        if (changed) plugin.SaveConfiguration();
    }
}
