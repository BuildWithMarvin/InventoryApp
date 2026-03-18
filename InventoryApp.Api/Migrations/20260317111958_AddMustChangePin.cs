using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace InventoryApp.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddMustChangePin : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Wir fügen NUR die neue Spalte zur existierenden Tabelle hinzu
            migrationBuilder.AddColumn<bool>(
                name: "MustChangePin",
                table: "Employees",
                type: "bit",
                nullable: false,
                defaultValue: true); // Standardmäßig auf 'true' setzen
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Die Rückgängig-Aktion löscht auch NUR diese eine Spalte
            migrationBuilder.DropColumn(
                name: "MustChangePin",
                table: "Employees");
        }
    }
}
