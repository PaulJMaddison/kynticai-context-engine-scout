using System.Text.Json;
using KynticAI.Scout.Application.Services;
using KynticAI.Scout.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace KynticAI.Scout.UnitTests;

public sealed class ScoutServiceAuditExportTests
{
    [Fact]
    public async Task CsvExport_GuardsCellsThatStartWithFormulaCharacters()
    {
        await using var harness = await ScoutServiceTestHarness.CreateAsync();
        var tenant = await harness.DbContext.Tenants.SingleAsync();
        harness.DbContext.AuditEvents.Add(AuditEvent.Create(
            tenant.Id,
            "=1+1",
            "test.action",
            "TestEntity",
            "entity-1",
            "csv-guard-1",
            "{}",
            null,
            null,
            harness.Clock.UtcNow.AddMinutes(-1)));
        await harness.DbContext.SaveChangesAsync();

        var result = await harness.Service.ExportAuditEventsAsync(tenant.Slug, "csv", CancellationToken.None);

        Assert.Equal("text/csv", result.ContentType);
        Assert.Contains("\"'=1+1\"", result.Content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CsvExport_LeavesNormalValuesUnchanged()
    {
        await using var harness = await ScoutServiceTestHarness.CreateAsync();
        var tenant = await harness.DbContext.Tenants.SingleAsync();
        harness.DbContext.AuditEvents.Add(AuditEvent.Create(
            tenant.Id,
            "plain-actor",
            "test.action",
            "TestEntity",
            "entity-1",
            "csv-normal-1",
            """{"note":"plain"}""",
            null,
            null,
            harness.Clock.UtcNow.AddMinutes(-1)));
        await harness.DbContext.SaveChangesAsync();

        var result = await harness.Service.ExportAuditEventsAsync(tenant.Slug, "csv", CancellationToken.None);

        Assert.Contains("\"plain-actor\"", result.Content, StringComparison.Ordinal);
        Assert.DoesNotContain("\"'plain-actor\"", result.Content, StringComparison.Ordinal);
        Assert.DoesNotContain("=cmd", result.Content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task JsonExport_RoundTripsAdversarialValues_AsValidJson()
    {
        await using var harness = await ScoutServiceTestHarness.CreateAsync();
        var tenant = await harness.DbContext.Tenants.SingleAsync();
        const string correlationId = "json-adversarial-1";
        const string actor = "=HYPERLINK(\"https://evil.example\")";
        const string action = "action,with,commas";
        const string entityType = "line1\nline2";
        const string entityId = "id\"with\"quotes";
        const string metadataJson = """{"note":"text with \"quotes\", commas\n and a newline"}""";
        harness.DbContext.AuditEvents.Add(AuditEvent.Create(
            tenant.Id,
            actor,
            action,
            entityType,
            entityId,
            correlationId,
            metadataJson,
            null,
            null,
            harness.Clock.UtcNow.AddMinutes(-1)));
        await harness.DbContext.SaveChangesAsync();

        var result = await harness.Service.ExportAuditEventsAsync(tenant.Slug, "json", CancellationToken.None);

        Assert.Equal("application/json", result.ContentType);
        using var document = JsonDocument.Parse(result.Content);
        var seeded = document.RootElement.EnumerateArray()
            .Single(element => element.GetProperty("CorrelationId").GetString() == correlationId);
        Assert.Equal(actor, seeded.GetProperty("Actor").GetString());
        Assert.Equal(action, seeded.GetProperty("Action").GetString());
        Assert.Equal(entityType, seeded.GetProperty("EntityType").GetString());
        Assert.Equal(entityId, seeded.GetProperty("EntityId").GetString());
        Assert.Equal(metadataJson, seeded.GetProperty("MetadataJson").GetString());
    }
}
