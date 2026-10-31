using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace CINE.Translate.Core;

/// <summary>
/// Writes the project's files in one fixed layout: keys sorted ordinally (UUIDv7 order = creation
/// order), two spaces, one entry per line, "\n" line endings, a final newline, no BOM, and
/// non-ASCII text left readable instead of \u-escaped. Anything that writes the files must go
/// through here, otherwise the editor and the CLI fight over formatting in every diff.
/// </summary>
public static class CanonicalJson
{
  /// <summary>A JSON string literal with only the escapes JSON requires (plus U+2028/2029, which old JavaScript can't take raw).</summary>
  public static string Quote(string value)
  {
    StringBuilder builder = new(value.Length + 2);
    builder.Append('"');
    for (int i = 0; i < value.Length; i++)
    {
      char c = value[i];
      switch (c)
      {
        case '"': builder.Append("\\\""); break;
        case '\\': builder.Append("\\\\"); break;
        case '\n': builder.Append("\\n"); break;
        case '\r': builder.Append("\\r"); break;
        case '\t': builder.Append("\\t"); break;
        case '\b': builder.Append("\\b"); break;
        case '\f': builder.Append("\\f"); break;
        default:
          bool loneSurrogate = (char.IsHighSurrogate(c) && (i + 1 >= value.Length || !char.IsLowSurrogate(value[i + 1])))
            || (char.IsLowSurrogate(c) && (i == 0 || !char.IsHighSurrogate(value[i - 1])));
          if (c < ' ' || c == (char)0x2028 || c == (char)0x2029 || loneSurrogate)
          {
            builder.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
          }
          else
          {
            builder.Append(c);
          }
          break;
      }
    }
    builder.Append('"');
    return builder.ToString();
  }

  /// <summary>A number with at most four decimals, invariant culture. Enough for region coordinates (0.01 % of an image).</summary>
  public static string Number(double value) =>
    Math.Round(value, 4, MidpointRounding.AwayFromZero).ToString("0.####", CultureInfo.InvariantCulture);

  /// <summary>A flat object of strings: the format of translation files and notes.json.</summary>
  public static string FlatObject(IEnumerable<KeyValuePair<string, string>> entries) =>
    Object(entries, Quote);

  /// <summary>
  /// An object whose entries are sorted by key and each written on one line by
  /// <paramref name="writeValue"/>. One line per entry means a changed review state or translation
  /// is a one-line diff.
  /// </summary>
  public static string Object<T>(IEnumerable<KeyValuePair<string, T>> entries, Func<T, string> writeValue)
  {
    List<KeyValuePair<string, T>> sorted = [.. entries];
    sorted.Sort((a, b) => string.CompareOrdinal(a.Key, b.Key));
    if (sorted.Count == 0)
    {
      return "{}\n";
    }
    StringBuilder builder = new();
    builder.Append("{\n");
    for (int i = 0; i < sorted.Count; i++)
    {
      builder.Append("  ").Append(Quote(sorted[i].Key)).Append(": ").Append(writeValue(sorted[i].Value));
      builder.Append(i < sorted.Count - 1 ? ",\n" : "\n");
    }
    builder.Append("}\n");
    return builder.ToString();
  }

  /// <summary>An inline object, <c>{ "a": "b", "c": 1 }</c>, from already written values. Null values are left out.</summary>
  public static string Inline(params (string Name, string? Json)[] fields)
  {
    StringBuilder builder = new("{ ");
    bool first = true;
    foreach ((string name, string? json) in fields)
    {
      if (json is null)
      {
        continue;
      }
      if (!first)
      {
        builder.Append(", ");
      }
      builder.Append(Quote(name)).Append(": ").Append(json);
      first = false;
    }
    builder.Append(" }");
    return builder.ToString();
  }
}
