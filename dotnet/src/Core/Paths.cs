using System;
using System.IO;

namespace Rcinel.Translate.Core;

/// <summary>Path helpers netstandard2.0 lacks.</summary>
public static class Paths
{
  /// <summary>
  /// <paramref name="path"/> relative to <paramref name="baseDirectory"/>, with the platform's
  /// separator, or the full path when it isn't below it. Path.GetRelativePath arrived after
  /// netstandard2.0.
  /// </summary>
  public static string Relative(string baseDirectory, string path)
  {
    string root = Path.GetFullPath(baseDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
    string full = Path.GetFullPath(path);
    // Windows paths compare case-insensitively, Linux ones don't.
    StringComparison comparison = Path.DirectorySeparatorChar == '\\' ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
    return full.StartsWith(root, comparison) ? full.Substring(root.Length) : full;
  }
}
