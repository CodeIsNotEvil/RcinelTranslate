using Rcinel.Translate;
using static Rcinel.Translate.Rcinel;

// Deliberately outside the Rcinel namespaces: this is how an app writes it, and it must keep
// compiling. Inside an app, `Rcinel.Translate(...)` would bind to the namespace Rcinel.Translate
// (CS0118), which is why the documented call is the bare Translate(...) after `using static`.
namespace SomeApp.Settings;

[Collection(Rcinel.Translate.Tests.RcinelTests.StaticState)]
public class AppUsageTests
{
  private static readonly string Translations = Path.Combine(AppContext.BaseDirectory, "fixtures", "translations");

  [Fact]
  public void An_app_calls_Translate_after_using_static()
  {
    try
    {
      Load("de", Translations);
      Assert.Equal("Speichern", Translate("0192f3a0-1c4e-7a10-8000-000000000001", "Save"));
      Assert.Equal("Not translated", Translate("0192f3a0-1c4e-7a10-8000-0000000000ff", "Not translated"));
      Assert.Equal("de", Language);
      Assert.IsType<TranslationSet>(Current);
    }
    finally
    {
      Reset();
    }
  }
}
