using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

namespace ReStore.Infrastructure.Persistence.Migrations;

public partial class AddPaymentAttempts : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "PaymentAttempts",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                AmountInRials = table.Column<long>(type: "bigint", nullable: false),
                Authority = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: true),
                CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                IsSandbox = table.Column<bool>(type: "boolean", nullable: false),
                OrderId = table.Column<int>(type: "integer", nullable: false),
                RefId = table.Column<long>(type: "bigint", nullable: true),
                Status = table.Column<int>(type: "integer", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_PaymentAttempts", attempt => attempt.Id);
                table.CheckConstraint("CK_PaymentAttempts_PositiveAmount", "\"AmountInRials\" > 0");
                table.ForeignKey("FK_PaymentAttempts_Orders_OrderId", attempt => attempt.OrderId,
                    principalTable: "Orders", principalColumn: "OrderId", onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex("IX_PaymentAttempts_OrderId", "PaymentAttempts", "OrderId");
        migrationBuilder.CreateIndex("IX_PaymentAttempts_IsSandbox_Authority", "PaymentAttempts",
            new[] { "IsSandbox", "Authority" }, unique: true);
        migrationBuilder.CreateIndex("IX_PaymentAttempts_OneSuccessPerOrder", "PaymentAttempts",
            "OrderId", unique: true, filter: "\"Status\" = 1");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
        => migrationBuilder.DropTable("PaymentAttempts");
}
