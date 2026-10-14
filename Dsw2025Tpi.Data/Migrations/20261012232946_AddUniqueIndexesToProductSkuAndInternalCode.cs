using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dsw2025Tpi.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddUniqueIndexesToProductSkuAndInternalCode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "UX_Products_InternalCode",
                table: "Products",
                column: "InternalCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_Products_Sku",
                table: "Products",
                column: "Sku",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_Products_InternalCode",
                table: "Products");

            migrationBuilder.DropIndex(
                name: "UX_Products_Sku",
                table: "Products");
        }
    }
}
