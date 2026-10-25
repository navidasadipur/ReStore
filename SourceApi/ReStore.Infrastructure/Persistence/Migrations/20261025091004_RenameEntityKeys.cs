using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ReStore.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RenameEntityKeys : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "Id",
                table: "Products",
                newName: "ProductId");

            migrationBuilder.RenameColumn(
                name: "Id",
                table: "Orders",
                newName: "OrderId");

            migrationBuilder.RenameColumn(
                name: "Id",
                table: "OrderItem",
                newName: "OrderItemId");

            migrationBuilder.RenameColumn(
                name: "Id",
                table: "Baskets",
                newName: "BasketId");

            migrationBuilder.RenameColumn(
                name: "Id",
                table: "BasketItems",
                newName: "BasketItemId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "ProductId",
                table: "Products",
                newName: "Id");

            migrationBuilder.RenameColumn(
                name: "OrderId",
                table: "Orders",
                newName: "Id");

            migrationBuilder.RenameColumn(
                name: "OrderItemId",
                table: "OrderItem",
                newName: "Id");

            migrationBuilder.RenameColumn(
                name: "BasketId",
                table: "Baskets",
                newName: "Id");

            migrationBuilder.RenameColumn(
                name: "BasketItemId",
                table: "BasketItems",
                newName: "Id");
        }
    }
}
