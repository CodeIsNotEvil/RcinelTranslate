using Rcinel.Translate.Ai;
using Rcinel.Translate.Core;

namespace Rcinel.Translate.Editor.Tests;

public class DraftPlannerTests
{
  private static readonly DateTimeOffset At = new(2026, 10, 31, 12, 0, 0, TimeSpan.Zero);

  [Fact]
  public void Strings_on_a_screenshot_travel_with_it_and_the_rest_go_in_batches()
  {
    using TempProject temp = new("de");
    TranslationProject p = temp.Project;
    p.Settings.Ai.BatchSize = 2;
    List<string> ids = [.. Enumerable.Range(0, 5).Select(_ => UuidV7.New())];
    foreach (string id in ids)
    {
      p.Source[id] = "Text " + ids.IndexOf(id);
    }
    Directory.CreateDirectory(p.ScreenshotDirectory);
    File.WriteAllBytes(Path.Combine(p.ScreenshotDirectory, "s.png"), [137, 80, 78, 71]);
    p.Screenshots["s"] = new Screenshot { Id = "s", File = "s.png", Title = "Main", Regions = [new Region { Key = ids[3] }] };
    p.Notes[ids[0]] = "verb";

    List<DraftBatch> batches = DraftPlanner.Plan(p, "de", DraftPlanner.OpenIds(p, "de"));

    Assert.Equal(3, batches.Count);
    Assert.Equal("Main", batches[0].Screenshot!.Title);
    Assert.Equal([ids[3]], batches[0].Items.Select(i => i.Id));
    Assert.Null(batches[1].Screenshot);
    Assert.Equal([ids[0], ids[1]], batches[1].Items.Select(i => i.Id));
    Assert.Equal("verb", batches[1].Items[0].Note);
    Assert.Equal([ids[2], ids[4]], batches[2].Items.Select(i => i.Id));
  }

  [Fact]
  public void Open_ids_are_missing_and_stale_ones_and_stale_ones_bring_the_old_text()
  {
    using TempProject temp = new("de");
    TranslationProject p = temp.Project;
    string done = UuidV7.New(), stale = UuidV7.New(), draft = UuidV7.New(), missing = UuidV7.New();
    foreach (string id in new[] { done, stale, draft, missing })
    {
      p.Source[id] = "x";
    }
    p.SetTranslation("de", done, "fertig", ReviewState.Confirmed, "alex", At);
    p.SetTranslation("de", stale, "alt", ReviewState.Confirmed, "alex", At);
    p.SetTranslation("de", draft, "entwurf", ReviewState.Draft, "ai:test", At);
    p.Source[stale] = "changed";

    List<string> open = DraftPlanner.OpenIds(p, "de");
    Assert.Equal([stale, missing], open);
    DraftItem item = DraftPlanner.Plan(p, "de", open)[0].Items[0];
    Assert.Equal("alt", item.Previous);

    DraftContext context = DraftPlanner.Context(p, "de");
    Assert.Equal([new KeyValuePair<string, string>("x", "fertig")], context.Examples);
  }
}
