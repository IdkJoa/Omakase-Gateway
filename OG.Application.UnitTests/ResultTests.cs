using System;
using Domain.Common;
using Xunit;

namespace OG.Application.UnitTests;

/// <summary>
/// Pruebas de la primitiva Result/Error (patrón Result).
/// </summary>
public class ResultTests
{
    [Fact]
    public void Success_IsSuccess_NoError()
    {
        var result = Result.Success();

        Assert.True(result.IsSuccess);
        Assert.False(result.IsFailure);
        Assert.Equal(Error.None, result.Error);
    }

    [Fact]
    public void Failure_IsFailure_CarriesError()
    {
        var error = new Error("X.Y", "algo falló");
        var result = Result.Failure(error);

        Assert.True(result.IsFailure);
        Assert.Equal(error, result.Error);
    }

    [Fact]
    public void SuccessOfT_ExposesValue()
    {
        var result = Result.Success(42);

        Assert.True(result.IsSuccess);
        Assert.Equal(42, result.Value);
    }

    [Fact]
    public void FailureOfT_AccessingValue_Throws()
    {
        Result<int> result = Result.Failure<int>(new Error("E", "fallo"));

        Assert.True(result.IsFailure);
        Assert.Throws<InvalidOperationException>(() => result.Value);
    }

    [Fact]
    public void ImplicitFromValue_IsSuccess()
    {
        Result<string> result = "ok";

        Assert.True(result.IsSuccess);
        Assert.Equal("ok", result.Value);
    }

    [Fact]
    public void ImplicitFromError_IsFailure()
    {
        Result<string> result = new Error("E", "fallo");

        Assert.True(result.IsFailure);
        Assert.Equal("E", result.Error.Code);
    }

    [Fact]
    public void ImplicitFromNullValue_IsFailure()
    {
        string? value = null;
        Result<string> result = value!;

        Assert.True(result.IsFailure);
        Assert.Equal(Error.NullValue, result.Error);
    }
}
