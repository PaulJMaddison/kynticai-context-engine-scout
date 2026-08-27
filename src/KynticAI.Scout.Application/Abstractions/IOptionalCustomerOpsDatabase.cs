using System.Data.Common;

namespace KynticAI.Scout.Application.Abstractions;

/// <summary>
/// Optional connection to the fictional LocalDemo/reference store.
/// Production Scout receives an instance with no connection unless reference
/// data is explicitly enabled.
/// </summary>
public interface IOptionalCustomerOpsDatabase
{
    DbConnection? Connection { get; }
}
