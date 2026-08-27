using System.Runtime.CompilerServices;

namespace KynticAI.Scout.UnitTests;

public sealed class CommercialReadinessDeliverablesTests
{
    private static readonly string[] LegalReviewTerms =
    [
        "not legal advice",
        "Customer data-plane",
        "Cloud control-plane",
        "Support data",
        "First-party event tracking",
        "GDPR",
        "PECR",
        "raw operational data"
    ];

    [Fact]
    public void Commercial_readiness_scripts_and_code_hooks_are_shipped()
    {
        var root = RepoRoot();
        var requiredPaths = new[]
        {
            "scripts/check-release-alignment.ps1",
            "scripts/check-production-env.ps1",
            "scripts/check-production-env.sh",
            "scripts/paid-pilot-rehearsal-check.ps1",
            "scripts/paid-pilot-local-rehearsal.ps1",
            "scripts/licence-install-rehearsal.ps1",
            "scripts/m2m-and-webhook-smoke.ps1",
            "src/KynticAI.Scout.Infrastructure/Configuration/ProductionEnvironmentReadinessValidator.cs",
            "docs/release-and-hosting-alignment.md",
            "docs/production-install-checklist.md",
            "docs/paid-pilot-end-to-end-rehearsal.md",
            "docs/licence-install-rehearsal.md",
            "docs/m2m-and-webhook-smoke.md",
            "docs/commercial-readiness-summary.md",
            "docs/customer-data-plane-install-runbook.md",
            "docs/windows-install-runbook.md",
            "docs/linux-install-runbook.md",
            "docs/machine-to-machine-identity.md",
            "docs/support-expectations.md",
            "docs/observability.md"
        };

        foreach (var requiredPath in requiredPaths)
        {
            Assert.True(File.Exists(Path.Combine(root, Normalise(requiredPath))), $"Missing {requiredPath}");
        }

        var productionCheck = File.ReadAllText(Path.Combine(root, "scripts", "check-production-env.ps1"));
        Assert.Contains("VITE_DEMO_FALLBACK must be false", productionCheck, StringComparison.Ordinal);
        Assert.Contains("SQLite/local database connection strings are not acceptable", productionCheck, StringComparison.Ordinal);
        Assert.Contains("must look like a PostgreSQL connection string", productionCheck, StringComparison.Ordinal);

        var rehearsalCheck = File.ReadAllText(Path.Combine(root, "scripts", "paid-pilot-rehearsal-check.ps1"));
        Assert.Contains("real SQL/PostgreSQL connector preview requires customer-approved endpoint", rehearsalCheck, StringComparison.Ordinal);
        Assert.Contains("No raw operational data is sent to cloud", rehearsalCheck, StringComparison.Ordinal);

        var licenceRehearsal = File.ReadAllText(Path.Combine(root, "scripts", "licence-install-rehearsal.ps1"));
        Assert.Contains("Licence__PublicKeyPem", licenceRehearsal, StringComparison.Ordinal);
        Assert.Contains("/api/v1/licence/status", licenceRehearsal, StringComparison.Ordinal);
        Assert.Contains("No local licence file found", licenceRehearsal, StringComparison.Ordinal);

        var licenceReader = File.ReadAllText(Path.Combine(root, "src", "KynticAI.Scout.Infrastructure", "Services", "LocalLicenceService.cs"));
        Assert.Contains("Scout-LICENCE-v1", licenceReader, StringComparison.Ordinal);
        Assert.Contains("VerifyCloudEnvelope", licenceReader, StringComparison.Ordinal);
        Assert.Contains("Licence:PublicKeyPem", licenceReader, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("docs/legal/privacy-notice-draft.md")]
    [InlineData("docs/legal/cookie-and-event-consent-draft.md")]
    [InlineData("docs/legal/terms-of-use-draft.md")]
    [InlineData("docs/legal/paid-pilot-agreement-outline.md")]
    [InlineData("docs/legal/data-processing-assumptions.md")]
    [InlineData("docs/legal/security-and-privacy-appendix.md")]
    public void Legal_readiness_drafts_keep_required_boundaries(string relativePath)
    {
        var text = File.ReadAllText(Path.Combine(RepoRoot(), Normalise(relativePath)));

        foreach (var term in LegalReviewTerms)
        {
            Assert.Contains(term, text, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void Commercial_summary_does_not_claim_complete_self_serve_saas()
    {
        var summary = File.ReadAllText(Path.Combine(RepoRoot(), "docs", "commercial-readiness-summary.md"));

        Assert.Contains("not ready for complete self-serve SaaS", summary, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Do not claim vendor-certified connectors", summary, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("We do not build the brain. We build the nervous system.", summary, StringComparison.Ordinal);
    }

    [Fact]
    public void Paid_pilot_lead_capture_has_visible_privacy_and_data_boundary_copy()
    {
        var page = File.ReadAllText(Path.Combine(RepoRoot(), "apps", "web", "src", "features", "marketing", "pilot-page.tsx"));

        Assert.Contains("The form collects your name, work email, company", page, StringComparison.Ordinal);
        Assert.Contains("Do not submit raw operational data", page, StringComparison.Ordinal);
        Assert.Contains("commercial contact", page, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("privacy notice", page, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("terms", page, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("drafts pending solicitor review", page, StringComparison.OrdinalIgnoreCase);
    }

    private static string Normalise(string relativePath) =>
        relativePath.Replace('/', Path.DirectorySeparatorChar);

    private static string RepoRoot([CallerFilePath] string sourceFilePath = "")
    {
        var current = new DirectoryInfo(Path.GetDirectoryName(sourceFilePath) ?? Directory.GetCurrentDirectory());
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "KynticAI.Scout.slnx")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate public repository root.");
    }
}
