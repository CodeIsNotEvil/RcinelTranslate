using System.Text.Json;
using Anthropic;
using Anthropic.Exceptions;
using Anthropic.Models.Beta.Messages;

namespace Rcinel.Translate.Ai;

/// <summary>Something translates batches. The editor's tests swap in a fake; the real one is <see cref="ClaudeTranslator"/>.</summary>
public interface ITranslator
{
  /// <summary>False when there is no API key; the UI then explains how to add one instead of failing on click.</summary>
  bool IsConfigured { get; }

  /// <summary>Returns id to translated text for the strings of <paramref name="batch"/>.</summary>
  /// <exception cref="TranslationException">The request failed; the message is fit to show a user.</exception>
  Task<IReadOnlyDictionary<string, string>> TranslateAsync(DraftContext context, DraftBatch batch, CancellationToken cancellationToken);
}

public sealed class TranslationException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>
/// Drafts through the Claude API with structured output, so the reply is always the JSON the
/// schema in <see cref="Prompts"/> asks for.
/// </summary>
public sealed class ClaudeTranslator : ITranslator
{
  private readonly AnthropicClient? client;

  /// <param name="apiKey">Defaults to the ANTHROPIC_API_KEY environment variable.</param>
  public ClaudeTranslator(string? apiKey = null)
  {
    apiKey ??= Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
    if (!string.IsNullOrWhiteSpace(apiKey))
    {
      client = new AnthropicClient { ApiKey = apiKey };
    }
  }

  /// <summary>For tests: sends through <paramref name="httpClient"/>, so a fake handler can answer.</summary>
  public ClaudeTranslator(string apiKey, HttpClient httpClient)
  {
    client = new AnthropicClient { ApiKey = apiKey, HttpClient = httpClient, MaxRetries = 0 };
  }

  public bool IsConfigured => client is not null;

  public async Task<IReadOnlyDictionary<string, string>> TranslateAsync(DraftContext context, DraftBatch batch, CancellationToken cancellationToken)
  {
    if (client is null)
    {
      throw new TranslationException("No API key: set the ANTHROPIC_API_KEY environment variable for the editor or the CLI.");
    }

    List<BetaContentBlockParam> content = [];
    if (batch.Screenshot is { } screenshot)
    {
      content.Add(new BetaImageBlockParam
      {
        Source = new BetaBase64ImageSource
        {
          Data = Convert.ToBase64String(screenshot.Image),
          MediaType = screenshot.MediaType switch
          {
            "image/jpeg" => MediaType.ImageJpeg,
            "image/webp" => MediaType.ImageWebP,
            "image/gif" => MediaType.ImageGif,
            _ => MediaType.ImagePng,
          },
        },
      });
    }
    content.Add(new BetaTextBlockParam { Text = Prompts.User(context, batch) });

    MessageCreateParams parameters = new()
    {
      Model = context.Ai.Model,
      // Translations are short, but a batch of 40 with thinking needs room; hitting the cap would
      // cut the JSON off.
      MaxTokens = 16000,
      // The system prompt is the same for every batch of a run: cache it.
      System = new List<BetaTextBlockParam> { new() { Text = Prompts.System(context), CacheControl = new BetaCacheControlEphemeral() } },
      OutputConfig = new BetaOutputConfig
      {
        Effort = ParseEffort(context.Ai.Effort),
        Format = new BetaJsonOutputFormat { Schema = Prompts.Schema },
      },
      // When the model declines a request for policy reasons (a UI string that looks like
      // something it shouldn't write), the API retries on a fallback model instead of failing the batch.
      Betas = ["server-side-fallback-2026-07-01"],
      Fallbacks = new Default(),
      Messages = [new BetaUserMessageParam { Content = content }],
    };

    BetaMessage response;
    try
    {
      response = await client.Beta.Messages.Create(parameters, cancellationToken);
    }
    catch (AnthropicApiException e)
    {
      throw new TranslationException($"The Claude API refused the request: {e.Message}", e);
    }
    catch (HttpRequestException e)
    {
      throw new TranslationException($"The Claude API can't be reached: {e.Message}", e);
    }

    string stop = response.StopReason?.ToString() ?? "";
    if (stop.Contains("refusal", StringComparison.OrdinalIgnoreCase))
    {
      throw new TranslationException("Claude declined to translate this batch. Translate these strings by hand.");
    }
    if (stop.Contains("max_tokens", StringComparison.OrdinalIgnoreCase))
    {
      throw new TranslationException("The reply was cut off. Lower ai.batchSize in rcinel-translate.json.");
    }

    string json = string.Concat(response.Content.Select(b => b.Value).OfType<BetaTextBlock>().Select(t => t.Text));
    return ParseReply(json, batch);
  }

  /// <summary>Reads the structured reply, keeping only ids that were asked for.</summary>
  internal static Dictionary<string, string> ParseReply(string json, DraftBatch batch)
  {
    HashSet<string> asked = [.. batch.Items.Select(i => i.Id)];
    Dictionary<string, string> result = new(StringComparer.Ordinal);
    try
    {
      using JsonDocument document = JsonDocument.Parse(json);
      foreach (JsonElement entry in document.RootElement.GetProperty("translations").EnumerateArray())
      {
        string? id = entry.GetProperty("id").GetString();
        string? text = entry.GetProperty("text").GetString();
        if (id is not null && text is not null && asked.Contains(id))
        {
          result[id] = text;
        }
      }
    }
    catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException)
    {
      throw new TranslationException($"The reply was not the expected JSON: {e.Message}", e);
    }
    return result;
  }

  private static Effort ParseEffort(string effort) => effort.ToLowerInvariant() switch
  {
    "low" => Effort.Low,
    "high" => Effort.High,
    "xhigh" => Effort.Xhigh,
    "max" => Effort.Max,
    _ => Effort.Medium,
  };
}
