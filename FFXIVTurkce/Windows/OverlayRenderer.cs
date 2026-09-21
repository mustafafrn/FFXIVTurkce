using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.ManagedFontAtlas;
using FFXIVTurkce.Game;
using FFXIVTurkce.Translation;

namespace FFXIVTurkce.Windows;

/// <summary>Diyalog overlay'i, pencere panelleri ve hover baloncuğunu çizer.</summary>
public sealed class OverlayRenderer
{
    private const ImGuiWindowFlags OverlayFlags =
        ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove |
        ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.AlwaysAutoResize |
        ImGuiWindowFlags.NoFocusOnAppearing | ImGuiWindowFlags.NoNav | ImGuiWindowFlags.NoInputs |
        ImGuiWindowFlags.NoSavedSettings;

    private static readonly Vector4 SpeakerColor = new(1f, 0.85f, 0.45f, 1f);
    private static readonly Vector4 PendingColor = new(0.7f, 0.7f, 0.7f, 1f);
    private static readonly Vector4 ErrorColor = new(1f, 0.4f, 0.4f, 1f);
    private static readonly Vector4 OriginalColor = new(0.6f, 0.6f, 0.6f, 1f);
    private static readonly Vector4 TitleColor = new(0.55f, 0.8f, 1f, 1f);

    private readonly DialogueWatcher dialogue;
    private readonly PanelWatcher panels;
    private readonly HoverWatcher hover;
    private readonly Func<Configuration> getConfig;
    private readonly Func<IFontHandle?> getFont;
    private readonly Dictionary<string, Vector2> lastSizes = new();
    private Func<double> inPlaceFrameMs = () => 0;

    public void SetFrameCostSource(Func<double> source) => inPlaceFrameMs = source;

    public OverlayRenderer(
        DialogueWatcher dialogue,
        PanelWatcher panels,
        HoverWatcher hover,
        Func<Configuration> getConfig,
        Func<IFontHandle?> getFont)
    {
        this.dialogue = dialogue;
        this.panels = panels;
        this.hover = hover;
        this.getConfig = getConfig;
        this.getFont = getFont;
    }

    public void Draw()
    {
        var cfg = getConfig();

        // Hover izleyicisine fare konumunu ve Shift durumunu ilet.
        var io = ImGui.GetIO();
        hover.MousePos = io.MousePos;
        hover.ModifierHeld = io.KeyShift;

        if (!cfg.Enabled) return;

        var font = getFont();
        var fontPushed = font is { Available: true };
        if (fontPushed) font!.Push();

        try
        {
            foreach (var state in dialogue.States)
            {
                if (state.Visible && state.Result is not null)
                    DrawDialogue(state, cfg);
            }

            if (cfg.PanelsEnabled)
            {
                foreach (var state in panels.States)
                {
                    if (state.Visible && state.Result is not null)
                        DrawPanel(state, cfg);
                }
            }

            if (cfg.HoverEnabled && hover.Result is not null)
                DrawHover(cfg);

            if (cfg.DebugWindow)
                DrawDebug();
        }
        finally
        {
            if (fontPushed) font!.Pop();
        }
    }

    private void DrawDialogue(DialogueOverlayState state, Configuration cfg)
    {
        var width = Math.Max(300f, Math.Min(cfg.OverlayWidth, state.Width > 0 ? state.Width : cfg.OverlayWidth));
        var id = "##trk_dialogue_" + state.AddonName;

        // İlk frame'de boyut bilinmediği için bir önceki frame'in boyutuyla konumlandır.
        var prevSize = lastSizes.TryGetValue(id, out var s) ? s : new Vector2(width, 80);
        var pos = new Vector2(state.X, state.Y - prevSize.Y + cfg.OverlayOffsetY);

        ImGui.SetNextWindowPos(pos, ImGuiCond.Always);
        ImGui.SetNextWindowSizeConstraints(new Vector2(width, 0), new Vector2(width, 700));
        ImGui.SetNextWindowBgAlpha(cfg.BackgroundAlpha);

        if (ImGui.Begin(id, OverlayFlags))
        {
            ImGui.PushTextWrapPos(width - ImGui.GetStyle().WindowPadding.X * 2);
            if (!string.IsNullOrWhiteSpace(state.Speaker))
                ImGui.TextColored(SpeakerColor, state.Speaker);

            DrawResult(state.Result!);

            if (cfg.ShowOriginal)
            {
                ImGui.Separator();
                ImGui.TextColored(OriginalColor, state.Original);
            }

            ImGui.PopTextWrapPos();
            lastSizes[id] = ImGui.GetWindowSize();
        }

        ImGui.End();
    }

    private void DrawPanel(PanelState state, Configuration cfg)
    {
        var width = cfg.PanelWidth;
        var id = "##trk_panel_" + state.AddonName;
        var screen = ImGui.GetIO().DisplaySize;

        // Pencerenin sağına; sığmazsa soluna.
        var x = state.X + state.Width + 8f;
        if (x + width > screen.X)
            x = Math.Max(0f, state.X - width - 8f);
        var y = Math.Max(0f, state.Y);

        ImGui.SetNextWindowPos(new Vector2(x, y), ImGuiCond.Always);
        ImGui.SetNextWindowSizeConstraints(new Vector2(width, 0), new Vector2(width, screen.Y - y));
        ImGui.SetNextWindowBgAlpha(cfg.BackgroundAlpha);

        if (ImGui.Begin(id, OverlayFlags))
        {
            ImGui.PushTextWrapPos(width - ImGui.GetStyle().WindowPadding.X * 2);
            DrawResult(state.Result!);
            ImGui.PopTextWrapPos();
        }

        ImGui.End();
    }

    private void DrawHover(Configuration cfg)
    {
        var result = hover.Result!;
        var maxWidth = Math.Min(620f, ImGui.GetIO().DisplaySize.X * 0.45f);

        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 8f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(14f, 10f));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 1f);
        ImGui.PushStyleColor(ImGuiCol.Border, new Vector4(0.85f, 0.75f, 0.45f, 0.6f));
        ImGui.PushStyleColor(ImGuiCol.PopupBg, new Vector4(0.05f, 0.05f, 0.07f, Math.Max(cfg.BackgroundAlpha, 0.92f)));

        ImGui.BeginTooltip();
        ImGui.PushTextWrapPos(maxWidth);
        if (cfg.HoverShowOriginal)
        {
            ImGui.TextColored(OriginalColor, hover.HoveredText);
            ImGui.Separator();
        }

        DrawResult(result);
        ImGui.PopTextWrapPos();
        ImGui.EndTooltip();

        ImGui.PopStyleColor(2);
        ImGui.PopStyleVar(3);
    }

    private void DrawDebug()
    {
        ImGui.SetNextWindowSize(new Vector2(520, 0), ImGuiCond.FirstUseEver);
        if (ImGui.Begin("FFXIV Türkçe — Hover tanı##trk_debug"))
        {
            var io = ImGui.GetIO();
            ImGui.TextUnformatted($"Fare: {io.MousePos.X:0},{io.MousePos.Y:0}   Ekran: {io.DisplaySize.X:0}x{io.DisplaySize.Y:0}");
            ImGui.TextUnformatted($"Yüklü addon: {hover.DebugAddonCount}   görünür: {hover.DebugVisibleAddons}   taranan metin: {hover.DebugTextCount}");
            ImGui.TextWrapped($"Fare altındaki addonlar: {(hover.DebugAddonsUnderMouse.Length > 0 ? hover.DebugAddonsUnderMouse : "-")}");
            if (hover.DebugBest is { } b)
                ImGui.TextWrapped($"Seçilen ({hover.DebugPick}): \"{b.Text}\"  @ {b.X:0},{b.Y:0} {b.W:0}x{b.H:0}");
            else
                ImGui.TextUnformatted("Seçilen: -");
            ImGui.TextUnformatted($"Hover metni: {hover.HoveredText}");
            ImGui.TextUnformatted($"Sonuç: {(hover.Result is null ? "yok" : hover.Result.Status.ToString())}");
            ImGui.Separator();
            ImGui.TextUnformatted($"Yerinde çeviri kare maliyeti: {inPlaceFrameMs():0.00} ms   (16 ms = 1 kare @60fps)");
        }

        ImGui.End();
    }

    private static void DrawResult(TranslationResult result)
    {
        switch (result.Status)
        {
            case TranslationStatus.Pending:
                ImGui.TextColored(PendingColor, "Çevriliyor...");
                break;
            case TranslationStatus.Error:
                ImGui.TextColored(ErrorColor, result.Text);
                break;
            default:
                ImGui.TextWrapped(result.Text);
                break;
        }
    }
}
