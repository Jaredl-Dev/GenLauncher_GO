using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Security;
using GenLauncherGO.Core.IO;
using GenLauncherGO.Core.Shell.Contracts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace GenLauncherGO.Infrastructure.Shell.Services;

/// <summary>
///     Opens external targets through the Windows shell.
/// </summary>
internal sealed class WindowsLauncherShellService : ILauncherShellService
{
    private readonly ILogger<WindowsLauncherShellService> _logger;

    private readonly Action<string> _openShellTarget;

    public WindowsLauncherShellService(ILogger<WindowsLauncherShellService>? logger = null)
        : this(logger, OpenShellTarget)
    {
    }

    internal WindowsLauncherShellService(
        ILogger<WindowsLauncherShellService>? logger,
        Action<string> openShellTarget)
    {
        _logger = logger ?? NullLogger<WindowsLauncherShellService>.Instance;
        _openShellTarget = openShellTarget ?? throw new ArgumentNullException(nameof(openShellTarget));
    }

    public void OpenUri(string uri)
    {
        if (string.IsNullOrWhiteSpace(uri))
        {
            _logger.LogWarning("Could not open shell URI because the target is empty.");
            return;
        }

        if (!Uri.TryCreate(uri, UriKind.Absolute, out Uri? parsedUri))
        {
            _logger.LogWarning("Could not open shell URI because the target is not absolute.");
            return;
        }

        if (!string.Equals(parsedUri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(parsedUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning(
                "Could not open shell URI because scheme {Scheme} is unsupported.",
                parsedUri.Scheme);
            return;
        }

        OpenShellTarget(parsedUri.AbsoluteUri, GetUriLogTarget(parsedUri));
    }

    public void OpenFolder(
        string folderPath,
        bool requireFiles = false,
        bool createIfMissing = false)
    {
        if (string.IsNullOrWhiteSpace(folderPath))
        {
            _logger.LogWarning("Could not open shell folder because the target is empty.");
            return;
        }

        string fullPath;
        try
        {
            fullPath = LexicalPath.NormalizeFullPath(folderPath);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException
                                              or PathTooLongException)
        {
            _logger.LogWarning(exception, "Could not normalize the shell folder target.");
            return;
        }

        if (!Directory.Exists(fullPath) && createIfMissing)
        {
            try
            {
                Directory.CreateDirectory(fullPath);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
                                                  or SecurityException)
            {
                _logger.LogWarning(
                    exception,
                    "Could not create shell folder target {Target}.",
                    GetFolderLogTarget(fullPath));

                return;
            }
        }

        if (!Directory.Exists(fullPath))
        {
            _logger.LogWarning(
                "Could not open shell folder {Target} because it does not exist.",
                GetFolderLogTarget(fullPath));
            return;
        }

        if (requireFiles && !Directory.EnumerateFiles(fullPath).Any())
        {
            _logger.LogWarning(
                "Could not open shell folder {Target} because it does not contain files.",
                GetFolderLogTarget(fullPath));
            return;
        }

        OpenShellTarget(fullPath, GetFolderLogTarget(fullPath));
    }

    private void OpenShellTarget(string target, string logTarget)
    {
        try
        {
            _openShellTarget(target);
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException or IOException)
        {
            _logger.LogWarning(
                exception,
                "Could not open shell target {Target}.",
                logTarget);
        }
    }

    private static string GetUriLogTarget(Uri uri)
    {
        return string.IsNullOrWhiteSpace(uri.Host)
            ? uri.Scheme
            : uri.Host;
    }

    private static string GetFolderLogTarget(string fullPath)
    {
        string folderName = Path.GetFileName(Path.TrimEndingDirectorySeparator(fullPath));
        return string.IsNullOrWhiteSpace(folderName)
            ? "folder"
            : folderName;
    }

    [ExcludeFromCodeCoverage(Justification =
        "Calls the host shell; shell-open behavior is covered through the injected adapter.")]
    private static void OpenShellTarget(string target)
    {
        Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
    }
}
