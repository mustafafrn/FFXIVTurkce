using System;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Anthropic;
using Anthropic.Models.Messages;

namespace FFXIVTurkce.Translation;

/// <summary>Anthropic Claude (resmi C# SDK) ile çeviri. API key: https://console.anthropic.com</summary>
public sealed class ClaudeTranslator : ITranslator
{
    private readonly Func<Configuration> getConfig;
    private AnthropicClient? client;
    private string clientKey = string.Empty;

    public ClaudeTranslator(Func<Configuration> getConfig)
    {
        this.getConfig = getConfig;
    }

    public string Name => "Claude";

    public async Task<string> TranslateAsync(string speaker, string text, CancellationToken ct)
    {
        var cfg = getConfig();
        var key = cfg.ClaudeApiKey.Trim();
        if (string.IsNullOrWhiteSpace(key))
            throw new InvalidOperationException("Claude API anahtarı girilmemiş (/trk ayarlar).");

        if (client is null || clientKey != key)
        {
            client = new AnthropicClient { ApiKey = key };
            clientKey = key;
        }

        var model = string.IsNullOrWhiteSpace(cfg.ClaudeModel) ? "claude-opus-5" : cfg.ClaudeModel.Trim();

        var response = await client.Messages.Create(new MessageCreateParams
        {
            Model = model,
            MaxTokens = 2048,
            System = PromptBuilder.BuildSystemPrompt(cfg.ExtraGlossary),
            // Kısa diyalog çevirisi: düşük effort hem hızlı hem ucuz, kalite yeterli.
            OutputConfig = new OutputConfig { Effort = Effort.Low },
            Messages =
            [
                new() { Role = Role.User, Content = PromptBuilder.BuildUserMessage(speaker, text) },
            ],
        }, cancellationToken: ct).ConfigureAwait(false);

        var sb = new StringBuilder();
        foreach (var block in response.Content.Select(b => b.Value).OfType<TextBlock>())
            sb.Append(block.Text);

        var result = sb.ToString().Trim();
        if (result.Length == 0)
            throw new InvalidOperationException($"Claude boş yanıt döndü (stop_reason={response.StopReason}).");

        return result;
    }
}
