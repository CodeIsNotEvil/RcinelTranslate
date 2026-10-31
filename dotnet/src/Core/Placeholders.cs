using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Rcinel.Translate.Core;

/// <summary>
/// Finds the parts of a text that the app fills in or interprets: {0}, {name}, {{name}}, printf-style
/// %s / %1$d / %@, and markup tags such as &lt;b&gt;. A translation must keep all of them, or the
/// app shows "{nmae}" or crashes in String.format. Order may change, since languages order words
/// differently.
/// </summary>
public static class Placeholders
{
  private static readonly Regex Pattern = new(
    @"\{\{\s*[\w.]+\s*\}\}"                         // i18next / mustache {{name}}
    + @"|\{[\w.]+(?:[,:][^{}]*)?\}"                  // {0}, {name}, {0:N2}, {count, plural, ...} heads
    + @"|%(?:\d+\$)?[-#+ 0,(]*\d*(?:\.\d+)?[sdfiuxXeEgGcboaA@]" // printf, Java, Android, iOS
    + @"|</?[a-zA-Z][\w-]*(?:\s[^<>]*)?/?>",          // <b>, </b>, <br/>, <a href="...">
    RegexOptions.CultureInvariant);

  /// <summary>The placeholders of <paramref name="text"/>, sorted, duplicates kept.</summary>
  public static List<string> Find(string text)
  {
    // "%%" is a literal percent sign in printf, not a placeholder; drop it before matching so
    // "100%% sure" isn't read as "%% s".
    List<string> found = [.. Pattern.Matches(text.Replace("%%", "")).Cast<Match>().Select(m => m.Value)];
    found.Sort(System.StringComparer.Ordinal);
    return found;
  }

  /// <summary>
  /// What <paramref name="translation"/> lost or added compared with <paramref name="source"/>, as
  /// readable text, or null when both have the same placeholders.
  /// </summary>
  public static string? Compare(string source, string translation)
  {
    List<string> expected = Find(source);
    List<string> actual = Find(translation);
    List<string> missing = Subtract(expected, actual);
    List<string> extra = Subtract(actual, expected);
    if (missing.Count == 0 && extra.Count == 0)
    {
      return null;
    }
    List<string> parts = [];
    if (missing.Count > 0)
    {
      parts.Add("missing " + string.Join(" ", missing));
    }
    if (extra.Count > 0)
    {
      parts.Add("unexpected " + string.Join(" ", extra));
    }
    return string.Join(", ", parts);
  }

  private static List<string> Subtract(List<string> from, List<string> remove)
  {
    List<string> result = [.. from];
    foreach (string item in remove)
    {
      result.Remove(item);
    }
    return result;
  }
}
