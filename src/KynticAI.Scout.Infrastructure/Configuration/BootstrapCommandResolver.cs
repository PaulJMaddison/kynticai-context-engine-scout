namespace KynticAI.Scout.Infrastructure.Configuration;

/// <summary>
/// Resolves the effective bootstrap behaviour from explicit command-line bootstrap
/// commands and the configured startup options.
///
/// Scout ships a single-store production data plane. The explicit `migrate` /
/// `bootstrap` / `init` / `migrate-database` command, and the explicit demo-seed command, must force database initialisation and
/// bootstrap regardless of the normal <see cref="BootstrapOptions.ApplyMigrationsOnStartup"/>
/// startup setting, so that a deploy-time command (for example Render's
/// <c>preDeployCommand</c>) reliably creates the Scout schema before connector-catalogue
/// seeding runs. Without this, a production deployment that disables startup migrations
/// (<c>Bootstrap:ApplyMigrationsOnStartup=false</c>) would skip schema creation and then
/// fail when the bootstrap seeds the connector catalogue.
/// </summary>
public static class BootstrapCommandResolver
{
    public static bool IsMigrationCommand(IEnumerable<string> args)
        => args.Any(static arg =>
            string.Equals(arg, "bootstrap", StringComparison.OrdinalIgnoreCase)
            || string.Equals(arg, "init", StringComparison.OrdinalIgnoreCase)
            || string.Equals(arg, "migrate", StringComparison.OrdinalIgnoreCase)
            || string.Equals(arg, "migrate-database", StringComparison.OrdinalIgnoreCase));

    public static bool IsSeedDemoCommand(IEnumerable<string> args)
        => args.Any(static arg =>
            string.Equals(arg, "bootstrap-demo", StringComparison.OrdinalIgnoreCase)
            || string.Equals(arg, "seed-demo", StringComparison.OrdinalIgnoreCase));

    public static BootstrapOptions Resolve(
        BootstrapOptions configured,
        bool explicitMigrationCommand,
        bool explicitSeedDemoCommand)
        => new BootstrapOptions
        {
            ApplyMigrationsOnStartup = explicitMigrationCommand || explicitSeedDemoCommand || configured.ApplyMigrationsOnStartup,
            SeedDemoData = explicitSeedDemoCommand || configured.SeedDemoData
        };
}
