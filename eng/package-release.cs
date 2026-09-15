#:property PublishAot=false
#:property IsPackable=false

using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace GenLauncherGO.Build;

/// <summary>
///     Builds and verifies the portable release using the repository's .NET SDK and pinned Velopack tool.
///     This file-based application stays outside the application and its publish dependencies.
/// </summary>
internal static class PackageRelease
{
    private const string PublishProfile = "WinX64SelfContained";
    private const string Channel = "win";

    private static async Task<int> Main(string[] args)
    {
        try
        {
            bool bootstrap = false;
            string? previousReleaseRepositoryUrl = null;
            for (int index = 0; index < args.Length; index++)
            {
                switch (args[index])
                {
                    case "--bootstrap":
                        bootstrap = true;
                        break;
                    case "--previous-release-repository-url" when index + 1 < args.Length:
                        previousReleaseRepositoryUrl = args[++index];
                        break;
                    case "--help":
                    case "-h":
                        string scriptPath = Path.GetRelativePath(
                            Environment.CurrentDirectory,
                            Path.Combine(GetRepositoryRoot(), "eng", "package-release.cs"));
                        Console.WriteLine(
                            $"dotnet run --file \"{scriptPath}\" --no-cache -- [--bootstrap | --previous-release-repository-url URL]");
                        return 0;
                    default:
                        throw new ArgumentException($"Unknown or incomplete option '{args[index]}'. Use --help for usage.");
                }
            }

            if (bootstrap && !string.IsNullOrWhiteSpace(previousReleaseRepositoryUrl))
            {
                throw new ArgumentException(
                    "--previous-release-repository-url cannot be combined with --bootstrap because a bootstrap release has no predecessor.");
            }

            await PackageAsync(bootstrap, previousReleaseRepositoryUrl);
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception.Message);
            return 1;
        }
    }

    private static async Task PackageAsync(bool bootstrap, string? previousReleaseRepositoryUrl)
    {
        string repositoryRoot = GetRepositoryRoot();
        string projectPath = Path.Combine(repositoryRoot, "GenLauncherGO", "GenLauncherGO.csproj");
        string toolManifestPath = Path.Combine(repositoryRoot, ".config", "dotnet-tools.json");
        string iconPath = Path.Combine(repositoryRoot, "GenLauncherGO", "Shared", "Resources", "Icons", "GenLauncherGo.ico");
        string artifactsDirectory = Path.Combine(repositoryRoot, "artifacts");
        string publishDirectory = Path.Combine(artifactsDirectory, "velopack-publish");
        string workingReleaseDirectory = Path.Combine(artifactsDirectory, "velopack-work");
        string uploadDirectory = Path.Combine(artifactsDirectory, "release");

        string metadataJson = await RunDotNetAsync(repositoryRoot, true,
            "msbuild", projectPath,
            $"-p:PublishProfile={PublishProfile}",
            "-getProperty:Version",
            "-getProperty:VelopackUpdateRepositoryUrl",
            "-getProperty:AssemblyName",
            "-getProperty:RuntimeIdentifier",
            "-getProperty:VelopackVersion");
        using var metadata = JsonDocument.Parse(metadataJson);
        JsonElement properties = metadata.RootElement.GetProperty("Properties");
        string version = ReadRequiredProperty(properties, "Version");
        string repositoryUrl = ReadRequiredProperty(properties, "VelopackUpdateRepositoryUrl");
        string packageId = ReadRequiredProperty(properties, "AssemblyName");
        string runtime = ReadRequiredProperty(properties, "RuntimeIdentifier");
        string velopackLibraryVersion = ReadRequiredProperty(properties, "VelopackVersion");

        using var toolManifest = JsonDocument.Parse(await File.ReadAllTextAsync(toolManifestPath));
        string? velopackToolVersion = toolManifest.RootElement.GetProperty("tools").GetProperty("vpk")
            .GetProperty("version").GetString();
        if (!string.Equals(velopackLibraryVersion, velopackToolVersion, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"Velopack library version {velopackLibraryVersion} does not match vpk tool version {velopackToolVersion}.");
        }

        await RunDotNetAsync(repositoryRoot, false, "tool", "restore");
        ResetOwnedDirectory(artifactsDirectory, publishDirectory);
        ResetOwnedDirectory(artifactsDirectory, workingReleaseDirectory);
        ResetOwnedDirectory(artifactsDirectory, uploadDirectory);

        if (!bootstrap)
        {
            string previousRepositoryUrl = string.IsNullOrWhiteSpace(previousReleaseRepositoryUrl)
                ? repositoryUrl
                : previousReleaseRepositoryUrl;
            await RunDotNetAsync(repositoryRoot, false,
                "tool", "run", "vpk", "--", "download", "github",
                "--repoUrl", previousRepositoryUrl,
                "--outputDir", workingReleaseDirectory,
                "--channel", Channel);
        }

        await RunDotNetAsync(repositoryRoot, false,
            "publish", projectPath,
            $"-p:PublishProfile={PublishProfile}",
            "--output", publishDirectory);

        string launcherPath = Path.Combine(publishDirectory, $"{packageId}.exe");
        if (!File.Exists(launcherPath))
        {
            throw new FileNotFoundException($"Expected the published launcher executable at {launcherPath}.");
        }

        await RunDotNetAsync(repositoryRoot, false,
            "tool", "run", "vpk", "--", "pack",
            "--packId", packageId,
            "--packVersion", version,
            "--packDir", publishDirectory,
            "--mainExe", $"{packageId}.exe",
            "--runtime", runtime,
            "--channel", Channel,
            "--icon", iconPath,
            "--packTitle", "GenLauncherGO",
            "--packAuthors", "GenLauncherGO",
            "--noInst", "true",
            "--shortcuts", "None",
            "--msi", "false",
            "--outputDir", workingReleaseDirectory);

        string[] requiredAssetNames =
        [
            $"{packageId}-{version}-full.nupkg",
            $"{packageId}-{Channel}-Portable.zip",
            $"releases.{Channel}.json"
        ];
        foreach (string assetName in requiredAssetNames)
        {
            string assetPath = Path.Combine(workingReleaseDirectory, assetName);
            if (!File.Exists(assetPath))
            {
                throw new FileNotFoundException($"Velopack did not produce the required release asset {assetName}.");
            }

            File.Copy(assetPath, Path.Combine(uploadDirectory, assetName));
        }

        string deltaName = $"{packageId}-{version}-delta.nupkg";
        string deltaPath = Path.Combine(workingReleaseDirectory, deltaName);
        if (File.Exists(deltaPath))
        {
            File.Copy(deltaPath, Path.Combine(uploadDirectory, deltaName));
        }

        VerifyPortableArchive(uploadDirectory, packageId);
        Console.WriteLine($"Created upload-ready Velopack release for version {version} in {uploadDirectory}");
        foreach (string assetPath in Directory.EnumerateFiles(uploadDirectory).Order(StringComparer.OrdinalIgnoreCase))
        {
            Console.WriteLine($"  {Path.GetFileName(assetPath)}");
        }
    }

    private static string ReadRequiredProperty(JsonElement properties, string name)
    {
        string? value = properties.GetProperty(name).GetString();
        return !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new InvalidDataException($"{name} must be defined by the application project.");
    }

    private static void ResetOwnedDirectory(string artifactsDirectory, string path)
    {
        string fullPath = Path.GetFullPath(path);
        string relativePath = Path.GetRelativePath(artifactsDirectory, fullPath);
        if (Path.IsPathRooted(relativePath) || relativePath is "." or ".." ||
            relativePath.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
        {
            throw new IOException($"Refusing to reset a directory outside the children of {artifactsDirectory}: {fullPath}");
        }

        DirectoryInfo directory = new(fullPath);
        if (directory.Exists)
        {
            // Match forced cleanup of build outputs without traversing links to change outside file attributes.
            if ((directory.Attributes & FileAttributes.ReparsePoint) == 0)
            {
                EnumerationOptions options = new()
                {
                    RecurseSubdirectories = true,
                    IgnoreInaccessible = false,
                    AttributesToSkip = FileAttributes.ReparsePoint
                };
                foreach (FileSystemInfo entry in directory.EnumerateFileSystemInfos("*", options))
                {
                    entry.Attributes &= ~FileAttributes.ReadOnly;
                }

                directory.Attributes &= ~FileAttributes.ReadOnly;
            }

            // Directory.Delete removes a reparse-point entry itself instead of recursing into its target.
            directory.Delete(true);
        }

        Directory.CreateDirectory(fullPath);
    }

    private static void VerifyPortableArchive(string uploadDirectory, string packageId)
    {
        using ZipArchive archive = ZipFile.OpenRead(Path.Combine(uploadDirectory, $"{packageId}-{Channel}-Portable.zip"));
        string[] entries = archive.Entries.Select(entry => entry.FullName.Replace('\\', '/').TrimStart('/')).ToArray();
        string[] expectedEntries = [".portable", $"{packageId}.exe", "Update.exe", $"current/{packageId}.exe", "current/sq.version"];
        if (!entries.Order(StringComparer.OrdinalIgnoreCase)
                .SequenceEqual(expectedEntries.Order(StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "The portable archive does not have the expected root stub, updater, marker, current app, and version metadata layout.");
        }

        if (entries.Any(entry => entry.Split('/').Contains("GenLauncherGO Data", StringComparer.OrdinalIgnoreCase)))
        {
            throw new InvalidDataException("The portable archive must not contain the user-owned GenLauncherGO Data directory.");
        }

        if (Directory.EnumerateFiles(uploadDirectory).Any(path =>
                path.EndsWith("Setup.exe", StringComparison.OrdinalIgnoreCase) ||
                path.EndsWith(".msi", StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidDataException("Portable-only packaging unexpectedly produced an installer or MSI asset.");
        }
    }

    private static async Task<string> RunDotNetAsync(string repositoryRoot, bool captureOutput, params string[] arguments)
    {
        ProcessStartInfo startInfo = new("dotnet")
        {
            WorkingDirectory = repositoryRoot,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = captureOutput
        };
        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using Process process = new() { StartInfo = startInfo };
        process.Start();
        Task<string> output = captureOutput ? process.StandardOutput.ReadToEndAsync() : Task.FromResult(string.Empty);
        await process.WaitForExitAsync();
        string result = await output;
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"dotnet {arguments[0]} failed with exit code {process.ExitCode}.");
        }

        return result;
    }

    private static string GetRepositoryRoot()
    {
        // CI remaps compile-time source paths. The documented commands and workflows run from the checkout root.
        string repositoryRoot = Environment.CurrentDirectory;
        if (!File.Exists(Path.Combine(repositoryRoot, "GenLauncherGO.sln")))
        {
            throw new InvalidOperationException("Run the packaging command from the repository root containing GenLauncherGO.sln.");
        }

        return repositoryRoot;
    }
}
