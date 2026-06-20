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
            // defaultValue 10 (SRS §7.6): garantiza que cualquier fila ya sembrada
            // herede el N correcto y evita una división por cero en el cálculo
            // de cold-start (1 - access_count / N) sobre datos preexistentes.
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
