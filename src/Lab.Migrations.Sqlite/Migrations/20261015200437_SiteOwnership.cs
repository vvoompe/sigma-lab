using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lab.Migrations.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class SiteOwnership : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Окрема межа міграції: SQLite завершує перебудову sites до видалення User у Down.
            migrationBuilder.CreateIndex(name: "IX_sites_OwnerId", table: "sites", column: "OwnerId");
            migrationBuilder.AddForeignKey(
                name: "FK_sites_lab_users_OwnerId", table: "sites", column: "OwnerId",
                principalTable: "lab_users", principalColumn: "Id", onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(name: "FK_sites_lab_users_OwnerId", table: "sites");
            migrationBuilder.DropIndex(name: "IX_sites_OwnerId", table: "sites");
        }
    }
}
