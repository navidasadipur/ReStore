using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ReStore.Infrastructure.Persistence.Migrations;

public partial class UseIranianRialCatalog : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Deliberate demo catalog repricing, NOT a USD/IRR exchange rate.
        // One former cent becomes 1,000 rials (100 tomans): $200 -> 2,000,000 tomans.
        migrationBuilder.Sql("UPDATE \"Products\" SET \"Price\" = \"Price\" * 1000;");
        // Apply the same demo repricing to saved order snapshots: all amounts are now rials.
        migrationBuilder.Sql("UPDATE \"Orders\" SET \"SubTotal\" = \"SubTotal\" * 1000, \"DeliveryFee\" = \"DeliveryFee\" * 1000;");
        migrationBuilder.Sql("UPDATE \"OrderItem\" SET \"Price\" = \"Price\" * 1000;");
        // A previously created USD intent must never be reused for the repriced basket.
        migrationBuilder.Sql("UPDATE \"Baskets\" SET \"PaymentIntentId\" = NULL, \"ClientSecret\" = NULL;");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
        => throw new System.NotSupportedException(
            "Restore a pre-migration backup to roll back catalog repricing without losing the original demo prices.");
}
