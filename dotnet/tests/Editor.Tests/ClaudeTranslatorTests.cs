using System.Net;
using System.Text;
using System.Text.Json;
using CINE.Translate.Ai;
using CINE.Translate.Core;

namespace CINE.Translate.Editor.Tests;

/// <summary>
/// The request the translator sends, checked on the wire with a fake HTTP handler: no API key or
/// network needed, and a change in the SDK that alters the request shows up here.
/// </summary>
public class ClaudeTranslatorTests
{
  private const string Save = "0192f3a0-1c4e-7a10-8000-000000000001";
  private const string Hello = "0192f3a0-1c4e-7a10-8000-000000000002";

  private sealed class FakeApi(string replyText, string stopReason = "end_turn") : HttpMessageHandler
  {
    public JsonElement Request { get; private set; }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
      Request = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken)).RootElement.Clone();
      string body = JsonSerializer.Serialize(new
      {
        id = "msg_test",
        type = "message",
        role = "assistant",
        model = "claude-opus-5-5",
        content = new[] { new { type = "text", text = replyText } },
        stop_reason = stopReason,
        stop_sequence = (string?)null,
        usage = new { input_tokens = 10, output_tokens = 5 },
      });
      return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    }
  }

  private static DraftContext Context() => new(
    "Demo",
    "en",
    new LanguageSettings { Code = "de", Name = "Deutsch", Instructions = "Duze den Nutzer." },
    new AiSettings { Instructions = "Keep RcinelTranslate in English." },
    [new("Settings", "Einstellungen")]);

  [Fact]
  public async Task Sends_strings_screenshot_schema_and_fallbacks()
  {
    FakeApi api = new($"{{\"translations\": [{{\"id\": \"{Save}\", \"text\": \"Speichern\"}}, {{\"id\": \"not-asked\", \"text\": \"x\"}}]}}");
    ClaudeTranslator translator = new("test-key", new HttpClient(api));
    DraftBatch batch = new(
      [new DraftItem(Save, "Save", "Button", null, new Region { X = 0.5, Y = 0.1, Width = 0.1, Height = 0.05 }), new DraftItem(Hello, "Hello {name}", null, "Hallo {name}", null)],
      new DraftScreenshot("Settings", [1, 2, 3], "image/png", 1000, 800));

    IReadOnlyDictionary<string, string> result = await translator.TranslateAsync(Context(), batch, CancellationToken.None);

    // Only ids that were asked for come back.
    Assert.Equal(new Dictionary<string, string> { [Save] = "Speichern" }, result);

    JsonElement request = api.Request;
    Assert.Equal("claude-opus-5-5", request.GetProperty("model").GetString());
    Assert.Equal("default", request.GetProperty("fallbacks").GetString());
    Assert.Equal("medium", request.GetProperty("output_config").GetProperty("effort").GetString());
    Assert.Equal("json_schema", request.GetProperty("output_config").GetProperty("format").GetProperty("type").GetString());
    JsonElement system = request.GetProperty("system")[0];
    Assert.Contains("Duze den Nutzer.", system.GetProperty("text").GetString());
    Assert.Contains("Einstellungen", system.GetProperty("text").GetString());
    Assert.Equal("ephemeral", system.GetProperty("cache_control").GetProperty("type").GetString());

    JsonElement content = request.GetProperty("messages")[0].GetProperty("content");
    Assert.Equal("image", content[0].GetProperty("type").GetString());
    Assert.Equal("image/png", content[0].GetProperty("source").GetProperty("media_type").GetString());
    string user = content[1].GetProperty("text").GetString()!;
    Assert.Contains("100 x 40 px of a 1000 x 800 px screenshot", user);
    Assert.Contains("Hallo {name}", user);
  }

  [Fact]
  public async Task Explains_refusals_and_cut_off_replies()
  {
    DraftBatch batch = new([new DraftItem(Save, "Save", null, null, null)], null);
    TranslationException refusal = await Assert.ThrowsAsync<TranslationException>(() =>
      new ClaudeTranslator("k", new HttpClient(new FakeApi("", "refusal"))).TranslateAsync(Context(), batch, CancellationToken.None));
    Assert.Contains("declined", refusal.Message);
    TranslationException cut = await Assert.ThrowsAsync<TranslationException>(() =>
      new ClaudeTranslator("k", new HttpClient(new FakeApi("{\"transl", "max_tokens"))).TranslateAsync(Context(), batch, CancellationToken.None));
    Assert.Contains("batchSize", cut.Message);
  }

  [Fact]
  public void Is_not_configured_without_a_key()
  {
    string? old = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
    Environment.SetEnvironmentVariable("ANTHROPIC_API_KEY", null);
    try
    {
      Assert.False(new ClaudeTranslator().IsConfigured);
    }
    finally
    {
      Environment.SetEnvironmentVariable("ANTHROPIC_API_KEY", old);
    }
  }
}
