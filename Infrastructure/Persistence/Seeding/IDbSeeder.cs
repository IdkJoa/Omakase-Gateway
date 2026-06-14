namespace Infrastructure.Persistence.Seeding;

public interface IDbSeeder
{
    Task SeedAsync(CancellationToken cancellationToken = default);
}
