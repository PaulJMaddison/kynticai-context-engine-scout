using System.Security.Cryptography;
using Microsoft.AspNetCore.Cryptography.KeyDerivation;

namespace KynticAI.Scout.Infrastructure.Auth;

public sealed class PasswordHashingService
{
    private const string FormatMarker = "pbkdf2-sha256";
    private const int SaltSize = 16;
    private const int KeySize = 32;
    private const int DefaultIterationCount = 600_000;
    private const int MaxVerificationIterationCount = 2_000_000;

    private readonly int iterationCount;

    public PasswordHashingService()
        : this(DefaultIterationCount)
    {
    }

    internal PasswordHashingService(int iterationCount)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(iterationCount, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(iterationCount, MaxVerificationIterationCount);
        this.iterationCount = iterationCount;
    }

    public string HashPassword(string password)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(password);

        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = KeyDerivation.Pbkdf2(
            password,
            salt,
            KeyDerivationPrf.HMACSHA256,
            iterationCount,
            KeySize);

        return string.Join(
            '$',
            FormatMarker,
            iterationCount,
            Convert.ToBase64String(salt),
            Convert.ToBase64String(hash));
    }

    public bool VerifyPassword(string password, string hashedPassword)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(password);
        if (string.IsNullOrWhiteSpace(hashedPassword))
        {
            return false;
        }

        var segments = hashedPassword.Split('$', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length != 4
            || !string.Equals(segments[0], FormatMarker, StringComparison.Ordinal)
            || !int.TryParse(segments[1], out var iterations)
            || iterations < 1
            || iterations > MaxVerificationIterationCount)
        {
            return false;
        }

        byte[] salt;
        byte[] expectedHash;
        try
        {
            salt = Convert.FromBase64String(segments[2]);
            expectedHash = Convert.FromBase64String(segments[3]);
        }
        catch (FormatException)
        {
            return false;
        }

        if (salt.Length != SaltSize || expectedHash.Length != KeySize)
        {
            return false;
        }

        var actualHash = KeyDerivation.Pbkdf2(
            password,
            salt,
            KeyDerivationPrf.HMACSHA256,
            iterations,
            KeySize);

        return CryptographicOperations.FixedTimeEquals(actualHash, expectedHash);
    }
}
