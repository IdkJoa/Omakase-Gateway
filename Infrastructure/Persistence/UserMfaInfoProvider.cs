using System;
using System.Threading;
using System.Threading.Tasks;
using Application.Common.Security.Mfa;
using Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Persistence;

/// <summary>
/// Puerto de datos del motor de riesgo para el estado MFA del usuario
/// (HU-046 / T-105, T-106). Proyección mínima sin tracking.
/// </summary>
public sealed class UserMfaInfoProvider : IUserMfaInfoProvider
{
    private readonly OmakaseDbContext _db;

    public UserMfaInfoProvider(OmakaseDbContext db)
    {
        _db = db;
    }

    public async Task<UserMfaInfo?> GetAsync(string userId, CancellationToken cancellationToken = default)
    {
        if (!Guid.TryParse(userId, out var guid))
            return null;

        var id = UserId.From(guid);

        var info = await _db.Users
            .AsNoTracking()
            .Where(u => u.Id == id)
            .Select(u => new UserMfaInfo(u.IsInteractive, u.MfaEnabled))
            .FirstOrDefaultAsync(cancellationToken);

        return info;
    }
}
