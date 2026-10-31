namespace Rcinel.Translate.Editor.Services;

/// <summary>The "Rcinel" configuration section. Environment variables use Rcinel__Project and so on.</summary>
public sealed class EditorOptions
{
  public const string Section = "Rcinel";

  /// <summary>rcinel.json or the folder that holds it. Usually the i18n folder of a git checkout.</summary>
  public string Project { get; set; } = "";

  /// <summary>
  /// Users, sign-in keys and nothing else. Kept apart from the project on purpose: password hashes
  /// don't belong in the app's repository.
  /// </summary>
  public string Data { get; set; } = "data";

  /// <summary>Creates this admin at start when there are no users yet. For containers; otherwise use the setup link from the log.</summary>
  public string AdminUser { get; set; } = "";

  public string AdminPassword { get; set; } = "";
}
