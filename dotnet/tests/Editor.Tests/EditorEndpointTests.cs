using System.Net;

namespace Rcinel.Translate.Editor.Tests;

public sealed class EditorEndpointTests(EditorFactory factory) : IClassFixture<EditorFactory>
{
  [Theory]
  [InlineData("/")]
  [InlineData("/languages/de")]
  [InlineData("/users")]
  [InlineData("/images/shot.png")]
  public async Task Everything_but_the_sign_in_needs_a_signed_in_user(string path)
  {
    HttpResponseMessage response = await factory.Anonymous().GetAsync(path);
    Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
    Assert.StartsWith("/login", response.Headers.Location!.PathAndQuery.Replace("http://localhost", ""));
  }

  [Fact]
  public async Task The_sign_in_page_and_the_styles_are_public()
  {
    HttpClient client = factory.Anonymous();
    Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/login")).StatusCode);
    Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/favicon.svg")).StatusCode);
  }

  [Fact]
  public async Task A_wrong_password_does_not_sign_in()
  {
    HttpClient client = factory.Anonymous();
    HttpResponseMessage response = await EditorFactory.SubmitLogin(client, EditorFactory.Admin, "not-the-password");
    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    Assert.Contains("The user name or password is wrong.", await response.Content.ReadAsStringAsync());
    Assert.Equal(HttpStatusCode.Redirect, (await client.GetAsync("/")).StatusCode);
  }

  [Fact]
  public async Task Signed_in_users_get_pages_and_screenshots()
  {
    HttpClient client = await factory.SignedIn();
    Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/")).StatusCode);
    HttpResponseMessage image = await client.GetAsync("/images/shot.png");
    Assert.Equal(HttpStatusCode.OK, image.StatusCode);
    Assert.Equal("image/png", image.Content.Headers.ContentType?.MediaType);
    Assert.Equal(EditorFactory.TinyPng, await image.Content.ReadAsByteArrayAsync());
  }

  [Theory]
  [InlineData("/images/..%2Frcinel-translate.json")]
  [InlineData("/images/..%2F..%2Fi18n%2Frcinel-translate.json")]
  [InlineData("/images/notes.json")]
  [InlineData("/images/missing.png")]
  public async Task Only_screenshot_images_are_served(string path)
  {
    HttpClient client = await factory.SignedIn();
    Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(path)).StatusCode);
  }

  [Fact]
  public async Task Logout_needs_the_antiforgery_token()
  {
    HttpClient client = await factory.SignedIn();
    HttpResponseMessage forged = await client.PostAsync("/account/logout", new FormUrlEncodedContent([]));
    Assert.Equal(HttpStatusCode.BadRequest, forged.StatusCode);
    Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/")).StatusCode);
  }

  [Theory]
  [InlineData("/somewhere", "/somewhere")]
  [InlineData("//evil.example", "/")]
  [InlineData("/\\evil.example", "/")]
  [InlineData("https://evil.example", "/")]
  [InlineData(null, "/")]
  public void Return_urls_stay_on_the_site(string? url, string expected) =>
    Assert.Equal(expected, Components.Pages.Login.SafeReturnUrl(url));
}
