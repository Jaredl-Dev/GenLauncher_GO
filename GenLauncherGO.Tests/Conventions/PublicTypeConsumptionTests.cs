using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Runtime.CompilerServices;
using GenLauncherGO.Core.IO;
using GenLauncherGO.Infrastructure.Logging;
using GenLauncherGO.UI.Features.Startup;

namespace GenLauncherGO.Tests.Conventions;

/// <summary>
///     Holds public visibility to types another production project actually consumes.
/// </summary>
/// <remarks>
///     Consumption is read from the consuming assembly's TypeRef table rather than from its public
///     signatures. A type used only as a local inside a method body appears in the former and in
///     none of the latter, so signature scanning would report live types as unused — and a gate
///     that accuses wrongly teaches people to add exclusions, which is the drift being prevented.
///     The test suite is deliberately not counted as a consumer: every production assembly grants
///     it InternalsVisibleTo, so needing it from a test is not a reason to be public.
/// </remarks>
public sealed class PublicTypeConsumptionTests
{
    [Fact]
    public void EveryPublicCoreType_IsConsumedByInfrastructureOrUi()
    {
        HashSet<string> consumed = ReadReferencedTypeNames(InfrastructureAssembly);
        consumed.UnionWith(ReadReferencedTypeNames(UiAssembly));

        AssertNoUnconsumedPublicTypes(CoreAssembly, consumed, "Infrastructure or UI");
    }

    [Fact]
    public void EveryPublicInfrastructureType_IsConsumedByUi()
    {
        AssertNoUnconsumedPublicTypes(
            InfrastructureAssembly,
            ReadReferencedTypeNames(UiAssembly),
            "UI");
    }

    [Fact]
    public void ConsumptionAnalysis_ReadsBothSidesOfTheBoundary()
    {
        // A metadata read that silently returned nothing would let both tests above pass while
        // checking nothing at all, which is the failure a convention test has to rule out
        // explicitly rather than assume.
        CoreAssembly.GetExportedTypes().Should().NotBeEmpty("Core must expose types to analyse");
        InfrastructureAssembly.GetExportedTypes().Should().NotBeEmpty(
            "Infrastructure must expose types to analyse");
        ReadReferencedTypeNames(InfrastructureAssembly).Should().NotBeEmpty(
            "Infrastructure must record the types it references");
        ReadReferencedTypeNames(UiAssembly).Should().NotBeEmpty(
            "UI must record the types it references");
    }

    private static Assembly CoreAssembly => typeof(LexicalPath).Assembly;

    private static Assembly InfrastructureAssembly => typeof(SensitiveDataRedactingTextFormatter).Assembly;

    private static Assembly UiAssembly => typeof(LauncherApplicationHost).Assembly;

    private static void AssertNoUnconsumedPublicTypes(
        Assembly producer,
        HashSet<string> consumedTypeNames,
        string consumerDescription)
    {
        List<string> offenders = producer.GetExportedTypes()
            .Where(type => !type.IsDefined(typeof(CompilerGeneratedAttribute), inherit: false))
            // A nested type is referenced through its declaring type, so the outermost type is what
            // a consumer's metadata records.
            .Select(GetOutermostType)
            .Select(type => type.FullName)
            .Where(name => name != null)
            .Distinct(StringComparer.Ordinal)
            .Where(name => !consumedTypeNames.Contains(name!))
            .Order(StringComparer.Ordinal)
            .ToList()!;

        offenders.Should().BeEmpty(
            "a type is public only when another production project consumes it, but {0} exposes {1} " +
            "that {2} never references:{3}{4}",
            producer.GetName().Name,
            offenders.Count,
            consumerDescription,
            Environment.NewLine,
            string.Join(Environment.NewLine, offenders));
    }

    private static Type GetOutermostType(Type type)
    {
        Type outermost = type;
        while (outermost.DeclaringType != null)
        {
            outermost = outermost.DeclaringType;
        }

        return outermost;
    }

    /// <summary>
    ///     Reads every type the assembly references, including types touched only inside method bodies.
    /// </summary>
    private static HashSet<string> ReadReferencedTypeNames(Assembly assembly)
    {
        var referencedTypeNames = new HashSet<string>(StringComparer.Ordinal);

        using FileStream stream = File.OpenRead(assembly.Location);
        using PEReader peReader = new(stream);
        MetadataReader metadata = peReader.GetMetadataReader();

        foreach (TypeReferenceHandle handle in metadata.TypeReferences)
        {
            TypeReference typeReference = metadata.GetTypeReference(handle);
            string namespaceName = metadata.GetString(typeReference.Namespace);
            string typeName = metadata.GetString(typeReference.Name);

            referencedTypeNames.Add(string.IsNullOrEmpty(namespaceName)
                ? typeName
                : $"{namespaceName}.{typeName}");
        }

        return referencedTypeNames;
    }
}
