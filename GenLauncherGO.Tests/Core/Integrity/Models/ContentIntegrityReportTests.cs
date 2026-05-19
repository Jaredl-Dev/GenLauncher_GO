using System.Collections.Generic;
using GenLauncherGO.Core.Integrity.Models;

namespace GenLauncherGO.Tests.Core.Integrity.Models;

public sealed class ContentIntegrityReportTests
{
    [Fact]
    public void ConstructorDefensivelyCopiesIssues()
    {
        List<ContentIntegrityIssue> issues = new()
        {
            new ContentIntegrityIssue(
                "target",
                "Target",
                ContentSourceKind.ManagedS3,
                IntegrityIssueKind.ModifiedFile,
                IntegrityIssueAction.Repair,
                "file.bin"),
        };

        ContentIntegrityReport report = new(issues);
        issues.Clear();

        report.Issues.Should().ContainSingle();
    }

    [Fact]
    public void IssueFlagsReflectActionableIssueKinds()
    {
        ContentIntegrityReport report = new(new[]
        {
            new ContentIntegrityIssue(
                "legacy",
                "Legacy",
                ContentSourceKind.UnknownLegacy,
                IntegrityIssueKind.ModifiedFile,
                IntegrityIssueAction.TrustAsManual,
                "legacy.big"),
            new ContentIntegrityIssue(
                "blocking",
                "Blocking",
                ContentSourceKind.UnknownLegacy,
                IntegrityIssueKind.VerificationError,
                IntegrityIssueAction.Block,
                "."),
        });

        report.HasIssues.Should().BeTrue();
        report.HasUnknownLegacyIssues.Should().BeTrue();
        report.HasBlockingIssues.Should().BeTrue();
    }

}
