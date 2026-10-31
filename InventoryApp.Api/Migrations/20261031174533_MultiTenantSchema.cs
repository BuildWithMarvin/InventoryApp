using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace InventoryApp.Api.Migrations
{
    /// <summary>
    /// Replaces the single-tenant schema with the multi-tenant one.
    ///
    /// This is a RESET, not an upgrade: the old tables are dropped, together with any rows
    /// they hold. It is only safe because no data worth keeping existed when it was written
    /// (confirmed by the project owner). There is deliberately no Down migration.
    /// </summary>
    public partial class MultiTenantSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Reset: drop the single-tenant tables, children first. __EFMigrationsHistory is untouched.
            migrationBuilder.DropTable(name: "ProductStocks");
            migrationBuilder.DropTable(name: "Products");
            migrationBuilder.DropTable(name: "StorageLocations");
            migrationBuilder.DropTable(name: "Employees");

            migrationBuilder.CreateTable(
                name: "Tenants",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Tenants", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Employees",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    BadgeBarcode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Role = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Employees", x => x.Id);
                    table.UniqueConstraint("AK_Employees_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_Employees_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "StorageLocations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<int>(type: "int", nullable: false),
                    LocationCode = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Zone = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Barcode = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StorageLocations", x => x.Id);
                    table.UniqueConstraint("AK_StorageLocations_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_StorageLocations_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Products",
                columns: table => new
                {
                    TenantId = table.Column<int>(type: "int", nullable: false),
                    InternalBarcode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    SupplierBarcode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Price = table.Column<decimal>(type: "decimal(10,2)", precision: 10, scale: 2, nullable: false),
                    LastUpdatedByEmployeeId = table.Column<int>(type: "int", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Products", x => new { x.TenantId, x.InternalBarcode });
                    table.ForeignKey(
                        name: "FK_Products_Employees_TenantId_LastUpdatedByEmployeeId",
                        columns: x => new { x.TenantId, x.LastUpdatedByEmployeeId },
                        principalTable: "Employees",
                        principalColumns: new[] { "TenantId", "Id" });
                    table.ForeignKey(
                        name: "FK_Products_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ProductStocks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<int>(type: "int", nullable: false),
                    Quantity = table.Column<int>(type: "int", nullable: false),
                    LastUpdatedByEmployeeId = table.Column<int>(type: "int", nullable: true),
                    ProductInternalBarcode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    StorageLocationId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductStocks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProductStocks_Employees_TenantId_LastUpdatedByEmployeeId",
                        columns: x => new { x.TenantId, x.LastUpdatedByEmployeeId },
                        principalTable: "Employees",
                        principalColumns: new[] { "TenantId", "Id" });
                    table.ForeignKey(
                        name: "FK_ProductStocks_Products_TenantId_ProductInternalBarcode",
                        columns: x => new { x.TenantId, x.ProductInternalBarcode },
                        principalTable: "Products",
                        principalColumns: new[] { "TenantId", "InternalBarcode" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ProductStocks_StorageLocations_TenantId_StorageLocationId",
                        columns: x => new { x.TenantId, x.StorageLocationId },
                        principalTable: "StorageLocations",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ProductStocks_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Employees_TenantId_BadgeBarcode",
                table: "Employees",
                columns: new[] { "TenantId", "BadgeBarcode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Products_TenantId_LastUpdatedByEmployeeId",
                table: "Products",
                columns: new[] { "TenantId", "LastUpdatedByEmployeeId" });

            migrationBuilder.CreateIndex(
                name: "IX_ProductStocks_TenantId_LastUpdatedByEmployeeId",
                table: "ProductStocks",
                columns: new[] { "TenantId", "LastUpdatedByEmployeeId" });

            migrationBuilder.CreateIndex(
                name: "IX_ProductStocks_TenantId_ProductInternalBarcode_StorageLocationId",
                table: "ProductStocks",
                columns: new[] { "TenantId", "ProductInternalBarcode", "StorageLocationId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProductStocks_TenantId_StorageLocationId",
                table: "ProductStocks",
                columns: new[] { "TenantId", "StorageLocationId" });

            migrationBuilder.CreateIndex(
                name: "IX_StorageLocations_TenantId_Barcode",
                table: "StorageLocations",
                columns: new[] { "TenantId", "Barcode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Tenants_Name",
                table: "Tenants",
                column: "Name",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new NotSupportedException(
                "MultiTenantSchema resets the schema and cannot be reverted. Restore from a backup instead.");
        }
    }
}
