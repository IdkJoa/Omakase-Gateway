using System.Text.Json;
using Infrastructure;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace OG.Application.UnitTests.Security;

/// <summary>
/// DbContext derivado para tests que agrega ValueConverters para SQLite
/// sobre los campos JsonDocument que EF no puede mapear automáticamente.
/// Hereda toda la configuración de entidades de OmakaseDbContext.
/// </summary>
public sealed class TestDbContext : OmakaseDbContext
{
    public TestDbContext(DbContextOptions<OmakaseDbContext> options) : base(options) { }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // SQLite no entiende JsonDocument — lo serializamos como TEXT
        var jsonConverter = new ValueConverter<JsonDocument?, string?>(
            v => v == null ? null : v.RootElement.GetRawText(),
            v => v == null ? null : JsonDocument.Parse(v));

        var dateTimeOffsetConverter = new ValueConverter<DateTimeOffset, string>(
            v => v.ToUniversalTime().ToString("O"),
            v => DateTimeOffset.Parse(v));

        var nullableDateTimeOffsetConverter = new ValueConverter<DateTimeOffset?, string?>(
            v => v == null ? null : v.Value.ToUniversalTime().ToString("O"),
            v => v == null ? null : DateTimeOffset.Parse(v));

        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            foreach (var property in entityType.GetProperties())
            {
                if (property.ClrType == typeof(JsonDocument))
                {
                    property.SetValueConverter(jsonConverter);
                }
                else if (property.ClrType == typeof(DateTimeOffset))
                {
                    property.SetValueConverter(dateTimeOffsetConverter);
                }
                else if (property.ClrType == typeof(DateTimeOffset?))
                {
                    property.SetValueConverter(nullableDateTimeOffsetConverter);
                }
            }
        }

        // Eliminar restricciones de columnas que SQLite no soporta (timestamptz, varchar(N) con tipo Npgsql, etc.)
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            foreach (var property in entityType.GetProperties())
            {
                var colType = property.GetColumnType();
                if (colType != null &&
                    (colType.Contains("timestamptz") ||
                     colType.Contains("jsonb")))
                {
                    property.SetColumnType(null);
                }
            }
        }
    }
}
