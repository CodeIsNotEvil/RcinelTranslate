using Rcinel.Translate.Ai;
using Rcinel.Translate.Editor.Components;
using Rcinel.Translate.Editor.Services;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<EditorOptions>(builder.Configuration.GetSection(EditorOptions.Section));
string data = Path.GetFullPath(Path.Combine(builder.Environment.ContentRootPath, builder.Configuration[$"{EditorOptions.Section}:Data"] ?? "data"));

// Sign-in cookies survive restarts and container updates only if the keys that protect them do.
builder.Services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(data, "keys")));

builder.Services.AddSingleton<ProjectStore>();
builder.Services.AddSingleton<UserStore>();
builder.Services.AddSingleton<ITranslator>(_ => new ClaudeTranslator());
builder.Services.AddSingleton<DraftJobs>();

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
  .AddCookie(options =>
  {
    options.LoginPath = "/login";
    options.AccessDeniedPath = "/denied";
    // Not just "rcinel": other Rcinel apps on the same host would overwrite each other's cookie.
    options.Cookie.Name = "rcinel-translate";
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Strict;
    options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
    options.ExpireTimeSpan = TimeSpan.FromDays(14);
    options.SlidingExpiration = true;
    // A changed password, role or a removed user ends the other sessions of that user.
    options.Events.OnValidatePrincipal = context =>
    {
      UserStore users = context.HttpContext.RequestServices.GetRequiredService<UserStore>();
      if (context.Principal is null || !users.IsCurrent(context.Principal))
      {
        context.RejectPrincipal();
        return context.HttpContext.SignOutAsync();
      }
      return Task.CompletedTask;
    };
  });
builder.Services.AddAuthorizationBuilder()
  // Everything needs a signed-in user unless it says otherwise (login, setup, the CSS).
  .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build())
  .AddPolicy(Roles.Admin, policy => policy.RequireRole(Roles.Admin));
builder.Services.AddCascadingAuthenticationState();

builder.Services.AddRazorComponents()
  .AddInteractiveServerComponents();

WebApplication app = builder.Build();

if (!app.Environment.IsDevelopment())
{
  app.UseExceptionHandler("/error", createScopeForErrors: true);
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);

app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapStaticAssets().AllowAnonymous();

// Screenshots are served from the project's context folder, only to signed-in users: they show
// unreleased UI.
app.MapGet("/images/{file}", (string file, ProjectStore store) =>
{
  if (file != Path.GetFileName(file) || !ImageInfo.Extensions.Contains(Path.GetExtension(file).ToLowerInvariant()))
  {
    return Results.NotFound();
  }
  string? path = store.EnsureLoaded() ? store.Read(p => Path.Combine(p.ScreenshotDirectory, file)) : null;
  return path is not null && File.Exists(path) ? Results.File(path, ImageInfo.ContentType(file), enableRangeProcessing: false) : Results.NotFound();
});

// UseAntiforgery only checks the token and records the result; an endpoint that binds no form has
// to look at it itself, or any site could sign the user out with a cross-site POST.
app.MapPost("/account/logout", async (HttpContext context, IAntiforgery antiforgery) =>
{
  if (!await antiforgery.IsRequestValidAsync(context))
  {
    return Results.BadRequest();
  }
  await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
  return Results.LocalRedirect("/login");
});

app.MapRazorComponents<App>()
  .AddInteractiveServerRenderMode();

// Touch the stores once so the setup link (or a broken project) shows in the log at start, not on
// the first request.
app.Services.GetRequiredService<UserStore>();
app.Services.GetRequiredService<ProjectStore>();

app.Run();

/// <summary>Visible to the integration tests' WebApplicationFactory.</summary>
public partial class Program;
