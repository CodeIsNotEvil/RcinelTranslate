using System.Collections.Concurrent;
using Rcinel.Translate.Ai;

namespace Rcinel.Translate.Editor.Services;

/// <summary>A running or finished AI run for one language.</summary>
public sealed class DraftJob(string language, string startedBy)
{
  internal readonly CancellationTokenSource Cancellation = new();

  public string Language { get; } = language;
  public string StartedBy { get; } = startedBy;
  public DateTimeOffset Started { get; } = DateTimeOffset.UtcNow;
  public DraftProgress Progress { get; internal set; } = new(0, 0, 0, []);
  public bool Finished { get; internal set; }
  public string? Error { get; internal set; }

  public void Cancel() => Cancellation.Cancel();
}

/// <summary>
/// Runs AI drafts in the background, one per language at a time, so a run over hundreds of strings
/// keeps going when the user who started it closes the tab, and everyone sees its progress.
/// </summary>
public sealed class DraftJobs(ProjectStore store, ITranslator translator, ILogger<DraftJobs> logger)
{
  private readonly ConcurrentDictionary<string, DraftJob> jobs = new(StringComparer.OrdinalIgnoreCase);

  public event Action? Changed;

  public bool IsConfigured => translator.IsConfigured;

  public DraftJob? Get(string language) => jobs.TryGetValue(language, out DraftJob? job) ? job : null;

  public bool IsRunning(string language) => Get(language) is { Finished: false };

  /// <summary>Starts drafting the open strings (or <paramref name="ids"/>) of a language. Returns null when a run is already going.</summary>
  public DraftJob? Start(string language, string user, IReadOnlyCollection<string>? ids = null)
  {
    DraftJob job = new(language, user);
    lock (jobs)
    {
      if (IsRunning(language))
      {
        return null;
      }
      jobs[language] = job;
    }
    _ = Task.Run(async () =>
    {
      try
      {
        job.Progress = await new TranslationDrafter(translator).RunAsync(store, language, ids, new SyncProgress(job, () => Changed?.Invoke()), job.Cancellation.Token);
      }
      catch (OperationCanceledException)
      {
        job.Error = "Stopped.";
      }
      catch (Exception e)
      {
        logger.LogError(e, "AI draft for {Language} failed", language);
        job.Error = e.Message;
      }
      finally
      {
        job.Finished = true;
        Changed?.Invoke();
      }
    });
    Changed?.Invoke();
    return job;
  }

  /// <summary>Asks for one suggestion without storing it, for the "Suggest" button.</summary>
  public async Task<string> SuggestAsync(string language, string id, CancellationToken cancellationToken)
  {
    (DraftContext context, DraftBatch batch) = store.Read(p =>
    {
      List<DraftBatch> plan = DraftPlanner.Plan(p, language, [id]);
      return (DraftPlanner.Context(p, language), plan[0]);
    });
    IReadOnlyDictionary<string, string> result = await translator.TranslateAsync(context, batch, cancellationToken);
    return result.TryGetValue(id, out string? text) ? text : throw new TranslationException("No suggestion came back.");
  }

  // Progress<T> would post to the thread pool and could report out of order.
  private sealed class SyncProgress(DraftJob job, Action changed) : IProgress<DraftProgress>
  {
    public void Report(DraftProgress value)
    {
      job.Progress = value;
      changed();
    }
  }
}
