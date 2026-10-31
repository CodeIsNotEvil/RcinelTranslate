using System.Globalization;

namespace CINE.Translate.Tests;

// Rcinel is static, so these tests share its state; xUnit runs a class's tests one after another,
// and this is the only class that touches it.
public class RcinelTests : IDisposable
{
  private static readonly string Translations = Path.Combine(AppContext.BaseDirectory, "fixtures", "translations");
  private const string Save = "0192f3a0-1c4e-7a10-8000-000000000001";
  private const string Greeting = "0192f3a0-1c4e-7a10-8000-000000000003";

  public void Dispose() => Rcinel.Reset();

  [Fact]
  public void Shows_the_english_text_before_anything_is_loaded()
  {
    Rcinel.Reset();
    Assert.Equal("Save", Rcinel.Translate(Save, "Save"));
    Assert.Equal("", Rcinel.Language);
  }

  [Fact]
  public void Load_switches_the_language_and_raises_LanguageChanged()
  {
    int raised = 0;
    void Count(object? sender, EventArgs e) => raised++;
    Rcinel.LanguageChanged += Count;
    try
    {
      Rcinel.Load("de-AT", Translations);
      Assert.Equal("de-AT", Rcinel.Language);
      Assert.Equal("Sichern", Rcinel.Translate(Save, "Save"));
      Assert.Equal(1, raised);
    }
    finally
    {
      Rcinel.LanguageChanged -= Count;
    }
  }

  [Fact]
  public void Load_uses_Directory_by_default()
  {
    string old = Rcinel.Directory;
    try
    {
      Rcinel.Directory = Translations;
      Rcinel.Load("de");
      Assert.Equal("Speichern", Rcinel.Translate(Save, "Save"));
    }
    finally
    {
      Rcinel.Directory = old;
    }
  }

  [Fact]
  public void TryLoad_keeps_the_current_language_when_the_app_does_not_ship_one()
  {
    Rcinel.Load("de", Translations);
    Assert.False(Rcinel.TryLoad("fr", Translations));
    Assert.Equal("de", Rcinel.Language);
  }

  [Fact]
  public void Formats_numbered_placeholders_with_the_current_culture()
  {
    Rcinel.Use(TranslationSet.Parse("de", $"{{\"{Greeting}\": \"{{0:N1}} Äpfel\"}}"));
    CultureInfo old = CultureInfo.CurrentCulture;
    try
    {
      CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
      Assert.Equal("1,5 Äpfel", Rcinel.Translate(Greeting, "{0:N1} apples", 1.5));
    }
    finally
    {
      CultureInfo.CurrentCulture = old;
    }
  }

  [Fact]
  public void FromStream_reads_embedded_resources()
  {
    // With a BOM, the way Visual Studio saves resources on Windows.
    using MemoryStream stream = new([0xEF, 0xBB, 0xBF, .. "{\"a\": \"b\"}"u8.ToArray()]);
    Assert.Equal("b", TranslationSet.FromStream("x", stream).Get("a", "fallback"));
  }
}
