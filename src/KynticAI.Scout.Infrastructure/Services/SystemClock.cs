using KynticAI.Scout.Application.Abstractions;

namespace KynticAI.Scout.Infrastructure.Services;

public sealed class SystemClock : IClock
{
    public static readonly SystemClock Instance = new();

    public DateTime UtcNow => DateTime.UtcNow;
}
