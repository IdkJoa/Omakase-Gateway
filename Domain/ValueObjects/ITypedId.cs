namespace Domain.ValueObjects;

public interface ITypedId<TSelf> where TSelf : struct, ITypedId<TSelf>
{
    Guid Value { get; }
    static abstract TSelf From(Guid value);
}
