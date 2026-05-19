using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using GenLauncherGO.Core.IO;
using GenLauncherGO.Infrastructure.Logging;
using GenLauncherGO.UI.Features.Startup;

namespace GenLauncherGO.Tests.Conventions;

/// <summary>
///     Holds the Async suffix on awaitable-returning methods across the production assemblies.
/// </summary>
/// <remarks>
///     The `dotnet_naming_rule` in .editorconfig only reaches methods carrying the `async` keyword,
///     because a naming style cannot inspect a return type. A method that returns a task without
///     being declared `async` — a forwarder, or one ending in a single `return SomethingAsync(...)` —
///     is invisible to it, and no CA rule covers the gap either. Reflection does, so the convention
///     is enforced here rather than left to reviewers.
/// </remarks>
public sealed class AsynchronousMethodNamingTests
{
    /// <summary>
    ///     The methods named for the task they return rather than for work to await.
    /// </summary>
    /// <remarks>
    ///     Such a method is a synchronous accessor handing back a handle to work already in flight. Naming
    ///     <c>GetActiveDownloadTask</c> as <c>GetActiveDownloadTaskAsync</c> would claim the getter itself is
    ///     asynchronous, which is the opposite of what it does. The exemption is written out member by member so a
    ///     newly added <c>...Task</c> method has to be justified rather than excused by its suffix.
    /// </remarks>
    private static readonly string[] _taskHandleAccessors =
    [
        "GenLauncherGO.UI.Features.Integrity.LauncherPackageActivityService.GetActiveDownloadTask"
    ];

    [Fact]
    public void EveryAwaitableReturningProductionMethod_EndsWithAsync()
    {
        var offenders = ScanAwaitableReturningMethods()
            .Where(method => !method.EndsWith("Async", StringComparison.Ordinal))
            .Where(method => !_taskHandleAccessors.Contains(method, StringComparer.Ordinal))
            .ToList();

        offenders.Should().BeEmpty(
            "a method returning Task or ValueTask must end in Async so callers can see it needs awaiting");
    }

    [Fact]
    public void NamingScan_CoversEveryAwaitableShapeItClaimsToGuard()
    {
        // A scan that silently matched nothing would let the test above pass while checking nothing at all, and
        // one that missed a task shape would leave that shape unguarded just as quietly.
        Type[] awaitableReturnTypes = [typeof(Task), typeof(Task<int>), typeof(ValueTask), typeof(ValueTask<int>)];
        Type[] otherReturnTypes = [typeof(void), typeof(IAsyncEnumerable<int>)];

        List<string> awaitableReturningMethods = ScanAwaitableReturningMethods();

        awaitableReturningMethods.Should().Contain(
            "GenLauncherGO.Infrastructure.Mods.Services.LauncherContentCatalogService.InitDataAsync",
            "the scan must reach the production assemblies it guards");
        awaitableReturnTypes.Should().OnlyContain(returnType => ReturnsAwaitable(returnType));
        otherReturnTypes.Should().NotContain(returnType => ReturnsAwaitable(returnType));
    }

    /// <summary>
    ///     Lists every awaitable-returning production method a person named, as
    ///     <c>Namespace.Type.Method</c>.
    /// </summary>
    private static List<string> ScanAwaitableReturningMethods()
    {
        Assembly[] productionAssemblies =
        [
            typeof(LexicalPath).Assembly,
            typeof(SensitiveDataRedactingTextFormatter).Assembly,
            typeof(LauncherApplicationHost).Assembly
        ];

        return productionAssemblies
            .SelectMany(assembly => assembly.GetTypes())
            .Where(type => !IsCompilerGenerated(type))
            .SelectMany(type => type
                .GetMethods(
                    BindingFlags.Public |
                    BindingFlags.NonPublic |
                    BindingFlags.Instance |
                    BindingFlags.Static |
                    BindingFlags.DeclaredOnly)
                .Where(method => ReturnsAwaitable(method.ReturnType))
                .Where(method => !IsCompilerGenerated(method))
                // Property accessors, event add/remove, and operators cannot carry a suffix.
                .Where(method => !method.IsSpecialName)
                .Select(method => $"{type.FullName}.{method.Name}"))
            .Order(StringComparer.Ordinal)
            .ToList();
    }

    private static bool ReturnsAwaitable(Type returnType)
    {
        if (returnType == typeof(Task) || returnType == typeof(ValueTask))
        {
            return true;
        }

        if (!returnType.IsGenericType)
        {
            return false;
        }

        Type openReturnType = returnType.GetGenericTypeDefinition();
        return openReturnType == typeof(Task<>) || openReturnType == typeof(ValueTask<>);
    }

    private static bool IsCompilerGenerated(MemberInfo member)
    {
        // Async state machines, iterators, and local functions are emitted as members whose names
        // the author never chose, so they are outside the convention.
        return member.IsDefined(typeof(CompilerGeneratedAttribute), inherit: false) ||
               member.Name.Contains('<', StringComparison.Ordinal);
    }
}
