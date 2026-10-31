using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace CINE.Translate;

/// <summary>
/// Reads a translation file: one JSON object whose values are all strings (docs/format.md).
/// Hand-written instead of System.Text.Json because netstandard2.0 has no JSON parser in the box,
/// and the client must not drag a package into the apps that use it. The Java client has a twin of
/// this reader; both are tested against spec/fixtures, so keep them in step.
/// </summary>
internal sealed class FlatJsonReader
{
  private readonly string text;
  private int position;

  private FlatJsonReader(string text) => this.text = text;

  /// <summary>Calls <paramref name="entry"/> for every key and value, in file order.</summary>
  /// <exception cref="FormatException">The text is not a flat JSON object of strings, or a key repeats.</exception>
  public static void Read(string text, Action<string, string> entry)
  {
    if (text is null)
    {
      throw new ArgumentNullException(nameof(text));
    }
    new FlatJsonReader(text).ReadObject(entry);
  }

  /// <summary>Reads the whole object into a dictionary that compares keys ordinally.</summary>
  public static Dictionary<string, string> ReadDictionary(string text)
  {
    Dictionary<string, string> result = new(StringComparer.Ordinal);
    Read(text, (key, value) => result.Add(key, value));
    return result;
  }

  private void ReadObject(Action<string, string> entry)
  {
    // A BOM is not JSON, but editors on Windows like to write one. Rejecting it would break files
    // that look fine to the person who saved them.
    if (text.Length > 0 && text[0] == (char)0xFEFF)
    {
      position = 1;
    }
    SkipWhitespace();
    Expect('{');
    SkipWhitespace();
    HashSet<string> seen = new(StringComparer.Ordinal);
    if (Peek() == '}')
    {
      position++;
    }
    else
    {
      while (true)
      {
        SkipWhitespace();
        int keyStart = position;
        string key = ReadString("a key in quotes");
        SkipWhitespace();
        Expect(':');
        SkipWhitespace();
        string value = ReadString("a string value (translation files hold only strings)");
        // A repeated key is legal JSON, but which text wins differs between parsers. The Java and
        // JavaScript clients must show the same text as this one, so it's an error everywhere.
        if (!seen.Add(key))
        {
          throw Error($"the key \"{key}\" appears twice", keyStart);
        }
        entry(key, value);
        SkipWhitespace();
        char next = Peek();
        if (next == ',')
        {
          position++;
          continue;
        }
        if (next == '}')
        {
          position++;
          break;
        }
        throw Error("expected ',' or '}'", position);
      }
    }
    SkipWhitespace();
    if (position < text.Length)
    {
      throw Error("unexpected text after the closing '}'", position);
    }
  }

  private string ReadString(string what)
  {
    if (Peek() != '"')
    {
      throw Error($"expected {what}", position);
    }
    position++;
    StringBuilder builder = new();
    while (true)
    {
      if (position >= text.Length)
      {
        throw Error("a string is not closed", position);
      }
      char c = text[position++];
      if (c == '"')
      {
        return builder.ToString();
      }
      if (c < ' ')
      {
        throw Error("control characters (such as line breaks) must be escaped, for example as \\n", position - 1);
      }
      if (c != '\\')
      {
        builder.Append(c);
        continue;
      }
      if (position >= text.Length)
      {
        throw Error("a string is not closed", position);
      }
      char escape = text[position++];
      switch (escape)
      {
        case '"': builder.Append('"'); break;
        case '\\': builder.Append('\\'); break;
        case '/': builder.Append('/'); break;
        case 'b': builder.Append('\b'); break;
        case 'f': builder.Append('\f'); break;
        case 'n': builder.Append('\n'); break;
        case 'r': builder.Append('\r'); break;
        case 't': builder.Append('\t'); break;
        case 'u':
          if (position + 4 > text.Length
            || !int.TryParse(text.Substring(position, 4), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out int code))
          {
            throw Error("\\u needs four hex digits", position - 2);
          }
          // Surrogate pairs (emoji are written as two \u escapes) come out right by appending the
          // halves one after the other, because .NET strings are UTF-16 as well.
          builder.Append((char)code);
          position += 4;
          break;
        default:
          throw Error($"unknown escape \\{escape}", position - 2);
      }
    }
  }

  private void SkipWhitespace()
  {
    while (position < text.Length && text[position] is ' ' or '\t' or '\n' or '\r')
    {
      position++;
    }
  }

  private char Peek() => position < text.Length ? text[position] : '\0';

  private void Expect(char expected)
  {
    if (Peek() != expected)
    {
      throw Error($"expected '{expected}'", position);
    }
    position++;
  }

  private FormatException Error(string message, int at)
  {
    int line = 1;
    int column = 1;
    for (int i = 0; i < at && i < text.Length; i++)
    {
      if (text[i] == '\n')
      {
        line++;
        column = 1;
      }
      else
      {
        column++;
      }
    }
    return new FormatException($"Line {line}, column {column}: {message}.");
  }
}
