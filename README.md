# FFXIV Türkçe

**FINAL FANTASY XIV** için gerçek zamanlı Türkçe çeviri eklentisi (Dalamud plugin).

Oyunun diyaloglarını, görev günlüğünü, menülerini ve arayüzdeki hemen her yazıyı **oyunun kendi kutusunun içinde, kendi renk ve hizasıyla** Türkçe gösterir. Ayrı bir pencere ya da baloncuk yoktur; sanki oyun Türkçe çıkmış gibi görünür.

> Resmi bir çeviri değildir. Metinler makine çevirisiyle (Google Translate) anlık çevrilir ve önbelleğe alınır. Hikâyeyi rahatça takip etmeye yeter; edebi kalite beklemeyin.

---

## Özellikler

- **Yerinde çeviri** – NPC diyalogları, ara sahneler, alt yazılar, konuşma balonları, görev günlüğü, görev takipçisi, seçim menüleri, Evet/Hayır kutuları, bildirimler, bölge adları, hata mesajları, ana menü, ayarlar… açtığınız her pencere.
- **Oyunun görünümü korunur** – Yazı rengi, kenar çizgisi, hizalama ve boyut oyundan alınır. Türkçe metin İngilizceden uzun gelirse sığacak şekilde küçülür, alttaki yazının üstüne binmez.
- **Türkçe karakter desteği** – Oyunun fontunda `ş ğ ı İ` yoktur; eklenti Windows'taki Segoe UI / Verdana / Tahoma gibi fontları kullanır. Font ayarlardan seçilebilir.
- **Kalıcı çeviri veritabanı** – Her çeviri diske yazılır; aynı cümle bir daha çevrilmez, oyunu kapatıp açınca kalır.
- **Görev ön-çevirisi** – Kabul ettiğiniz görevin tüm metinleri oyun verisinden okunup arka planda önceden çevrilir; NPC'ye vardığınızda çeviri hazırdır.
- **Hover çevirisi** – Çevrilmeyen (hariç tutulan) yerlerde fareyi bir yazının üstünde tutunca Türkçesi baloncukta çıkar.
- **API anahtarı gerekmez** – Varsayılan motor Google Translate'in ücretsiz uç noktasıdır. İsteğe bağlı olarak Gemini veya Claude API anahtarı girilebilir (daha doğal çeviri).

## Gereksinimler

- Windows 10/11
- FINAL FANTASY XIV (Steam veya normal sürüm)
- [XIVLauncher](https://goatcorp.github.io/) ve Dalamud (XIVLauncher ile birlikte gelir, API seviyesi 15)

## Kurulum

### 1. XIVLauncher

Oyunu XIVLauncher ile başlatın. İlk başlatmada Dalamud otomatik kurulur. Oyunda `/xlplugins` yazınca eklenti penceresi açılıyorsa hazırsınız.

### 2. Eklenti deposunu ekleyin

1. Oyunda `/xlplugins` → sağ üstteki **dişli** ikonu → **Experimental** sekmesi
2. **Custom Plugin Repositories** kısmına şu adresi yapıştırın ve **+** ile ekleyin:

   ```
   https://raw.githubusercontent.com/mustafafrn/FFXIVTurkce/main/repo.json
   ```

3. **Save and Close**
4. Eklenti listesinde **FFXIV Türkçe** aratın → **Install**

### 3. Kullanım

Eklenti kurulur kurulmaz çalışır; ayar gerekmez. Bir NPC ile konuşun, diyalog Türkçe gelmelidir. İlk karşılaştığınız metinler 1–2 saniye İngilizce kalıp Türkçeye döner; sonraki karşılaşmalarda anındadır.

| Komut | İşlev |
|---|---|
| `/trk` | Ayar penceresi |
| `/trk toggle` | Çeviriyi aç / kapat |
| `/trk debug` | Tanı penceresi (sorun bildirirken) |

## Ayarlar (`/trk`)

- **Çeviri motoru** – Google (varsayılan, anahtar gerekmez) / Gemini / Claude
- **Yerinde çeviri** – Tüm pencereler ya da sadece seçtikleriniz; sorun çıkaran pencereleri hariç tutabilirsiniz
- **Yazı rengi** – Oyunun rengi (önerilen) / Beyaz / Siyah
- **Font** – Kurulu Windows fontlarından seçim; yazı boyutu çarpanı
- **Görev ön-çevirisi** – Açık/kapalı ve istek temposu
- **Hover çevirisi** – Baloncuk gecikmesi, sadece Shift basılıyken seçeneği
- **Ek sözlük** – Gemini/Claude kullanırken tutarlı kalmasını istediğiniz terimler (satır başına `İngilizce = Türkçe`)

### Hariç tutulan pencereler

Şu yerler kasıtlı olarak çevrilmez, çünkü makine çevirisi burada zarar verir: action bar'lar, parti/düşman listesi, hedef bilgisi, sohbet, isim etiketleri, arkadaş/FC listeleri, envanter, market, retainer, craft/gather pencereleri, eşya ve yetenek tooltip'leri, metin yazdığınız kutular. Bu yerlerde **hover çevirisi** çalışır: fareyi yazının üstünde tutun.

## Çeviri kalitesi

- **Google (varsayılan):** Ücretsiz, hızlı, anahtar gerekmez. Özel isimleri de çevirebilir ("Scions" → "Filizler" gibi). Çok yoğun kullanımda Google IP'nizi geçici olarak sınırlayabilir (429); eklenti otomatik yavaşlar ve kaldığı yerden devam eder.
- **Gemini:** [aistudio.google.com](https://aistudio.google.com) üzerinden ücretsiz anahtar. Bağlama uygun, isimleri koruyan, oyun terimlerini tanıyan çeviri. Görev ön-çevirisi açıkken her görev için 50–150 istek atar.
- **Claude:** [console.anthropic.com](https://console.anthropic.com) üzerinden ücretli anahtar. En doğal Türkçe.

API anahtarları yalnızca bilgisayarınızda, Dalamud'un eklenti ayar dosyasında saklanır; bu depoda ya da eklenti paketinde anahtar yoktur.

## Sık sorulanlar

**Türkçe harfler kutu/soru işareti çıkıyor.**
Ayarlarda font listesinden başka bir font seçin (Segoe UI Semibold, Verdana, Tahoma). "Kullanılan:" satırında bir dosya yolu görünmeli.

**Bir pencere bozuk görünüyor / yazılar üst üste biniyor.**
`/trk debug` ile tanı penceresini açın, fareyi o pencerenin üstüne götürün, "Fare altındaki addonlar" satırındaki adı `/trk` → **Hariç tut** kutusuna yazın. Sorunu bir issue olarak bildirirseniz düzeltilir.

**Oyun takılıyor.**
`/trk debug` penceresindeki "kare maliyeti" değerine bakın; 1 ms'nin altında olmalı. Echoglossian gibi başka bir çeviri eklentisi açıksa kapatın.

**Yamadan sonra eklenti yüklenmiyor.**
Her büyük FFXIV yamasında Dalamud birkaç gün çalışmaz; güncellemeyi bekleyin. Dalamud API seviyesi değişirse eklentinin de güncellenmesi gerekir.

## Kaynaktan derleme

```bash
git clone https://github.com/mustafafrn/FFXIVTurkce.git
cd FFXIVTurkce/FFXIVTurkce
dotnet build -c Release
```

- .NET 10 SDK ve kurulu bir XIVLauncher/Dalamud gerekir (SDK, Dalamud DLL'lerini `%AppData%\XIVLauncher\addon\Hooks\dev\` altından bulur).
- Çıktı: `bin/Release/FFXIVTurkce/latest.zip` (dağıtım paketi) ve `bin/Release/FFXIVTurkce.dll` (dev plugin olarak doğrudan yüklenebilir: `/xlplugins` → Settings → Experimental → Dev Plugin Locations).

## Nasıl çalışıyor (kısaca)

Eklenti, her oyun penceresi çizilmeden hemen önce (`AddonLifecycle.PreDraw`) penceredeki metin düğümlerini okur. Çevirisi hazır olan düğümün alfa değerini sıfırlar (oyun bunu geri yazıyorsa görünürlük bayrağını kapatır) ve aynı dikdörtgene, düğümün yazı boyutu / rengi / kenar bayrakları / hizasıyla Türkçesini ImGui üzerinden çizer. Oyun dosyalarına dokunulmaz; eklenti kapatılınca her şey eski hâline döner.

## Sorumluluk reddi

Square Enix üçüncü parti araçları resmî olarak yasaklar. Dalamud ve çeviri eklentileri için ban verildiği görülmemiştir ama risk size aittir. Eklentiden oyun içinde bahsetmeyin.

FINAL FANTASY XIV © SQUARE ENIX CO., LTD. Bu proje Square Enix ile ilişkili değildir.

## Lisans

MIT — bkz. [LICENSE](LICENSE).
