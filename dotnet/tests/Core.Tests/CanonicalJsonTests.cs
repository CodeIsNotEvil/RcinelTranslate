using CINE.Translate.Core;

namespace CINE.Translate.Core.Tests;

public class CanonicalJsonTests
{
  [Fact]
  public void Sorts_by_key_one_entry_per_line()
  {
    string json = CanonicalJson.FlatObject(new Dictionary<string, string>
    {
      ["0192f3a0-1c4e-7a10-8000-000000000002"] = "Zwei",
      ["0192f3a0-1c4e-7a10-8000-000000000001"] = "Eins",
    });
    Assert.Equal(
      "{\n  \"0192f3a0-1c4e-7a10-8000-000000000001\": \"Eins\",\n  \"0192f3a0-1c4e-7a10-8000-000000000002\": \"Zwei\"\n}\n",
      json);
  }

  [Fact]
  public void Empty_objects_are_one_line() => Assert.Equal("{}\n", CanonicalJson.FlatObject([]));

  [Theory]
  [InlineData("plain")]
  [InlineData("Grüße 😀 ünd ß")]
  [InlineData("quote \" backslash \\ slash /")]
  [InlineData("line\nbreak\r\ttab\b\f")]
  [InlineData("\u0001\u001f")]
  [InlineData("separators \u2028 \u2029")]
  [InlineData("lone \ud800 surrogate")]
  public void Round_trips_through_the_client_reader(string text)
  {
    string json = CanonicalJson.FlatObject([new("k", text)]);
    Assert.Equal(text, FlatJsonReader.ReadDictionary(json)["k"]);
    // Readable diffs: non-ASCII letters stay as they are.
    if (text.Contains('ü'))
    {
      Assert.Contains("ü", json);
    }
  }

  [Fact]
  public void Numbers_have_at_most_four_decimals_in_any_culture()
  {
    System.Globalization.CultureInfo old = System.Globalization.CultureInfo.CurrentCulture;
    try
    {
      System.Globalization.CultureInfo.CurrentCulture = new("de-DE");
      Assert.Equal("0.1235", CanonicalJson.Number(0.123456));
      Assert.Equal("0.3", CanonicalJson.Number(0.1 + 0.2));
      Assert.Equal("1", CanonicalJson.Number(1));
    }
    finally
    {
      System.Globalization.CultureInfo.CurrentCulture = old;
    }
  }
}
