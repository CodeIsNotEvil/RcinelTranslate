using CINE.Translate.Ai;
using CINE.Translate.Core;

namespace CINE.Translate.Editor.Tests;

public class TranslationDrafterTests
{
  /// <summary>Answers "[de] source", and lets a test act between planning and applying.</summary>
  private sealed class FakeTranslator(Action? duringRequest = null, Func<DraftItem, string>? translate = null) : ITranslator
  {
    public bool IsConfigured => true;
    public int Calls { get; private set; }

    public Task<IReadOnlyDictionary<string, string>> TranslateAsync(DraftContext context, DraftBatch batch, CancellationToken cancellationToken)
    {
      Calls++;
      duringRequest?.Invoke();
      IReadOnlyDictionary<string, string> result = batch.Items.ToDictionary(i => i.Id, i => translate?.Invoke(i) ?? $"[{context.Language.Code}] {i.Source}");
      return Task.FromResult(result);
    }
  }

  [Fact]
  public async Task Stores_drafts_attributed_to_the_model()
  {
    using TempProject temp = new("de");
    TranslationProject p = temp.Project;
    string a = UuidV7.New(), b = UuidV7.New();
    p.Source[a] = "Save";
    p.Source[b] = "Cancel";

    DraftProgress result = await new TranslationDrafter(new FakeTranslator()).RunAsync(new DirectProjectAccess(p), "de");

    Assert.Equal((2, 2, 2), (result.Done, result.Total, result.Applied));
    Assert.Equal("[de] Save", p.GetLanguage("de").Texts[a]);
    Assert.Equal(TranslationState.Draft, p.GetState("de", a));
    Assert.Equal("ai:claude-opus-5-5", p.GetLanguage("de").Review[a].By);
    // Saved, not just changed in memory.
    Assert.Equal("[de] Cancel", temp.Reload().GetLanguage("de").Texts[b]);
  }

  [Fact]
  public async Task Never_overwrites_what_a_human_changed_during_the_run()
  {
    using TempProject temp = new("de");
    TranslationProject p = temp.Project;
    string a = UuidV7.New(), b = UuidV7.New();
    p.Source[a] = "Save";
    p.Source[b] = "Cancel";
    FakeTranslator translator = new(() =>
      p.SetTranslation("de", a, "Sichern", ReviewState.Confirmed, "alex", DateTimeOffset.UtcNow));

    DraftProgress result = await new TranslationDrafter(translator).RunAsync(new DirectProjectAccess(p), "de");

    Assert.Equal(1, result.Applied);
    Assert.Equal("Sichern", p.GetLanguage("de").Texts[a]);
    Assert.Equal(TranslationState.Confirmed, p.GetState("de", a));
  }

  [Fact]
  public async Task Drops_drafts_that_lost_a_placeholder()
  {
    using TempProject temp = new("de");
    TranslationProject p = temp.Project;
    string a = UuidV7.New();
    p.Source[a] = "Hello {name}";

    DraftProgress result = await new TranslationDrafter(new FakeTranslator(translate: _ => "Hallo")).RunAsync(new DirectProjectAccess(p), "de");

    Assert.Equal(0, result.Applied);
    Assert.Contains(result.Problems, m => m.Contains("{name}"));
    Assert.Equal(TranslationState.Missing, p.GetState("de", a));
  }
}
