using System.Buffers.Binary;
using System.Data;
using System.Data.Common;
using System.Security.Cryptography;
using System.Text;
using KynticAI.Scout.Application.Abstractions;
using KynticAI.Scout.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace KynticAI.Scout.Infrastructure.Jobs;

/// <summary>
/// Cross-process single-owner guard for one persisted recompute correlation. PostgreSQL uses a
/// session advisory lock, which is released automatically if the process/connection dies. Local
/// deterministic providers use an acquired no-op lease because the in-process queue already
/// deduplicates delivery there.
/// </summary>
internal sealed class RecomputeExecutionLease : IAsyncDisposable
{
    private readonly DbConnection? connection;
    private readonly long lockKey;
    private readonly bool closeConnection;

    private RecomputeExecutionLease(bool acquired, DbConnection? connection = null, long lockKey = 0, bool closeConnection = false)
    {
        Acquired = acquired;
        this.connection = connection;
        this.lockKey = lockKey;
        this.closeConnection = closeConnection;
    }

    public bool Acquired { get; }

    public static async Task<RecomputeExecutionLease> TryAcquireAsync(
        ScoutDbContext dbContext,
        ContextRecomputeRequest request,
        CancellationToken cancellationToken)
    {
        var providerName = dbContext.Database.ProviderName;
        if (string.IsNullOrWhiteSpace(providerName)
            || !providerName.Contains("Npgsql", StringComparison.OrdinalIgnoreCase))
        {
            return new RecomputeExecutionLease(acquired: true);
        }

        var connection = dbContext.Database.GetDbConnection();
        var wasClosed = connection.State == ConnectionState.Closed;
        if (wasClosed)
        {
            await connection.OpenAsync(cancellationToken);
        }

        var lockKey = CreateLockKey(request.TenantId, request.CorrelationId);
        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT pg_try_advisory_lock(@lock_key);";
            var parameter = command.CreateParameter();
            parameter.ParameterName = "lock_key";
            parameter.Value = lockKey;
            command.Parameters.Add(parameter);
            var result = await command.ExecuteScalarAsync(cancellationToken);
            var acquired = result is bool value && value;
            if (!acquired && wasClosed)
            {
                await connection.CloseAsync();
            }

            return new RecomputeExecutionLease(acquired, acquired ? connection : null, lockKey, acquired && wasClosed);
        }
        catch
        {
            if (wasClosed)
            {
                await connection.CloseAsync();
            }
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (!Acquired || connection is null)
        {
            return;
        }

        try
        {
            if (connection.State == ConnectionState.Open)
            {
                await using var command = connection.CreateCommand();
                command.CommandText = "SELECT pg_advisory_unlock(@lock_key);";
                var parameter = command.CreateParameter();
                parameter.ParameterName = "lock_key";
                parameter.Value = lockKey;
                command.Parameters.Add(parameter);
                await command.ExecuteScalarAsync();
            }
        }
        finally
        {
            if (closeConnection && connection.State != ConnectionState.Closed)
            {
                await connection.CloseAsync();
            }
        }
    }

    private static long CreateLockKey(Guid tenantId, string correlationId)
    {
        var payload = Encoding.UTF8.GetBytes($"scout-recompute|{tenantId:D}|{correlationId}");
        Span<byte> digest = stackalloc byte[32];
        SHA256.HashData(payload, digest);
        return BinaryPrimitives.ReadInt64BigEndian(digest);
    }
}
