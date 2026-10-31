using CINE.Translate.Core;

namespace CINE.Translate.Editor.Services;

/// <summary>
/// Copies of project data for rendering. Components get these from ProjectStore.Read and never the
/// project's own objects, which another user's save may be changing at the same moment.
/// </summary>
public static class Views
{
  public static Screenshot Copy(Screenshot s) => new()
  {
    Id = s.Id,
    File = s.File,
    Title = s.Title,
    Width = s.Width,
    Height = s.Height,
    Regions = [.. s.Regions.Select(Copy)],
  };

  public static Region Copy(Region r) => new()
  {
    Key = r.Key,
    X = r.X,
    Y = r.Y,
    Width = r.Width,
    Height = r.Height,
    Align = r.Align,
    Background = r.Background,
    Color = r.Color,
    FontSize = r.FontSize,
  };

  /// <summary>What a region shows in <paramref name="language"/>: the translation, or the source text when there is none (as the app would).</summary>
  public static Dictionary<string, string> RegionTexts(TranslationProject project, Screenshot shot, string? language)
  {
    Dictionary<string, string> texts = [];
    LanguageData? data = language is not null && project.HasLanguage(language) ? project.GetLanguage(language) : null;
    foreach (Region region in shot.Regions)
    {
      string source = project.Source.TryGetValue(region.Key, out string? s) ? s : "";
      texts[region.Key] = data is not null && data.Texts.TryGetValue(region.Key, out string? t) ? t : source;
    }
    return texts;
  }

  public static Dictionary<string, string> RegionSources(TranslationProject project, Screenshot shot) =>
    shot.Regions.Select(r => r.Key).Distinct().ToDictionary(k => k, k => project.Source.TryGetValue(k, out string? s) ? s : "");

  /// <summary>"2026-10-31", from the id. Strings sort by it, so it says where in the app's history a string came from.</summary>
  public static string Created(string id) => UuidV7.IsValid(id) ? UuidV7.GetTimestamp(id).ToString("yyyy-MM-dd") : "";

  public static string StateName(TranslationState state) => state switch
  {
    TranslationState.Missing => "Missing",
    TranslationState.Draft => "Draft",
    TranslationState.Stale => "Outdated",
    _ => "Confirmed",
  };
}
