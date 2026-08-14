namespace Domain.Common;

/// <summary>
/// Represents a failure as a value (code + description), used by the Result pattern
/// instead of throwing exceptions for expected/handled error paths.
/// </summary>
public sealed record Error(string Code, string Description)
{
    public static readonly Error None = new(string.Empty, string.Empty);

    public static readonly Error NullValue = new("Error.NullValue", "Se proporcionó un valor nulo.");
}
