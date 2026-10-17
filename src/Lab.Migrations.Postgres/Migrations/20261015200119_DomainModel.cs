using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Lab.Migrations.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class DomainModel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Зберегти старий ідентифікатор без створення вигаданих User.
            migrationBuilder.RenameColumn(
                name: "OwnerId",
                table: "sites",
                newName: "LegacyOwnerId");

            migrationBuilder.AddColumn<int>(
                name: "OwnerId",
                table: "sites",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "lab_users",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    NormalizedEmail = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_lab_users", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "reference_values",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Unit = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Value = table.Column<double>(type: "double precision", nullable: false),
                    AbsoluteTolerance = table.Column<double>(type: "double precision", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_reference_values", x => x.Id);
                    table.CheckConstraint("ck_reference_tolerance", "\"AbsoluteTolerance\" >= 0");
                });

            migrationBuilder.CreateTable(
                name: "instruments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    OwnerId = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    SerialNumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Unit = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    MinValue = table.Column<double>(type: "double precision", nullable: false),
                    MaxValue = table.Column<double>(type: "double precision", nullable: false),
                    AbsoluteError = table.Column<double>(type: "double precision", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_instruments", x => x.Id);
                    table.CheckConstraint("ck_instruments_error", "\"AbsoluteError\" >= 0");
                    table.CheckConstraint("ck_instruments_range", "\"MinValue\" < \"MaxValue\"");
                    table.ForeignKey(
                        name: "FK_instruments_lab_users_OwnerId",
                        column: x => x.OwnerId,
                        principalTable: "lab_users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "experiments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    SiteId = table.Column<int>(type: "integer", nullable: false),
                    OwnerId = table.Column<int>(type: "integer", nullable: false),
                    InstrumentId = table.Column<int>(type: "integer", nullable: false),
                    ReferenceValueId = table.Column<int>(type: "integer", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    SignedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    SignedById = table.Column<int>(type: "integer", nullable: true),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_experiments", x => x.Id);
                    table.CheckConstraint("ck_experiments_signature", "(\"Status\" = 0 AND \"SignedAt\" IS NULL AND \"SignedById\" IS NULL) OR (\"Status\" = 1 AND \"SignedAt\" IS NOT NULL AND \"SignedById\" IS NOT NULL)");
                    table.CheckConstraint("ck_experiments_version", "\"Version\" > 0");
                    table.ForeignKey(
                        name: "FK_experiments_instruments_InstrumentId",
                        column: x => x.InstrumentId,
                        principalTable: "instruments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_experiments_lab_users_OwnerId",
                        column: x => x.OwnerId,
                        principalTable: "lab_users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_experiments_lab_users_SignedById",
                        column: x => x.SignedById,
                        principalTable: "lab_users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_experiments_reference_values_ReferenceValueId",
                        column: x => x.ReferenceValueId,
                        principalTable: "reference_values",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_experiments_sites_SiteId",
                        column: x => x.SiteId,
                        principalTable: "sites",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "measurements",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ExperimentId = table.Column<int>(type: "integer", nullable: false),
                    ClientId = table.Column<Guid>(type: "uuid", nullable: false),
                    Value = table.Column<double>(type: "double precision", nullable: false),
                    MeasuredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_measurements", x => x.Id);
                    table.CheckConstraint("ck_measurements_version", "\"Version\" > 0");
                    table.ForeignKey(
                        name: "FK_measurements_experiments_ExperimentId",
                        column: x => x.ExperimentId,
                        principalTable: "experiments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_experiments_InstrumentId",
                table: "experiments",
                column: "InstrumentId");

            migrationBuilder.CreateIndex(
                name: "IX_experiments_OwnerId_CreatedAt",
                table: "experiments",
                columns: new[] { "OwnerId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_experiments_ReferenceValueId",
                table: "experiments",
                column: "ReferenceValueId");

            migrationBuilder.CreateIndex(
                name: "IX_experiments_SignedById",
                table: "experiments",
                column: "SignedById");

            migrationBuilder.CreateIndex(
                name: "IX_experiments_SiteId_Status",
                table: "experiments",
                columns: new[] { "SiteId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_instruments_OwnerId",
                table: "instruments",
                column: "OwnerId");

            migrationBuilder.CreateIndex(
                name: "IX_instruments_SerialNumber",
                table: "instruments",
                column: "SerialNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_lab_users_NormalizedEmail",
                table: "lab_users",
                column: "NormalizedEmail",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_measurements_ClientId",
                table: "measurements",
                column: "ClientId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_measurements_ExperimentId_MeasuredAt",
                table: "measurements",
                columns: new[] { "ExperimentId", "MeasuredAt" });

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "measurements");

            migrationBuilder.DropTable(
                name: "experiments");

            migrationBuilder.DropTable(
                name: "instruments");

            migrationBuilder.DropTable(
                name: "reference_values");

            migrationBuilder.DropTable(
                name: "lab_users");

            migrationBuilder.DropColumn(
                name: "OwnerId",
                table: "sites");

            migrationBuilder.RenameColumn(
                name: "LegacyOwnerId",
                table: "sites",
                newName: "OwnerId");
        }
    }
}
