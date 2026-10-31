using Rcinel.Translate.Core;

namespace Rcinel.Translate.Core.Tests;

public class TranslationProjectTests
{
  private const string Save = "0192f3a0-1c4e-7a10-8000-000000000001";
  private const string Cancel = "0192f3a0-1c4e-7a10-8000-000000000002";
  private const string Hello = "0192f3a0-1c4e-7a10-8000-000000000003";
  private static readonly DateTimeOffset At = new(2026, 10, 31, 12, 0, 0, TimeSpan.Zero);

  private static TempProject WithSource()
  {
    TempProject temp = new("de", "fr");
    temp.Project.Source[Save] = "Save";
    temp.Project.Source[Cancel] = "Cancel";
    temp.Project.Source[Hello] = "Hello {name}!";
    temp.Project.Save();
    return temp;
  }

  [Fact]
  public void Create_writes_the_layout()
  {
    using TempProject temp = new("de");
    string dir = temp.ProjectDirectory;
    Assert.True(File.Exists(Path.Combine(dir, "rcinel.json")));
    Assert.True(File.Exists(Path.Combine(dir, "translations", "en.json")));
    Assert.True(File.Exists(Path.Combine(dir, "translations", "de.json")));
    Assert.True(File.Exists(Path.Combine(dir, "review", "de.json")));
    Assert.True(File.Exists(Path.Combine(dir, "context", "notes.json")));
    Assert.True(File.Exists(Path.Combine(dir, "context", "screenshots.json")));
  }

  [Fact]
  public void States_follow_the_review_and_the_source_text()
  {
    using TempProject temp = WithSource();
    TranslationProject p = temp.Project;
    Assert.Equal(TranslationState.Missing, p.GetState("de", Save));

    p.SetTranslation("de", Save, "Speichern", ReviewState.Confirmed, "alex", At);
    p.SetTranslation("de", Cancel, "Abbrechen", ReviewState.Draft, "ai:claude-opus-5-5", At);
    Assert.Equal(TranslationState.Confirmed, p.GetState("de", Save));
    Assert.Equal(TranslationState.Draft, p.GetState("de", Cancel));

    // The developer changes the English text: the confirmed translation needs another look.
    p.Source[Save] = "Save changes";
    Assert.Equal(TranslationState.Stale, p.GetState("de", Save));

    // A text somebody typed into the file by hand has no review: it counts as a draft.
    p.GetLanguage("de").Texts[Hello] = "Hallo {name}!";
    Assert.Equal(TranslationState.Draft, p.GetState("de", Hello));

    LanguageProgress progress = p.GetProgress("de");
    Assert.Equal(new LanguageProgress("de", 3, 0, 2, 1, 0, 0), progress);
    Assert.Equal(100, progress.TranslatedPercent);
    Assert.Equal(0, progress.ConfirmedPercent);
  }

  [Fact]
  public void Percentages_round_down()
  {
    LanguageProgress progress = new("de", 1000, 999, 0, 0, 1, 0);
    Assert.Equal(99, progress.ConfirmedPercent);
    Assert.Equal(100, new LanguageProgress("de", 0, 0, 0, 0, 0, 0).ConfirmedPercent);
  }

  [Fact]
  public void Saves_and_loads_everything()
  {
    using TempProject temp = WithSource();
    TranslationProject p = temp.Project;
    p.SetTranslation("de", Save, "Speichern", ReviewState.Confirmed, "alex", At);
    p.Notes[Save] = "Button that stores the form";
    p.Screenshots["0192f3a0-1c4e-7a10-8000-0000000000aa"] = new Screenshot
    {
      Id = "0192f3a0-1c4e-7a10-8000-0000000000aa",
      File = "settings.png",
      Title = "Settings",
      Width = 1280,
      Height = 800,
      Regions = [new Region { Key = Save, X = 0.1, Y = 0.2, Width = 0.15, Height = 0.05, Background = "#123456", Color = "#fedcba", Align = "left", FontSize = 0.0225 }],
    };
    p.Settings.Languages[0].Instructions = "Informal (du).";
    p.Settings.Ai.Instructions = "Keep \"RcinelTranslate\" in English.";
    p.Save();

    TranslationProject loaded = temp.Reload();
    Assert.Equal("Speichern", loaded.GetLanguage("de").Texts[Save]);
    Assert.Equal(TranslationState.Confirmed, loaded.GetState("de", Save));
    Assert.Equal(new ReviewEntry(ReviewState.Confirmed, SourceHash.Of("Save"), "alex", At), loaded.GetLanguage("de").Review[Save]);
    Assert.Equal("Button that stores the form", loaded.Notes[Save]);
    Region region = Assert.Single(loaded.Screenshots.Values.Single().Regions);
    Assert.Equal((0.1, 0.2, 0.15, 0.05, "#123456", "#fedcba", "left", 0.0225), (region.X, region.Y, region.Width, region.Height, region.Background, region.Color, region.Align, region.FontSize));
    Assert.Equal("Informal (du).", loaded.Settings.Languages[0].Instructions);
    Assert.Equal("Keep \"RcinelTranslate\" in English.", loaded.Settings.Ai.Instructions);
    Assert.Equal(["de", "fr"], loaded.Languages.Select(l => l.Code));
    Assert.Empty(ProjectChecker.NonCanonicalFiles(loaded));
  }

  [Fact]
  public void Review_files_have_one_line_per_entry()
  {
    using TempProject temp = WithSource();
    temp.Project.SetTranslation("de", Save, "Speichern", ReviewState.Confirmed, "alex", At);
    temp.Project.Save();
    string review = File.ReadAllText(Path.Combine(temp.ProjectDirectory, "review", "de.json"));
    Assert.Equal(
      $"{{\n  \"{Save}\": {{ \"state\": \"confirmed\", \"source\": \"{SourceHash.Of("Save")}\", \"by\": \"alex\", \"at\": \"2026-10-31T12:00:00Z\" }}\n}}\n",
      review);
  }

  [Fact]
  public void Unchanged_files_are_not_rewritten()
  {
    using TempProject temp = WithSource();
    string file = Path.Combine(temp.ProjectDirectory, "translations", "de.json");
    DateTime old = new(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    File.SetLastWriteTimeUtc(file, old);
    temp.Project.Save();
    Assert.Equal(old, File.GetLastWriteTimeUtc(file));
  }

  [Fact]
  public void Prune_removes_what_the_source_lost()
  {
    using TempProject temp = WithSource();
    TranslationProject p = temp.Project;
    p.SetTranslation("de", Cancel, "Abbrechen", ReviewState.Confirmed, "alex", At);
    p.Notes[Cancel] = "note";
    p.Source.Remove(Cancel);
    Assert.Equal(1, p.GetProgress("de").Orphaned);
    Assert.Equal(2, p.Prune());
    Assert.Empty(p.GetLanguage("de").Texts);
    Assert.Empty(p.GetLanguage("de").Review);
  }

  [Fact]
  public void Finds_the_project_from_a_folder_below_it_or_its_i18n_folder()
  {
    using TempProject temp = WithSource();
    string deep = Path.Combine(temp.ProjectDirectory, "translations");
    Assert.Equal(temp.Project.ProjectFile, TranslationProject.FindProjectFile(deep));
    Assert.Equal(temp.Project.ProjectFile, TranslationProject.FindProjectFile(temp.Root));
  }

  [Fact]
  public void Broken_files_name_themselves()
  {
    using TempProject temp = WithSource();
    File.WriteAllText(Path.Combine(temp.ProjectDirectory, "translations", "de.json"), "{ \"a\": 1 }");
    FormatException error = Assert.Throws<FormatException>(() => temp.Reload());
    Assert.Contains("de.json", error.Message);
  }

  [Fact]
  public void AddLanguage_rejects_duplicates_and_odd_codes()
  {
    using TempProject temp = WithSource();
    Assert.Throws<ArgumentException>(() => temp.Project.AddLanguage(new LanguageSettings { Code = "de" }));
    Assert.Throws<ArgumentException>(() => temp.Project.AddLanguage(new LanguageSettings { Code = "en" }));
    Assert.Throws<ArgumentException>(() => temp.Project.AddLanguage(new LanguageSettings { Code = "../x" }));
    temp.Project.AddLanguage(new LanguageSettings { Code = "pt-BR", Name = "Português" });
    temp.Project.Save();
    Assert.True(temp.Reload().HasLanguage("pt-BR"));
  }

  [Fact]
  public void SetTranslation_needs_the_id_in_the_source()
  {
    using TempProject temp = WithSource();
    Assert.Throws<KeyNotFoundException>(() =>
      temp.Project.SetTranslation("de", "0192f3a0-1c4e-7a10-8000-0000000000ff", "x", ReviewState.Draft, "a", At));
  }
}
