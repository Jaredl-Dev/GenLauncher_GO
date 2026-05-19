using System;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using GenLauncherGO.Infrastructure.Updating.Contracts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace GenLauncherGO.Infrastructure.Updating.Services;

/// <summary>
///     Computes MD5 hashes for local files.
/// </summary>
internal sealed class Md5FileHashService : IFileHashService
{
    private readonly ILogger<Md5FileHashService> _logger;

    public Md5FileHashService(ILogger<Md5FileHashService>? logger = null)
    {
        _logger = logger ?? NullLogger<Md5FileHashService>.Instance;
    }

    public async Task<string> ComputeMd5HashAsync(string filePath, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        try
        {
            await using FileStream fileStream = new(
                filePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                1024 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);

            byte[] hash = await MD5.HashDataAsync(fileStream, cancellationToken).ConfigureAwait(false);
            return Convert.ToHexString(hash).ToUpper(CultureInfo.InvariantCulture);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
                                              or CryptographicException)
        {
            _logger.LogWarning(
                exception,
                "Failed to compute MD5 hash for {FileName}.",
                Path.GetFileName(filePath));
            throw;
        }
    }
}
