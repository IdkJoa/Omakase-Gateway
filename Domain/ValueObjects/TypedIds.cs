namespace Domain.ValueObjects;

public readonly record struct UserId(Guid Value) : ITypedId<UserId>
{
    public static UserId New() => new(Guid.CreateVersion7());
    public static UserId From(Guid value) => new(value);
    public override string ToString() => Value.ToString();
}

public readonly record struct AccessPolicyId(Guid Value) : ITypedId<AccessPolicyId>
{
    public static AccessPolicyId New() => new(Guid.CreateVersion7());
    public static AccessPolicyId From(Guid value) => new(value);
    public override string ToString() => Value.ToString();
}

public readonly record struct UserBehaviorProfileId(Guid Value) : ITypedId<UserBehaviorProfileId>
{
    public static UserBehaviorProfileId New() => new(Guid.CreateVersion7());
    public static UserBehaviorProfileId From(Guid value) => new(value);
    public override string ToString() => Value.ToString();
}

public readonly record struct AuditLogId(Guid Value) : ITypedId<AuditLogId>
{
    public static AuditLogId New() => new(Guid.CreateVersion7());
    public static AuditLogId From(Guid value) => new(value);
    public override string ToString() => Value.ToString();
}

public readonly record struct ProtectedServiceId(Guid Value) : ITypedId<ProtectedServiceId>
{
    public static ProtectedServiceId New() => new(Guid.CreateVersion7());
    public static ProtectedServiceId From(Guid value) => new(value);
    public override string ToString() => Value.ToString();
}

public readonly record struct RiskScoreConfigId(Guid Value) : ITypedId<RiskScoreConfigId>
{
    public static RiskScoreConfigId New() => new(Guid.CreateVersion7());
    public static RiskScoreConfigId From(Guid value) => new(value);
    public override string ToString() => Value.ToString();
}

public readonly record struct RefreshTokenId(Guid Value) : ITypedId<RefreshTokenId>
{
    public static RefreshTokenId New() => new(Guid.CreateVersion7());
    public static RefreshTokenId From(Guid value) => new(value);
    public override string ToString() => Value.ToString();
}

public readonly record struct ServicePolicyId(Guid Value) : ITypedId<ServicePolicyId>
{
    public static ServicePolicyId New() => new(Guid.CreateVersion7());
    public static ServicePolicyId From(Guid value) => new(value);
    public override string ToString() => Value.ToString();
}

public readonly record struct RoleId(Guid Value) : ITypedId<RoleId>
{
    public static RoleId New() => new(Guid.CreateVersion7());
    public static RoleId From(Guid value) => new(value);
    public override string ToString() => Value.ToString();
}

public readonly record struct UserRoleId(Guid Value) : ITypedId<UserRoleId>
{
    public static UserRoleId New() => new(Guid.CreateVersion7());
    public static UserRoleId From(Guid value) => new(value);
    public override string ToString() => Value.ToString();
}