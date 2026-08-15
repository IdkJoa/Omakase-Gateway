using System;
using System.Text.Json;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "protected_services",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "varchar(150)", nullable: false),
                    upstream_url = table.Column<string>(type: "varchar(500)", nullable: false),
                    requires_auth = table.Column<bool>(type: "boolean", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_protected_services", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "risk_score_config",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    policy_weight = table.Column<decimal>(type: "numeric(5,4)", nullable: false),
                    anomaly_weight = table.Column<decimal>(type: "numeric(5,4)", nullable: false),
                    cold_start_penalty = table.Column<decimal>(type: "numeric(5,2)", nullable: false),
                    block_threshold = table.Column<decimal>(type: "numeric(5,2)", nullable: false),
                    challenge_threshold = table.Column<decimal>(type: "numeric(5,2)", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_risk_score_config", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "roles",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "varchar(50)", nullable: false),
                    description = table.Column<string>(type: "varchar(255)", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_roles", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "users",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    username = table.Column<string>(type: "varchar(150)", nullable: false),
                    user_type = table.Column<string>(type: "varchar(20)", nullable: false),
                    password_hash = table.Column<string>(type: "varchar(255)", nullable: true),
                    keycloak_sub = table.Column<string>(type: "varchar(255)", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    failed_attempts = table.Column<int>(type: "integer", nullable: false),
                    locked_until = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_users", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "access_policies",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "varchar(150)", nullable: false),
                    type = table.Column<string>(type: "varchar(30)", nullable: false),
                    config = table.Column<JsonDocument>(type: "jsonb", nullable: false),
                    weight = table.Column<decimal>(type: "numeric(4,3)", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_access_policies", x => x.id);
                    table.ForeignKey(
                        name: "FK_access_policies_users_created_by",
                        column: x => x.created_by,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "audit_logs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    evaluation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    service_id = table.Column<Guid>(type: "uuid", nullable: true),
                    source_ip = table.Column<string>(type: "varchar(45)", nullable: false),
                    geo = table.Column<JsonDocument>(type: "jsonb", nullable: true),
                    user_agent = table.Column<string>(type: "text", nullable: true),
                    fingerprint_hash = table.Column<string>(type: "varchar(64)", nullable: true),
                    policy_score = table.Column<decimal>(type: "numeric(5,2)", nullable: false),
                    anomaly_score = table.Column<decimal>(type: "numeric(5,2)", nullable: false),
                    risk_score = table.Column<decimal>(type: "numeric(5,2)", nullable: false),
                    verdict = table.Column<string>(type: "varchar(10)", nullable: false),
                    triggered_rules = table.Column<JsonDocument>(type: "jsonb", nullable: true),
                    evaluated_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_audit_logs", x => x.id);
                    table.ForeignKey(
                        name: "FK_audit_logs_protected_services_service_id",
                        column: x => x.service_id,
                        principalTable: "protected_services",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_audit_logs_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "refresh_tokens",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    token_hash = table.Column<string>(type: "varchar(255)", nullable: false),
                    device_info = table.Column<string>(type: "varchar(255)", nullable: true),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    is_revoked = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_refresh_tokens", x => x.id);
                    table.ForeignKey(
                        name: "FK_refresh_tokens_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_behavior_profiles",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    feature_vector = table.Column<JsonDocument>(type: "jsonb", nullable: true),
                    access_count = table.Column<int>(type: "integer", nullable: false),
                    is_cold_start = table.Column<bool>(type: "boolean", nullable: false),
                    base_risk_penalty = table.Column<decimal>(type: "numeric(5,2)", nullable: false),
                    last_trained_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_behavior_profiles", x => x.id);
                    table.ForeignKey(
                        name: "FK_user_behavior_profiles_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_roles",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    role_id = table.Column<Guid>(type: "uuid", nullable: false),
                    assigned_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_roles", x => x.id);
                    table.ForeignKey(
                        name: "FK_user_roles_roles_role_id",
                        column: x => x.role_id,
                        principalTable: "roles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_user_roles_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "service_policies",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    service_id = table.Column<Guid>(type: "uuid", nullable: false),
                    policy_id = table.Column<Guid>(type: "uuid", nullable: false),
                    is_enabled = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_service_policies", x => x.id);
                    table.ForeignKey(
                        name: "FK_service_policies_access_policies_policy_id",
                        column: x => x.policy_id,
                        principalTable: "access_policies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_service_policies_protected_services_service_id",
                        column: x => x.service_id,
                        principalTable: "protected_services",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_access_policies_created_by",
                table: "access_policies",
                column: "created_by");

            migrationBuilder.CreateIndex(
                name: "ix_audit_logs_evaluated_at",
                table: "audit_logs",
                column: "evaluated_at");

            migrationBuilder.CreateIndex(
                name: "ix_audit_logs_evaluation_id",
                table: "audit_logs",
                column: "evaluation_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_audit_logs_service_id",
                table: "audit_logs",
                column: "service_id");

            migrationBuilder.CreateIndex(
                name: "ix_audit_logs_triggered_rules_gin",
                table: "audit_logs",
                column: "triggered_rules")
                .Annotation("Npgsql:IndexMethod", "GIN");

            migrationBuilder.CreateIndex(
                name: "IX_audit_logs_user_id",
                table: "audit_logs",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_refresh_tokens_token_hash",
                table: "refresh_tokens",
                column: "token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_refresh_tokens_user_active",
                table: "refresh_tokens",
                columns: new[] { "user_id", "is_revoked" },
                filter: "is_revoked = false");

            migrationBuilder.CreateIndex(
                name: "ix_roles_name",
                table: "roles",
                column: "name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_service_policies_policy_id",
                table: "service_policies",
                column: "policy_id");

            migrationBuilder.CreateIndex(
                name: "ix_service_policies_service_policy",
                table: "service_policies",
                columns: new[] { "service_id", "policy_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_user_behavior_profiles_user_id",
                table: "user_behavior_profiles",
                column: "user_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_user_roles_role_id",
                table: "user_roles",
                column: "role_id");

            migrationBuilder.CreateIndex(
                name: "ix_user_roles_user_role",
                table: "user_roles",
                columns: new[] { "user_id", "role_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_users_keycloak_sub",
                table: "users",
                column: "keycloak_sub",
                unique: true,
                filter: "keycloak_sub IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_users_username",
                table: "users",
                column: "username",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "audit_logs");

            migrationBuilder.DropTable(
                name: "refresh_tokens");

            migrationBuilder.DropTable(
                name: "risk_score_config");

            migrationBuilder.DropTable(
                name: "service_policies");

            migrationBuilder.DropTable(
                name: "user_behavior_profiles");

            migrationBuilder.DropTable(
                name: "user_roles");

            migrationBuilder.DropTable(
                name: "access_policies");

            migrationBuilder.DropTable(
                name: "protected_services");

            migrationBuilder.DropTable(
                name: "roles");

            migrationBuilder.DropTable(
                name: "users");
        }
    }
}
