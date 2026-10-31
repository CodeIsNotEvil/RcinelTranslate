using System;
using System.Collections.Generic;

namespace Rcinel.Translate.Core;

/// <summary>rcinel.json: what the project is and where its files live (docs/format.md, "The project file").</summary>
public sealed class ProjectSettings
{
  /// <summary>The version of the file layout. Raised only for changes old tools can't read.</summary>
  public const int CurrentFormat = 1;

  public int Format { get; set; } = CurrentFormat;

  /// <summary>Shown in the editor and given to the AI as context ("translating the UI of ...").</summary>
  public string Name { get; set; } = "";

  /// <summary>The language the texts in the code are written in. "en" unless the app is written in another language.</summary>
  public string SourceLanguage { get; set; } = "en";

  /// <summary>The folder with the shipped files, relative to rcinel.json. Apps copy this folder.</summary>
  public string Translations { get; set; } = "translations";

  /// <summary>Folders `rcinel scan` searches for Rcinel.Translate calls, relative to rcinel.json.</summary>
  public List<string> Sources { get; set; } = [];

  /// <summary>
  /// The calls `rcinel scan` looks for. The default covers C# (Translate, after
  /// `using static Rcinel.Translate.Rcinel;`) and Java and JavaScript (Rcinel.translate). Add more if
  /// the app wraps the call, for example "T".
  /// </summary>
  public List<string> Functions { get; set; } = ["Translate", "Rcinel.translate"];

  /// <summary>The target languages, in the order the editor lists them.</summary>
  public List<LanguageSettings> Languages { get; set; } = [];

  public AiSettings Ai { get; set; } = new();
}

/// <summary>One target language.</summary>
public sealed class LanguageSettings
{
  /// <summary>The file name without .json, a BCP 47 tag such as "de", "pt-BR" or "zh-Hant".</summary>
  public string Code { get; set; } = "";

  /// <summary>The name translators see, such as "Deutsch". Optional.</summary>
  public string Name { get; set; } = "";

  /// <summary>Style rules for this language only, such as "Address the user informally (du)". Given to the AI and shown to reviewers.</summary>
  public string Instructions { get; set; } = "";

  public string DisplayName => string.IsNullOrWhiteSpace(Name) ? Code : $"{Name} ({Code})";
}

/// <summary>How the AI drafts translations.</summary>
public sealed class AiSettings
{
  public const string DefaultModel = "claude-opus-5-5";

  public string Model { get; set; } = DefaultModel;

  /// <summary>low, medium, high, xhigh or max. More effort is slower and costs more.</summary>
  public string Effort { get; set; } = "medium";

  /// <summary>Rules for every language: tone, product names that stay English, a glossary.</summary>
  public string Instructions { get; set; } = "";

  /// <summary>How many strings go into one request.</summary>
  public int BatchSize { get; set; } = 40;
}

/// <summary>Where a translation stands for one language.</summary>
public enum TranslationState
{
  /// <summary>No translation: the app shows the source text.</summary>
  Missing,
  /// <summary>Translated, but nobody confirmed it yet (AI drafts, imports, hand edits of the file).</summary>
  Draft,
  /// <summary>Was reviewed, but the source text changed since.</summary>
  Stale,
  /// <summary>A reviewer confirmed it for the current source text.</summary>
  Confirmed,
}

/// <summary>What a reviewer decided, as stored in review/&lt;language&gt;.json.</summary>
public enum ReviewState
{
  Draft,
  Confirmed,
}

/// <summary>One line of review/&lt;language&gt;.json.</summary>
/// <param name="State">Draft or confirmed.</param>
/// <param name="Source">The <see cref="SourceHash"/> of the source text the translation was made for.</param>
/// <param name="By">Who wrote or confirmed it: a user name, or "ai:&lt;model&gt;".</param>
/// <param name="At">When, in UTC.</param>
public sealed record ReviewEntry(ReviewState State, string Source, string By, DateTimeOffset At);

/// <summary>A UI screenshot with the places where strings appear on it.</summary>
public sealed class Screenshot
{
  /// <summary>A UUIDv7, so screenshots sort by when they were added too.</summary>
  public string Id { get; set; } = "";

  /// <summary>The image file name in context/screenshots.</summary>
  public string File { get; set; } = "";

  public string Title { get; set; } = "";
  public int Width { get; set; }
  public int Height { get; set; }
  public List<Region> Regions { get; set; } = [];
}

/// <summary>
/// The box a string fills on a screenshot. Coordinates are fractions of the image (0 to 1), so they
/// survive a screenshot being replaced by one at another resolution.
/// </summary>
public sealed class Region
{
  public string Key { get; set; } = "";
  public double X { get; set; }
  public double Y { get; set; }
  public double Width { get; set; }
  public double Height { get; set; }

  /// <summary>left, center or right.</summary>
  public string Align { get; set; } = "center";

  /// <summary>The colour behind the text, as #rrggbb. The preview paints it over the original text.</summary>
  public string Background { get; set; } = "#ffffff";

  /// <summary>The text colour, as #rrggbb.</summary>
  public string Color { get; set; } = "#000000";

  /// <summary>
  /// The font size of the original text as a fraction of the image height, measured from the
  /// pixels when the box was drawn; 0 when unknown. The preview shows translations at this size,
  /// so "too long" means too long for the font the app really uses, not for the box.
  /// </summary>
  public double FontSize { get; set; }
}

/// <summary>How far one language is.</summary>
public sealed record LanguageProgress(string Language, int Total, int Confirmed, int Draft, int Stale, int Missing, int Orphaned)
{
  /// <summary>Strings that have some translation, confirmed or not.</summary>
  public int Translated => Total - Missing;

  /// <summary>0 to 100, rounded down so 99.9 % never shows as done.</summary>
  public int ConfirmedPercent => Percent(Confirmed);

  public int TranslatedPercent => Percent(Translated);

  private int Percent(int count) => Total == 0 ? 100 : (int)Math.Floor(count * 100.0 / Total);
}
