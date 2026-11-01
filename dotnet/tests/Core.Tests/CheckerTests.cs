using Rcinel.Translate.Core;

namespace Rcinel.Translate.Core.Tests;

public class CheckerTests
{
  private const string A = "0192f3a0-1c4e-7a10-8000-000000000001";
  private const string B = "0192f3a0-1c4e-7a10-8000-000000000002";
  private static readonly DateTimeOffset At = new(2026, 10, 31, 12, 0, 0, TimeSpan.Zero);

  [Theory]
  [InlineData("Hello {name}", "Hallo {name}", null)]
  [InlineData("{0} of {1}", "{1} von {0}", null)]
  [InlineData("Hello {name}", "Hallo {nmae}", "missing {name}, unexpected {nmae}")]
  [InlineData("%1$s has %2$d files", "%1$s hat %2$d Dateien", null)]
  [InlineData("%s files", "Dateien", "missing %s")]
  [InlineData("100%% sure", "100%% sicher", null)]
  [InlineData("<b>Bold</b> text", "<b>Fetter</b> Text", null)]
  [InlineData("<b>Bold</b>", "Fett", "missing </b> <b>")]
  [InlineData("{{count}} items", "{{count}} Einträge", null)]
  [InlineData("{} of {}", "{} von {}", null)]
  [InlineData("{} of {}", "{} von", "missing {}")]
  [InlineData("value: {:?}", "Wert:", "missing {:?}")]
  [InlineData("Hello %(name)s, %(count)d new", "%(count)d neue, hallo %(name)s", null)]
  [InlineData("Hello %(name)s", "Hallo", "missing %(name)s")]
  public void Placeholders_must_survive(string source, string translation, string? problem) =>
    Assert.Equal(problem, Placeholders.Compare(source, translation));

  [Fact]
  public void Finds_placeholder_orphan_and_format_problems()
  {
    using TempProject temp = new("de");
    TranslationProject p = temp.Project;
    p.Source[A] = "Hello {name}";
    p.SetTranslation("de", A, "Hallo", ReviewState.Confirmed, "alex", At);
    p.GetLanguage("de").Texts[B] = "Verwaist";
    p.Save();
    // A hand edit with other indentation.
    File.WriteAllText(Path.Combine(temp.ProjectDirectory, "translations", "en.json"), $"{{\"{A}\": \"Hello {{name}}\"}}");

    List<ProjectIssue> issues = ProjectChecker.Check(temp.Reload());
    Assert.Contains(issues, i => i is { Code: "placeholders", Severity: IssueSeverity.Error, Language: "de", Id: A });
    Assert.Contains(issues, i => i is { Code: "orphaned", Severity: IssueSeverity.Warning, Id: B });
    Assert.Contains(issues, i => i is { Code: "format", Severity: IssueSeverity.Warning } && i.Message.Contains("en.json"));
  }

  [Fact]
  public void RequireConfirmed_turns_open_work_into_errors()
  {
    using TempProject temp = new("de");
    TranslationProject p = temp.Project;
    p.Source[A] = "Save";
    p.Source[B] = "Cancel";
    p.SetTranslation("de", A, "Speichern", ReviewState.Draft, "ai:test", At);

    Assert.DoesNotContain(ProjectChecker.Check(p), i => i.Severity == IssueSeverity.Error);
    List<ProjectIssue> strict = ProjectChecker.Check(p, new CheckOptions { RequireConfirmed = true });
    Assert.Contains(strict, i => i.Code == "missing");
    Assert.Contains(strict, i => i.Code == "unconfirmed");
    List<ProjectIssue> complete = ProjectChecker.Check(p, new CheckOptions { RequireComplete = true });
    Assert.Contains(complete, i => i.Code == "missing");
    Assert.DoesNotContain(complete, i => i.Code == "unconfirmed");
  }

  [Fact]
  public void Export_can_leave_out_unconfirmed_text()
  {
    using TempProject temp = new("de");
    TranslationProject p = temp.Project;
    p.Source[A] = "Save";
    p.Source[B] = "Cancel";
    p.SetTranslation("de", A, "Speichern", ReviewState.Confirmed, "alex", At);
    p.SetTranslation("de", B, "Abbrechen", ReviewState.Draft, "ai:test", At);
    string output = Path.Combine(temp.Root, "out");

    Exporter.Export(p, output, confirmedOnly: true);
    TranslationSet set = TranslationSet.Load("de", output);
    Assert.Equal(1, set.Count);
    Assert.Equal("Speichern", set.Get(A, "Save"));

    Exporter.Export(p, output, confirmedOnly: false);
    Assert.Equal(2, TranslationSet.Load("de", output).Count);
    Assert.Equal(2, TranslationSet.Load("en", output).Count);
  }
}
