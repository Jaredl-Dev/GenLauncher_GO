using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace GenLauncherGO.TestAnalyzers;

/// <summary>
///     Enforces the repository's behavior-oriented xUnit test naming convention.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class TestMethodNamingAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "GLT001";

    private static readonly DiagnosticDescriptor _rule = new(
        DiagnosticId,
        "Test method name must describe behavior",
        "Test method '{0}' must use 'MemberOrBehavior_ExpectedOutcome' or " +
        "'MemberOrBehavior_Scenario_ExpectedOutcome'",
        "Naming",
        DiagnosticSeverity.Error,
        true,
        "xUnit test names use two or three PascalCase segments so behavior and expected outcome remain scannable.");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [_rule];

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSymbolAction(AnalyzeMethod, SymbolKind.Method);
    }

    private static void AnalyzeMethod(SymbolAnalysisContext context)
    {
        var method = (IMethodSymbol)context.Symbol;
        if (!IsXunitTestMethod(method) || TestMethodNameConvention.IsValid(method.Name))
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(
            _rule,
            method.Locations[0],
            method.Name));
    }

    private static bool IsXunitTestMethod(IMethodSymbol method)
    {
        foreach (AttributeData attribute in method.GetAttributes())
        {
            for (INamedTypeSymbol? attributeType = attribute.AttributeClass;
                 attributeType is not null;
                 attributeType = attributeType.BaseType)
            {
                if (attributeType.ToDisplayString() == "Xunit.FactAttribute")
                {
                    return true;
                }
            }
        }

        return false;
    }
}
