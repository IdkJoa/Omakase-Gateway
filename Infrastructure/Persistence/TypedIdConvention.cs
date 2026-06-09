using Domain.ValueObjects;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Infrastructure.Persistence;

/// <summary>
/// EF Core model-building convention that automatically registers a
/// <see cref="TypedIdValueConverter{TId}"/> for every property whose CLR type
/// implements <see cref="ITypedId{TSelf}"/>.
/// <para>
/// Detection is based on the interface — not on naming heuristics — so it is
/// precise and works for any future typed ID added to the domain.
/// </para>
/// </summary>
public sealed class TypedIdConvention : IPropertyAddedConvention
{
    // The open generic interface type used for detection.
    private static readonly Type _typedIdInterface = typeof(ITypedId<>);

    public void ProcessPropertyAdded(
        IConventionPropertyBuilder propertyBuilder,
        IConventionContext<IConventionPropertyBuilder> context)
    {
        var clrType = propertyBuilder.Metadata.ClrType;

        // Check whether this property's type implements ITypedId<TSelf>
        if (!ImplementsTypedIdInterface(clrType))
            return;

        // Build TypedIdValueConverter<TId> for the concrete type
        var converterType = typeof(TypedIdValueConverter<>).MakeGenericType(clrType);
        var converter = (ValueConverter)Activator.CreateInstance(converterType)!;

        propertyBuilder.HasConversion(converter);
    }

    private static bool ImplementsTypedIdInterface(Type type)
    {
        if (!type.IsValueType) return false;

        return type.GetInterfaces().Any(i =>
            i.IsGenericType &&
            i.GetGenericTypeDefinition() == _typedIdInterface);
    }
}
