namespace KynticAI.Scout.Application.Abstractions;

/// <summary>Default absence value for the LocalDemo-only reference store.</summary>
public sealed class UnavailableOptionalCustomerOpsDatabase : IOptionalCustomerOpsDatabase
{
    public System.Data.Common.DbConnection? Connection => null;
}
