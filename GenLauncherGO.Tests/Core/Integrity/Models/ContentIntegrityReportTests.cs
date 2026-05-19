using System.Collections.Generic;
using GenLauncherGO.Core.Integrity.Models;

namespace GenLauncherGO.Tests.Core.Integrity.Models;

public sealed class ContentIntegrityReportTests
{
    [Fact]
    public void ConstructorDefensively_CopiesIssues()
    {
        List<ContentIntegrityIssue> issues =
        [
            new ContentIntegrityIssue(
                "target",
                "Target",
                ContentSourceKind.ManagedS3,
                IntegrityIssueKind.ModifiedFile,
                IntegrityIssueAction.Repair,
                "file.bin")
        ];

        ContentIntegrityReport report = new(issues);
        issues.Clear();

        report.Issues.Should().ContainSingle();
    }

    [Fact]
    public void IssueFlags_ReflectActionableIssueKinds()
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
                ".")
        });

        report.HasIssues.Should().BeTrue();
        report.HasUnknownLegacyIssues.Should().BeTrue();
        report.HasBlockingIssues.Should().BeTrue();
    }

    [Fact]
    public void IssueFlags_WithOnlyRepairableIssues_ReportNeitherLegacyNorBlocking()
    {
        ContentIntegrityReport report = new(new[]
        {
            new ContentIntegrityIssue(
                "repairable",
                "Repairable",
                ContentSourceKind.ManagedS3,
                IntegrityIssueKind.ModifiedFile,
                IntegrityIssueAction.Repair,
                "file.bin")
        });

        report.HasIssues.Should().BeTrue();
        report.HasUnknownLegacyIssues.Should().BeFalse();
        report.HasBlockingIssues.Should().BeFalse();
    }

    [Fact]
    public void IssueFlags_WithoutIssues_ReportNoIssues()
    {
        ContentIntegrityReport report = new([]);

        report.HasIssues.Should().BeFalse();
    }
}
