using System.Globalization;
using System.Text.Json;
using Rcinel.Translate.Ai;
using Rcinel.Translate.Core;

namespace Rcinel.Translate.Cli;

/// <summary>
/// The rcinel command. Hand-rolled argument parsing: nine small commands don't justify a
/// dependency, and the tool should start fast in CI.
/// </summary>
internal sealed class Cli(TextWriter output, TextWriter error, Func<ITranslator>? translatorFactory = null)
{
  public const int Ok = 0;
  public const int Failed = 1;
  public const int Usage = 2;

  private const string Help = """
    rcinel - translations keyed by UUIDv7 (https://github.com/CodeIsNotEvil/RcinelTranslate)

    Usage: rcinel <command> [options]

      init [folder]          Create a project (default folder: ./i18n)
          --name <name>        App name, shown to translators and the AI
          --source <code>      Language of the texts in the code (default: en)
          --languages <codes>  Target languages, comma-separated: de,fr,pt-BR
          --sources <folders>  Code folders to scan, relative to the project (default: ..)
      id [count]             Print new UUIDv7 ids
      scan                   Collect Rcinel.Translate calls from the code into the source file
          --assign             Give calls with an empty id "" a new id, in the code files
          --dry-run            Report only, write nothing
      status                 Progress per language: confirmed, draft, outdated, missing
          --json               As JSON, for badges and scripts
      check                  Find problems; exits with 1 when there are errors
          --require-complete   Missing translations are errors
          --require-confirmed  Missing and unconfirmed translations are errors
          --strict             Warnings are errors too
      format                 Rewrite every file in canonical form (sorted by id)
      prune                  Remove translations of ids that are no longer in the code
      add-language <code>    Add a target language (--name "Deutsch")
      export <folder>        Write the files an app ships to <folder>
          --confirmed-only     Leave out drafts, stale and unreviewed translations
      draft <code>           Draft missing and stale translations with Claude (needs ANTHROPIC_API_KEY)

    Every command but init and id takes --project <path>: rcinel.json, its folder, or a folder
    below it. Without it, rcinel looks in the current folder, its i18n folder and the folders above.
    """;

  public int Run(string[] args) => RunAsync(args).GetAwaiter().GetResult();

  public async Task<int> RunAsync(string[] args)
  {
    if (args.Length == 0 || args[0] is "help" or "-h" or "--help")
    {
      output.WriteLine(Help);
      return args.Length == 0 ? Usage : Ok;
    }
    Arguments a;
    try
    {
      a = Arguments.Parse(args.Skip(1));
    }
    catch (ArgumentException e)
    {
      error.WriteLine(e.Message);
      return Usage;
    }
    try
    {
      return args[0] switch
      {
        "init" => Init(a),
        "id" => Id(a),
        "scan" => Scan(a),
        "status" => Status(a),
        "check" => Check(a),
        "format" => Format(a),
        "prune" => Prune(a),
        "add-language" => AddLanguage(a),
        "export" => Export(a),
        "draft" => await Draft(a),
        "version" or "--version" => Version(),
        _ => Unknown(args[0]),
      };
    }
    catch (Exception e) when (e is FileNotFoundException or FormatException or IOException or ArgumentException or KeyNotFoundException)
    {
      error.WriteLine("error: " + e.Message);
      return Failed;
    }
  }

  private int Unknown(string command)
  {
    error.WriteLine($"Unknown command \"{command}\". Run `rcinel help`.");
    return Usage;
  }

  private int Version()
  {
    output.WriteLine(typeof(Cli).Assembly.GetName().Version?.ToString(3));
    return Ok;
  }

  private TranslationProject Load(Arguments a) => TranslationProject.Load(a.Option("project") ?? Directory.GetCurrentDirectory());

  private int Init(Arguments a)
  {
    string folder = Path.GetFullPath(a.Positional(0) ?? "i18n");
    ProjectSettings settings = new()
    {
      Name = a.Option("name") ?? Path.GetFileName(Path.GetDirectoryName(folder)) ?? "",
      SourceLanguage = a.Option("source") ?? "en",
      Sources = [.. (a.Option("sources") ?? "..").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)],
    };
    foreach (string code in (a.Option("languages") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
    {
      settings.Languages.Add(new LanguageSettings { Code = code });
    }
    TranslationProject project = TranslationProject.Create(folder, settings);
    output.WriteLine($"Created {project.ProjectFile}.");
    output.WriteLine($"Ship the folder {project.TranslationsDirectory} with the app, then write texts as Translate(\"\", \"English text\") (C#, with `using static Rcinel.Translate.Rcinel;`) or Rcinel.translate(\"\", \"English text\") (Java, JavaScript) and run `rcinel scan --assign`.");
    return Ok;
  }

  private int Id(Arguments a)
  {
    int count = int.TryParse(a.Positional(0), NumberStyles.None, CultureInfo.InvariantCulture, out int n) && n > 0 ? n : 1;
    for (int i = 0; i < count; i++)
    {
      output.WriteLine(UuidV7.New());
    }
    return Ok;
  }

  private int Scan(Arguments a)
  {
    TranslationProject project = Load(a);
    if (project.Settings.Sources.Count == 0)
    {
      error.WriteLine("rcinel.json lists no \"sources\" folders to scan.");
      return Failed;
    }
    bool dryRun = a.Flag("dry-run");
    ScanResult scan = SourceScanner.Scan(project, assignIds: a.Flag("assign") && !dryRun);
    foreach (ScanIssue issue in scan.Issues)
    {
      (issue.IsError ? error : output).WriteLine($"{Paths.Relative(Directory.GetCurrentDirectory(), issue.File)}:{issue.Line}: {(issue.IsError ? "error" : "warning")}: {issue.Message}");
    }
    if (scan.HasErrors)
    {
      error.WriteLine("The source file was not changed. Fix the errors above and scan again.");
      return Failed;
    }
    SourceUpdate update = SourceScanner.Apply(project, scan);
    if (!dryRun)
    {
      project.Save();
    }
    output.WriteLine($"{scan.Strings.Count} strings in {scan.FilesScanned} files: {update.Added} new, {update.Changed} changed, {update.Removed} removed"
      + (scan.AssignedIds > 0 ? $", {scan.AssignedIds} ids assigned" : "") + (dryRun ? " (dry run, nothing written)." : "."));
    if (update.Removed > 0)
    {
      output.WriteLine("Translations of removed strings stay until `rcinel prune`.");
    }
    return Ok;
  }

  private int Status(Arguments a)
  {
    TranslationProject project = Load(a);
    List<LanguageProgress> rows = [.. project.Languages.Select(l => project.GetProgress(l.Code))];
    if (a.Flag("json"))
    {
      output.WriteLine(JsonSerializer.Serialize(new
      {
        total = project.Source.Count,
        languages = rows.Select(r => new
        {
          language = r.Language,
          confirmed = r.Confirmed,
          draft = r.Draft,
          outdated = r.Stale,
          missing = r.Missing,
          orphaned = r.Orphaned,
          confirmedPercent = r.ConfirmedPercent,
          translatedPercent = r.TranslatedPercent,
        }),
      }, new JsonSerializerOptions { WriteIndented = true }));
      return Ok;
    }
    output.WriteLine($"{project.Settings.Name}: {project.Source.Count} strings in {project.Settings.SourceLanguage}");
    if (rows.Count == 0)
    {
      output.WriteLine("No target languages yet. Add one with `rcinel add-language de`.");
      return Ok;
    }
    int width = Math.Max(8, rows.Max(r => r.Language.Length));
    output.WriteLine($"{"Language".PadRight(width)}  Confirmed       Draft  Outdated  Missing");
    foreach (LanguageProgress r in rows)
    {
      output.WriteLine($"{r.Language.PadRight(width)}  {Bar(r)} {r.ConfirmedPercent,3}%  {r.Draft,5}  {r.Stale,8}  {r.Missing,7}");
    }
    return Ok;
  }

  // Confirmed as full blocks, drafts and stale as light ones: the translated share reads at a glance.
  private static string Bar(LanguageProgress r)
  {
    const int Width = 10;
    int confirmed = r.Total == 0 ? Width : r.Confirmed * Width / r.Total;
    int translated = r.Total == 0 ? Width : r.Translated * Width / r.Total;
    return new string('#', confirmed) + new string('-', translated - confirmed) + new string('.', Width - translated);
  }

  private int Check(Arguments a)
  {
    TranslationProject project = Load(a);
    List<ProjectIssue> issues = ProjectChecker.Check(project, new CheckOptions
    {
      RequireComplete = a.Flag("require-complete"),
      RequireConfirmed = a.Flag("require-confirmed"),
      Strict = a.Flag("strict"),
    });
    foreach (ProjectIssue issue in issues)
    {
      string where = issue.Language is null ? "" : $"[{issue.Language}] ";
      (issue.Severity == IssueSeverity.Error ? error : output).WriteLine($"{(issue.Severity == IssueSeverity.Error ? "error" : "warning")} {issue.Code}: {where}{issue.Message}");
    }
    int errors = issues.Count(i => i.Severity == IssueSeverity.Error);
    output.WriteLine(errors == 0
      ? $"OK: {project.Source.Count} strings, {project.Settings.Languages.Count} languages, {issues.Count} warning(s)."
      : $"{errors} error(s), {issues.Count - errors} warning(s).");
    return errors == 0 ? Ok : Failed;
  }

  private int Format(Arguments a)
  {
    TranslationProject project = Load(a);
    List<string> before = ProjectChecker.NonCanonicalFiles(project);
    project.Save();
    output.WriteLine(before.Count == 0 ? "All files were already in canonical form." : $"Rewrote {string.Join(", ", before)}.");
    return Ok;
  }

  private int Prune(Arguments a)
  {
    TranslationProject project = Load(a);
    int removed = project.Prune();
    project.Save();
    output.WriteLine($"Removed {removed} entries of strings that are no longer in the code.");
    return Ok;
  }

  private int AddLanguage(Arguments a)
  {
    string code = a.Positional(0) ?? throw new ArgumentException("Which language? For example: rcinel add-language de --name Deutsch");
    TranslationProject project = Load(a);
    project.AddLanguage(new LanguageSettings { Code = code, Name = a.Option("name") ?? "" });
    project.Save();
    output.WriteLine($"Added {code}. Draft it with `rcinel draft {code}` or open it in the editor.");
    return Ok;
  }

  private int Export(Arguments a)
  {
    string folder = a.Positional(0) ?? throw new ArgumentException("Where to? For example: rcinel export build/translations --confirmed-only");
    TranslationProject project = Load(a);
    List<string> files = Exporter.Export(project, Path.GetFullPath(folder), a.Flag("confirmed-only"));
    output.WriteLine($"Wrote {files.Count} files to {Path.GetFullPath(folder)}.");
    return Ok;
  }

  private async Task<int> Draft(Arguments a)
  {
    string code = a.Positional(0) ?? throw new ArgumentException("Which language? For example: rcinel draft de");
    TranslationProject project = Load(a);
    ITranslator translator = translatorFactory?.Invoke() ?? new ClaudeTranslator();
    if (!translator.IsConfigured)
    {
      error.WriteLine("Set ANTHROPIC_API_KEY to an API key from https://platform.claude.com to draft with Claude.");
      return Failed;
    }
    project.GetLanguage(code);
    TranslationDrafter drafter = new(translator);
    DraftProgress result = await drafter.RunAsync(new DirectProjectAccess(project), code, progress: new SyncProgress(output));
    foreach (string problem in result.Problems)
    {
      error.WriteLine("warning: " + problem);
    }
    output.WriteLine($"Drafted {result.Applied} of {result.Total} strings for {code}. Review them in the editor.");
    return result.Applied == result.Total ? Ok : Failed;
  }

  // Progress<T> posts to the thread pool, so lines would come out of order after the summary.
  private sealed class SyncProgress(TextWriter output) : IProgress<DraftProgress>
  {
    public void Report(DraftProgress value)
    {
      if (value.Done > 0)
      {
        output.WriteLine($"  {value.Done}/{value.Total} strings");
      }
    }
  }
}

/// <summary>Positional values, --name value options and --flag switches.</summary>
internal sealed class Arguments
{
  private static readonly HashSet<string> Flags = ["assign", "dry-run", "json", "require-complete", "require-confirmed", "strict", "confirmed-only"];
  private readonly List<string> positional = [];
  private readonly Dictionary<string, string> options = new(StringComparer.Ordinal);
  private readonly HashSet<string> flags = new(StringComparer.Ordinal);

  public static Arguments Parse(IEnumerable<string> args)
  {
    Arguments result = new();
    using IEnumerator<string> e = args.GetEnumerator();
    while (e.MoveNext())
    {
      string arg = e.Current;
      if (!arg.StartsWith("--", StringComparison.Ordinal))
      {
        result.positional.Add(arg);
        continue;
      }
      string name = arg[2..];
      int equals = name.IndexOf('=');
      if (equals >= 0)
      {
        result.options[name[..equals]] = name[(equals + 1)..];
      }
      else if (Flags.Contains(name))
      {
        result.flags.Add(name);
      }
      else if (e.MoveNext())
      {
        result.options[name] = e.Current;
      }
      else
      {
        throw new ArgumentException($"--{name} needs a value.");
      }
    }
    return result;
  }

  public string? Positional(int index) => index < positional.Count ? positional[index] : null;
  public string? Option(string name) => options.TryGetValue(name, out string? value) ? value : null;
  public bool Flag(string name) => flags.Contains(name);
}
