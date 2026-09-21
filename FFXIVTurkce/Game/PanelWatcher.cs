using System;
using System.Collections.Generic;
using System.Text;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Component.GUI;
using FFXIVTurkce.Translation;

namespace FFXIVTurkce.Game;

/// <summary>Bir oyun penceresindeki tüm görünür metinlerin toplu çevirisi.</summary>
public sealed class PanelState
{
    public string AddonName = string.Empty;
    public bool Visible;
    public float X;
    public float Y;
    public float Width;
    public float Height;
    public string Block = string.Empty;
    public TranslationResult? Result;
}

/// <summary>Bilinen pencereler (görev günlüğü, seçim menüleri, toast'lar...) için genel çeviri paneli.</summary>
public sealed unsafe class PanelWatcher : IDisposable
{
    /// <summary>Panel olarak çevrilebilecek pencereler (Talk/BattleTalk hariç).</summary>
    public static readonly (string Addon, string Label, bool Default)[] Known =
        System.Array.FindAll(KnownAddons.All, k => k.Addon != "Talk" && k.Addon != "BattleTalk");

    private readonly IFramework framework;
    private readonly IGameGui gameGui;
    private readonly IPluginLog log;
    private readonly Func<Configuration> getConfig;
    private readonly TranslationService translation;
    private readonly Func<string, bool> inPlaceHandles;

    public readonly List<PanelState> States = new();

    public PanelWatcher(
        IFramework framework,
        IGameGui gameGui,
        IPluginLog log,
        Func<Configuration> getConfig,
        TranslationService translation,
        Func<string, bool> inPlaceHandles)
    {
        this.framework = framework;
        this.gameGui = gameGui;
        this.log = log;
        this.getConfig = getConfig;
        this.translation = translation;
        this.inPlaceHandles = inPlaceHandles;

        foreach (var k in Known)
            States.Add(new PanelState { AddonName = k.Addon });

        framework.Update += OnUpdate;
    }

    private void OnUpdate(IFramework _)
    {
        var cfg = getConfig();
        if (!cfg.Enabled || !cfg.PanelsEnabled)
        {
            foreach (var s in States) s.Visible = false;
            return;
        }

        foreach (var state in States)
        {
            if (!cfg.EnabledPanels.Contains(state.AddonName))
            {
                state.Visible = false;
                continue;
            }

            try
            {
                UpdateOne(state);
            }
            catch (Exception e)
            {
                state.Visible = false;
                log.Error(e, $"{state.AddonName} paneli okunurken hata");
            }
        }
    }

    private void UpdateOne(PanelState state)
    {
        if (inPlaceHandles(state.AddonName))
        {
            state.Visible = false;
            return;
        }

        var addon = (AtkUnitBase*)gameGui.GetAddonByName(state.AddonName).Address;
        if (addon == null || !addon->IsVisible || addon->RootNode == null)
        {
            state.Visible = false;
            return;
        }

        var texts = UiTextScanner.Collect(addon);
        if (texts.Count == 0)
        {
            state.Visible = false;
            return;
        }

        // Aynı metni tekrar etme, yukarıdan aşağı sırala.
        texts.Sort((a, b) => a.Y != b.Y ? a.Y.CompareTo(b.Y) : a.X.CompareTo(b.X));
        var seen = new HashSet<string>();
        var sb = new StringBuilder();
        foreach (var t in texts)
        {
            var line = t.Text.Trim();
            if (!seen.Add(line)) continue;
            if (sb.Length > 0) sb.Append('\n');
            sb.Append(line);
        }

        var block = sb.ToString();
        if (block.Length == 0)
        {
            state.Visible = false;
            return;
        }

        state.Visible = true;
        state.X = addon->X;
        state.Y = addon->Y;
        state.Width = addon->RootNode->Width * addon->Scale;
        state.Height = addon->RootNode->Height * addon->Scale;

        if (block != state.Block)
        {
            state.Block = block;
            state.Result = translation.Request(string.Empty, block);
        }
    }

    public void Dispose()
    {
        framework.Update -= OnUpdate;
    }
}
