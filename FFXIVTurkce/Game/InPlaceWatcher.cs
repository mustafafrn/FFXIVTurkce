using System;
using System.Collections.Generic;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Component.GUI;
using FFXIVTurkce.Translation;

namespace FFXIVTurkce.Game;

/// <summary>Yerinde çizilecek bir metin: oyunun düğümü gizlendi, aynı yere Türkçesi çizilecek.</summary>
public struct InPlaceEntry
{
    public string Text;
    public float X, Y, W, H;
    public float FontSize;
    public float LineHeight;
    public AlignmentType Alignment;
    public uint TextColor;
    public uint EdgeColor;
    public byte Alpha;
    public float ClipX1, ClipY1, ClipX2, ClipY2;

    /// <summary>Alttaki bir sonraki metne kadar kullanılabilir yükseklik (0 = sınırsız).</summary>
    public float MaxHeight;

    /// <summary>0 = kenar yok, 1 = dış hat (Edge/Glare), 2 = gölge (Emboss).</summary>
    public int EdgeStyle;

    public string Addon;
}

/// <summary>
/// "Yerinde çizim": her addon çizilmeden hemen önce (PreDraw) metin düğümlerini okur,
/// çevirisi hazır olanların alfa değerini 0 yapar ve çizim listesine Türkçesini ekler.
/// </summary>
public sealed unsafe class InPlaceWatcher : IDisposable
{
    private readonly IAddonLifecycle addonLifecycle;
    private readonly IGameGui gameGui;
    private readonly IPluginLog log;
    private readonly Func<Configuration> getConfig;
    private readonly TranslationService translation;

    // Gizlediğimiz düğümler: işaretçi → (addon adı, orijinal alfa)
    private readonly Dictionary<nint, (string Addon, byte Alpha)> hidden = new();

    // Görünürlük bayrağıyla gizlediğimiz düğümler (alfa sıfırlamanın tutmadığı pencereler).
    private readonly HashSet<nint> hiddenByFlag = new();
    private readonly Dictionary<nint, int> rewrites = new();

    // Alfa sıfırlamasını oyunun her karede geri yazdığı pencereler → bayrakla gizle.
    private readonly HashSet<string> stubborn = new(StringComparer.Ordinal) { "_TextError" };

    // Addon adı → (çizim listesi, son çizim zamanı)
    private readonly Dictionary<string, (List<InPlaceEntry> Entries, long Tick)> frames = new();

    public InPlaceWatcher(
        IAddonLifecycle addonLifecycle,
        IGameGui gameGui,
        IPluginLog log,
        Func<Configuration> getConfig,
        TranslationService translation)
    {
        this.addonLifecycle = addonLifecycle;
        this.gameGui = gameGui;
        this.log = log;
        this.getConfig = getConfig;
        this.translation = translation;

        RefreshListeners();
    }

    private readonly HashSet<string> listening = new(StringComparer.Ordinal);

    private bool globalListening;

    /// <summary>
    /// "Tüm pencereler" modunda tek bir genel PreDraw dinleyicisi; liste modunda
    /// bilinen + ek pencereler için ayrı dinleyiciler.
    /// </summary>
    public void RefreshListeners()
    {
        var cfg = getConfig();
        var wanted = new HashSet<string>(StringComparer.Ordinal);

        if (cfg.InPlaceAll)
        {
            if (!globalListening)
            {
                addonLifecycle.RegisterListener(AddonEvent.PreDraw, OnPreDraw);
                globalListening = true;
            }
        }
        else
        {
            if (globalListening)
            {
                addonLifecycle.UnregisterListener(AddonEvent.PreDraw, OnPreDraw);
                globalListening = false;
            }

            foreach (var k in KnownAddons.All) wanted.Add(k.Addon);
            foreach (var extra in cfg.ExtraInPlaceAddons.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                wanted.Add(extra);
        }

        foreach (var name in listening)
        {
            if (!wanted.Contains(name))
                addonLifecycle.UnregisterListener(AddonEvent.PreDraw, name, OnPreDraw);
        }

        foreach (var name in wanted)
        {
            if (listening.Add(name))
                addonLifecycle.RegisterListener(AddonEvent.PreDraw, name, OnPreDraw);
        }

        listening.RemoveWhere(n => !wanted.Contains(n));
    }

    /// <summary>Renderer için: son 100 ms içinde çizilmiş addonların girdileri.</summary>
    public IEnumerable<InPlaceEntry> CurrentEntries()
    {
        var now = Environment.TickCount64;
        foreach (var kv in frames)
        {
            if (now - kv.Value.Tick > 100) continue;
            foreach (var e in kv.Value.Entries) yield return e;
        }
    }

    // Handles() her addon için her kare çağrılır; string bölme/eşleştirme yapmamak için sonuç önbelleği.
    private readonly Dictionary<string, bool> handlesCache = new(StringComparer.Ordinal);
    private bool hcAll;
    private string? hcExcluded;
    private string? hcExtra;
    private int hcListCount;

    public bool Handles(string addonName)
    {
        var cfg = getConfig();
        if (!cfg.InPlaceEnabled) return false;

        // Ayar değiştiyse önbelleği boşalt (string'ler değişmedikçe aynı referans kalır; ek yük yok).
        if (hcAll != cfg.InPlaceAll || !ReferenceEquals(hcExcluded, cfg.ExcludedInPlaceAddons)
            || !ReferenceEquals(hcExtra, cfg.ExtraInPlaceAddons) || hcListCount != cfg.InPlaceAddons.Count)
        {
            handlesCache.Clear();
            hcAll = cfg.InPlaceAll;
            hcExcluded = cfg.ExcludedInPlaceAddons;
            hcExtra = cfg.ExtraInPlaceAddons;
            hcListCount = cfg.InPlaceAddons.Count;
        }

        if (handlesCache.TryGetValue(addonName, out var cached)) return cached;

        bool result;
        if (cfg.InPlaceAll)
            result = !KnownAddons.IsExcludedByDefault(addonName) && !MatchesList(cfg.ExcludedInPlaceAddons, addonName);
        else
            result = cfg.InPlaceAddons.Contains(addonName) || MatchesList(cfg.ExtraInPlaceAddons, addonName);

        handlesCache[addonName] = result;
        return result;
    }

    // Düğüm başına çeviri durumu: aynı metin için her kare yeniden sorgu + anahtar üretmeyi önler.
    private sealed class NodeState
    {
        public string Text = string.Empty;
        public TranslationResult? Result;
        public long LastErrorCheck;
    }

    private readonly Dictionary<nint, NodeState> nodeStates = new();

    // Addon adı → gizlediğimiz düğümler (stale taraması için, tüm sözlüğü gezmemek adına).
    private readonly Dictionary<string, HashSet<nint>> hiddenByAddon = new(StringComparer.Ordinal);

    /// <summary>Tanı: son karede PreDraw işlemlerinin toplam süresi (ms).</summary>
    public double LastFrameMs { get; private set; }
    private double frameAccumMs;
    private long frameStamp;
    private readonly System.Diagnostics.Stopwatch sw = new();

    private TranslationResult GetResult(nint ptr, string text)
    {
        if (!nodeStates.TryGetValue(ptr, out var st))
        {
            st = new NodeState();
            nodeStates[ptr] = st;
        }

        // Ayrıştırma önbelleği metin değişmediyse aynı string örneğini döndürür → referans karşılaştırması yeter.
        if (st.Result is not null && ReferenceEquals(st.Text, text))
        {
            if (st.Result.Status != TranslationStatus.Error) return st.Result;
            var now = Environment.TickCount64;
            if (now - st.LastErrorCheck < 5000) return st.Result;
            st.LastErrorCheck = now;
        }

        st.Text = text;
        st.Result = translation.Request(string.Empty, text);
        return st.Result;
    }

    private static bool MatchesList(string list, string addonName)
    {
        if (list.Length == 0) return false;
        foreach (var item in list.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (item.EndsWith('*'))
            {
                if (addonName.StartsWith(item.AsSpan(0, item.Length - 1), StringComparison.Ordinal)) return true;
            }
            else if (string.Equals(item, addonName, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private void OnPreDraw(AddonEvent type, AddonArgs args)
    {
        var name = args.AddonName;
        var addon = (AtkUnitBase*)args.Addon.Address;
        if (addon == null) return;

        var cfg = getConfig();
        if (!cfg.Enabled || !Handles(name))
        {
            RestoreAddon(name, addon);
            return;
        }

        try
        {
            Process(name, addon, cfg);
        }
        catch (Exception e)
        {
            log.Error(e, $"{name} yerinde çeviri hatası");
        }
    }

    private void Process(string name, AtkUnitBase* addon, Configuration cfg)
    {
        var stamp = Environment.TickCount64;
        if (stamp != frameStamp)
        {
            LastFrameMs = frameAccumMs;
            frameAccumMs = 0;
            frameStamp = stamp;
        }

        sw.Restart();
        try
        {
            ProcessInner(name, addon, cfg);
        }
        finally
        {
            frameAccumMs += sw.Elapsed.TotalMilliseconds;
        }
    }

    private void ProcessInner(string name, AtkUnitBase* addon, Configuration cfg)
    {
        if (!frames.TryGetValue(name, out var frame))
            frame = (new List<InPlaceEntry>(), 0);
        frame.Entries.Clear();

        if (!addon->IsVisible || addon->RootNode == null)
        {
            frames[name] = (frame.Entries, 0);
            return;
        }

        UiTextScanner.TryGetRootBounds(addon, out var root);
        var seen = new HashSet<nint>();
        var infos = UiTextScanner.CollectNodes(addon, hiddenByFlag);
        var useFlag = stubborn.Contains(name);

        foreach (var info in infos)
        {
            var node = info.Node;
            var ptr = (nint)node;
            seen.Add(ptr);

            if (KnownAddons.IsNameNode(name, node->NodeId))
            {
                Restore(ptr, node);
                continue;
            }

            var text = info.Rect.Text;
            var result = GetResult(ptr, text);
            if (result.Status != TranslationStatus.Done)
            {
                // Çeviri gelene kadar İngilizcesi görünsün.
                Restore(ptr, node);
                continue;
            }

            // Oyun bazı yazıları solarak gösterir (bölge adı vb.): alfa her karede değişir.
            // O anki değeri oku; 0 ise (henüz gizlediğimizden) son bilinen değeri kullan.
            var currentAlpha = node->Color.A;
            if (!hidden.TryGetValue(ptr, out var h))
            {
                h = Claim(ptr, name, currentAlpha);

                if (cfg.DebugWindow)
                {
                    var snippet = text.Length > 40 ? text[..40] + "…" : text;
                    log.Information(
                        $"[renk] {name} node={node->NodeId} text=({node->TextColor.R},{node->TextColor.G},{node->TextColor.B},{node->TextColor.A}) " +
                        $"edge=({node->EdgeColor.R},{node->EdgeColor.G},{node->EdgeColor.B},{node->EdgeColor.A}) " +
                        $"bg=({node->BackgroundColor.R},{node->BackgroundColor.G},{node->BackgroundColor.B},{node->BackgroundColor.A}) " +
                        $"add=({node->AddRed},{node->AddGreen},{node->AddBlue}) mul=({node->MultiplyRed},{node->MultiplyGreen},{node->MultiplyBlue}) " +
                        $"nodeColor=({node->Color.R},{node->Color.G},{node->Color.B},{node->Color.A}) flags={node->TextFlags} font={node->FontType}/{node->FontSize} " +
                        $"align={node->AlignmentType} rect=({info.Rect.X:0},{info.Rect.Y:0},{info.Rect.W:0}x{info.Rect.H:0}) root=({root.X:0},{root.Y:0},{root.W:0}x{root.H:0}) \"{snippet}\"");
                }
            }

            else if (h.Addon != name)
            {
                // Aynı düğüm başka bir pencerenin ağacında da görünüyor (iç içe pencereler): sahipliği devral.
                h = Claim(ptr, name, h.Alpha);
            }
            else if (currentAlpha != 0)
            {
                // Geçen kare sıfırlamıştık, oyun geri yazmış (solma animasyonu ya da her kare SetAlpha).
                // Değeri kendi solmamız için sakla; art arda iki kez olursa bu pencerede alfa gizleme
                // tutmuyor demektir → görünürlük bayrağıyla gizle.
                h = Claim(ptr, name, currentAlpha);
                rewrites[ptr] = rewrites.TryGetValue(ptr, out var n) ? n + 1 : 1;
                if (!useFlag && rewrites[ptr] >= 2)
                {
                    stubborn.Add(name);
                    useFlag = true;
                    log.Information($"{name}: alfa gizleme tutmuyor, görünürlük bayrağına geçildi.");
                }
            }

            node->Color.A = 0;
            if (useFlag)
            {
                node->NodeFlags &= ~NodeFlags.Visible;
                hiddenByFlag.Add(ptr);
            }

            ResolveOriginalColors(node, KnownAddons.LightBackground.Contains(name), out var origText, out var origEdge, out var origEdgeStyle);

            var clipToRoot = root.W > 0 && root.H > 0
                             && info.Rect.X >= root.X - 2 && info.Rect.Y >= root.Y - 2
                             && info.Rect.X + info.Rect.W <= root.X + root.W + 2;

            var scale = info.Scale;
            var fontSize = Math.Max(8f, node->FontSize * scale * cfg.InPlaceFontScale);
            var lineHeight = node->LineSpacing > 0 ? node->LineSpacing * scale : fontSize * 1.3f;

            frame.Entries.Add(new InPlaceEntry
            {
                Addon = name,
                Text = result.Text,
                X = info.Rect.X,
                Y = info.Rect.Y,
                W = info.Rect.W,
                H = info.Rect.H,
                FontSize = fontSize,
                LineHeight = Math.Max(lineHeight, fontSize * 1.15f),
                Alignment = node->AlignmentType,
                TextColor = cfg.InPlaceColor switch
                {
                    InPlaceColorMode.White => 0xFFFFFFFF,
                    InPlaceColorMode.Black => 0xFF000000,
                    _ => origText,
                },
                EdgeColor = !cfg.InPlaceOutline ? 0u : cfg.InPlaceColor switch
                {
                    InPlaceColorMode.White => 0xE6000000,
                    InPlaceColorMode.Black => 0x99FFFFFF,
                    _ => origEdge,
                },
                EdgeStyle = !cfg.InPlaceOutline ? 0 : cfg.InPlaceColor == InPlaceColorMode.Original ? origEdgeStyle : 1,
                // Bazı toast'larda (_WideText, _AreaText) düğümün kendi alfası hep 0 okunur;
                // oyun solmayı üst düğümden yapar. 0'ı "tam görünür" say, yoksa yazı hiç çıkmaz.
                Alpha = h.Alpha == 0 ? (byte)255 : h.Alpha,
                // Kök düğüm metni kapsıyorsa ona kırp (kaydırmalı pencereler); kapsamıyorsa
                // (bölge afişi gibi kök düğümü küçük olan pencereler) kırpma.
                ClipX1 = clipToRoot ? root.X : 0,
                ClipY1 = clipToRoot ? root.Y : 0,
                ClipX2 = clipToRoot ? root.X + root.W : float.MaxValue,
                ClipY2 = clipToRoot ? root.Y + root.H : float.MaxValue,
            });
        }

        // Türkçe metin İngilizceden uzun olabilir; alttaki metnin üstüne binmesin diye
        // her girdi için bir sonraki metne kadar olan boşluğu hesapla.
        for (var i = 0; i < frame.Entries.Count; i++)
        {
            var e = frame.Entries[i];
            var limit = float.MaxValue;
            foreach (var other in infos)
            {
                var r = other.Rect;
                if (r.Y <= e.Y + 4f) continue;
                if (r.X >= e.X + e.W || r.X + r.W <= e.X) continue;
                limit = Math.Min(limit, r.Y - e.Y - 2f);
            }

            e.MaxHeight = limit == float.MaxValue ? 0f : Math.Max(limit, e.FontSize);
            frame.Entries[i] = e;
        }

        // Bu addon'da daha önce gizlediğimiz ama artık görünmeyen/çevrilmeyen düğümleri geri al.
        List<nint>? stale = null;
        if (hiddenByAddon.TryGetValue(name, out var mine))
        {
            foreach (var ptr in mine)
            {
                if (seen.Contains(ptr)) continue;
                stale ??= new List<nint>();
                stale.Add(ptr);
            }
        }

        if (stale is not null)
        {
            foreach (var ptr in stale)
            {
                if (!hidden.TryGetValue(ptr, out var h) || h.Addon != name)
                {
                    // Sahipliği başka pencere aldı ya da kayıt zaten silinmiş: sadece bu kümeden düş.
                    mine!.Remove(ptr);
                    continue;
                }

                var node = (AtkResNode*)ptr;
                if (UiTextScanner.ContainsNode(addon, node))
                    RestoreNode(node, ptr, h.Alpha);
                else
                    hiddenByFlag.Remove(ptr);
                Forget(ptr, name);
                nodeStates.Remove(ptr);
            }
        }

        frames[name] = (frame.Entries, Environment.TickCount64);

        if (cfg.DebugWindow && frame.Entries.Count > 0 && Environment.TickCount64 - lastDebugLog > 1000)
        {
            lastDebugLog = Environment.TickCount64;
            var e0 = frame.Entries[0];
            log.Information($"[çizim] {name}: {frame.Entries.Count} yazı; ilk: \"{(e0.Text.Length > 30 ? e0.Text[..30] : e0.Text)}\" @ ({e0.X:0},{e0.Y:0}) {e0.W:0}x{e0.H:0} font={e0.FontSize:0} alpha={e0.Alpha} clip=({e0.ClipX1:0},{e0.ClipY1:0})-({e0.ClipX2:0},{e0.ClipY2:0})");
        }
    }

    private long lastDebugLog;

    private void Restore(nint ptr, AtkTextNode* node)
    {
        if (!hidden.TryGetValue(ptr, out var h)) return;
        RestoreNode((AtkResNode*)node, ptr, h.Alpha);
        Forget(ptr, h.Addon);
    }

    /// <summary>Düğümü bu pencerenin gizlediği kayıtlara yazar; başka pencereye kayıtlıysa oradan taşır.</summary>
    private (string Addon, byte Alpha) Claim(nint ptr, string name, byte alpha)
    {
        if (hidden.TryGetValue(ptr, out var old) && old.Addon != name
            && hiddenByAddon.TryGetValue(old.Addon, out var oldSet))
            oldSet.Remove(ptr);

        var entry = (name, alpha);
        hidden[ptr] = entry;
        if (!hiddenByAddon.TryGetValue(name, out var set)) hiddenByAddon[name] = set = new HashSet<nint>();
        set.Add(ptr);
        return entry;
    }

    private void Forget(nint ptr, string addon)
    {
        hidden.Remove(ptr);
        if (hiddenByAddon.TryGetValue(addon, out var set)) set.Remove(ptr);
    }

    private void RestoreNode(AtkResNode* node, nint ptr, byte alpha)
    {
        node->Color.A = alpha;
        rewrites.Remove(ptr);
        if (hiddenByFlag.Remove(ptr))
            node->NodeFlags |= NodeFlags.Visible;
    }

    private void RestoreAddon(string name, AtkUnitBase* addon)
    {
        if (!hiddenByAddon.TryGetValue(name, out var set) || set.Count == 0)
        {
            frames.Remove(name);
            return;
        }

        var list = new List<nint>(set);
        foreach (var ptr in list)
        {
            if (!hidden.TryGetValue(ptr, out var h) || h.Addon != name)
            {
                set.Remove(ptr);
                continue;
            }

            var node = (AtkResNode*)ptr;
            if (UiTextScanner.ContainsNode(addon, node))
                RestoreNode(node, ptr, h.Alpha);
            else
                hiddenByFlag.Remove(ptr);
            Forget(ptr, name);
            nodeStates.Remove(ptr);
        }

        frames.Remove(name);
    }

    /// <summary>
    /// Oyunun gerçekte çizdiği renk: TextColor + düğümün Add/Multiply tonlaması.
    /// Kenar rengi alanı hep dolu gelir ama oyun onu yalnızca TextFlags'te Edge/Glare (dış hat)
    /// ya da Emboss (gölge) varsa çizer; aksi halde düz metindir (örn. parşömen zeminli görev günlüğü).
    /// </summary>
    private static void ResolveOriginalColors(AtkTextNode* node, bool lightBackground, out uint text, out uint edge, out int edgeStyle)
    {
        var tr = Tint(node->TextColor.R, node->MultiplyRed, node->AddRed);
        var tg = Tint(node->TextColor.G, node->MultiplyGreen, node->AddGreen);
        var tb = Tint(node->TextColor.B, node->MultiplyBlue, node->AddBlue);
        var ta = node->TextColor.A == 0 ? (byte)255 : node->TextColor.A;

        var flags = node->TextFlags;
        edgeStyle = 0;
        if ((flags & (TextFlags.Edge | TextFlags.Glare)) != 0) edgeStyle = 1;
        else if ((flags & TextFlags.Emboss) != 0) edgeStyle = 2;

        var er = node->EdgeColor.R;
        var eg = node->EdgeColor.G;
        var eb = node->EdgeColor.B;
        var ea = node->EdgeColor.A == 0 ? (byte)255 : node->EdgeColor.A;

        // Koyu zeminli pencerede koyu yazı: oyun bunu başka bir yoldan açık gösteriyor (görev listesi başlıkları vb.).
        // Biz düz beyaz + siyah kenarla çiziyoruz ki görünür kalsın.
        if (!lightBackground && Luma(tr, tg, tb) < 0.25f)
        {
            (tr, tg, tb) = (255, 255, 255);
            (er, eg, eb, ea) = (0, 0, 0, 230);
            edgeStyle = 1;
        }

        text = Pack(tr, tg, tb, ta);
        edge = edgeStyle == 0 ? 0u : Pack(er, eg, eb, ea);
    }

    private static float Luma(byte r, byte g, byte b) => (0.299f * r + 0.587f * g + 0.114f * b) / 255f;

    private static byte Tint(byte value, byte multiply, short add)
    {
        var m = multiply == 0 ? 100 : multiply;
        return (byte)Math.Clamp(value * m / 100 + add, 0, 255);
    }

    private static uint Pack(byte r, byte g, byte b, byte a) =>
        (uint)r | ((uint)g << 8) | ((uint)b << 16) | ((uint)a << 24);

    public void Dispose()
    {
        addonLifecycle.UnregisterListener(OnPreDraw);
        globalListening = false;

        // Gizlediğimiz her düğümü, addon hâlâ yüklüyse ve düğüm hâlâ ağacındaysa geri al.
        foreach (var kv in hidden)
        {
            try
            {
                var addon = (AtkUnitBase*)gameGui.GetAddonByName(kv.Value.Addon).Address;
                var node = (AtkResNode*)kv.Key;
                if (addon != null && UiTextScanner.ContainsNode(addon, node))
                    RestoreNode(node, kv.Key, kv.Value.Alpha);
            }
            catch (Exception e)
            {
                log.Warning($"Düğüm geri alınamadı ({kv.Value.Addon}): {e.Message}");
            }
        }

        hidden.Clear();
        hiddenByAddon.Clear();
        hiddenByFlag.Clear();
        rewrites.Clear();
        nodeStates.Clear();
        frames.Clear();
    }
}
