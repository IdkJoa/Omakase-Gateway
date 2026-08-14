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
    private static readonly Type _typedIdInterface = typeof(ITypedId<>);

    public void ProcessPropertyAdded(
        IConventionPropertyBuilder propertyBuilder,
        IConventionContext<IConventionPropertyBuilder> context)
    {
        var clrType = propertyBuilder.Metadata.ClrType;

        if (!ImplementsTypedIdInterface(clrType))
            return;

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
