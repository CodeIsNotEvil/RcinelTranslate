using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace CINE.Translate.Core;

/// <summary>
/// Writes the files an app ships to another folder, for builds that must not ship unreviewed text.
/// The translations folder in git holds drafts too (that's how reviewers see them in the app);
/// a release build runs `rcinel export --confirmed-only` and copies the result instead.
/// </summary>
public static class Exporter
{
  /// <summary>Writes the source file and one file per language into <paramref name="directory"/>. Returns the files written.</summary>
  public static List<string> Export(TranslationProject project, string directory, bool confirmedOnly)
  {
    Directory.CreateDirectory(directory);
    List<string> written = [];
    string sourceFile = Path.Combine(directory, project.Settings.SourceLanguage + ".json");
    ProjectFiles.Write(sourceFile, CanonicalJson.FlatObject(project.Source));
    written.Add(sourceFile);
    foreach (LanguageData language in project.Languages)
    {
      IEnumerable<KeyValuePair<string, string>> texts = language.Texts.Where(pair =>
        project.Source.ContainsKey(pair.Key)
        && (!confirmedOnly || project.GetState(language, pair.Key) == TranslationState.Confirmed));
      string file = Path.Combine(directory, language.Code + ".json");
      ProjectFiles.Write(file, CanonicalJson.FlatObject(texts));
      written.Add(file);
    }
    return written;
  }
}
