using CINE.Translate.Core;

namespace CINE.Translate.Core.Tests;

/// <summary>A project in a fresh temp folder, deleted again after the test.</summary>
internal sealed class TempProject : IDisposable
{
  public TempProject(params string[] languages)
  {
    Root = Path.Combine(Path.GetTempPath(), "rcinel-tests", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(Root);
    ProjectSettings settings = new() { Name = "Test app", Sources = [Path.Combine("..", "src")] };
    foreach (string language in languages)
    {
      settings.Languages.Add(new LanguageSettings { Code = language });
    }
    Project = TranslationProject.Create(ProjectDirectory, settings);
  }

  public string Root { get; }
  public string ProjectDirectory => Path.Combine(Root, "i18n");
  public string SourceDirectory => Path.Combine(Root, "src");
  public TranslationProject Project { get; private set; }

  public TranslationProject Reload() => Project = TranslationProject.Load(ProjectDirectory);

  public string WriteSource(string name, string content)
  {
    Directory.CreateDirectory(SourceDirectory);
    string path = Path.Combine(SourceDirectory, name);
    File.WriteAllText(path, content);
    return path;
  }

  public void Dispose()
  {
    try
    {
      Directory.Delete(Root, recursive: true);
    }
    catch (IOException)
    {
      // A leftover temp folder isn't worth failing a test over.
    }
  }
}
