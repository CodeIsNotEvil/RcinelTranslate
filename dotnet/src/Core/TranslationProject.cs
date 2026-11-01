using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace Rcinel.Translate.Core;

/// <summary>The texts and review states of one target language.</summary>
public sealed class LanguageData(LanguageSettings settings)
{
  public LanguageSettings Settings { get; } = settings;
  public string Code => Settings.Code;

  /// <summary>translations/&lt;code&gt;.json: id to translated text.</summary>
  public SortedDictionary<string, string> Texts { get; } = new(StringComparer.Ordinal);

  /// <summary>review/&lt;code&gt;.json: id to review entry.</summary>
  public SortedDictionary<string, ReviewEntry> Review { get; } = new(StringComparer.Ordinal);
}

/// <summary>
/// A translation project on disk, loaded into memory: rcinel-translate.json plus the files next to it
/// (docs/format.md has the layout). Not thread-safe; the editor serializes access with a lock.
/// </summary>
public sealed class TranslationProject
{
  /// <summary>
  /// The name of the project file. Named after the tool, not just "rcinel", because other Rcinel
  /// apps may keep their own configuration in the same repository.
  /// </summary>
  public const string FileName = "rcinel-translate.json";

  /// <summary>
  /// The name before 0.1.0 was released. Still found and read, and written back under the same
  /// name, so a project from then keeps working; rename the file to switch.
  /// </summary>
  public const string LegacyFileName = "rcinel.json";

  private readonly Dictionary<string, LanguageData> languages = new(StringComparer.OrdinalIgnoreCase);

  private TranslationProject(string projectFile, ProjectSettings settings)
  {
    ProjectFile = Path.GetFullPath(projectFile);
    Directory = Path.GetDirectoryName(ProjectFile)!;
    Settings = settings;
  }

  /// <summary>The folder that holds rcinel-translate.json.</summary>
  public string Directory { get; }

  public ProjectSettings Settings { get; }

  /// <summary>The project file this project was loaded from or created as.</summary>
  public string ProjectFile { get; }

  /// <summary>The shipped folder: the source file and one file per target language.</summary>
  public string TranslationsDirectory => Path.Combine(Directory, Settings.Translations);

  public string ReviewDirectory => Path.Combine(Directory, "review");
  public string ContextDirectory => Path.Combine(Directory, "context");
  public string ScreenshotDirectory => Path.Combine(ContextDirectory, "screenshots");

  /// <summary>translations/&lt;source&gt;.json: id to source (English) text, written by `rtr scan`.</summary>
  public SortedDictionary<string, string> Source { get; } = new(StringComparer.Ordinal);

  /// <summary>context/notes.json: id to a hint for translators ("verb, on a button").</summary>
  public SortedDictionary<string, string> Notes { get; } = new(StringComparer.Ordinal);

  /// <summary>context/screenshots.json, by screenshot id.</summary>
  public SortedDictionary<string, Screenshot> Screenshots { get; } = new(StringComparer.Ordinal);

  /// <summary>The target languages in the order of rcinel-translate.json.</summary>
  public IEnumerable<LanguageData> Languages => Settings.Languages.Select(l => languages[l.Code]);

  /// <summary>
  /// Finds rcinel-translate.json: <paramref name="path"/> may be the file itself, the folder that holds it,
  /// or any folder below that one (the way git finds .git), so the CLI works from anywhere in the app.
  /// </summary>
  /// <exception cref="FileNotFoundException">No rcinel-translate.json was found.</exception>
  public static string FindProjectFile(string path)
  {
    string full = Path.GetFullPath(path);
    if (File.Exists(full))
    {
      return full;
    }
    for (string? dir = full; dir is not null; dir = Path.GetDirectoryName(dir))
    {
      // Projects usually sit in an i18n folder next to the code. The current name wins over the
      // legacy one in each place.
      foreach (string folder in new[] { dir, Path.Combine(dir, "i18n") })
      {
        foreach (string name in new[] { FileName, LegacyFileName })
        {
          string candidate = Path.Combine(folder, name);
          if (File.Exists(candidate))
          {
            return candidate;
          }
        }
      }
    }
    throw new FileNotFoundException($"No {FileName} in {full} or a folder above it. Create one with `rtr init`.");
  }

  /// <summary>Loads the project that <paramref name="path"/> points to (see <see cref="FindProjectFile"/>).</summary>
  /// <exception cref="FormatException">A file is not valid; the message names it.</exception>
  public static TranslationProject Load(string path)
  {
    string file = FindProjectFile(path);
    ProjectSettings settings = ProjectFiles.ReadSettings(file);
    TranslationProject project = new(file, settings);
    project.ReadFiles();
    return project;
  }

  /// <summary>Creates a new, empty project in <paramref name="directory"/> and writes its files.</summary>
  /// <exception cref="IOException">The folder already has a project.</exception>
  public static TranslationProject Create(string directory, ProjectSettings settings)
  {
    foreach (string name in new[] { FileName, LegacyFileName })
    {
      if (File.Exists(Path.Combine(directory, name)))
      {
        throw new IOException($"{Path.Combine(directory, name)} already exists.");
      }
    }
    System.IO.Directory.CreateDirectory(directory);
    TranslationProject project = new(Path.Combine(directory, FileName), settings);
    foreach (LanguageSettings language in settings.Languages)
    {
      project.languages[language.Code] = new LanguageData(language);
    }
    project.Save();
    return project;
  }

  /// <summary>Reads everything again from disk, for example after a git pull.</summary>
  public static TranslationProject Reload(TranslationProject project) => Load(project.ProjectFile);

  public LanguageData GetLanguage(string code) =>
    languages.TryGetValue(code, out LanguageData? data)
      ? data
      : throw new KeyNotFoundException($"The project has no language \"{code}\".");

  public bool HasLanguage(string code) => languages.ContainsKey(code);

  /// <summary>Adds a target language with no translations yet.</summary>
  public LanguageData AddLanguage(LanguageSettings settings)
  {
    if (string.IsNullOrWhiteSpace(settings.Code) || settings.Code.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || settings.Code.Contains('.'))
    {
      throw new ArgumentException($"\"{settings.Code}\" is not a usable language code. Use a tag such as de or pt-BR.");
    }
    if (string.Equals(settings.Code, Settings.SourceLanguage, StringComparison.OrdinalIgnoreCase) || languages.ContainsKey(settings.Code))
    {
      throw new ArgumentException($"The project already has the language \"{settings.Code}\".");
    }
    Settings.Languages.Add(settings);
    LanguageData data = new(settings);
    languages[settings.Code] = data;
    return data;
  }

  /// <summary>Where the translation of <paramref name="id"/> stands in <paramref name="language"/>.</summary>
  public TranslationState GetState(string language, string id) => GetState(GetLanguage(language), id);

  public TranslationState GetState(LanguageData language, string id)
  {
    if (!language.Texts.ContainsKey(id))
    {
      return TranslationState.Missing;
    }
    // A translation without a review entry was written by hand into the file or imported. It may
    // be fine, but nobody here vouched for it.
    if (!language.Review.TryGetValue(id, out ReviewEntry? review))
    {
      return TranslationState.Draft;
    }
    if (Source.TryGetValue(id, out string? source) && review.Source != SourceHash.Of(source))
    {
      return TranslationState.Stale;
    }
    return review.State == ReviewState.Confirmed ? TranslationState.Confirmed : TranslationState.Draft;
  }

  public LanguageProgress GetProgress(string language)
  {
    LanguageData data = GetLanguage(language);
    int confirmed = 0, draft = 0, stale = 0, missing = 0;
    foreach (string id in Source.Keys)
    {
      switch (GetState(data, id))
      {
        case TranslationState.Confirmed: confirmed++; break;
        case TranslationState.Draft: draft++; break;
        case TranslationState.Stale: stale++; break;
        default: missing++; break;
      }
    }
    int orphaned = data.Texts.Keys.Count(id => !Source.ContainsKey(id));
    return new LanguageProgress(data.Code, Source.Count, confirmed, draft, stale, missing, orphaned);
  }

  /// <summary>
  /// Stores a translation and who stands behind it. The review entry records the current source
  /// text, so the translation turns stale when that text changes.
  /// </summary>
  public void SetTranslation(string language, string id, string text, ReviewState state, string by, DateTimeOffset at)
  {
    if (!Source.TryGetValue(id, out string? source))
    {
      throw new KeyNotFoundException($"\"{id}\" is not in the source file. Run `rtr scan` first.");
    }
    LanguageData data = GetLanguage(language);
    data.Texts[id] = text;
    data.Review[id] = new ReviewEntry(state, SourceHash.Of(source), by, at.ToUniversalTime());
  }

  /// <summary>Removes a translation, so the app shows the source text again.</summary>
  public void RemoveTranslation(string language, string id)
  {
    LanguageData data = GetLanguage(language);
    data.Texts.Remove(id);
    data.Review.Remove(id);
  }

  /// <summary>
  /// Removes translations, review entries, notes and screenshot regions of ids that are no longer
  /// in the source file. Returns how many entries went.
  /// </summary>
  public int Prune()
  {
    int removed = 0;
    foreach (LanguageData data in languages.Values)
    {
      removed += RemoveWhere(data.Texts, id => !Source.ContainsKey(id));
      RemoveWhere(data.Review, id => !Source.ContainsKey(id));
    }
    removed += RemoveWhere(Notes, id => !Source.ContainsKey(id));
    foreach (Screenshot screenshot in Screenshots.Values)
    {
      removed += screenshot.Regions.RemoveAll(r => !Source.ContainsKey(r.Key));
    }
    return removed;
  }

  /// <summary>The screenshots that show <paramref name="id"/>.</summary>
  public IEnumerable<Screenshot> ScreenshotsOf(string id) =>
    Screenshots.Values.Where(s => s.Regions.Any(r => r.Key == id));

  /// <summary>
  /// Writes every file in canonical form. Files whose content didn't change are left alone, so
  /// their timestamps don't move and file watchers (and the editor's reload check) stay quiet.
  /// </summary>
  public void Save()
  {
    ProjectFiles.Write(ProjectFile, ProjectFiles.SettingsJson(Settings));
    ProjectFiles.Write(Path.Combine(TranslationsDirectory, Settings.SourceLanguage + ".json"), CanonicalJson.FlatObject(Source));
    foreach (LanguageData data in languages.Values)
    {
      ProjectFiles.Write(Path.Combine(TranslationsDirectory, data.Code + ".json"), CanonicalJson.FlatObject(data.Texts));
      ProjectFiles.Write(Path.Combine(ReviewDirectory, data.Code + ".json"), ProjectFiles.ReviewJson(data.Review));
    }
    ProjectFiles.Write(Path.Combine(ContextDirectory, "notes.json"), CanonicalJson.FlatObject(Notes));
    ProjectFiles.Write(Path.Combine(ContextDirectory, "screenshots.json"), ProjectFiles.ScreenshotsJson(Screenshots));
  }

  /// <summary>The newest write time of the files this project reads, to notice changes made outside (git pull, an editor).</summary>
  public DateTime LastWriteTimeUtc()
  {
    DateTime newest = File.GetLastWriteTimeUtc(ProjectFile);
    foreach (string dir in new[] { TranslationsDirectory, ReviewDirectory, ContextDirectory })
    {
      if (!System.IO.Directory.Exists(dir))
      {
        continue;
      }
      foreach (string file in System.IO.Directory.EnumerateFiles(dir, "*.json"))
      {
        DateTime time = File.GetLastWriteTimeUtc(file);
        if (time > newest)
        {
          newest = time;
        }
      }
    }
    return newest;
  }

  private void ReadFiles()
  {
    string sourceFile = Path.Combine(TranslationsDirectory, Settings.SourceLanguage + ".json");
    Copy(ProjectFiles.ReadFlat(sourceFile), Source);
    foreach (LanguageSettings language in Settings.Languages)
    {
      LanguageData data = new(language);
      Copy(ProjectFiles.ReadFlat(Path.Combine(TranslationsDirectory, language.Code + ".json")), data.Texts);
      foreach (KeyValuePair<string, ReviewEntry> pair in ProjectFiles.ReadReview(Path.Combine(ReviewDirectory, language.Code + ".json")))
      {
        data.Review[pair.Key] = pair.Value;
      }
      languages[language.Code] = data;
    }
    Copy(ProjectFiles.ReadFlat(Path.Combine(ContextDirectory, "notes.json")), Notes);
    foreach (Screenshot screenshot in ProjectFiles.ReadScreenshots(Path.Combine(ContextDirectory, "screenshots.json")))
    {
      Screenshots[screenshot.Id] = screenshot;
    }
  }

  private static void Copy(Dictionary<string, string> from, SortedDictionary<string, string> to)
  {
    foreach (KeyValuePair<string, string> pair in from)
    {
      to[pair.Key] = pair.Value;
    }
  }

  private static int RemoveWhere<T>(SortedDictionary<string, T> dictionary, Func<string, bool> predicate)
  {
    List<string> keys = [.. dictionary.Keys.Where(predicate)];
    foreach (string key in keys)
    {
      dictionary.Remove(key);
    }
    return keys.Count;
  }
}

/// <summary>Reading and writing the individual files. Missing optional files read as empty.</summary>
internal static class ProjectFiles
{
  private static readonly JsonDocumentOptions Options = new() { AllowTrailingCommas = false, CommentHandling = JsonCommentHandling.Disallow };

  public static Dictionary<string, string> ReadFlat(string path)
  {
    if (!File.Exists(path))
    {
      return new Dictionary<string, string>(StringComparer.Ordinal);
    }
    try
    {
      return FlatJsonReader.ReadDictionary(File.ReadAllText(path, Encoding.UTF8));
    }
    catch (FormatException e)
    {
      throw new FormatException($"{path}: {e.Message}", e);
    }
  }

  public static ProjectSettings ReadSettings(string path) => Parse(path, root =>
  {
    ProjectSettings settings = new()
    {
      Format = Int(root, "format") ?? 1,
      Name = String(root, "name") ?? "",
      SourceLanguage = String(root, "sourceLanguage") ?? "en",
      Translations = String(root, "translations") ?? "translations",
    };
    if (settings.Format > ProjectSettings.CurrentFormat)
    {
      throw new FormatException($"it uses format {settings.Format}, but this version of RcinelTranslate reads only up to {ProjectSettings.CurrentFormat}. Update it.");
    }
    if (root.TryGetProperty("sources", out JsonElement sources))
    {
      settings.Sources = [.. sources.EnumerateArray().Select(s => s.GetString() ?? "")];
    }
    if (root.TryGetProperty("functions", out JsonElement functions))
    {
      settings.Functions = [.. functions.EnumerateArray().Select(s => s.GetString() ?? "")];
    }
    if (root.TryGetProperty("languages", out JsonElement languages))
    {
      foreach (JsonElement language in languages.EnumerateArray())
      {
        // A plain string is enough for a language without a name or instructions.
        settings.Languages.Add(language.ValueKind == JsonValueKind.String
          ? new LanguageSettings { Code = language.GetString() ?? "" }
          : new LanguageSettings
          {
            Code = String(language, "code") ?? "",
            Name = String(language, "name") ?? "",
            Instructions = String(language, "instructions") ?? "",
          });
      }
    }
    if (root.TryGetProperty("ai", out JsonElement ai))
    {
      settings.Ai.Model = String(ai, "model") ?? AiSettings.DefaultModel;
      settings.Ai.Effort = String(ai, "effort") ?? "medium";
      settings.Ai.Instructions = String(ai, "instructions") ?? "";
      settings.Ai.BatchSize = Int(ai, "batchSize") ?? 40;
    }
    return settings;
  });

  public static string SettingsJson(ProjectSettings settings)
  {
    StringBuilder b = new();
    b.Append("{\n");
    b.Append("  \"format\": ").Append(settings.Format.ToString(CultureInfo.InvariantCulture)).Append(",\n");
    b.Append("  \"name\": ").Append(CanonicalJson.Quote(settings.Name)).Append(",\n");
    b.Append("  \"sourceLanguage\": ").Append(CanonicalJson.Quote(settings.SourceLanguage)).Append(",\n");
    b.Append("  \"translations\": ").Append(CanonicalJson.Quote(settings.Translations)).Append(",\n");
    b.Append("  \"sources\": [").Append(string.Join(", ", settings.Sources.Select(CanonicalJson.Quote))).Append("],\n");
    b.Append("  \"functions\": [").Append(string.Join(", ", settings.Functions.Select(CanonicalJson.Quote))).Append("],\n");
    b.Append("  \"languages\": [");
    for (int i = 0; i < settings.Languages.Count; i++)
    {
      LanguageSettings l = settings.Languages[i];
      b.Append(i == 0 ? "\n" : ",\n").Append("    ").Append(CanonicalJson.Inline(
        ("code", CanonicalJson.Quote(l.Code)),
        ("name", CanonicalJson.Quote(l.Name)),
        ("instructions", CanonicalJson.Quote(l.Instructions))));
    }
    b.Append(settings.Languages.Count > 0 ? "\n  ],\n" : "],\n");
    b.Append("  \"ai\": {\n");
    b.Append("    \"model\": ").Append(CanonicalJson.Quote(settings.Ai.Model)).Append(",\n");
    b.Append("    \"effort\": ").Append(CanonicalJson.Quote(settings.Ai.Effort)).Append(",\n");
    b.Append("    \"batchSize\": ").Append(settings.Ai.BatchSize.ToString(CultureInfo.InvariantCulture)).Append(",\n");
    b.Append("    \"instructions\": ").Append(CanonicalJson.Quote(settings.Ai.Instructions)).Append('\n');
    b.Append("  }\n");
    b.Append("}\n");
    return b.ToString();
  }

  public static Dictionary<string, ReviewEntry> ReadReview(string path)
  {
    Dictionary<string, ReviewEntry> result = new(StringComparer.Ordinal);
    if (!File.Exists(path))
    {
      return result;
    }
    return Parse(path, root =>
    {
      foreach (JsonProperty entry in root.EnumerateObject())
      {
        JsonElement e = entry.Value;
        ReviewState state = String(e, "state") switch
        {
          "confirmed" => ReviewState.Confirmed,
          "draft" => ReviewState.Draft,
          string other => throw new FormatException($"\"{entry.Name}\" has the unknown state \"{other}\" (use draft or confirmed)"),
          null => ReviewState.Draft,
        };
        DateTimeOffset at = DateTimeOffset.TryParse(String(e, "at"), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out DateTimeOffset parsed)
          ? parsed
          : DateTimeOffset.MinValue;
        result[entry.Name] = new ReviewEntry(state, String(e, "source") ?? "", String(e, "by") ?? "", at);
      }
      return result;
    });
  }

  public static string ReviewJson(IEnumerable<KeyValuePair<string, ReviewEntry>> review) =>
    CanonicalJson.Object(review, r => CanonicalJson.Inline(
      ("state", CanonicalJson.Quote(r.State == ReviewState.Confirmed ? "confirmed" : "draft")),
      ("source", CanonicalJson.Quote(r.Source)),
      ("by", CanonicalJson.Quote(r.By)),
      ("at", CanonicalJson.Quote(r.At.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture)))));

  public static List<Screenshot> ReadScreenshots(string path)
  {
    if (!File.Exists(path))
    {
      return [];
    }
    return Parse(path, root =>
    {
      List<Screenshot> result = [];
      foreach (JsonProperty entry in root.EnumerateObject())
      {
        JsonElement e = entry.Value;
        Screenshot screenshot = new()
        {
          Id = entry.Name,
          File = String(e, "file") ?? "",
          Title = String(e, "title") ?? "",
          Width = Int(e, "width") ?? 0,
          Height = Int(e, "height") ?? 0,
        };
        if (e.TryGetProperty("regions", out JsonElement regions))
        {
          foreach (JsonElement r in regions.EnumerateArray())
          {
            screenshot.Regions.Add(new Region
            {
              Key = String(r, "key") ?? "",
              X = Double(r, "x"),
              Y = Double(r, "y"),
              Width = Double(r, "width"),
              Height = Double(r, "height"),
              Align = String(r, "align") ?? "center",
              Background = String(r, "background") ?? "#ffffff",
              Color = String(r, "color") ?? "#000000",
              FontSize = Double(r, "fontSize"),
            });
          }
        }
        result.Add(screenshot);
      }
      return result;
    });
  }

  public static string ScreenshotsJson(IEnumerable<KeyValuePair<string, Screenshot>> screenshots) =>
    CanonicalJson.Object(screenshots, s =>
    {
      StringBuilder b = new();
      b.Append("{\n");
      b.Append("    \"file\": ").Append(CanonicalJson.Quote(s.File)).Append(",\n");
      b.Append("    \"title\": ").Append(CanonicalJson.Quote(s.Title)).Append(",\n");
      b.Append("    \"width\": ").Append(s.Width.ToString(CultureInfo.InvariantCulture)).Append(",\n");
      b.Append("    \"height\": ").Append(s.Height.ToString(CultureInfo.InvariantCulture)).Append(",\n");
      b.Append("    \"regions\": [");
      // Sorted by key, then position, so moving one region doesn't reorder the others in the diff.
      List<Region> regions = [.. s.Regions.OrderBy(r => r.Key, StringComparer.Ordinal).ThenBy(r => r.Y).ThenBy(r => r.X)];
      for (int i = 0; i < regions.Count; i++)
      {
        Region r = regions[i];
        b.Append(i == 0 ? "\n" : ",\n").Append("      ").Append(CanonicalJson.Inline(
          ("key", CanonicalJson.Quote(r.Key)),
          ("x", CanonicalJson.Number(r.X)),
          ("y", CanonicalJson.Number(r.Y)),
          ("width", CanonicalJson.Number(r.Width)),
          ("height", CanonicalJson.Number(r.Height)),
          ("align", CanonicalJson.Quote(r.Align)),
          ("background", CanonicalJson.Quote(r.Background)),
          ("color", CanonicalJson.Quote(r.Color)),
          // Left out when unknown, so regions from before it existed don't change.
          ("fontSize", r.FontSize > 0 ? CanonicalJson.Number(r.FontSize) : null)));
      }
      b.Append(regions.Count > 0 ? "\n    ]\n" : "]\n");
      b.Append("  }");
      return b.ToString();
    });

  /// <summary>Writes atomically (temp file, then rename), and only when the content changed.</summary>
  public static void Write(string path, string content)
  {
    if (File.Exists(path) && File.ReadAllText(path, Encoding.UTF8) == content)
    {
      return;
    }
    string? dir = Path.GetDirectoryName(path);
    if (!string.IsNullOrEmpty(dir))
    {
      System.IO.Directory.CreateDirectory(dir);
    }
    // A crash or a full disk halfway through must not leave a cut-off translation file that the
    // app then fails to parse; the rename is atomic on the same file system.
    string temp = path + ".tmp";
    File.WriteAllText(temp, content, new UTF8Encoding(false));
    if (File.Exists(path))
    {
      File.Replace(temp, path, null);
    }
    else
    {
      File.Move(temp, path);
    }
  }

  private static T Parse<T>(string path, Func<JsonElement, T> read)
  {
    try
    {
      using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path, Encoding.UTF8), Options);
      if (document.RootElement.ValueKind != JsonValueKind.Object)
      {
        throw new FormatException("expected a JSON object");
      }
      return read(document.RootElement);
    }
    catch (Exception e) when (e is JsonException or FormatException or InvalidOperationException)
    {
      throw new FormatException($"{path}: {e.Message}", e);
    }
  }

  private static string? String(JsonElement e, string name) =>
    e.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

  private static int? Int(JsonElement e, string name) =>
    e.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : null;

  private static double Double(JsonElement e, string name) =>
    e.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : 0;
}
