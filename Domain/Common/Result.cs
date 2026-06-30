namespace Domain.Common;

/// <summary>
/// Outcome of an operation that can fail: success, or failure carrying an <see cref="Error"/>.
/// Avoids exceptions for expected error paths and makes failure explicit in signatures.
/// </summary>
public class Result
{
    protected Result(bool isSuccess, Error error)
    {
        // Invariantes: un éxito nunca lleva error; un fallo siempre lleva uno.
        if (isSuccess && error != Error.None)
            throw new InvalidOperationException("Un resultado exitoso no puede llevar un error.");
        if (!isSuccess && error == Error.None)
            throw new InvalidOperationException("Un resultado fallido debe llevar un error.");

        IsSuccess = isSuccess;
        Error = error;
    }

    public bool IsSuccess { get; }
    public bool IsFailure => !IsSuccess;
    public Error Error { get; }

    public static Result Success() => new(true, Error.None);
    public static Result Failure(Error error) => new(false, error);

    public static Result<TValue> Success<TValue>(TValue value) => new(value, true, Error.None);
    public static Result<TValue> Failure<TValue>(Error error) => new(default, false, error);
}

/// <summary>
/// <see cref="Result"/> that carries a value on success.
/// </summary>
/// <typeparam name="TValue">Type of the value produced on success.</typeparam>
public class Result<TValue> : Result
{
    private readonly TValue? _value;

    protected internal Result(TValue? value, bool isSuccess, Error error)
        : base(isSuccess, error) => _value = value;

    /// <summary>The value; throws if the result is a failure.</summary>
    public TValue Value => IsSuccess
        ? _value!
        : throw new InvalidOperationException("No se puede acceder al valor de un resultado fallido.");

    // Ergonomía: `return value;` => éxito, `return error;` => fallo.
    // Un valor null se trata como fallo (NullValue) para no envolver null como éxito.
    public static implicit operator Result<TValue>(TValue value) =>
        value is not null ? Success(value) : Failure<TValue>(Error.NullValue);

    public static implicit operator Result<TValue>(Error error) => Failure<TValue>(error);
}
