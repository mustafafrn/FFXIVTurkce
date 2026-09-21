using System.Text;

namespace FFXIVTurkce.Translation;

public static class PromptBuilder
{
    private const string BaseSystemPrompt =
        """
        Sen Final Fantasy XIV oyunu için profesyonel bir oyun yerelleştirme çevirmenisin.
        Sana verilen İngilizce NPC diyaloğunu doğal, akıcı ve oyunun tonuna uygun Türkçeye çevir.

        Kurallar:
        - SADECE çeviriyi yaz. Açıklama, not, tırnak, başlık ekleme.
        - Karakter isimleri, yer adları, şehir adları, ırk adları (Hyur, Miqo'te, Lalafell...), iş/sınıf adları (Paladin, White Mage...), örgüt adları (Scions of the Seventh Dawn, Garlean Empire...) ve oyun terimleri (aether, Echo, primal, FATE, Duty) İngilizce kalsın; gerekirse Türkçe ek al (Limsa Lominsa'ya, Scions'ların).
        - Satır sonlarını koru.
        - Eski/şiirsel İngilizce (thee, thou, 'tis, pray) kullanan karakterlerde Türkçede de ağırbaşlı, eski bir üslup kullan.
        - Argo veya lehçeli karakterlerde (örneğin korsanlar, Ul'dah tüccarları) doğal Türkçe karşılığı kullan.
        - "You" için bağlama göre "sen" veya "siz" seç; asil/resmi ortamlarda "siz".
        - Büyük harf ve noktalama Türkçe kurallarına uysun.
        """;

    public static string BuildSystemPrompt(string extraGlossary)
    {
        if (string.IsNullOrWhiteSpace(extraGlossary))
            return BaseSystemPrompt;

        var sb = new StringBuilder(BaseSystemPrompt);
        sb.AppendLine();
        sb.AppendLine();
        sb.AppendLine("Ek sözlük (bu karşılıkları tutarlı kullan):");
        foreach (var line in extraGlossary.Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.Length > 0)
                sb.Append("- ").AppendLine(trimmed);
        }

        return sb.ToString();
    }

    public static string BuildUserMessage(string speaker, string text)
    {
        return string.IsNullOrWhiteSpace(speaker)
            ? text
            : $"Konuşan: {speaker}\n\n{text}";
    }
}
