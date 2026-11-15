using Microsoft.EntityFrameworkCore.Migrations;

namespace ReStore.Infrastructure.Persistence.Migrations;

public partial class AddPendingCheckoutRequest : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<Guid>("CheckoutRequestId", "Orders", type: "uuid", nullable: true);
        migrationBuilder.AlterColumn<string>("PaymentIntentId", "Orders", type: "text", nullable: true,
            oldClrType: typeof(string), oldType: "text");
        migrationBuilder.CreateIndex("IX_Orders_BuyerId_CheckoutRequestId", "Orders",
            new[] { "BuyerId", "CheckoutRequestId" }, unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // PostgreSQL refuses the rollback if a pending order has no Stripe intent.
        migrationBuilder.AlterColumn<string>("PaymentIntentId", "Orders", type: "text", nullable: false,
            oldClrType: typeof(string), oldType: "text", oldNullable: true);
        migrationBuilder.DropIndex("IX_Orders_BuyerId_CheckoutRequestId", "Orders");
        migrationBuilder.DropColumn("CheckoutRequestId", "Orders");
    }
}
