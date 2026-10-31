using System.Text.Json;
using Rcinel.Translate.Core;

namespace Rcinel.Translate.Cli.Tests;

/// <summary>The whole flow a developer goes through, in a temp folder: init, write code, scan, check, export.</summary>
public sealed class CliTests : IDisposable
{
  private readonly string root = Path.Combine(Path.GetTempPath(), "rcinel-cli-tests", Guid.NewGuid().ToString("N"));
  private readonly StringWriter output = new();
  private readonly StringWriter error = new();

  public CliTests() => Directory.CreateDirectory(root);

  public void Dispose() => Directory.Delete(root, recursive: true);

  private int Run(params string[] args)
  {
    output.GetStringBuilder().Clear();
    error.GetStringBuilder().Clear();
    return new Cli(output, error).Run(args);
  }

  private string I18n => Path.Combine(root, "i18n");

  [Fact]
  public void Init_scan_status_check_export()
  {
    Assert.Equal(Cli.Ok, Run("init", I18n, "--name", "Demo", "--languages", "de,fr"));
    string code = Path.Combine(root, "src", "Page.cs");
    Directory.CreateDirectory(Path.GetDirectoryName(code)!);
    File.WriteAllText(code, "a = Translate(\"\", \"Save\");\nb = Translate(\"\", \"Hello {name}\");\n");

    // Without --assign the empty ids are errors and nothing is written.
    Assert.Equal(Cli.Failed, Run("scan", "--project", I18n));
    Assert.Contains("rcinel scan --assign", error.ToString());

    Assert.Equal(Cli.Ok, Run("scan", "--project", I18n, "--assign"));
    Assert.Contains("2 strings in 1 files: 2 new", output.ToString());
    TranslationProject project = TranslationProject.Load(I18n);
    Assert.Equal(["Save", "Hello {name}"], project.Source.Values);
    Assert.DoesNotContain("Translate(\"\"", File.ReadAllText(code));

    Assert.Equal(Cli.Ok, Run("status", "--project", I18n, "--json"));
    using JsonDocument status = JsonDocument.Parse(output.ToString());
    Assert.Equal(2, status.RootElement.GetProperty("total").GetInt32());
    Assert.Equal(2, status.RootElement.GetProperty("languages")[0].GetProperty("missing").GetInt32());

    Assert.Equal(Cli.Ok, Run("check", "--project", I18n));
    Assert.Equal(Cli.Failed, Run("check", "--project", I18n, "--require-complete"));

    string id = project.Source.Keys.First();
    project.SetTranslation("de", id, "Speichern", ReviewState.Confirmed, "alex", DateTimeOffset.UtcNow);
    project.Save();
    Assert.Equal(Cli.Ok, Run("export", Path.Combine(root, "out"), "--project", I18n, "--confirmed-only"));
    Assert.Equal("Speichern", TranslationSet.Load("de", Path.Combine(root, "out")).Get(id, "Save"));
  }

  [Fact]
  public void Finds_the_project_from_the_app_folder()
  {
    Run("init", I18n);
    string old = Directory.GetCurrentDirectory();
    try
    {
      Directory.SetCurrentDirectory(root);
      Assert.Equal(Cli.Ok, Run("status"));
    }
    finally
    {
      Directory.SetCurrentDirectory(old);
    }
  }

  [Fact]
  public void Id_prints_sorted_uuidv7s()
  {
    Assert.Equal(Cli.Ok, Run("id", "5"));
    string[] ids = output.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    Assert.Equal(5, ids.Length);
    Assert.All(ids, id => Assert.True(UuidV7.IsValid(id)));
    Assert.Equal(ids.Order(StringComparer.Ordinal), ids);
  }

  [Fact]
  public void Format_and_add_language_and_errors()
  {
    Run("init", I18n, "--languages", "de");
    File.WriteAllText(Path.Combine(I18n, "translations", "de.json"), "{\"b\":\"2\",\"a\":\"1\"}");
    Assert.Equal(Cli.Ok, Run("format", "--project", I18n));
    Assert.Contains("de.json", output.ToString());
    Assert.Equal("{\n  \"a\": \"1\",\n  \"b\": \"2\"\n}\n", File.ReadAllText(Path.Combine(I18n, "translations", "de.json")));

    Assert.Equal(Cli.Ok, Run("add-language", "pt-BR", "--name", "Português", "--project", I18n));
    Assert.True(TranslationProject.Load(I18n).HasLanguage("pt-BR"));
    Assert.Equal(Cli.Failed, Run("add-language", "de", "--project", I18n));

    Assert.Equal(Cli.Usage, Run("frobnicate"));
    // Walking up from a folder outside any project finds nothing.
    Assert.Equal(Cli.Failed, Run("status", "--project", Path.Combine(Path.GetTempPath(), "rcinel-none-" + Guid.NewGuid().ToString("N"))));
  }
}
