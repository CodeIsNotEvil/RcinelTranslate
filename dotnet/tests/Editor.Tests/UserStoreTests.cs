using Rcinel.Translate.Editor.Services;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Rcinel.Translate.Editor.Tests;

public sealed class UserStoreTests : IDisposable
{
  private readonly string data = Path.Combine(Path.GetTempPath(), "rcinel-users", Guid.NewGuid().ToString("N"));

  private sealed class Environment(string root) : IHostEnvironment
  {
    public string EnvironmentName { get; set; } = "Test";
    public string ApplicationName { get; set; } = "Test";
    public string ContentRootPath { get; set; } = root;
    public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;
  }

  private UserStore Store(string admin = "", string password = "") =>
    new(Options.Create(new EditorOptions { Data = data, AdminUser = admin, AdminPassword = password }), new Environment(data), NullLogger<UserStore>.Instance);

  public void Dispose() => Directory.Delete(data, recursive: true);

  [Fact]
  public void Without_users_there_is_a_setup_token_until_the_first_account_exists()
  {
    UserStore store = Store();
    Assert.NotNull(store.SetupToken);
    store.Add("alex", "a-long-password", Roles.Admin);
    Assert.Null(store.SetupToken);
    // A new process reads the same file.
    Assert.NotNull(Store().Find("ALEX"));
  }

  [Fact]
  public void The_configured_admin_is_created_once()
  {
    UserStore store = Store("admin", "a-long-password");
    Assert.Null(store.SetupToken);
    Assert.Equal(Roles.Admin, store.Find("admin")!.Role);
  }

  [Fact]
  public void Passwords_are_hashed_and_checked()
  {
    UserStore store = Store("admin", "a-long-password");
    Assert.DoesNotContain("a-long-password", File.ReadAllText(Path.Combine(data, "users.json")));
    Assert.NotNull(store.Verify("admin", "a-long-password"));
    Assert.Null(store.Verify("admin", "wrong"));
    Assert.Throws<ArgumentException>(() => store.Add("short", "short", Roles.Translator));
    Assert.Throws<ArgumentException>(() => store.Add("ai:bot", "a-long-password", Roles.Translator));
  }

  [Fact]
  public void Five_wrong_passwords_lock_the_name()
  {
    UserStore store = Store("admin", "a-long-password");
    for (int i = 0; i < 5; i++)
    {
      Assert.Null(store.Verify("admin", "wrong"));
    }
    Assert.True(store.IsLockedOut("admin"));
    Assert.Null(store.Verify("admin", "a-long-password"));
  }

  [Fact]
  public void A_new_password_ends_old_sessions()
  {
    UserStore store = Store("admin", "a-long-password");
    System.Security.Claims.ClaimsPrincipal session = UserStore.Principal(store.Find("admin")!);
    Assert.True(store.IsCurrent(session));
    store.SetPassword("admin", "another-long-password");
    Assert.False(store.IsCurrent(session));
  }

  [Fact]
  public void The_last_admin_stays()
  {
    UserStore store = Store("admin", "a-long-password");
    store.Add("tina", "a-long-password", Roles.Translator);
    Assert.Throws<ArgumentException>(() => store.Remove("admin"));
    Assert.Throws<ArgumentException>(() => store.SetRole("admin", Roles.Translator));
    store.Remove("tina");
    Assert.Null(store.Find("tina"));
  }
}
