using System.Collections.Generic;
using System.IO;
using GenLauncherGO.Infrastructure.Updating.Models;

namespace GenLauncherGO.Infrastructure.Updating.Support;

/// <summary>
/// Determines when S3 package files should be validated with reliable MD5 hashes.
/// </summary>
internal static class S3HashValidationPolicy
{
    /// <summary>
    /// Returns whether a manifest entry should be validated with MD5.
    /// </summary>
    public static bool ShouldCheckHash(
        RemoteFileManifestEntry file,
        IReadOnlySet<string> hashCheckedExtensions)
    {
        return hashCheckedExtensions.Contains(Path.GetExtension(file.FileName)) &&
               IsReliableMd5Hash(file.Hash);
    }

    /// <summary>
    /// Returns whether a manifest hash is a plain 32-character hexadecimal MD5 value.
    /// </summary>
    public static bool IsReliableMd5Hash(string hash)
    {
        if (hash.Length != 32)
        {
            return false;
        }

        foreach (char character in hash)
        {
            bool isHexDigit = character is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F';
            if (!isHexDigit)
            {
                return false;
            }
        }

        return true;
    }
}
