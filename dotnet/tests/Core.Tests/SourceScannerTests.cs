using Rcinel.Translate.Core;

namespace Rcinel.Translate.Core.Tests;

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
    temp.WriteSource("Page.cs", $"label.Text = Translate(\"{A}\", \"Save\");\nvar x = Translate( \"{B}\" ,\n  \"Line\\none \\\"quoted\\\"\", count);");
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
    string file = temp.WriteSource("Page.cs", "a = Translate(\"\", \"One\");\nb = Translate(\"\", \"Two\");");
    ScanResult withoutAssign = SourceScanner.Scan(temp.Project);
    Assert.Equal(2, withoutAssign.Issues.Count(i => i.IsError));

    ScanResult scan = SourceScanner.Scan(temp.Project, assignIds: true);
    Assert.Equal(2, scan.AssignedIds);
    Assert.False(scan.HasErrors);
    string content = File.ReadAllText(file);
    foreach (SourceString s in scan.Strings.Values)
    {
      Assert.Contains($"Translate(\"{s.Id}\", \"{s.Text}\")", content);
    }
    // The first call got the smaller id, so the files list the strings in the code's order.
    Assert.Equal(["One", "Two"], scan.Strings.Values.Select(s => s.Text));
  }

  [Fact]
  public void Reports_what_it_cannot_read()
  {
    using TempProject temp = new("de");
    temp.WriteSource("a.js", $"Rcinel.translate('{A}', `Hi ${{name}}`);\nRcinel.translate(id, 'x');\nRcinel.translate('save-button', 'Save');");
    temp.WriteSource("b.cs", $"Translate(\"{B}\", $\"Hi {{name}}\");");
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
    temp.WriteSource("a.cs", $"Translate(\"{A}\", \"Save\"); Translate(\"{A}\", \"Save\");");
    temp.WriteSource("b.cs", $"Translate(\"{A}\", \"Store\");");
    ScanResult scan = SourceScanner.Scan(temp.Project);
    ScanIssue issue = Assert.Single(scan.Issues);
    Assert.Contains("another text", issue.Message);
  }

  [Fact]
  public void Skips_build_output_and_dependencies()
  {
    using TempProject temp = new("de");
    temp.WriteSource("a.cs", $"Translate(\"{A}\", \"Save\");");
    Directory.CreateDirectory(Path.Combine(temp.SourceDirectory, "node_modules"));
    temp.WriteSource(Path.Combine("node_modules", "lib.js"), $"Rcinel.translate('{B}', 'x');");
    Directory.CreateDirectory(Path.Combine(temp.SourceDirectory, "bin"));
    temp.WriteSource(Path.Combine("bin", "gen.cs"), $"Translate(\"{C}\", \"x\");");
    Assert.Equal([A], SourceScanner.Scan(temp.Project).Strings.Keys);
  }

  [Fact]
  public void Other_names_do_not_count()
  {
    using TempProject temp = new("de");
    temp.WriteSource("a.cs", $"MyTranslate(\"{A}\", \"x\"); foo.Translate(\"{B}\", \"x\"); MyRcinel.translate(\"{C}\", \"x\");");
    Assert.Empty(SourceScanner.Scan(temp.Project).Strings);
  }

  [Fact]
  public void Apply_counts_changes_and_keeps_translations_of_removed_ids()
  {
    using TempProject temp = new("de");
    temp.Project.Source[A] = "Save";
    temp.Project.Source[B] = "Old";
    temp.Project.SetTranslation("de", B, "Alt", ReviewState.Confirmed, "alex", DateTimeOffset.UtcNow);
    temp.WriteSource("a.cs", $"Translate(\"{A}\", \"Save now\"); Translate(\"{C}\", \"New\");");
    SourceUpdate update = SourceScanner.Apply(temp.Project, SourceScanner.Scan(temp.Project));
    Assert.Equal(new SourceUpdate(Added: 1, Changed: 1, Removed: 1), update);
    Assert.Equal([A, C], temp.Project.Source.Keys);
    Assert.Equal("Alt", temp.Project.GetLanguage("de").Texts[B]);
  }

  [Fact]
  public void Finds_the_bare_call_in_java_and_javascript()
  {
    using TempProject temp = new("de");
    temp.WriteSource("Main.java", $"import static io.github.codeisnotevil.rcinel.Rcinel.translate;\nlabel.setText(translate(\"{A}\", \"Save\"));");
    temp.WriteSource("app.js", $"import {{ translate }} from 'rcinel-translate';\nel.textContent = translate('{B}', 'Cancel');\nconst other = i18n.translate('greeting', 'de');\nconst t = translate(key, 'de');");
    ScanResult scan = SourceScanner.Scan(temp.Project);
    Assert.Empty(scan.Issues);
    Assert.Equal(["Save", "Cancel"], scan.Strings.Values.Select(s => s.Text));
  }

  [Fact]
  public void Finds_calls_in_python_and_rust()
  {
    using TempProject temp = new("de");
    temp.WriteSource("app.py", $$"""
      from rcinel_translate import translate
      title = translate('{{A}}', 'Settings')
      greeting = translate("{{B}}", "It's {name}", name=name)
      other = translate("", f"Hi {name}")
      unrelated = translator.translate('hello', 'de')
      """);
    temp.WriteSource("main.rs", $$"""
      use rcinel_translate::translate;
      let t = translate("{{C}}", "Gr\u{fc}\u{df}e");
      let q = rcinel_translate::translate("{{D}}", "Quit");
      """);
    ScanResult scan = SourceScanner.Scan(temp.Project);
    Assert.Equal(["Settings", "It's {name}", "Grüße", "Quit"], scan.Strings.Values.Select(s => s.Text));
    // The f-string can't be read; it's reported, not guessed.
    ScanIssue issue = Assert.Single(scan.Issues);
    Assert.False(issue.IsError);
    Assert.EndsWith("app.py", issue.File);
  }

  [Fact]
  public void Other_apis_called_Translate_are_left_alone()
  {
    using TempProject temp = new("de");
    // Somebody else's Translate: neither the first argument nor anything else looks like ours.
    temp.WriteSource("Other.cs", "var t = Translate(\"hello\", \"de\"); var u = Translate(word, target); var v = Translate();");
    // Ours, but written wrongly: a UUID that isn't a lowercase v7 is still reported.
    temp.WriteSource("Mine.cs", "var w = Translate(\"0192F3A0-1C4E-7A10-8000-000000000001\", \"Save\");\nvar x = Translate(\"\", $\"Hi {name}\");");
    ScanResult scan = SourceScanner.Scan(temp.Project);
    Assert.Empty(scan.Strings);
    Assert.Contains(scan.Issues, i => i.IsError && i.File.EndsWith("Mine.cs") && i.Message.Contains("not a lowercase UUIDv7"));
    Assert.Contains(scan.Issues, i => !i.IsError && i.File.EndsWith("Mine.cs") && i.Line == 2);
    Assert.DoesNotContain(scan.Issues, i => i.File.EndsWith("Other.cs"));
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
