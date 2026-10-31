using System.Diagnostics;

namespace Rcinel.Translate.Tests;

/// <summary>
/// The NuGet package as apps get it: packs Rcinel.Translate and builds small projects against it in
/// a temp folder, outside this repository's Directory.Build.props. A ProjectReference wouldn't do,
/// because NuGet imports build/Rcinel.Translate.targets only from a package.
/// </summary>
/// <remarks>Slow (a pack and three builds, about a minute), and the netstandard project needs nuget.org.</remarks>
public sealed class PackageTests : IClassFixture<PackageTests.Feed>
{
  private const string Call = "Translate(\"01a192e3-5bcc-7000-a8d2-327b1cb0f737\", \"Settings\")";

  private readonly Feed feed;

  public PackageTests(Feed feed) => this.feed = feed;

  /// <summary>Packs once for all tests, into a temp feed with its own package cache.</summary>
  public sealed class Feed : IDisposable
  {
    public Feed()
    {
      Root = Path.Combine(Path.GetTempPath(), "rcinel-package-tests", Guid.NewGuid().ToString("N"));
      Directory.CreateDirectory(Root);
      string repo = FindRepository();
      Packages = Path.Combine(Root, "feed");
      Result pack = Dotnet(Root, "pack", Path.Combine(repo, "dotnet", "src", "Translate", "Rcinel.Translate.csproj"), "-c", "Release", "-o", Packages);
      Assert.True(pack.ExitCode == 0, pack.Output);
      Version = Path.GetFileNameWithoutExtension(Directory.GetFiles(Packages, "Rcinel.Translate.*.nupkg").Single())["Rcinel.Translate.".Length..];
      File.WriteAllText(Path.Combine(Root, "nuget.config"), $"""
        <configuration>
          <packageSources>
            <clear />
            <add key="local" value="{Packages}" />
            <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
          </packageSources>
        </configuration>
        """);
    }

    public string Root { get; }
    public string Packages { get; }
    public string Version { get; }

    public void Dispose()
    {
      try
      {
        Directory.Delete(Root, recursive: true);
      }
      catch (IOException)
      {
        // A leftover temp folder isn't worth failing the run over.
      }
    }
  }

  [Fact]
  public void Apps_on_modern_dotnet_call_Translate_without_any_using()
  {
    string project = Project("App", "net10.0", "Exe", properties: "",
      code: $"System.Console.WriteLine({Call});");
    Result run = Dotnet(feed.Root, "run", "--project", project);
    Assert.True(run.ExitCode == 0, run.Output);
    Assert.Contains("Settings", run.Output);
  }

  [Fact]
  public void The_switch_turns_the_global_using_off()
  {
    string project = Project("OptOut", "net10.0", "Exe", properties: "<RcinelTranslateGlobalUsing>false</RcinelTranslateGlobalUsing>",
      code: $"System.Console.WriteLine({Call});");
    Result build = Dotnet(feed.Root, "build", project);
    Assert.NotEqual(0, build.ExitCode);
    Assert.Contains("CS0103", build.Output);
  }

  [Fact]
  public void Older_targets_get_no_global_using_and_import_per_file()
  {
    // netstandard2.0 defaults to C# 7.3, which doesn't know global usings: if the package added one
    // here, this project would fail with CS8773.
    string project = Project("Library", "netstandard2.0", "Library", properties: "",
      code: $"public static class Texts {{ public static string Title() => {Call}; }}",
      usings: "using static Rcinel.Translate.Rcinel;");
    Result build = Dotnet(feed.Root, "build", project);
    Assert.True(build.ExitCode == 0, build.Output);
  }

  private string Project(string name, string framework, string outputType, string properties, string code, string usings = "")
  {
    string dir = Path.Combine(feed.Root, name);
    Directory.CreateDirectory(dir);
    string file = Path.Combine(dir, name + ".csproj");
    File.WriteAllText(file, $"""
      <Project Sdk="Microsoft.NET.Sdk">
        <PropertyGroup>
          <TargetFramework>{framework}</TargetFramework>
          <OutputType>{outputType}</OutputType>
          <RestorePackagesPath>{Path.Combine(feed.Root, "packages")}</RestorePackagesPath>
          {properties}
        </PropertyGroup>
        <ItemGroup>
          <PackageReference Include="Rcinel.Translate" Version="{feed.Version}" />
        </ItemGroup>
      </Project>
      """);
    string body = outputType == "Exe" ? $"public static class Program {{ public static void Main() {{ {code} }} }}" : code;
    File.WriteAllText(Path.Combine(dir, "Code.cs"), $"{usings}\nnamespace {name}\n{{\n{body}\n}}\n");
    return file;
  }

  private sealed record Result(int ExitCode, string Output);

  private static Result Dotnet(string workingDirectory, params string[] args)
  {
    ProcessStartInfo start = new(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet")
    {
      WorkingDirectory = workingDirectory,
      RedirectStandardOutput = true,
      RedirectStandardError = true,
    };
    foreach (string arg in args)
    {
      start.ArgumentList.Add(arg);
    }
    // `dotnet test` runs inside MSBuild, whose variables would steer the nested builds to this
    // repository's SDK resolution and properties.
    foreach (string key in start.Environment.Keys.Where(k => k.StartsWith("MSBuild", StringComparison.OrdinalIgnoreCase)).ToList())
    {
      start.Environment.Remove(key);
    }
    start.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
    start.Environment["DOTNET_NOLOGO"] = "1";
    using Process process = Process.Start(start)!;
    Task<string> output = process.StandardOutput.ReadToEndAsync();
    Task<string> error = process.StandardError.ReadToEndAsync();
    if (!process.WaitForExit(TimeSpan.FromMinutes(5)))
    {
      process.Kill(entireProcessTree: true);
      return new Result(-1, "timed out\n" + output.Result + error.Result);
    }
    return new Result(process.ExitCode, output.Result + error.Result);
  }

  private static string FindRepository()
  {
    for (DirectoryInfo? dir = new(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
    {
      if (File.Exists(Path.Combine(dir.FullName, "dotnet", "RcinelTranslate.slnx")))
      {
        return dir.FullName;
      }
    }
    throw new DirectoryNotFoundException("The test runs outside the repository.");
  }
}
