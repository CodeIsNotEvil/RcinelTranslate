using System.Collections.Concurrent;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace CINE.Translate.Editor.Services;

public static class Roles
{
  /// <summary>Everything, including users, languages and settings.</summary>
  public const string Admin = "admin";

  /// <summary>Translates, reviews, adds screenshots.</summary>
  public const string Translator = "translator";
}

/// <summary>One account. The stamp changes with the password, which signs out every other session of the user.</summary>
public sealed record EditorUser(string Name, string Role, string PasswordHash, string Stamp, DateTimeOffset Created);

/// <summary>
/// Accounts in data/users.json. A file instead of a database: an editor has a handful of users, and
/// a file is one less thing to run next to it. Passwords are hashed with ASP.NET Core Identity's
/// PBKDF2 hasher.
/// </summary>
public sealed class UserStore
{
  private const int MaxFailures = 5;
  private static readonly TimeSpan LockoutTime = TimeSpan.FromMinutes(1);
  private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

  private readonly object gate = new();
  private readonly string file;
  private readonly PasswordHasher<EditorUser> hasher = new();
  private readonly ConcurrentDictionary<string, (int Count, DateTimeOffset Until)> failures = new(StringComparer.OrdinalIgnoreCase);
  private List<EditorUser> users;

  public UserStore(IOptions<EditorOptions> options, IHostEnvironment environment, ILogger<UserStore> logger)
  {
    string data = Path.GetFullPath(Path.Combine(environment.ContentRootPath, options.Value.Data));
    Directory.CreateDirectory(data);
    file = Path.Combine(data, "users.json");
    users = File.Exists(file) ? JsonSerializer.Deserialize<List<EditorUser>>(File.ReadAllText(file), Json) ?? [] : [];

    if (users.Count == 0 && !string.IsNullOrWhiteSpace(options.Value.AdminUser) && !string.IsNullOrWhiteSpace(options.Value.AdminPassword))
    {
      Add(options.Value.AdminUser, options.Value.AdminPassword, Roles.Admin);
      logger.LogInformation("Created the admin account {User} from the configuration", options.Value.AdminUser);
    }
    if (users.Count == 0)
    {
      // Whoever reads the server log may create the first account. Without the token, a freshly
      // started editor on a public address would belong to the first visitor.
      SetupToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
      logger.LogWarning("No users yet. Create the first admin account at /setup?token={Token}", SetupToken);
    }
  }

  /// <summary>The token /setup asks for while there are no users; null once the first account exists.</summary>
  public string? SetupToken { get; private set; }

  public IReadOnlyList<EditorUser> All()
  {
    lock (gate)
    {
      return [.. users.OrderBy(u => u.Name, StringComparer.OrdinalIgnoreCase)];
    }
  }

  public EditorUser? Find(string name)
  {
    lock (gate)
    {
      return users.FirstOrDefault(u => string.Equals(u.Name, name, StringComparison.OrdinalIgnoreCase));
    }
  }

  /// <summary>Creates an account. Throws ArgumentException with a message for the form.</summary>
  public EditorUser Add(string name, string password, string role)
  {
    name = name.Trim();
    if (name.Length is < 2 or > 40 || name.Any(c => char.IsWhiteSpace(c) || char.IsControl(c)) || name.StartsWith("ai:", StringComparison.OrdinalIgnoreCase))
    {
      throw new ArgumentException("A user name needs 2 to 40 characters, without spaces, and may not start with \"ai:\".");
    }
    CheckPassword(password);
    if (role is not (Roles.Admin or Roles.Translator))
    {
      throw new ArgumentException("Unknown role.");
    }
    lock (gate)
    {
      if (users.Any(u => string.Equals(u.Name, name, StringComparison.OrdinalIgnoreCase)))
      {
        throw new ArgumentException($"There already is a user called {name}.");
      }
      EditorUser user = new(name, role, "", NewStamp(), DateTimeOffset.UtcNow);
      user = user with { PasswordHash = hasher.HashPassword(user, password) };
      users.Add(user);
      Save();
      SetupToken = null;
      return user;
    }
  }

  public void SetPassword(string name, string password)
  {
    CheckPassword(password);
    Update(name, u => u with { PasswordHash = hasher.HashPassword(u, password), Stamp = NewStamp() });
  }

  public void SetRole(string name, string role)
  {
    lock (gate)
    {
      if (role != Roles.Admin && users.Count(u => u.Role == Roles.Admin && !string.Equals(u.Name, name, StringComparison.OrdinalIgnoreCase)) == 0)
      {
        throw new ArgumentException("The editor needs at least one admin.");
      }
      Update(name, u => u with { Role = role, Stamp = NewStamp() });
    }
  }

  public void Remove(string name)
  {
    lock (gate)
    {
      EditorUser user = Find(name) ?? throw new ArgumentException($"There is no user called {name}.");
      if (user.Role == Roles.Admin && users.Count(u => u.Role == Roles.Admin) == 1)
      {
        throw new ArgumentException("The last admin can't be removed.");
      }
      users.Remove(user);
      Save();
    }
  }

  /// <summary>Checks a sign-in. Five wrong passwords lock the name for a minute, which makes guessing slow.</summary>
  public EditorUser? Verify(string name, string password)
  {
    if (failures.TryGetValue(name, out (int Count, DateTimeOffset Until) f) && f.Count >= MaxFailures && f.Until > DateTimeOffset.UtcNow)
    {
      return null;
    }
    EditorUser? user = Find(name);
    if (user is not null && hasher.VerifyHashedPassword(user, user.PasswordHash, password) != PasswordVerificationResult.Failed)
    {
      failures.TryRemove(name, out _);
      return user;
    }
    failures.AddOrUpdate(name, _ => (1, DateTimeOffset.UtcNow + LockoutTime), (_, old) => (old.Count + 1, DateTimeOffset.UtcNow + LockoutTime));
    return null;
  }

  public bool IsLockedOut(string name) =>
    failures.TryGetValue(name, out (int Count, DateTimeOffset Until) f) && f.Count >= MaxFailures && f.Until > DateTimeOffset.UtcNow;

  /// <summary>The claims of the sign-in cookie.</summary>
  public static ClaimsPrincipal Principal(EditorUser user) => new(new ClaimsIdentity(
    [new Claim(ClaimTypes.Name, user.Name), new Claim(ClaimTypes.Role, user.Role), new Claim("stamp", user.Stamp)],
    "Cookies"));

  /// <summary>True when the cookie still belongs to an existing user with the same password and role.</summary>
  public bool IsCurrent(ClaimsPrincipal principal)
  {
    EditorUser? user = principal.Identity?.Name is string name ? Find(name) : null;
    return user is not null && principal.FindFirst("stamp")?.Value == user.Stamp;
  }

  private void Update(string name, Func<EditorUser, EditorUser> change)
  {
    lock (gate)
    {
      int index = users.FindIndex(u => string.Equals(u.Name, name, StringComparison.OrdinalIgnoreCase));
      if (index < 0)
      {
        throw new ArgumentException($"There is no user called {name}.");
      }
      users[index] = change(users[index]);
      Save();
    }
  }

  private static void CheckPassword(string password)
  {
    if (password.Length < 10)
    {
      throw new ArgumentException("A password needs at least 10 characters.");
    }
  }

  private static string NewStamp() => Convert.ToHexString(RandomNumberGenerator.GetBytes(8));

  private void Save()
  {
    string temp = file + ".tmp";
    File.WriteAllText(temp, JsonSerializer.Serialize(users, Json));
    File.Move(temp, file, overwrite: true);
  }
}
