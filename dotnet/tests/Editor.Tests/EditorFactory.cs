using System.Net;
using System.Text.RegularExpressions;
using Rcinel.Translate.Core;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Rcinel.Translate.Editor.Tests;

/// <summary>The real editor in memory, on a temp project and a temp data folder.</summary>
public sealed class EditorFactory : WebApplicationFactory<Program>
{
  public const string Admin = "admin";
  public const string Password = "correct-horse-battery";

  public EditorFactory()
  {
    Project = new TempProject("de");
    Project.Project.Source["0192f3a0-1c4e-7a10-8000-000000000001"] = "Save";
    Directory.CreateDirectory(Project.Project.ScreenshotDirectory);
    File.WriteAllBytes(Path.Combine(Project.Project.ScreenshotDirectory, "shot.png"), TinyPng);
    Project.Project.Save();
  }

  public TempProject Project { get; }

  /// <summary>A 1 x 1 PNG.</summary>
  public static readonly byte[] TinyPng = Convert.FromBase64String(
    "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

  protected override void ConfigureWebHost(IWebHostBuilder builder)
  {
    builder.UseSetting("Rcinel:Project", Project.ProjectDirectory);
    builder.UseSetting("Rcinel:Data", Path.Combine(Project.Root, "data"));
    builder.UseSetting("Rcinel:AdminUser", Admin);
    builder.UseSetting("Rcinel:AdminPassword", Password);
    builder.UseEnvironment("Production");
  }

  public HttpClient Anonymous() => CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true });

  /// <summary>Signs in through the real login form, antiforgery token and all.</summary>
  public async Task<HttpClient> SignedIn(string name = Admin, string password = Password)
  {
    HttpClient client = Anonymous();
    HttpResponseMessage response = await SubmitLogin(client, name, password);
    if (response.StatusCode != HttpStatusCode.Redirect)
    {
      throw new InvalidOperationException($"Sign-in failed: {response.StatusCode}");
    }
    return client;
  }

  public static async Task<HttpResponseMessage> SubmitLogin(HttpClient client, string name, string password)
  {
    string page = await client.GetStringAsync("/login");
    string token = Regex.Match(page, "name=\"__RequestVerificationToken\" value=\"([^\"]+)\"").Groups[1].Value;
    return await client.PostAsync("/login", new FormUrlEncodedContent(new Dictionary<string, string>
    {
      ["_handler"] = "login",
      ["__RequestVerificationToken"] = token,
      ["Input.Name"] = name,
      ["Input.Password"] = password,
    }));
  }

  protected override void Dispose(bool disposing)
  {
    base.Dispose(disposing);
    if (disposing)
    {
      Project.Dispose();
    }
  }
}
