using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Rcinel.Translate;

/// <summary>
/// The translations of one language, read from the shipped files. Immutable, so one set can be
/// shared by every thread of the app.
/// </summary>
public sealed class TranslationSet
{
  private readonly Dictionary<string, string> texts;

  private TranslationSet(string language, Dictionary<string, string> texts)
  {
    Language = language;
    this.texts = texts;
  }

  /// <summary>An empty set: every id falls back to the English text from the call.</summary>
  public static TranslationSet Empty { get; } = new(string.Empty, new Dictionary<string, string>(StringComparer.Ordinal));

  /// <summary>The language code, such as "de" or "de-AT". Empty for <see cref="Empty"/>.</summary>
  public string Language { get; }

  /// <summary>How many ids have a translation.</summary>
  public int Count => texts.Count;

  /// <summary>Every id that has a translation.</summary>
  public IEnumerable<string> Ids => texts.Keys;

  /// <summary>Looks up the translation of an id.</summary>
  public bool TryGet(string id, out string text)
  {
    if (id is not null && texts.TryGetValue(id, out string? found))
    {
      text = found;
      return true;
    }
    text = string.Empty;
    return false;
  }

  /// <summary>The translation of <paramref name="id"/>, or <paramref name="fallback"/> when there is none.</summary>
  public string Get(string id, string fallback) => TryGet(id, out string text) ? text : fallback;

  /// <summary>Reads a set from the text of a translation file.</summary>
  /// <exception cref="FormatException">The text is not a valid translation file.</exception>
  public static TranslationSet Parse(string language, string json) =>
    new(language ?? throw new ArgumentNullException(nameof(language)), FlatJsonReader.ReadDictionary(json));

  /// <summary>Reads a set from a stream, for embedded resources or Android assets. The stream is read as UTF-8.</summary>
  public static TranslationSet FromStream(string language, Stream stream)
  {
    using StreamReader reader = new(stream, new UTF8Encoding(false), detectEncodingFromByteOrderMarks: true);
    return Parse(language, reader.ReadToEnd());
  }

  /// <summary>Reads a set from one translation file.</summary>
  public static TranslationSet FromFile(string language, string path)
  {
    try
    {
      return Parse(language, File.ReadAllText(path, Encoding.UTF8));
    }
    catch (FormatException e)
    {
      // Without the path the app's log says "Line 3, column 7" and nobody knows which file.
      throw new FormatException($"{path}: {e.Message}", e);
    }
  }

  /// <summary>
  /// Loads <paramref name="language"/> from <paramref name="directory"/> with its fallback chain:
  /// "de-AT" reads de.json and then de-AT.json on top of it, so a regional file only needs the
  /// strings that differ. Files of the chain that don't exist are skipped.
  /// </summary>
  /// <exception cref="FileNotFoundException">No file of the chain exists.</exception>
  public static TranslationSet Load(string language, string directory)
  {
    if (!TryLoad(language, directory, out TranslationSet? set))
    {
      throw new FileNotFoundException(
        $"There is no translation file for \"{language}\" in {directory} (looked for {string.Join(", ", FallbackChain(language))}).");
    }
    return set!;
  }

  /// <summary>
  /// Like <see cref="Load"/>, but returns false instead of throwing when no file of the chain exists,
  /// or when <paramref name="language"/> is empty: the invariant culture's name is "", which is
  /// what CultureInfo.CurrentUICulture.Name gives in many containers.
  /// </summary>
  public static bool TryLoad(string language, string directory, out TranslationSet? set)
  {
    if (string.IsNullOrWhiteSpace(language))
    {
      set = null;
      return false;
    }
    Dictionary<string, string> merged = new(StringComparer.Ordinal);
    bool found = false;
    // FallbackChain goes from specific to general; apply general first so specific files win.
    List<string> chain = FallbackChain(language);
    for (int i = chain.Count - 1; i >= 0; i--)
    {
      string path = Path.Combine(directory, chain[i] + ".json");
      if (!File.Exists(path))
      {
        continue;
      }
      found = true;
      foreach (KeyValuePair<string, string> pair in FromFile(chain[i], path).texts)
      {
        merged[pair.Key] = pair.Value;
      }
    }
    set = found ? new TranslationSet(language, merged) : null;
    return found;
  }

  /// <summary>
  /// The file names, without .json, that make up <paramref name="language"/>, most specific first:
  /// "zh-Hant-TW" gives zh-Hant-TW, zh-Hant, zh. Underscores count as dashes, so "de_AT" works too.
  /// </summary>
  public static List<string> FallbackChain(string language)
  {
    if (string.IsNullOrWhiteSpace(language))
    {
      throw new ArgumentException("A language code is needed, such as \"de\".", nameof(language));
    }
    string code = language.Trim().Replace('_', '-');
    List<string> chain = [];
    while (true)
    {
      chain.Add(code);
      int dash = code.LastIndexOf('-');
      if (dash <= 0)
      {
        return chain;
      }
      code = code.Substring(0, dash);
    }
  }
}
