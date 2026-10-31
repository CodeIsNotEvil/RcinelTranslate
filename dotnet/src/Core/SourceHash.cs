using System.Security.Cryptography;
using System.Text;

namespace Rcinel.Translate.Core;

/// <summary>
/// A short fingerprint of a source text, stored with each review. When the English text in the code
/// changes, the stored hash no longer matches and the translation shows as stale. A hash instead of
/// the text itself keeps review files small and their diffs about reviews, not about English.
/// </summary>
public static class SourceHash
{
  /// <summary>The first 16 hex digits of the SHA-256 of the UTF-8 text. 64 bits: collisions don't happen in practice.</summary>
  public static string Of(string text)
  {
    using SHA256 sha = SHA256.Create();
    byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(text));
    StringBuilder builder = new(16);
    for (int i = 0; i < 8; i++)
    {
      builder.Append(hash[i].ToString("x2"));
    }
    return builder.ToString();
  }
}
