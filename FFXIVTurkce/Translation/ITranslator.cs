using System.Threading;
using System.Threading.Tasks;

namespace FFXIVTurkce.Translation;

public interface ITranslator
{
    string Name { get; }

    /// <summary>İngilizce metni Türkçeye çevirir. Hata durumunda exception fırlatır.</summary>
    Task<string> TranslateAsync(string speaker, string text, CancellationToken ct);
}
