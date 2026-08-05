using KynticAI.Scout.Application.Abstractions;
using Microsoft.Extensions.Options;

namespace KynticAI.Scout.Infrastructure.Configuration;

public sealed class PlatformRuntimeOptions(
    IOptions<PlatformOptions> platformOptions,
    IOptions<FeatureFlagOptions> featureFlagOptions) : IPlatformRuntimeOptions
{
    public string Mode => platformOptions.Value.Mode;

    public IReadOnlyList<string> EnabledFeatureFlags => featureFlagOptions.Value.EnabledFlags();
}
