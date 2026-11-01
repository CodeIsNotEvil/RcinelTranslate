using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace Rcinel.Translate.Core;

/// <summary>
/// UUIDv7 (RFC 9562) ids, the keys of every string. The first 48 bits are the Unix time in
/// milliseconds, big-endian, so the lowercase text form sorts by creation time with a plain ordinal
/// comparison. That's what keeps the files diff-friendly: new strings land at the end of each file
/// instead of in the middle (decision 0002 in the second brain).
/// </summary>
public static class UuidV7
{
  // Lowercase only. "019A..." and "019a..." would be the same UUID but different keys to the
  // clients, which look ids up with an ordinal comparison to stay fast.
  private static readonly Regex Pattern = new(
    "^[0-9a-f]{8}-[0-9a-f]{4}-7[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$",
    RegexOptions.CultureInvariant);

  private static readonly object Gate = new();
  private static readonly RandomNumberGenerator Random = RandomNumberGenerator.Create();
  private static long lastMilliseconds;
  private static int counter;

  /// <summary>True when <paramref name="id"/> is a UUIDv7 in lowercase canonical form.</summary>
  public static bool IsValid(string? id) => id is not null && Pattern.IsMatch(id);

  /// <summary>The creation time stored in the id.</summary>
  /// <exception cref="FormatException">The id is not a valid UUIDv7.</exception>
  public static DateTimeOffset GetTimestamp(string id)
  {
    if (!IsValid(id))
    {
      throw new FormatException($"\"{id}\" is not a lowercase UUIDv7.");
    }
    string hex = id.Substring(0, 8) + id.Substring(9, 4);
    long milliseconds = long.Parse(hex, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture);
    return DateTimeOffset.FromUnixTimeMilliseconds(milliseconds);
  }

  /// <summary>A new id for the current time.</summary>
  public static string New() => New(DateTimeOffset.UtcNow);

  /// <summary>
  /// A new id for <paramref name="now"/>. Ids made in the same millisecond count up in the 12
  /// "rand_a" bits (RFC 9562 method 1), so ids from one `rtr scan --assign` run still sort in the
  /// order they were made.
  /// </summary>
  public static string New(DateTimeOffset now)
  {
    byte[] bytes = new byte[16];
    long milliseconds;
    int sequence;
    lock (Gate)
    {
      milliseconds = now.ToUnixTimeMilliseconds();
      if (milliseconds <= lastMilliseconds)
      {
        // Same millisecond, or the clock went back: keep counting from the last id instead.
        milliseconds = lastMilliseconds;
        counter++;
        if (counter > 0xFFF)
        {
          milliseconds++;
          counter = 0;
        }
      }
      else
      {
        counter = 0;
      }
      lastMilliseconds = milliseconds;
      sequence = counter;
      Random.GetBytes(bytes);
    }
    for (int i = 0; i < 6; i++)
    {
      bytes[i] = (byte)(milliseconds >> (8 * (5 - i)));
    }
    bytes[6] = (byte)(0x70 | ((sequence >> 8) & 0x0F));
    bytes[7] = (byte)sequence;
    bytes[8] = (byte)(0x80 | (bytes[8] & 0x3F));
    string hex = BitConverter.ToString(bytes).Replace("-", string.Empty).ToLowerInvariant();
    return $"{hex.Substring(0, 8)}-{hex.Substring(8, 4)}-{hex.Substring(12, 4)}-{hex.Substring(16, 4)}-{hex.Substring(20, 12)}";
  }
}
