using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddColdStartNToRiskScoreConfig : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // defaultValue 10 (SRS §7.6): evita división por cero en el cálculo de cold-start (1 - access_count / N) para filas preexistentes.
            migrationBuilder.AddColumn<int>(
                name: "cold_start_n",
                table: "risk_score_config",
                type: "integer",
                nullable: false,
                defaultValue: 10);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "cold_start_n",
                table: "risk_score_config");
        }
    }
}
