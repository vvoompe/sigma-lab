using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lab.Migrations.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class AddSiteManager : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ManagerId",
                table: "sites",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_sites_ManagerId",
                table: "sites",
                column: "ManagerId");

            migrationBuilder.AddForeignKey(
                name: "FK_sites_lab_users_ManagerId",
                table: "sites",
                column: "ManagerId",
                principalTable: "lab_users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_sites_lab_users_ManagerId",
                table: "sites");

            migrationBuilder.DropIndex(
                name: "IX_sites_ManagerId",
                table: "sites");

            migrationBuilder.DropColumn(
                name: "ManagerId",
                table: "sites");
        }
    }
}
