using System.Data.Common;
using KynticAI.Scout.Application.Abstractions;

namespace KynticAI.Scout.Infrastructure.ReferenceData;

public sealed class OptionalCustomerOpsDatabase(DbConnection? connection) : IOptionalCustomerOpsDatabase
{
    public DbConnection? Connection { get; } = connection;
}
