namespace Domain.Common;

/// <summary>
/// Represents a failure as a value (code + description), used by the Result pattern
/// instead of throwing exceptions for expected/handled error paths.
/// </summary>
public sealed record Error(string Code, string Description)
{
    /// <summary>Absence of error; the error carried by a successful result.</summary>
    public static readonly Error None = new(string.Empty, string.Empty);

    /// <summary>A null value was provided where a value was required.</summary>
    public static readonly Error NullValue = new("Error.NullValue", "Se proporcionó un valor nulo.");
}
