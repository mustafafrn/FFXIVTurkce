using System;
using System.Collections.Generic;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Component.GUI;
using FFXIVTurkce.Translation;

namespace FFXIVTurkce.Game;

/// <summary>Bir diyalog penceresinin (Talk, BattleTalk) ekrandaki durumu — overlay bunu çizer.</summary>
public sealed class DialogueOverlayState
{
    public string AddonName = string.Empty;
    public bool Visible;
    public float X;
    public float Y;
    public float Width;
    public float Height;
    public float Scale = 1f;
    public string Speaker = string.Empty;
    public string Original = string.Empty;
    public TranslationResult? Result;
}

/// <summary>Her frame Talk / BattleTalk pencerelerini okur, metin değişince çeviri ister.</summary>
public sealed unsafe class DialogueWatcher : IDisposable
{
    // Talk: 2 = konuşan adı, 3 = diyalog metni. BattleTalk: 4 = ad, 6 = metin.
    // Yanlış çıkarsa "/trk nodes Talk" komutuyla doğrula.
    private static readonly (string Addon, uint NameNode, uint TextNode, Func<Configuration, bool> Enabled)[] Targets =
    [
        ("Talk", 2, 3, c => c.TranslateTalk),
        ("BattleTalk", 4, 6, c => c.TranslateBattleTalk),
    ];

    private readonly IFramework framework;
    private readonly IGameGui gameGui;
    private readonly IPluginLog log;
    private readonly Func<Configuration> getConfig;
    private readonly TranslationService translation;
    private readonly Func<string, bool> inPlaceHandles;

    public readonly List<DialogueOverlayState> States = new();

    public DialogueWatcher(
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

        foreach (var t in Targets)
            States.Add(new DialogueOverlayState { AddonName = t.Addon });

        framework.Update += OnUpdate;
    }

    private void OnUpdate(IFramework _)
    {
        var cfg = getConfig();
        translation.Cache.Tick();

        for (var i = 0; i < Targets.Length; i++)
        {
            var target = Targets[i];
            var state = States[i];

            if (!cfg.Enabled || !target.Enabled(cfg) || inPlaceHandles(target.Addon))
            {
                state.Visible = false;
                continue;
            }

            try
            {
                UpdateOne(target.Addon, target.NameNode, target.TextNode, state);
            }
            catch (Exception e)
            {
                state.Visible = false;
                log.Error(e, $"{target.Addon} okunurken hata");
            }
        }
    }

    private void UpdateOne(string addonName, uint nameNode, uint textNode, DialogueOverlayState state)
    {
        var addon = (AtkUnitBase*)gameGui.GetAddonByName(addonName).Address;
        if (addon == null || !addon->IsVisible || addon->RootNode == null)
        {
            state.Visible = false;
            return;
        }

        var text = AddonText.ReadTextNode(addon, textNode);
        if (string.IsNullOrWhiteSpace(text))
        {
            state.Visible = false;
            return;
        }

        var speaker = AddonText.ReadTextNode(addon, nameNode);

        state.Visible = true;
        state.X = addon->X;
        state.Y = addon->Y;
        state.Scale = addon->Scale;
        state.Width = addon->RootNode->Width * addon->Scale;
        state.Height = addon->RootNode->Height * addon->Scale;

        if (text != state.Original || speaker != state.Speaker)
        {
            state.Original = text;
            state.Speaker = speaker;
            state.Result = translation.Request(speaker, text);
        }
    }

    /// <summary>"/trk nodes <addon>" — metin düğümlerini log'a döker.</summary>
    public void DumpNodes(string addonName)
    {
        var addon = (AtkUnitBase*)gameGui.GetAddonByName(addonName).Address;
        if (addon == null)
        {
            log.Information($"{addonName} açık değil.");
            return;
        }

        foreach (var (id, text) in AddonText.DumpTextNodes(addon))
            log.Information($"[{addonName}] node {id}: \"{text}\"");
    }

    public void Dispose()
    {
        framework.Update -= OnUpdate;
    }
}
