using Rcinel.Translate.Core;

namespace Rcinel.Translate.Ai;

/// <summary>
/// How the drafter reaches the project. The editor implements it with its lock (other users edit
/// while a run is going); the CLI with a plain load and save.
/// </summary>
public interface IProjectAccess
{
  T Read<T>(Func<TranslationProject, T> read);

  /// <summary>Changes the project and saves it.</summary>
  void Write(Action<TranslationProject> write);
}

/// <summary>Where a run stands, for a progress bar.</summary>
public sealed record DraftProgress(int Done, int Total, int Applied, IReadOnlyList<string> Problems);

/// <summary>
/// Drafts translations batch by batch and stores them as drafts, attributed to "ai:&lt;model&gt;".
/// A reviewer then confirms or corrects each one. Requests run outside the project lock; each
/// result is applied only if nobody changed that string meanwhile, so a run never overwrites a
/// human's edit.
/// </summary>
public sealed class TranslationDrafter(ITranslator translator)
{
  public async Task<DraftProgress> RunAsync(
    IProjectAccess access,
    string language,
    IReadOnlyCollection<string>? ids = null,
    IProgress<DraftProgress>? progress = null,
    CancellationToken cancellationToken = default)
  {
    (DraftContext context, List<DraftBatch> batches, Dictionary<string, string?> before) = access.Read(p =>
    {
      List<string> wanted = ids is null ? DraftPlanner.OpenIds(p, language) : [.. ids];
      LanguageData data = p.GetLanguage(language);
      Dictionary<string, string?> current = wanted.ToDictionary(id => id, id => data.Texts.TryGetValue(id, out string? t) ? t : null);
      return (DraftPlanner.Context(p, language), DraftPlanner.Plan(p, language, wanted), current);
    });

    int total = batches.Sum(b => b.Items.Count);
    int done = 0, applied = 0;
    List<string> problems = [];
    progress?.Report(new DraftProgress(0, total, 0, problems));

    foreach (DraftBatch batch in batches)
    {
      cancellationToken.ThrowIfCancellationRequested();
      IReadOnlyDictionary<string, string> result;
      try
      {
        result = await translator.TranslateAsync(context, batch, cancellationToken);
      }
      catch (TranslationException e)
      {
        problems.Add(e.Message);
        done += batch.Items.Count;
        progress?.Report(new DraftProgress(done, total, applied, [.. problems]));
        continue;
      }

      access.Write(p =>
      {
        LanguageData data = p.GetLanguage(language);
        foreach (DraftItem item in batch.Items)
        {
          if (!result.TryGetValue(item.Id, out string? text))
          {
            problems.Add($"No translation came back for \"{item.Source}\".");
            continue;
          }
          string? now = data.Texts.TryGetValue(item.Id, out string? t) ? t : null;
          bool untouched = p.Source.TryGetValue(item.Id, out string? source) && source == item.Source
            && now == before[item.Id]
            && p.GetState(data, item.Id) != TranslationState.Confirmed;
          if (!untouched)
          {
            continue;
          }
          // A draft with a lost {name} would ship as broken text; leave the string for a human.
          if (Placeholders.Compare(item.Source, text) is string problem)
          {
            problems.Add($"\"{item.Source}\": the draft \"{text}\" was not stored ({problem}).");
            continue;
          }
          p.SetTranslation(language, item.Id, text, ReviewState.Draft, "ai:" + context.Ai.Model, DateTimeOffset.UtcNow);
          applied++;
        }
      });
      done += batch.Items.Count;
      progress?.Report(new DraftProgress(done, total, applied, [.. problems]));
    }
    return new DraftProgress(done, total, applied, problems);
  }
}

/// <summary>Plain access for one process: the CLI.</summary>
public sealed class DirectProjectAccess(TranslationProject project) : IProjectAccess
{
  public T Read<T>(Func<TranslationProject, T> read) => read(project);

  public void Write(Action<TranslationProject> write)
  {
    write(project);
    project.Save();
  }
}
