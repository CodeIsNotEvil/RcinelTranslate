using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace CINE.Translate.Core;

/// <summary>A Rcinel.Translate call found in the code.</summary>
public sealed record SourceString(string Id, string Text, string File, int Line);

/// <summary>Something about a call that the scanner couldn't take as it is.</summary>
public sealed record ScanIssue(bool IsError, string File, int Line, string Message);

/// <summary>What a scan found.</summary>
public sealed class ScanResult
{
  /// <summary>The strings by id, sorted (the future source file).</summary>
  public SortedDictionary<string, SourceString> Strings { get; } = new(StringComparer.Ordinal);
  public List<ScanIssue> Issues { get; } = [];
  public int FilesScanned { get; set; }

  /// <summary>Ids that were empty in the code and got one (only with assignIds).</summary>
  public int AssignedIds { get; set; }

  public bool HasErrors => Issues.Any(i => i.IsError);
}

/// <summary>What applying a scan changed in the source file.</summary>
public sealed record SourceUpdate(int Added, int Changed, int Removed);

/// <summary>
/// Collects Rcinel.Translate("id", "text") calls from C#, Java, Kotlin and JavaScript/TypeScript
/// files, like xgettext does for gettext. The code is the source of truth for the source texts; the
/// source file is generated from it, which is why the editor doesn't let anyone edit English.
/// </summary>
/// <remarks>
/// It matches text, it doesn't parse the languages: both arguments must be plain string literals
/// ("..." or '...', or `...` without ${}). Anything else (a variable, string concatenation, C#'s
/// $"..." or @"...") is reported, because a text the scanner can't read can't be translated.
/// </remarks>
public static class SourceScanner
{
  /// <summary>File types that are scanned by default.</summary>
  public static readonly string[] Extensions =
    [".cs", ".razor", ".cshtml", ".xaml", ".axaml", ".java", ".kt", ".kts", ".js", ".jsx", ".mjs", ".cjs", ".ts", ".tsx", ".mts", ".cts", ".vue", ".svelte"];

  // Build output and dependencies: big, generated, and full of other people's calls.
  private static readonly HashSet<string> SkippedFolders = new(StringComparer.OrdinalIgnoreCase)
  {
    "bin", "obj", "node_modules", "build", "dist", "out", "target", ".git", ".gradle", ".idea", ".vs", ".next", "coverage",
  };

  /// <summary>Scans the folders in <see cref="ProjectSettings.Sources"/> of <paramref name="project"/>.</summary>
  public static ScanResult Scan(TranslationProject project, bool assignIds = false) =>
    Scan([.. project.Settings.Sources.Select(s => Path.GetFullPath(Path.Combine(project.Directory, s)))], project.Settings.Functions, assignIds);

  /// <summary>
  /// Scans <paramref name="roots"/> (folders or single files). With <paramref name="assignIds"/>,
  /// calls with an empty id ("") get a new UUIDv7 written into the file, so a developer can write
  /// <c>Rcinel.Translate("", "Save")</c> and let the scan fill the id in.
  /// </summary>
  public static ScanResult Scan(IReadOnlyList<string> roots, IReadOnlyList<string> functions, bool assignIds = false)
  {
    ScanResult result = new();
    Regex call = CallPattern(functions);
    Regex anyCall = AnyCallPattern(functions);
    foreach (string file in roots.SelectMany(EnumerateFiles).Distinct(StringComparer.Ordinal).OrderBy(f => f, StringComparer.Ordinal))
    {
      result.FilesScanned++;
      ScanFile(file, call, anyCall, assignIds, result);
    }
    return result;
  }

  /// <summary>Replaces the source texts of <paramref name="project"/> with what the scan found.</summary>
  public static SourceUpdate Apply(TranslationProject project, ScanResult scan)
  {
    int added = 0, changed = 0;
    foreach (SourceString s in scan.Strings.Values)
    {
      if (!project.Source.TryGetValue(s.Id, out string? old))
      {
        added++;
      }
      else if (old != s.Text)
      {
        changed++;
      }
    }
    List<string> removed = [.. project.Source.Keys.Where(id => !scan.Strings.ContainsKey(id))];
    project.Source.Clear();
    foreach (SourceString s in scan.Strings.Values)
    {
      project.Source[s.Id] = s.Text;
    }
    // Translations of removed ids stay until `rcinel prune`: a string that comes back after a
    // refactor (or a revert) keeps its reviewed translations.
    return new SourceUpdate(added, changed, removed.Count);
  }

  private static IEnumerable<string> EnumerateFiles(string root)
  {
    if (File.Exists(root))
    {
      return [root];
    }
    if (!Directory.Exists(root))
    {
      return [];
    }
    return Walk(root);
  }

  private static IEnumerable<string> Walk(string dir)
  {
    foreach (string file in Directory.EnumerateFiles(dir))
    {
      if (Extensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase))
      {
        yield return file;
      }
    }
    foreach (string sub in Directory.EnumerateDirectories(dir))
    {
      if (!SkippedFolders.Contains(Path.GetFileName(sub)))
      {
        foreach (string file in Walk(sub))
        {
          yield return file;
        }
      }
    }
  }

  // "$name" or "${x}" in a Kotlin "..." string is filled in at run time, so the text in the code
  // isn't the text the user sees. C#'s $"..." and @"..." never reach the call pattern at all: it
  // wants the quote right after the comma, so they're reported as unreadable calls.
  private static readonly Regex KotlinTemplate = new(@"(?<!\\)\$(?:[A-Za-z_{])", RegexOptions.CultureInvariant);

  private static string FunctionAlternatives(IReadOnlyList<string> functions) =>
    string.Join("|", functions.Where(f => !string.IsNullOrWhiteSpace(f))
      .Select(f => string.Join(@"\s*\.\s*", f.Trim().Split('.').Select(Regex.Escape))));

  private static Regex CallPattern(IReadOnlyList<string> functions) => new(
    // (?<![\w.]) so "MyRcinel.Translate" or "x.Rcinel.Translate" in another API don't count, but
    // "CINE.Translate.Rcinel.Translate" would need the full name in functions.
    @"(?<![\w.])(?:" + FunctionAlternatives(functions) + @")\s*\(\s*"
    + @"(?<q1>[""'`])(?<id>[^""'`\\\r\n]*)\k<q1>\s*,\s*"
    + @"(?<q2>[""'`])(?<text>(?:\\.|(?!\k<q2>)[^\\])*?)\k<q2>\s*(?<end>[,)])",
    RegexOptions.CultureInvariant | RegexOptions.Singleline);

  private static Regex AnyCallPattern(IReadOnlyList<string> functions) => new(
    @"(?<![\w.])(?:" + FunctionAlternatives(functions) + @")\s*\(",
    RegexOptions.CultureInvariant);

  private static void ScanFile(string file, Regex call, Regex anyCall, bool assignIds, ScanResult result)
  {
    string content = File.ReadAllText(file, Encoding.UTF8);
    if (!anyCall.IsMatch(content))
    {
      return;
    }
    bool isKotlin = Path.GetExtension(file) is ".kt" or ".kts";
    HashSet<int> matchedStarts = [];
    // Insertions of new ids, applied from the end so earlier offsets stay valid.
    List<(int Index, string Id)> insertions = [];
    foreach (Match m in call.Matches(content))
    {
      matchedStarts.Add(m.Index);
      int line = LineOf(content, m.Index);
      string quote = m.Groups["q2"].Value;
      string raw = m.Groups["text"].Value;
      if (quote == "`" && raw.Contains("${"))
      {
        result.Issues.Add(new ScanIssue(true, file, line, "the text is a template literal with ${...}; pass the values separately, for example with {name} placeholders"));
        continue;
      }
      if (isKotlin && quote == "\"" && KotlinTemplate.IsMatch(raw))
      {
        result.Issues.Add(new ScanIssue(true, file, line, "the text is a Kotlin string template with $; pass the values separately, for example with {name} placeholders"));
        continue;
      }
      string text = Unescape(raw);
      string id = m.Groups["id"].Value;
      if (id.Length == 0)
      {
        if (!assignIds)
        {
          result.Issues.Add(new ScanIssue(true, file, line, $"\"{Shorten(text)}\" has no id yet; run `rcinel scan --assign` to give it one"));
          continue;
        }
        id = UuidV7.New();
        insertions.Add((m.Groups["id"].Index, id));
        result.AssignedIds++;
      }
      else if (!UuidV7.IsValid(id))
      {
        result.Issues.Add(new ScanIssue(true, file, line, $"\"{id}\" is not a lowercase UUIDv7; get one with `rcinel id`"));
        continue;
      }
      if (result.Strings.TryGetValue(id, out SourceString? existing))
      {
        // The same id twice with the same text is fine (a string used in two places); with
        // different texts one of them would be translated wrongly.
        if (existing.Text != text)
        {
          result.Issues.Add(new ScanIssue(true, file, line,
            $"{id} is also used at {existing.File}:{existing.Line} with another text (\"{Shorten(existing.Text)}\" vs \"{Shorten(text)}\"); every text needs its own id"));
        }
        continue;
      }
      result.Strings[id] = new SourceString(id, text, file, line);
    }
    foreach (Match m in anyCall.Matches(content))
    {
      if (!matchedStarts.Contains(m.Index))
      {
        result.Issues.Add(new ScanIssue(false, file, LineOf(content, m.Index),
          "a translate call whose id and text aren't both plain string literals; the scanner can't read it, so it won't be translated"));
      }
    }
    if (insertions.Count > 0)
    {
      StringBuilder updated = new(content);
      foreach ((int index, string id) in insertions.OrderByDescending(i => i.Index))
      {
        updated.Insert(index, id);
      }
      File.WriteAllText(file, updated.ToString(), DetectEncoding(file));
    }
  }

  // Keep a BOM if the file had one, so --assign doesn't show up as a whole-file change.
  private static Encoding DetectEncoding(string file)
  {
    byte[] start = new byte[3];
    using (FileStream stream = File.OpenRead(file))
    {
      _ = stream.Read(start, 0, 3);
    }
    return new UTF8Encoding(start[0] == 0xEF && start[1] == 0xBB && start[2] == 0xBF);
  }

  /// <summary>Turns the escapes C#, Java, Kotlin and JavaScript share into the characters they stand for.</summary>
  internal static string Unescape(string raw)
  {
    if (raw.IndexOf('\\') < 0)
    {
      return raw;
    }
    StringBuilder b = new(raw.Length);
    for (int i = 0; i < raw.Length; i++)
    {
      char c = raw[i];
      if (c != '\\' || i + 1 >= raw.Length)
      {
        b.Append(c);
        continue;
      }
      char e = raw[++i];
      switch (e)
      {
        case 'n': b.Append('\n'); break;
        case 't': b.Append('\t'); break;
        case 'r': b.Append('\r'); break;
        case '0': b.Append('\0'); break;
        case 'b': b.Append('\b'); break;
        case 'f': b.Append('\f'); break;
        case 'u' when i + 1 < raw.Length && raw[i + 1] == '{':
          // JavaScript's \u{1F600}
          int close = raw.IndexOf('}', i);
          if (close > 0 && int.TryParse(raw.Substring(i + 2, close - i - 2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out int cp))
          {
            b.Append(char.ConvertFromUtf32(cp));
            i = close;
          }
          else
          {
            b.Append(e);
          }
          break;
        case 'u' when TryHex(raw, i + 1, 4, out int code):
          b.Append((char)code);
          i += 4;
          break;
        case 'x' when TryHex(raw, i + 1, 2, out int hex):
          b.Append((char)hex);
          i += 2;
          break;
        case '\r':
        case '\n':
          // A JavaScript line continuation: the backslash and the line break both vanish.
          if (e == '\r' && i + 1 < raw.Length && raw[i + 1] == '\n')
          {
            i++;
          }
          break;
        default:
          // \" \' \` \\ \$ and anything unknown stand for themselves.
          b.Append(e);
          break;
      }
    }
    return b.ToString();
  }

  private static bool TryHex(string s, int start, int length, out int value)
  {
    value = 0;
    return start + length <= s.Length
      && int.TryParse(s.Substring(start, length), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out value);
  }

  private static int LineOf(string content, int index)
  {
    int line = 1;
    for (int i = 0; i < index; i++)
    {
      if (content[i] == '\n')
      {
        line++;
      }
    }
    return line;
  }

  private static string Shorten(string text) => text.Length <= 40 ? text : text.Substring(0, 37) + "...";
}
