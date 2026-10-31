using Rcinel.Translate.Core;

namespace Rcinel.Translate.Ai;

/// <summary>One string to translate, with everything a translator would want to know about it.</summary>
/// <param name="Previous">The translation made for an older source text, when the string is stale.</param>
/// <param name="Box">Where the string sits on <see cref="DraftBatch.Screenshot"/>, if it does.</param>
public sealed record DraftItem(string Id, string Source, string? Note, string? Previous, Region? Box);

/// <summary>A screenshot sent along with a batch.</summary>
public sealed record DraftScreenshot(string Title, byte[] Image, string MediaType, int Width, int Height);

/// <summary>Strings that go into one request.</summary>
public sealed record DraftBatch(IReadOnlyList<DraftItem> Items, DraftScreenshot? Screenshot);

/// <summary>What stays the same for every batch of a run: who the app is, which language, the style rules.</summary>
/// <param name="Examples">Confirmed translations of the same language, so drafts reuse the reviewers' terms.</param>
public sealed record DraftContext(
  string ProjectName,
  string SourceLanguage,
  LanguageSettings Language,
  AiSettings Ai,
  IReadOnlyList<KeyValuePair<string, string>> Examples);

/// <summary>
/// Splits the strings to draft into requests. Strings that appear on a screenshot travel with that
/// screenshot, so the model sees a "Save" button as a button; the rest go in text-only batches.
/// Runs inside the editor's project lock, so it copies everything it needs and never touches the
/// project again.
/// </summary>
public static class DraftPlanner
{
  /// <summary>Images above this go without the screenshot; the API rejects images over 5 MB.</summary>
  public const int MaxImageBytes = 5 * 1024 * 1024;

  /// <summary>How many confirmed translations go into the prompt as examples.</summary>
  public const int MaxExamples = 40;

  /// <summary>The ids that a "draft what's open" run should translate: missing and stale.</summary>
  public static List<string> OpenIds(TranslationProject project, string language) =>
    [.. project.Source.Keys.Where(id => project.GetState(language, id) is TranslationState.Missing or TranslationState.Stale)];

  public static DraftContext Context(TranslationProject project, string language)
  {
    LanguageData data = project.GetLanguage(language);
    // The most recently confirmed first: they reflect the reviewers' current choice of words.
    List<KeyValuePair<string, string>> examples = [.. data.Review
      .Where(r => r.Value.State == ReviewState.Confirmed && project.GetState(data, r.Key) == TranslationState.Confirmed)
      .OrderByDescending(r => r.Value.At)
      .Take(MaxExamples)
      .Select(r => new KeyValuePair<string, string>(project.Source[r.Key], data.Texts[r.Key]))];
    return new DraftContext(
      string.IsNullOrWhiteSpace(project.Settings.Name) ? "this app" : project.Settings.Name,
      project.Settings.SourceLanguage,
      data.Settings,
      project.Settings.Ai,
      examples);
  }

  public static List<DraftBatch> Plan(TranslationProject project, string language, IEnumerable<string> ids)
  {
    LanguageData data = project.GetLanguage(language);
    int size = Math.Max(1, project.Settings.Ai.BatchSize);
    HashSet<string> remaining = [.. ids.Where(project.Source.ContainsKey)];
    List<DraftBatch> batches = [];

    DraftItem Item(string id, Region? box) => new(
      id,
      project.Source[id],
      project.Notes.TryGetValue(id, out string? note) ? note : null,
      project.GetState(data, id) == TranslationState.Stale ? data.Texts[id] : null,
      box);

    foreach (Screenshot screenshot in project.Screenshots.Values)
    {
      List<Region> boxes = [.. screenshot.Regions.Where(r => remaining.Contains(r.Key)).GroupBy(r => r.Key).Select(g => g.First())];
      if (boxes.Count == 0)
      {
        continue;
      }
      DraftScreenshot? image = LoadImage(project, screenshot);
      foreach (Region[] chunk in boxes.Chunk(size))
      {
        batches.Add(new DraftBatch([.. chunk.Select(r => Item(r.Key, r))], image));
      }
      remaining.ExceptWith(boxes.Select(b => b.Key));
    }

    // Source order (= creation order) keeps strings of one screen together in the text batches.
    foreach (string[] chunk in project.Source.Keys.Where(remaining.Contains).Chunk(size))
    {
      batches.Add(new DraftBatch([.. chunk.Select(id => Item(id, null))], null));
    }
    return batches;
  }

  private static DraftScreenshot? LoadImage(TranslationProject project, Screenshot screenshot)
  {
    string path = Path.Combine(project.ScreenshotDirectory, screenshot.File);
    string? mediaType = Path.GetExtension(path).ToLowerInvariant() switch
    {
      ".png" => "image/png",
      ".jpg" or ".jpeg" => "image/jpeg",
      ".webp" => "image/webp",
      ".gif" => "image/gif",
      _ => null,
    };
    if (mediaType is null || !File.Exists(path) || new FileInfo(path).Length > MaxImageBytes)
    {
      return null;
    }
    return new DraftScreenshot(screenshot.Title, File.ReadAllBytes(path), mediaType, screenshot.Width, screenshot.Height);
  }
}
