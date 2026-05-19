using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GenLauncherGO.Infrastructure.Remote.Contracts;

namespace GenLauncherGO.Tests.Testing;

internal sealed class StubRemoteYamlDocumentReader : IRemoteYamlDocumentReader
{
    private readonly Dictionary<(Type DocumentType, Uri DocumentUri), int> _readCounts = [];

    private readonly Dictionary<
        (Type DocumentType, Uri DocumentUri),
        Func<int, CancellationToken, Task<object>>> _readHandlers = [];

    private readonly Lock _sync = new();

    public Task<T> ReadYamlAsync<T>(Uri documentUri, CancellationToken cancellationToken)
    {
        Func<int, CancellationToken, Task<object>> handler;
        int callIndex;

        lock (_sync)
        {
            (Type DocumentType, Uri DocumentUri) key = (typeof(T), documentUri);
            callIndex = _readCounts.GetValueOrDefault(key) + 1;
            _readCounts[key] = callIndex;

            if (!_readHandlers.TryGetValue(key, out handler!))
            {
                throw new InvalidOperationException(
                    $"No YAML response was configured for {typeof(T).Name} at {documentUri}.");
            }
        }

        return ReadConfiguredAsync<T>(handler, callIndex, cancellationToken);
    }

    public void SetResult<T>(Uri documentUri, T result)
    {
        SetHandler<T>(documentUri, (_, _) => Task.FromResult(result));
    }

    public void SetException<T>(Uri documentUri, Exception exception)
    {
        SetHandler<T>(documentUri, (_, _) => Task.FromException<T>(exception));
    }

    public void SetHandler<T>(
        Uri documentUri,
        Func<int, CancellationToken, Task<T>> handler)
    {
        ArgumentNullException.ThrowIfNull(documentUri);
        ArgumentNullException.ThrowIfNull(handler);

        lock (_sync)
        {
            _readHandlers[(typeof(T), documentUri)] = async (callIndex, cancellationToken) =>
                (await handler(callIndex, cancellationToken).ConfigureAwait(false))!;
        }
    }

    public int GetReadCount<T>(Uri documentUri)
    {
        lock (_sync)
        {
            return _readCounts.GetValueOrDefault((typeof(T), documentUri));
        }
    }

    public int GetReadCount<T>()
    {
        lock (_sync)
        {
            int count = 0;

            foreach (((Type DocumentType, Uri DocumentUri) key, int value) in _readCounts)
            {
                if (key.DocumentType == typeof(T))
                {
                    count += value;
                }
            }

            return count;
        }
    }

    private static async Task<T> ReadConfiguredAsync<T>(
        Func<int, CancellationToken, Task<object>> handler,
        int callIndex,
        CancellationToken cancellationToken)
    {
        object result = await handler(callIndex, cancellationToken).ConfigureAwait(false);
        return (T)result;
    }
}
