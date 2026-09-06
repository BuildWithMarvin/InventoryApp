using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace InventoryApp.Api.Migrations
{
    /// <inheritdoc />
    public partial class UpdateInventoryModel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "LastUpdatedByEmployeeId",
                table: "ProductStocks",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProductStocks_LastUpdatedByEmployeeId",
                table: "ProductStocks",
                column: "LastUpdatedByEmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_Products_LastUpdatedByEmployeeId",
                table: "Products",
                column: "LastUpdatedByEmployeeId");

            migrationBuilder.AddForeignKey(
                name: "FK_Products_Employees_LastUpdatedByEmployeeId",
                table: "Products",
                column: "LastUpdatedByEmployeeId",
                principalTable: "Employees",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_ProductStocks_Employees_LastUpdatedByEmployeeId",
                table: "ProductStocks",
                column: "LastUpdatedByEmployeeId",
                principalTable: "Employees",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Products_Employees_LastUpdatedByEmployeeId",
                table: "Products");

            migrationBuilder.DropForeignKey(
                name: "FK_ProductStocks_Employees_LastUpdatedByEmployeeId",
                table: "ProductStocks");

            migrationBuilder.DropIndex(
                name: "IX_ProductStocks_LastUpdatedByEmployeeId",
                table: "ProductStocks");

            migrationBuilder.DropIndex(
                name: "IX_Products_LastUpdatedByEmployeeId",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "LastUpdatedByEmployeeId",
                table: "ProductStocks");
        }
    }
}
