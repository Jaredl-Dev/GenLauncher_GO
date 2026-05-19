using System;
using System.Threading;
using System.Threading.Tasks;
using GenLauncherGO.Infrastructure.Updating.Contracts;

namespace GenLauncherGO.Tests.Testing;

internal sealed class StubFileHashService : IFileHashService
{
    /// <summary>
    ///     The hash a manifest fixture declares when the file on disk is meant to match it.
    /// </summary>
    public const string MatchingHash = "0123456789ABCDEF0123456789ABCDEF";

    /// <summary>
    ///     A hash that differs from <see cref="MatchingHash" />, for the corrupted-download cases.
    /// </summary>
    public const string MismatchedHash = "FEDCBA9876543210FEDCBA9876543210";

    public Func<string, string> HashForPath { get; init; } = _ => MatchingHash;

    public Task<string> ComputeMd5HashAsync(string filePath, CancellationToken cancellationToken)
    {
        return Task.FromResult(HashForPath(filePath));
    }
}
