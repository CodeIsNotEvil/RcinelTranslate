using Rcinel.Translate.Ai;
using Rcinel.Translate.Core;
using Microsoft.Extensions.Options;

namespace Rcinel.Translate.Editor.Services;

/// <summary>
/// The one project the editor works on, shared by every user. All access goes through
/// <see cref="Read{T}"/> and <see cref="Write"/>, which hold a lock: TranslationProject isn't
/// thread-safe, and Blazor Server runs every user's circuit on its own thread.
/// </summary>
/// <remarks>
/// Read must copy what it returns. Handing out the project's dictionaries would let a component
/// render them while another user's save changes them.
/// </remarks>
public sealed class ProjectStore : IProjectAccess
{
  private static readonly TimeSpan DiskCheckInterval = TimeSpan.FromSeconds(1);

  private readonly object gate = new();
  private readonly string path;
  private readonly ILogger<ProjectStore> logger;
  private TranslationProject? project;
  private DateTime loadedWriteTime;
  private DateTime lastDiskCheck;

  public ProjectStore(IOptions<EditorOptions> options, ILogger<ProjectStore> logger)
  {
    this.logger = logger;
    path = string.IsNullOrWhiteSpace(options.Value.Project) ? Directory.GetCurrentDirectory() : options.Value.Project;
    lock (gate)
    {
      TryLoad();
    }
  }

  /// <summary>Raised after any change, by this editor or on disk, so open pages can refresh.</summary>
  public event Action? Changed;

  /// <summary>Why the project couldn't be loaded (a merge conflict in a file, a wrong path), or null.</summary>
  public string? LoadError { get; private set; }

  /// <summary>True when the project is loaded; tries loading again (after a fix on disk) when it isn't.</summary>
  public bool EnsureLoaded()
  {
    bool reloaded;
    bool loaded;
    lock (gate)
    {
      reloaded = ReloadIfChangedOnDisk();
      loaded = project is not null;
    }
    if (reloaded)
    {
      Changed?.Invoke();
    }
    return loaded;
  }

  public T Read<T>(Func<TranslationProject, T> read)
  {
    bool reloaded;
    T result;
    lock (gate)
    {
      reloaded = ReloadIfChangedOnDisk();
      result = read(project ?? throw new InvalidOperationException(LoadError));
    }
    if (reloaded)
    {
      Changed?.Invoke();
    }
    return result;
  }

  public void Write(Action<TranslationProject> write)
  {
    lock (gate)
    {
      ReloadIfChangedOnDisk(force: true);
      TranslationProject current = project ?? throw new InvalidOperationException(LoadError);
      write(current);
      current.Save();
      loadedWriteTime = current.LastWriteTimeUtc();
    }
    Changed?.Invoke();
  }

  /// <summary>Reads everything from disk again, for the Reload button after a git pull.</summary>
  public void Reload()
  {
    lock (gate)
    {
      TryLoad();
    }
    Changed?.Invoke();
  }

  private bool TryLoad()
  {
    try
    {
      project = TranslationProject.Load(path);
      loadedWriteTime = project.LastWriteTimeUtc();
      LoadError = null;
      logger.LogInformation("Editing {Project}", project.ProjectFile);
      return true;
    }
    catch (Exception e) when (e is IOException or FormatException or UnauthorizedAccessException)
    {
      // A file with merge-conflict markers, say. Drop the old state rather than keep editing it:
      // saving it would silently overwrite whatever the merge was about. Every page shows the
      // error until the file is fixed, and the next read tries again.
      LoadError = e.Message;
      logger.LogError("Can't load the project: {Error}", e.Message);
      project = null;
      return false;
    }
  }

  // Someone ran git pull or `rcinel scan` while the editor was open. Checked at most once a second,
  // since every render reads.
  private bool ReloadIfChangedOnDisk(bool force = false)
  {
    if (project is null)
    {
      return TryLoad();
    }
    DateTime now = DateTime.UtcNow;
    if (!force && now - lastDiskCheck < DiskCheckInterval)
    {
      return false;
    }
    lastDiskCheck = now;
    DateTime onDisk;
    try
    {
      onDisk = project.LastWriteTimeUtc();
    }
    catch (IOException)
    {
      return false;
    }
    if (onDisk == loadedWriteTime)
    {
      return false;
    }
    logger.LogInformation("Files changed on disk, reloading");
    return TryLoad();
  }
}
