using System;
using System.IO;
using System.Threading;

namespace CINE.Translate;

/// <summary>
/// The app-wide entry point. Load a language once at start (and again when the user picks another),
/// then write every UI text as <c>Rcinel.Translate("UUIDv7", "English text")</c>.
/// </summary>
/// <remarks>
/// The English text in the call is the source of truth and the fallback: `rcinel scan` collects
/// the calls into the source file, and an id that has no translation (yet) simply shows the English
/// text. That's why there is nothing to configure before the first call.
/// </remarks>
public static class Rcinel
{
  // One reference swap per language change. Translate only reads it, so it needs no lock, and a
  // thread that reads the old set during a switch still gets a complete one.
  private static TranslationSet current = TranslationSet.Empty;

  /// <summary>
  /// Where <see cref="Load(string)"/> looks for the files: the "translations" folder next to the app.
  /// Ship the project's translations folder there (see README, "Shipping the files").
  /// </summary>
  public static string Directory { get; set; } = Path.Combine(AppContext.BaseDirectory, "translations");

  /// <summary>The loaded set. <see cref="TranslationSet.Empty"/> until a language is loaded.</summary>
  public static TranslationSet Current => Volatile.Read(ref current);

  /// <summary>The loaded language, such as "de". Empty while the app shows its English texts.</summary>
  public static string Language => Current.Language;

  /// <summary>Raised after the set changed, so a UI can redraw its texts.</summary>
  public static event EventHandler? LanguageChanged;

  /// <summary>The translation of <paramref name="id"/>, or <paramref name="english"/> when the loaded language has none.</summary>
  public static string Translate(string id, string english) => Current.Get(id, english);

  /// <summary>
  /// <see cref="Translate(string, string)"/>, then fills <c>{0}</c>, <c>{1}</c>, ... with
  /// <paramref name="args"/> using <see cref="string.Format(IFormatProvider, string, object[])"/>
  /// and the current culture. Named placeholders such as <c>{name}</c> are left for the app.
  /// </summary>
  public static string Translate(string id, string english, params object?[] args) =>
    string.Format(System.Globalization.CultureInfo.CurrentCulture, Translate(id, english), args);

  /// <summary>Loads <paramref name="language"/> from <see cref="Directory"/>, with its fallback chain (de-AT, then de).</summary>
  /// <exception cref="FileNotFoundException">There is no file for the language.</exception>
  public static void Load(string language) => Use(TranslationSet.Load(language, Directory));

  /// <summary>Loads <paramref name="language"/> from <paramref name="directory"/>, with its fallback chain.</summary>
  public static void Load(string language, string directory) => Use(TranslationSet.Load(language, directory));

  /// <summary>
  /// Loads <paramref name="language"/> if the app ships it and returns true; otherwise keeps the
  /// current set and returns false. Handy for following the system language:
  /// <c>Rcinel.TryLoad(CultureInfo.CurrentUICulture.Name)</c>.
  /// </summary>
  public static bool TryLoad(string language, string? directory = null)
  {
    if (!TranslationSet.TryLoad(language, directory ?? Directory, out TranslationSet? set))
    {
      return false;
    }
    Use(set!);
    return true;
  }

  /// <summary>Switches to a set the app built itself, for example from an embedded resource.</summary>
  public static void Use(TranslationSet set)
  {
    Volatile.Write(ref current, set ?? throw new ArgumentNullException(nameof(set)));
    LanguageChanged?.Invoke(null, EventArgs.Empty);
  }

  /// <summary>Goes back to the English texts of the calls.</summary>
  public static void Reset() => Use(TranslationSet.Empty);
}
