using Domain.ValueObjects;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using System.Linq.Expressions;

namespace Infrastructure.Persistence;

/// <summary>
/// EF Core value converter that maps a strongly-typed ID to and from a raw <see cref="Guid"/> column.
/// <para>
/// The <see cref="ITypedId{TSelf}"/> constraint ensures only valid typed IDs are accepted.
/// The conversion lambdas are built once via <see cref="Expression"/> trees and cached,
/// sidestepping the C# limitation that prevents calling static abstract interface
/// members directly inside expression trees (CS8927).
/// </para>
/// </summary>
/// <typeparam name="TId">
/// Any <c>readonly record struct</c> that implements <see cref="ITypedId{TSelf}"/>.
/// </typeparam>
public sealed class TypedIdValueConverter<TId> : ValueConverter<TId, Guid>
    where TId : struct, ITypedId<TId>
{
    public TypedIdValueConverter()
        : base(
            BuildToGuid(),
            BuildFromGuid())
    {
    }

    // Built as an expression tree so EF Core can translate it to SQL when needed.
    private static Expression<Func<TId, Guid>> BuildToGuid()
    {
        var param = Expression.Parameter(typeof(TId), "id");
        var body  = Expression.Property(param, nameof(ITypedId<TId>.Value));
        return Expression.Lambda<Func<TId, Guid>>(body, param);
    }

    // Calls the static From() method via MethodInfo since CS8927 forbids referencing static abstract
    // interface members directly inside expression trees.
    private static Expression<Func<Guid, TId>> BuildFromGuid()
    {
        var fromMethod = typeof(TId).GetMethod(
            nameof(ITypedId<TId>.From),
            [typeof(Guid)])!;

        var param = Expression.Parameter(typeof(Guid), "guid");
        var body  = Expression.Call(null, fromMethod, param);
        return Expression.Lambda<Func<Guid, TId>>(body, param);
    }
}
