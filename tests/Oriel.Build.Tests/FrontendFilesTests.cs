using System.Diagnostics;

namespace Oriel.Build.Tests;

/// <summary>
/// Publishes a project with Oriel's targets, as an app's would be, to see
/// what ends up in the published wwwroot.
/// </summary>
public sealed class FrontendFilesTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "oriel-frontend-" + Guid.NewGuid().ToString("N"));

    public FrontendFilesTests()
    {
        var build = Path.Combine(RepositoryRoot(), "src", "Oriel", "build");
        Directory.CreateDirectory(Path.Combine(_dir, "dist", "assets"));
        File.WriteAllText(Path.Combine(_dir, "App.csproj"), $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <Import Project="{build}/Oriel.props" />
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <OrielFrontendDist>dist</OrielFrontendDist>
              </PropertyGroup>
              <ItemGroup>
                <!-- A file of the app's own in wwwroot, besides the frontend's. -->
                <Content Include="extra.txt" Link="wwwroot/extra.txt" CopyToOutputDirectory="PreserveNewest" CopyToPublishDirectory="PreserveNewest" />
              </ItemGroup>
              <Import Project="{build}/Oriel.targets" />
            </Project>
            """);
        File.WriteAllText(Path.Combine(_dir, "extra.txt"), "kept");
        File.WriteAllText(Path.Combine(_dir, "dist", "index.html"), "<script src=\"assets/app-1.js\"></script>");
        File.WriteAllText(Path.Combine(_dir, "dist", "assets", "app-1.js"), "1");
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void KeepsOnlyTheFilesOfTheLastFrontend()
    {
        Publish("");
        // The frontend is built again: the bundler names its files after their content.
        File.Delete(Path.Combine(_dir, "dist", "assets", "app-1.js"));
        File.WriteAllText(Path.Combine(_dir, "dist", "assets", "app-2.js"), "2");
        File.WriteAllText(Path.Combine(_dir, "dist", "index.html"), "<script src=\"assets/app-2.js\"></script>");
        // With other options: the SDK cleans up after the publishes of the same options only.
        Publish("-r win-x64 --self-contained false");

        Assert.Equal(["assets/app-2.js", "extra.txt", "index.html"], Files(Path.Combine(_dir, "publish", "wwwroot")));
    }

    private void Publish(string options)
    {
        var dotnet = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") is { Length: > 0 } host ? host : "dotnet";
        using var process = Process.Start(new ProcessStartInfo(dotnet, $"publish -c Release -o publish -nologo {options}")
        {
            WorkingDirectory = _dir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        })!;
        var output = process.StandardOutput.ReadToEndAsync();
        var errors = process.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, output.Result + errors);
    }

    private static string[] Files(string folder) =>
        [.. Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(folder, f).Replace('\\', '/'))
            .Order(StringComparer.Ordinal)];

    private static string RepositoryRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "Oriel.slnx")))
                return dir.FullName;
        throw new InvalidOperationException("The tests do not run from the repository.");
    }
}
