namespace KynticAI.Scout.Infrastructure.Auth;

public static class ApiScopes
{
    public const string ContextRead = "context:read";
    public const string ContextWrite = "context:write";
    public const string SelectorsRead = "selectors:read";
    public const string SelectorsWrite = "selectors:write";
    public const string EventsIngest = "events:ingest";
    public const string AuditRead = "audit:read";
    public const string AdminManage = "admin:manage";
    public const string BlueprintsWrite = "blueprints:write";
    public const string BillingRead = "billing:read";

    public static readonly IReadOnlySet<string> Canonical = new HashSet<string>(StringComparer.Ordinal)
    {
        ContextRead,
        ContextWrite,
        SelectorsRead,
        SelectorsWrite,
        EventsIngest,
        AuditRead,
        AdminManage,
        BlueprintsWrite,
        BillingRead
    };

    private static readonly Dictionary<string, string> Aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["context.read"] = ContextRead,
        ["context.write"] = ContextWrite,
        ["context.recompute"] = ContextWrite,
        ["connectors.read"] = ContextRead,
        ["events.write"] = EventsIngest,
        ["audit.read"] = AuditRead,
        ["admin.manage"] = AdminManage,
        ["blueprints.write"] = BlueprintsWrite,
        ["billing.read"] = BillingRead
    };

    public static IReadOnlyList<string> Normalize(IReadOnlyList<string> scopes)
        => scopes
            .Where(scope => !string.IsNullOrWhiteSpace(scope))
            .Select(Normalize)
            .Distinct(StringComparer.Ordinal)
            .ToList();

    public static string Normalize(string scope)
    {
        var trimmed = scope.Trim();
        return Aliases.TryGetValue(trimmed, out var mapped) ? mapped : trimmed;
    }
}
