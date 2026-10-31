using CINE.Translate.Core;

namespace CINE.Translate.Core.Tests;

public class SourceScannerTests
{
  private const string A = "0192f3a0-1c4e-7a10-8000-000000000001";
  private const string B = "0192f3a0-1c4e-7a10-8000-000000000002";
  private const string C = "0192f3a0-1c4e-7a10-8000-000000000003";
  private const string D = "0192f3a0-1c4e-7a10-8000-000000000004";

  [Fact]
  public void Finds_calls_in_every_supported_language()
  {
    using TempProject temp = new("de");
    temp.WriteSource("Page.cs", $"label.Text = Rcinel.Translate(\"{A}\", \"Save\");\nvar x = Rcinel.Translate( \"{B}\" ,\n  \"Line\\none \\\"quoted\\\"\", count);");
    temp.WriteSource("Main.java", $"button.setText(Rcinel.translate(\"{C}\", \"Gr\\u00fc\\u00dfe\"));");
    temp.WriteSource("app.ts", $"el.title = Rcinel.translate('{D}', 'It\\'s here');");
    ScanResult scan = SourceScanner.Scan(temp.Project);

    Assert.Empty(scan.Issues);
    Assert.Equal(4, scan.Strings.Count);
    Assert.Equal("Save", scan.Strings[A].Text);
    Assert.Equal("Line\none \"quoted\"", scan.Strings[B].Text);
    Assert.Equal(2, scan.Strings[B].Line);
    Assert.Equal("Grüße", scan.Strings[C].Text);
    Assert.Equal("It's here", scan.Strings[D].Text);
  }

  [Fact]
  public void Assign_fills_empty_ids_in_the_file()
  {
    using TempProject temp = new("de");
    string file = temp.WriteSource("Page.cs", "a = Rcinel.Translate(\"\", \"One\");\nb = Rcinel.Translate(\"\", \"Two\");");
    ScanResult withoutAssign = SourceScanner.Scan(temp.Project);
    Assert.Equal(2, withoutAssign.Issues.Count(i => i.IsError));

    ScanResult scan = SourceScanner.Scan(temp.Project, assignIds: true);
    Assert.Equal(2, scan.AssignedIds);
    Assert.False(scan.HasErrors);
    string content = File.ReadAllText(file);
    foreach (SourceString s in scan.Strings.Values)
    {
      Assert.Contains($"Rcinel.Translate(\"{s.Id}\", \"{s.Text}\")", content);
    }
    // The first call got the smaller id, so the files list the strings in the code's order.
    Assert.Equal(["One", "Two"], scan.Strings.Values.Select(s => s.Text));
  }

  [Fact]
  public void Reports_what_it_cannot_read()
  {
    using TempProject temp = new("de");
    temp.WriteSource("a.js", $"Rcinel.translate('{A}', `Hi ${{name}}`);\nRcinel.translate(id, 'x');\nRcinel.translate('save-button', 'Save');");
    temp.WriteSource("b.cs", $"Rcinel.Translate(\"{B}\", $\"Hi {{name}}\");");
    temp.WriteSource("c.kt", $"Rcinel.translate(\"{C}\", \"Hi $name\")");
    ScanResult scan = SourceScanner.Scan(temp.Project);
    Assert.Empty(scan.Strings);
    Assert.Contains(scan.Issues, i => i.IsError && i.Message.Contains("template literal"));
    Assert.Contains(scan.Issues, i => i.IsError && i.Message.Contains("not a lowercase UUIDv7"));
    Assert.Contains(scan.Issues, i => i.IsError && i.Message.Contains("Kotlin"));
    Assert.Equal(2, scan.Issues.Count(i => !i.IsError));
  }

  [Fact]
  public void The_same_id_with_two_texts_is_an_error()
  {
    using TempProject temp = new("de");
    temp.WriteSource("a.cs", $"Rcinel.Translate(\"{A}\", \"Save\"); Rcinel.Translate(\"{A}\", \"Save\");");
    temp.WriteSource("b.cs", $"Rcinel.Translate(\"{A}\", \"Store\");");
    ScanResult scan = SourceScanner.Scan(temp.Project);
    ScanIssue issue = Assert.Single(scan.Issues);
    Assert.Contains("another text", issue.Message);
  }

  [Fact]
  public void Skips_build_output_and_dependencies()
  {
    using TempProject temp = new("de");
    temp.WriteSource("a.cs", $"Rcinel.Translate(\"{A}\", \"Save\");");
    Directory.CreateDirectory(Path.Combine(temp.SourceDirectory, "node_modules"));
    temp.WriteSource(Path.Combine("node_modules", "lib.js"), $"Rcinel.translate('{B}', 'x');");
    Directory.CreateDirectory(Path.Combine(temp.SourceDirectory, "bin"));
    temp.WriteSource(Path.Combine("bin", "gen.cs"), $"Rcinel.Translate(\"{C}\", \"x\");");
    Assert.Equal([A], SourceScanner.Scan(temp.Project).Strings.Keys);
  }

  [Fact]
  public void Other_names_do_not_count()
  {
    using TempProject temp = new("de");
    temp.WriteSource("a.cs", $"MyRcinel.Translate(\"{A}\", \"x\"); foo.Rcinel.Translate(\"{B}\", \"x\");");
    Assert.Empty(SourceScanner.Scan(temp.Project).Strings);
  }

  [Fact]
  public void Apply_counts_changes_and_keeps_translations_of_removed_ids()
  {
    using TempProject temp = new("de");
    temp.Project.Source[A] = "Save";
    temp.Project.Source[B] = "Old";
    temp.Project.SetTranslation("de", B, "Alt", ReviewState.Confirmed, "alex", DateTimeOffset.UtcNow);
    temp.WriteSource("a.cs", $"Rcinel.Translate(\"{A}\", \"Save now\"); Rcinel.Translate(\"{C}\", \"New\");");
    SourceUpdate update = SourceScanner.Apply(temp.Project, SourceScanner.Scan(temp.Project));
    Assert.Equal(new SourceUpdate(Added: 1, Changed: 1, Removed: 1), update);
    Assert.Equal([A, C], temp.Project.Source.Keys);
    Assert.Equal("Alt", temp.Project.GetLanguage("de").Texts[B]);
  }

  [Theory]
  [InlineData(@"a\nb", "a\nb")]
  [InlineData(@"tab\there", "tab\there")]
  [InlineData(@"é", "é")]
  [InlineData(@"\u{1F600}", "😀")]
  [InlineData(@"\x41", "A")]
  [InlineData(@"\'\""\`\\\$", "'\"`\\$")]
  public void Unescapes_string_literals(string raw, string expected) => Assert.Equal(expected, SourceScanner.Unescape(raw));
}
