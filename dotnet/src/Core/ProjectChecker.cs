using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Rcinel.Translate.Core;

public enum IssueSeverity
{
  Warning,
  Error,
}

/// <summary>One finding of <see cref="ProjectChecker"/>.</summary>
/// <param name="Code">A stable name for scripts and docs, such as "placeholders".</param>
public sealed record ProjectIssue(IssueSeverity Severity, string Code, string Message, string? Language = null, string? Id = null);

/// <summary>What <see cref="ProjectChecker.Check"/> should treat as an error.</summary>
public sealed class CheckOptions
{
  /// <summary>Missing and draft translations are errors, not just numbers in the progress table. For CI before a release.</summary>
  public bool RequireConfirmed { get; set; }

  /// <summary>Missing translations are errors, but drafts are fine.</summary>
  public bool RequireComplete { get; set; }

  /// <summary>Warnings fail the check too.</summary>
  public bool Strict { get; set; }
}

/// <summary>
/// Finds what would break or confuse an app: ids that aren't UUIDv7, placeholders a translation
/// lost, translations of ids that no longer exist, files that aren't in canonical form. `rtr
/// check` runs it, and CI should too.
/// </summary>
public static class ProjectChecker
{
  public static List<ProjectIssue> Check(TranslationProject project, CheckOptions? options = null)
  {
    options ??= new CheckOptions();
    List<ProjectIssue> issues = [];
    foreach (string id in project.Source.Keys.Where(id => !UuidV7.IsValid(id)))
    {
      issues.Add(new(IssueSeverity.Error, "invalid-id", $"{id} in the source file is not a lowercase UUIDv7", Id: id));
    }

    foreach (LanguageData language in project.Languages)
    {
      foreach (KeyValuePair<string, string> pair in language.Texts)
      {
        if (!project.Source.TryGetValue(pair.Key, out string? source))
        {
          issues.Add(new(IssueSeverity.Warning, "orphaned", $"{pair.Key} is translated but no longer in the source; `rtr prune` removes it", language.Code, pair.Key));
          continue;
        }
        if (Placeholders.Compare(source, pair.Value) is string problem)
        {
          issues.Add(new(IssueSeverity.Error, "placeholders", $"{pair.Key}: {problem} (source \"{source}\", translation \"{pair.Value}\")", language.Code, pair.Key));
        }
        if (pair.Value.Length == 0 && source.Length > 0)
        {
          issues.Add(new(IssueSeverity.Warning, "empty", $"{pair.Key} is translated as an empty text", language.Code, pair.Key));
        }
      }

      LanguageProgress progress = project.GetProgress(language.Code);
      if (progress.Stale > 0)
      {
        issues.Add(new(IssueSeverity.Warning, "outdated", $"{progress.Stale} translation(s) were made for an older source text and need a review", language.Code));
      }
      if ((options.RequireConfirmed || options.RequireComplete) && progress.Missing > 0)
      {
        issues.Add(new(IssueSeverity.Error, "missing", $"{progress.Missing} string(s) have no translation", language.Code));
      }
      if (options.RequireConfirmed && progress.Draft + progress.Stale > 0)
      {
        issues.Add(new(IssueSeverity.Error, "unconfirmed", $"{progress.Draft + progress.Stale} translation(s) are not confirmed", language.Code));
      }
    }

    foreach (Screenshot screenshot in project.Screenshots.Values)
    {
      if (!File.Exists(Path.Combine(project.ScreenshotDirectory, screenshot.File)))
      {
        issues.Add(new(IssueSeverity.Warning, "screenshot-file", $"the image {screenshot.File} of screenshot \"{screenshot.Title}\" is missing"));
      }
      foreach (Region region in screenshot.Regions.Where(r => !project.Source.ContainsKey(r.Key)))
      {
        issues.Add(new(IssueSeverity.Warning, "orphaned", $"screenshot \"{screenshot.Title}\" marks {region.Key}, which is no longer in the source", Id: region.Key));
      }
    }

    foreach (string file in NonCanonicalFiles(project))
    {
      issues.Add(new(IssueSeverity.Warning, "format", $"{file} is not in canonical form (sorted by id, one entry per line); `rtr format` rewrites it"));
    }

    if (options.Strict)
    {
      issues = [.. issues.Select(i => i with { Severity = IssueSeverity.Error })];
    }
    return issues;
  }

  /// <summary>
  /// The translation files whose text differs from what <see cref="TranslationProject.Save"/> would
  /// write. Hand edits that put a new id in the middle or use other indentation show up here.
  /// </summary>
  public static List<string> NonCanonicalFiles(TranslationProject project)
  {
    List<string> files = [];
    void Compare(string path, string expected)
    {
      if (File.Exists(path) && File.ReadAllText(path, Encoding.UTF8) != expected)
      {
        files.Add(Paths.Relative(project.Directory, path));
      }
    }
    Compare(Path.Combine(project.TranslationsDirectory, project.Settings.SourceLanguage + ".json"), CanonicalJson.FlatObject(project.Source));
    foreach (LanguageData language in project.Languages)
    {
      Compare(Path.Combine(project.TranslationsDirectory, language.Code + ".json"), CanonicalJson.FlatObject(language.Texts));
      Compare(Path.Combine(project.ReviewDirectory, language.Code + ".json"), ProjectFiles.ReviewJson(language.Review));
    }
    return files;
  }
}
