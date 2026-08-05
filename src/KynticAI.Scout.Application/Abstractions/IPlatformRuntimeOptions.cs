namespace KynticAI.Scout.Application.Abstractions;

public interface IPlatformRuntimeOptions
{
    string Mode { get; }

    IReadOnlyList<string> EnabledFeatureFlags { get; }
}
