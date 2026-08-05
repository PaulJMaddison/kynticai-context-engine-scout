namespace KynticAI.Scout.Sdk;

/// <summary>
/// The semantic type of a context fact value, mirroring the API's
/// <c>FactValueType</c>. Integer values are pinned to the API wire format by
/// contract tests in the SDK test project.
/// </summary>
public enum FactValueType
{
    String = 1,
    Number = 2,
    Boolean = 3,
    Json = 4,
    Enum = 5,
    EnumSet = 6
}
