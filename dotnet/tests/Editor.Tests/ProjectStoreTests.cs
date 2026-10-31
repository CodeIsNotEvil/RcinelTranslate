using CINE.Translate.Core;
using CINE.Translate.Editor.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CINE.Translate.Editor.Tests;

public class ProjectStoreTests
{
  private const string Save = "0192f3a0-1c4e-7a10-8000-000000000001";

  private static ProjectStore Open(TempProject temp) =>
    new(Options.Create(new EditorOptions { Project = temp.ProjectDirectory }), NullLogger<ProjectStore>.Instance);

  [Fact]
  public void Writes_save_and_tell_open_pages()
  {
    using TempProject temp = new("de");
    temp.Project.Source[Save] = "Save";
    temp.Project.Save();
    ProjectStore store = Open(temp);
    int changes = 0;
    store.Changed += () => changes++;

    store.Write(p => p.SetTranslation("de", Save, "Speichern", ReviewState.Confirmed, "alex", DateTimeOffset.UtcNow));

    Assert.Equal(1, changes);
    Assert.Equal("Speichern", temp.Reload().GetLanguage("de").Texts[Save]);
  }

  [Fact]
  public void Picks_up_changes_made_on_disk()
  {
    using TempProject temp = new("de");
    temp.Project.Source[Save] = "Save";
    temp.Project.Save();
    ProjectStore store = Open(temp);
    Assert.Equal("Save", store.Read(p => p.Source[Save]));

    // `git pull` or `rcinel scan` in another process.
    TranslationProject other = TranslationProject.Load(temp.ProjectDirectory);
    other.Source[Save] = "Save all";
    other.Save();
    File.SetLastWriteTimeUtc(Path.Combine(temp.ProjectDirectory, "translations", "en.json"), DateTime.UtcNow.AddMinutes(1));

    // A write always checks the disk first, so it never saves over what came in.
    store.Write(p => p.Notes[Save] = "button");
    Assert.Equal("Save all", store.Read(p => p.Source[Save]));
    Assert.Equal("button", temp.Reload().Notes[Save]);
  }

  [Fact]
  public void A_broken_file_is_reported_and_never_saved_over()
  {
    using TempProject temp = new("de");
    temp.Project.Save();
    string file = Path.Combine(temp.ProjectDirectory, "translations", "de.json");
    File.WriteAllText(file, "<<<<<<< HEAD\n{}\n=======\n{}\n>>>>>>> branch\n");
    ProjectStore store = Open(temp);

    Assert.False(store.EnsureLoaded());
    Assert.Contains("de.json", store.LoadError);
    Assert.Throws<InvalidOperationException>(() => store.Write(p => p.Notes["x"] = "y"));
    Assert.StartsWith("<<<<<<<", File.ReadAllText(file));

    File.WriteAllText(file, "{}\n");
    Assert.True(store.EnsureLoaded());
    Assert.Null(store.LoadError);
  }
}
