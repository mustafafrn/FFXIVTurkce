using System;
using System.IO;
using Dalamud.Game.Command;
using Dalamud;
using Dalamud.Interface;
using Dalamud.Interface.ManagedFontAtlas;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using FFXIVTurkce.Game;
using FFXIVTurkce.Translation;
using FFXIVTurkce.Windows;

namespace FFXIVTurkce;

public sealed class Plugin : IDalamudPlugin
{
    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static IFramework Framework { get; private set; } = null!;
    [PluginService] internal static IGameGui GameGui { get; private set; } = null!;
    [PluginService] internal static IAddonLifecycle AddonLifecycle { get; private set; } = null!;
    [PluginService] internal static IDataManager DataManager { get; private set; } = null!;
    [PluginService] internal static IClientState ClientState { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;

    private const string CommandName = "/trk";

    // Türkçe harfler için gerekli glyph aralıkları: Temel Latin, Latin-1, Latin Extended-A (ğ ı İ ş), Ş/ş virgüllü varyantlar, tırnaklar.
    private static readonly ushort[] TurkishGlyphRanges =
    [
        0x0020, 0x00FF,
        0x0100, 0x017F,
        0x0218, 0x021B,
        0x2010, 0x2027,
        0x2030, 0x203A,
        0,
    ];

    public Configuration Configuration { get; private set; }
    public TranslationService Translation { get; }
    public QuestPrefetcher Prefetcher { get; }
    public IFontHandle? OverlayFont { get; private set; }

    /// <summary>Ayar penceresi için daha küçük, Türkçe destekli font.</summary>
    public IFontHandle? UiFont { get; private set; }

    /// <summary>Yerinde çizim için büyük atlas (küçültülerek çizilir, keskin kalır).</summary>
    public IFontHandle? InPlaceFont { get; private set; }

    private readonly TranslationCache cache;
    private readonly DialogueWatcher watcher;
    private readonly PanelWatcher panels;
    private readonly HoverWatcher hover;
    private readonly InPlaceWatcher inPlace;
    private readonly OverlayRenderer overlay;
    private readonly InPlaceRenderer inPlaceRenderer;
    private readonly WindowSystem windowSystem = new("FFXIVTurkce");
    private readonly ConfigWindow configWindow;

    public Plugin()
    {
        Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        if (Configuration.Version < 2)
        {
            // v1'de paneller açık geliyordu; artık varsayılan hover.
            Configuration.PanelsEnabled = false;
        }

        if (Configuration.Version < 3)
        {
            // Segoe UI aynı piksel boyutunda AXIS'ten küçük görünüyor.
            Configuration.InPlaceFontScale = 1.3f;
        }

        if (Configuration.Version < 4)
        {
            Configuration.InPlaceColor = Configuration.InPlaceForceWhite ? InPlaceColorMode.White : InPlaceColorMode.Original;
        }

        if (Configuration.Version < 5)
        {
            // Yeni eklenen pencereleri mevcut listeye kat.
            foreach (var a in new[] { "_MiniTalk", "_LocationTitle", "_LocationTitleShort", "_AreaText", "_PopUpText" })
                if (!Configuration.InPlaceAddons.Contains(a)) Configuration.InPlaceAddons.Add(a);
        }

        if (Configuration.Version < 6)
        {
            Configuration.InPlaceAll = true;
            Configuration.Version = 6;
            PluginInterface.SavePluginConfig(Configuration);
        }

        cache = new TranslationCache(PluginInterface.GetPluginConfigDirectory(), Log);
        Translation = new TranslationService(() => Configuration, cache, Log);
        Prefetcher = new QuestPrefetcher(Framework, DataManager, ClientState, Log, () => Configuration, Translation);
        inPlace = new InPlaceWatcher(AddonLifecycle, GameGui, Log, () => Configuration, Translation);
        watcher = new DialogueWatcher(Framework, GameGui, Log, () => Configuration, Translation, inPlace.Handles);
        panels = new PanelWatcher(Framework, GameGui, Log, () => Configuration, Translation, inPlace.Handles);
        hover = new HoverWatcher(Framework, Log, () => Configuration, Translation, inPlace.Handles);
        overlay = new OverlayRenderer(watcher, panels, hover, () => Configuration, () => OverlayFont);
        inPlaceRenderer = new InPlaceRenderer(inPlace, () => Configuration, () => InPlaceFont);
        overlay.SetFrameCostSource(() => inPlace.LastFrameMs);

        configWindow = new ConfigWindow(this);
        windowSystem.AddWindow(configWindow);

        RebuildFontNow();

        CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "Ayarları açar. /trk toggle → aç/kapat. /trk debug → hover tanı penceresi.",
        });

        // Dalamud'un otomatik gizlemelerini kapat: ara sahne, gpose, bölge geçişi ve kullanıcı gizlemesi.
        // Oyunun yazısını şeffaf yaptığımız için biz çizmezsek yazı tamamen kaybolur.
        // Kullanıcı UI'ı gizlediğinde overlay/ayar penceresini Draw() içinde kendimiz saklıyoruz.
        PluginInterface.UiBuilder.DisableCutsceneUiHide = true;
        PluginInterface.UiBuilder.DisableGposeUiHide = true;
        PluginInterface.UiBuilder.DisableAutomaticUiHide = true;
        PluginInterface.UiBuilder.DisableUserUiHide = true;

        Framework.Update += OnFrameworkUpdate;

        PluginInterface.UiBuilder.Draw += Draw;
        PluginInterface.UiBuilder.OpenConfigUi += configWindow.Toggle;
        PluginInterface.UiBuilder.OpenMainUi += configWindow.Toggle;

        Log.Information("FFXIV Türkçe yüklendi.");
    }

    public void SaveConfiguration() => PluginInterface.SavePluginConfig(Configuration);

    /// <summary>Kullanılan font dosyası (ayar penceresinde gösterilir).</summary>
    public string ActiveFontPath { get; private set; } = string.Empty;

    private bool fontRebuildPending;

    /// <summary>Fontları bir sonraki karede yeniden yükler (çizim sırasında imha etmek çökertir).</summary>
    public void RebuildFont() => fontRebuildPending = true;

    private void OnFrameworkUpdate(IFramework _)
    {
        if (!fontRebuildPending) return;
        fontRebuildPending = false;
        RebuildFontNow();
    }

    private void RebuildFontNow()
    {
        OverlayFont?.Dispose();
        UiFont?.Dispose();
        InPlaceFont?.Dispose();
        var path = ResolveFontPath();
        ActiveFontPath = path;

        OverlayFont = MakeFont(path, Math.Clamp(Configuration.FontSize, 12f, 48f));
        UiFont = MakeFont(path, 17f);
        InPlaceFont = MakeFont(path, 30f);
    }

    private IFontHandle MakeFont(string path, float size)
    {
        return PluginInterface.UiBuilder.FontAtlas.NewDelegateFontHandle(e =>
            e.OnPreBuild(tk =>
            {
                var cfg = new SafeFontConfig { SizePx = size, GlyphRanges = TurkishGlyphRanges };
                if (path.Length > 0)
                {
                    // Dalamud'un "varsayılan font"u oyunun AXIS fontu; onda ş ğ ı İ yok.
                    // O yüzden tam Türkçe destekli bir TTF yüklüyoruz.
                    tk.AddFontFromFile(path, cfg);
                }
                else
                {
                    tk.AddDalamudAssetFont(DalamudAsset.NotoSansCjkMedium, cfg);
                }
            }));
    }

    private string ResolveFontPath()
    {
        var custom = Configuration.FontPath?.Trim() ?? string.Empty;
        if (custom.Length > 0 && File.Exists(custom)) return custom;

        foreach (var (_, file) in FontChoices)
        {
            var p = Path.Combine(WindowsFontsDir, file);
            if (File.Exists(p)) return p;
        }

        return string.Empty;
    }

    public static readonly string WindowsFontsDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Fonts");

    /// <summary>Ayar penceresindeki font listesi (ad, dosya). İlk bulunan varsayılandır.</summary>
    public static readonly (string Label, string File)[] FontChoices =
    [
        ("Segoe UI Semibold (önerilen)", "seguisb.ttf"),
        ("Segoe UI Bold", "segoeuib.ttf"),
        ("Segoe UI", "segoeui.ttf"),
        ("Verdana", "verdana.ttf"),
        ("Verdana Bold", "verdanab.ttf"),
        ("Tahoma", "tahoma.ttf"),
        ("Tahoma Bold", "tahomabd.ttf"),
        ("Arial", "arial.ttf"),
        ("Arial Bold", "arialbd.ttf"),
        ("Trebuchet MS", "trebuc.ttf"),
        ("Trebuchet MS Bold", "trebucbd.ttf"),
        ("Calibri", "calibri.ttf"),
        ("Calibri Bold", "calibrib.ttf"),
        ("Candara", "Candara.ttf"),
        ("Corbel", "corbel.ttf"),
        ("Bahnschrift", "bahnschrift.ttf"),
        ("Georgia (serif)", "georgia.ttf"),
        ("Cambria Bold (serif)", "cambriab.ttf"),
    ];

    private void Draw()
    {
        // Yerinde çizim her zaman (oyun pencereleri görünüyorsa PreDraw zaten çalışmıştır).
        inPlaceRenderer.Draw();

        // Kullanıcı UI'ı gizlediyse (Scroll Lock) overlay ve ayar penceresini de sakla.
        if (GameGui.GameUiHidden) return;

        windowSystem.Draw();
        overlay.Draw();
    }

    /// <summary>Ayarlardaki ek pencere listesi değişince dinleyicileri yeniler.</summary>
    public void RefreshInPlaceListeners() => inPlace.RefreshListeners();

    private void OnCommand(string command, string args)
    {
        var parts = args.Trim().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            configWindow.Toggle();
            return;
        }

        switch (parts[0].ToLowerInvariant())
        {
            case "nodes":
                watcher.DumpNodes(parts.Length > 1 ? parts[1] : "Talk");
                break;
            case "debug":
                Configuration.DebugWindow = !Configuration.DebugWindow;
                SaveConfiguration();
                break;
            case "toggle":
                Configuration.Enabled = !Configuration.Enabled;
                SaveConfiguration();
                Log.Information($"Çeviri {(Configuration.Enabled ? "açıldı" : "kapatıldı")}.");
                break;
            default:
                configWindow.Toggle();
                break;
        }
    }

    public void Dispose()
    {
        Framework.Update -= OnFrameworkUpdate;
        PluginInterface.UiBuilder.Draw -= Draw;
        PluginInterface.UiBuilder.OpenConfigUi -= configWindow.Toggle;
        PluginInterface.UiBuilder.OpenMainUi -= configWindow.Toggle;
        CommandManager.RemoveHandler(CommandName);

        windowSystem.RemoveAllWindows();
        watcher.Dispose();
        panels.Dispose();
        hover.Dispose();
        inPlace.Dispose();
        Prefetcher.Dispose();
        Translation.Dispose();
        cache.Dispose();
        OverlayFont?.Dispose();
        UiFont?.Dispose();
        InPlaceFont?.Dispose();
    }
}
