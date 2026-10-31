using System.Text.Json;

namespace CINE.Translate.Tests;

/// <summary>
/// spec/fixtures/cases.json, the cases the Java and JavaScript clients run too. If one of them
/// fails here, the clients disagree about what an app shows.
/// </summary>
public class ConformanceTests
{
  private static readonly string Fixtures = Path.Combine(AppContext.BaseDirectory, "fixtures");
  private static readonly string Translations = Path.Combine(Fixtures, "translations");
  private static readonly JsonElement Cases = JsonDocument.Parse(File.ReadAllText(Path.Combine(Fixtures, "cases.json"))).RootElement;

  public static TheoryData<string, string, string, string> TranslateCases()
  {
    TheoryData<string, string, string, string> data = [];
    foreach (JsonElement c in Cases.GetProperty("translate").EnumerateArray())
    {
      data.Add(c.GetProperty("language").GetString()!, c.GetProperty("id").GetString()!, c.GetProperty("english").GetString()!, c.GetProperty("expected").GetString()!);
    }
    return data;
  }

  public static TheoryData<string> InvalidFiles()
  {
    TheoryData<string> data = [];
    foreach (JsonElement name in Cases.GetProperty("invalid").EnumerateArray())
    {
      data.Add(name.GetString()!);
    }
    return data;
  }

  [Theory]
  [MemberData(nameof(TranslateCases))]
  public void Translates_like_the_other_clients(string language, string id, string english, string expected)
  {
    TranslationSet set = TranslationSet.Load(language, Translations);
    Assert.Equal(expected, set.Get(id, english));
  }

  [Fact]
  public void Merged_sets_have_the_expected_size()
  {
    foreach (JsonProperty count in Cases.GetProperty("counts").EnumerateObject())
    {
      Assert.Equal(count.Value.GetInt32(), TranslationSet.Load(count.Name, Translations).Count);
    }
  }

  [Fact]
  public void Fallback_chains_go_from_specific_to_general()
  {
    foreach (JsonElement c in Cases.GetProperty("fallbackChains").EnumerateArray())
    {
      string[] expected = [.. c.GetProperty("chain").EnumerateArray().Select(e => e.GetString()!)];
      Assert.Equal(expected, TranslationSet.FallbackChain(c.GetProperty("language").GetString()!));
    }
  }

  [Fact]
  public void Missing_languages_throw_and_TryLoad_returns_false()
  {
    foreach (JsonElement language in Cases.GetProperty("missingLanguages").EnumerateArray())
    {
      Assert.Throws<FileNotFoundException>(() => TranslationSet.Load(language.GetString()!, Translations));
      Assert.False(TranslationSet.TryLoad(language.GetString()!, Translations, out _));
    }
  }

  [Theory]
  [MemberData(nameof(InvalidFiles))]
  public void Rejects_invalid_files(string name)
  {
    string path = Path.Combine(Fixtures, "invalid", name);
    FormatException error = Assert.Throws<FormatException>(() => TranslationSet.FromFile("x", path));
    // The message must say which file, or nobody finds the broken one among twenty languages.
    Assert.Contains(name, error.Message);
  }
}
