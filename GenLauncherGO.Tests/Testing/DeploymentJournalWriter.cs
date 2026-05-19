using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GenLauncherGO.Core.Startup;
using GenLauncherGO.Infrastructure.Common;
using GenLauncherGO.Infrastructure.Launching.Support;

namespace GenLauncherGO.Tests.Testing;

/// <summary>
///     Writes the recovery journal an interrupted deployment would have left behind.
/// </summary>
internal static class DeploymentJournalWriter
{
    private const string JournalFileName = "journal.jsonl";

    /// <summary>
    ///     Writes a journal whose header binds it to <paramref name="paths" />, followed by the supplied records.
    /// </summary>
    public static void Write(LauncherPaths paths, params DeploymentJournalRecord[] records)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(records);

        Directory.CreateDirectory(paths.DeploymentDirectory);
        List<DeploymentJournalRecord> journal =
        [
            DeploymentJournalRecord.DeploymentStarted(
                "crash",
                PhysicalDirectoryPath.ResolveExisting(paths.GameDirectory),
                DeploymentStateStore.GetGameRootIdentity(paths.GameDirectory),
                paths.Game)
        ];
        journal.AddRange(records);

        var serializerOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        File.WriteAllLines(
            Path.Combine(paths.DeploymentDirectory, JournalFileName),
            journal.Select(record => JsonSerializer.Serialize(record, serializerOptions)));
    }

    /// <summary>
    ///     Builds the fingerprint the deployment engine would have recorded for a file holding
    ///     <paramref name="contents" />.
    /// </summary>
    public static DeploymentFileFingerprint FingerprintFrom(string contents)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(contents);
        return new DeploymentFileFingerprint(bytes.Length, Convert.ToHexString(SHA256.HashData(bytes)));
    }
}
