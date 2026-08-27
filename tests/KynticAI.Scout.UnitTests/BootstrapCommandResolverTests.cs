using KynticAI.Scout.Infrastructure.Configuration;

namespace KynticAI.Scout.UnitTests;

public sealed class BootstrapCommandResolverTests
{
    [Theory]
    [InlineData("migrate", true)]
    [InlineData("bootstrap", true)]
    [InlineData("init", true)]
    [InlineData("migrate-database", true)]
    [InlineData("MIGRATE", true)]
    [InlineData("Migrate", true)]
    [InlineData("", false)]
    [InlineData("run", false)]
    [InlineData("bootstrap-demo", false)]
    [InlineData("seed-demo", false)]
    public void IsMigrationCommand_RecognisesAllMigrationAliases(string arg, bool expected)
    {
        var args = string.IsNullOrEmpty(arg) ? Array.Empty<string>() : new[] { arg };
        Assert.Equal(expected, BootstrapCommandResolver.IsMigrationCommand(args));
    }

    [Fact]
    public void IsMigrationCommand_TrueWhenAnyArgumentIsAMigrationAlias()
    {
        Assert.True(BootstrapCommandResolver.IsMigrationCommand(new[] { "run", "migrate" }));
    }

    [Theory]
    [InlineData("bootstrap-demo", true)]
    [InlineData("seed-demo", true)]
    [InlineData("SEED-DEMO", true)]
    [InlineData("migrate", false)]
    [InlineData("init", false)]
    [InlineData("run", false)]
    public void IsSeedDemoCommand_RecognisesSeedAliases(string arg, bool expected)
    {
        var args = string.IsNullOrEmpty(arg) ? Array.Empty<string>() : new[] { arg };
        Assert.Equal(expected, BootstrapCommandResolver.IsSeedDemoCommand(args));
    }

    [Fact]
    public void TheExplicitMigrateCommand_ForcesMigrations_EvenWhenProductionSettingIsFalse()
    {
        var configured = new BootstrapOptions { ApplyMigrationsOnStartup = false, SeedDemoData = false };

        var resolved = BootstrapCommandResolver.Resolve(
            configured,
            explicitMigrationCommand: true,
            explicitSeedDemoCommand: false);

        Assert.True(resolved.ApplyMigrationsOnStartup);
    }

    [Fact]
    public void OrdinaryStartup_WithMigrationsDisabled_DoesNotUnexpectedlyMigrate()
    {
        var configured = new BootstrapOptions { ApplyMigrationsOnStartup = false, SeedDemoData = false };

        var resolved = BootstrapCommandResolver.Resolve(
            configured,
            explicitMigrationCommand: false,
            explicitSeedDemoCommand: false);

        Assert.False(resolved.ApplyMigrationsOnStartup);
    }

    [Fact]
    public void OrdinaryStartup_WithMigrationsEnabled_KeepsThemEnabled()
    {
        var configured = new BootstrapOptions { ApplyMigrationsOnStartup = true, SeedDemoData = false };

        var resolved = BootstrapCommandResolver.Resolve(
            configured,
            explicitMigrationCommand: false,
            explicitSeedDemoCommand: false);

        Assert.True(resolved.ApplyMigrationsOnStartup);
    }

    [Fact]
    public void SeedDemo_IsForcedByTheExplicitSeedCommand_RegardlessOfConfiguration()
    {
        var configured = new BootstrapOptions { ApplyMigrationsOnStartup = false, SeedDemoData = false };

        var resolved = BootstrapCommandResolver.Resolve(
            configured,
            explicitMigrationCommand: false,
            explicitSeedDemoCommand: true);

        Assert.True(resolved.SeedDemoData);
    }

    [Fact]
    public void MigrationAndSeedResolution_AreIndependent()
    {
        var configured = new BootstrapOptions { ApplyMigrationsOnStartup = false, SeedDemoData = true };

        var resolved = BootstrapCommandResolver.Resolve(
            configured,
            explicitMigrationCommand: true,
            explicitSeedDemoCommand: false);

        Assert.True(resolved.ApplyMigrationsOnStartup);
        Assert.True(resolved.SeedDemoData);
    }
}
