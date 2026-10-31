using System.Globalization;
using System.Text;
using System.Text.Json;
using Rcinel.Translate.Core;

namespace Rcinel.Translate.Ai;

/// <summary>
/// The prompt. The system part depends only on the run (project, language, examples), so it is the
/// same for every batch and can be cached; everything that changes per batch goes in the user turn.
/// </summary>
public static class Prompts
{
  public static string System(DraftContext context)
  {
    LanguageSettings language = context.Language;
    string target = string.IsNullOrWhiteSpace(language.Name) ? language.Code : $"{language.Name} ({language.Code})";
    StringBuilder b = new();
    b.AppendLine($"You translate the user interface of {context.ProjectName} from {context.SourceLanguage} to {target}.");
    b.AppendLine("Your translations are drafts: a native speaker reviews each one in an editor and either confirms it or corrects it, so aim for text they can confirm unchanged.");
    b.AppendLine();
    b.AppendLine("How to translate:");
    b.AppendLine("- Translate the meaning a user of this screen needs, not word for word, and use the terms apps in this language usually use for common actions and settings.");
    b.AppendLine("- Keep every placeholder and tag exactly as written ({name}, {0}, {{count}}, %s, %1$d, <b>...</b>). Move them wherever the grammar of the target language needs them, but never translate, drop or add one.");
    b.AppendLine("- Keep leading and trailing spaces, line breaks, and the punctuation style: a trailing colon, ellipsis or question mark stays.");
    b.AppendLine("- When a string comes with a box size on a screenshot, the translation has to fit in that box at the same font size. Prefer a shorter word over a long one there; avoid abbreviations unless the language commonly uses one for that term.");
    b.AppendLine("- Leave product names, brand names, file names, code and URLs as they are.");
    b.AppendLine("- Short strings are ambiguous (\"Open\" as a verb or an adjective). Use the note, the screenshot and the neighbouring strings to pick the sense; when that doesn't settle it, pick the sense most likely on a button or label.");
    b.AppendLine("- Translate every string you are given. Return the ids exactly as given.");
    if (!string.IsNullOrWhiteSpace(context.Ai.Instructions))
    {
      b.AppendLine();
      b.AppendLine("Instructions from the project:");
      b.AppendLine(context.Ai.Instructions.Trim());
    }
    if (!string.IsNullOrWhiteSpace(language.Instructions))
    {
      b.AppendLine();
      b.AppendLine($"Instructions for {target}:");
      b.AppendLine(language.Instructions.Trim());
    }
    if (context.Examples.Count > 0)
    {
      b.AppendLine();
      b.AppendLine("Translations reviewers already confirmed in this app. Reuse their terms and tone so the app reads consistently:");
      b.AppendLine("<confirmed>");
      foreach (KeyValuePair<string, string> example in context.Examples)
      {
        b.AppendLine(JsonSerializer.Serialize(new { source = example.Key, translation = example.Value }));
      }
      b.AppendLine("</confirmed>");
    }
    return b.ToString();
  }

  public static string User(DraftContext context, DraftBatch batch)
  {
    StringBuilder b = new();
    if (batch.Screenshot is { } screenshot)
    {
      b.AppendLine($"The image is the screenshot \"{screenshot.Title}\". Strings with a \"box\" are shown inside that box on it.");
    }
    b.AppendLine($"Translate these {batch.Items.Count} strings into {context.Language.DisplayName}:");
    b.AppendLine("<strings>");
    foreach (DraftItem item in batch.Items)
    {
      Dictionary<string, string> entry = new() { ["id"] = item.Id, ["source"] = item.Source };
      if (!string.IsNullOrWhiteSpace(item.Note))
      {
        entry["note"] = item.Note!;
      }
      if (item.Previous is not null)
      {
        entry["previous"] = item.Previous;
        entry["previousNote"] = "a reviewer confirmed this for an older version of the source; keep its wording where the source didn't change";
      }
      if (item.Box is { } box && batch.Screenshot is { } shot)
      {
        entry["box"] = DescribeBox(box, shot);
      }
      b.AppendLine(JsonSerializer.Serialize(entry));
    }
    b.AppendLine("</strings>");
    return b.ToString();
  }

  /// <summary>"left 10%, top 20%, 192 x 40 px" - pixel sizes when the screenshot's size is known, since those say how much text fits.</summary>
  internal static string DescribeBox(Region box, DraftScreenshot shot)
  {
    string position = string.Create(CultureInfo.InvariantCulture, $"left {box.X * 100:0}%, top {box.Y * 100:0}%");
    if (shot.Width > 0 && shot.Height > 0)
    {
      string size = string.Create(CultureInfo.InvariantCulture, $"{position}, {box.Width * shot.Width:0} x {box.Height * shot.Height:0} px of a {shot.Width} x {shot.Height} px screenshot");
      return box.FontSize > 0 ? size + string.Create(CultureInfo.InvariantCulture, $", text {box.FontSize * shot.Height:0} px high") : size;
    }
    return string.Create(CultureInfo.InvariantCulture, $"{position}, {box.Width * 100:0}% wide and {box.Height * 100:0}% high");
  }

  /// <summary>The structured-output schema: the API guarantees the reply matches it.</summary>
  public static readonly Dictionary<string, JsonElement> Schema = new()
  {
    ["type"] = JsonSerializer.SerializeToElement("object"),
    ["properties"] = JsonSerializer.SerializeToElement(new
    {
      translations = new
      {
        type = "array",
        items = new
        {
          type = "object",
          properties = new { id = new { type = "string" }, text = new { type = "string" } },
          required = new[] { "id", "text" },
          additionalProperties = false,
        },
      },
    }),
    ["required"] = JsonSerializer.SerializeToElement(new[] { "translations" }),
    ["additionalProperties"] = JsonSerializer.SerializeToElement(false),
  };
}
