using System;
using System.Collections.Generic;
using System.Linq;

namespace GenLauncherGO.Core.Launching;

/// <summary>
/// Updates game executable command-line arguments controlled by launcher settings.
/// </summary>
public static class LauncherGameArgumentService
{
    /// <summary>
    /// The argument that starts the game in windowed mode.
    /// </summary>
    public const string WindowedArgument = "-win";

    /// <summary>
    /// The argument that skips the normal game startup sequence.
    /// </summary>
    public const string QuickStartArgument = "-quickstart";

    /// <summary>
    /// Adds or removes a command-line argument.
    /// </summary>
    public static string SetArgumentEnabled(string arguments, string argument, bool enabled)
    {
        return enabled
            ? AddArgument(arguments, argument)
            : RemoveArgument(arguments, argument);
    }

    /// <summary>
    /// Determines whether the argument string contains a standalone command-line argument.
    /// </summary>
    public static bool ContainsArgument(string arguments, string argument)
    {
        EnsureArgument(argument);

        return EnumerateTokens(arguments)
            .Any(token => String.Equals(token.Value, argument, StringComparison.OrdinalIgnoreCase));
    }

    private static string AddArgument(string arguments, string argument)
    {
        EnsureArgument(argument);

        if (ContainsArgument(arguments, argument))
        {
            return arguments ?? string.Empty;
        }

        if (string.IsNullOrWhiteSpace(arguments))
        {
            return argument;
        }

        return $"{arguments.Trim()} {argument}";
    }

    private static string RemoveArgument(string arguments, string argument)
    {
        EnsureArgument(argument);

        if (string.IsNullOrWhiteSpace(arguments))
        {
            return string.Empty;
        }

        return String.Join(
            ' ',
            EnumerateTokens(arguments)
                .Where(token => !String.Equals(token.Value, argument, StringComparison.OrdinalIgnoreCase))
                .Select(token => token.Raw));
    }

    private static void EnsureArgument(string argument)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(argument);
    }

    /// <summary>
    /// Enumerates whitespace-delimited command-line tokens while preserving quoted token text.
    /// </summary>
    private static IEnumerable<(string Raw, string Value)> EnumerateTokens(string arguments)
    {
        if (string.IsNullOrWhiteSpace(arguments))
        {
            yield break;
        }

        int index = 0;
        while (index < arguments.Length)
        {
            while (index < arguments.Length && Char.IsWhiteSpace(arguments[index]))
            {
                index++;
            }

            if (index >= arguments.Length)
            {
                yield break;
            }

            int start = index;
            bool isQuoted = arguments[index] == '"';
            bool inQuotes = isQuoted;
            if (isQuoted)
            {
                index++;
            }

            while (index < arguments.Length)
            {
                char current = arguments[index];
                if (current == '"')
                {
                    inQuotes = !inQuotes;
                    index++;
                    continue;
                }

                if (!inQuotes && Char.IsWhiteSpace(current))
                {
                    break;
                }

                index++;
            }

            int end = index;
            string raw = arguments[start..end];
            string value = GetComparableTokenValue(raw, isQuoted);
            yield return (raw, value);
        }
    }

    private static string GetComparableTokenValue(string raw, bool isQuoted)
    {
        return isQuoted && raw.Length >= 2 && raw[^1] == '"'
            ? raw[1..^1]
            : raw;
    }
}
