using ReStore.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using ReStore.Domain.Entities.OrderAggregare;

namespace ReStore.Infrastructure.Persistence
{
    public class StoreContext : IdentityDbContext<User, Role, int>
    {
        public StoreContext(DbContextOptions options) : base(options)
        {
        }

        public DbSet<ProductEntity> Products { get; set; }
        public DbSet<BasketEntity> Baskets { get; set; }
        public DbSet<OrderEntity> Orders { get; set; }

        public DbSet<PaymentAttemptEntity> PaymentAttempts { get; set; }

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);

            builder.Entity<User>()
                .HasOne(a => a.Address)
                .WithOne()
                .HasForeignKey<UserAddress>(a => a.Id)
                .OnDelete(DeleteBehavior.Cascade);

            builder.Entity<PaymentAttemptEntity>(attempt =>
            {
                attempt.ToTable("PaymentAttempts", table =>
                    table.HasCheckConstraint("CK_PaymentAttempts_PositiveAmount", "\"AmountInRials\" > 0"));
                attempt.Property(payment => payment.Authority).HasMaxLength(36);
                attempt.HasOne<OrderEntity>().WithMany().HasForeignKey(payment => payment.OrderId)
                    .OnDelete(DeleteBehavior.Restrict);
                attempt.HasIndex(payment => payment.OrderId);
                attempt.HasIndex(payment => new { payment.IsSandbox, payment.Authority }).IsUnique();
                attempt.HasIndex(payment => payment.OrderId, "IX_PaymentAttempts_OneSuccessPerOrder")
                    .IsUnique().HasFilter($"\"Status\" = {(int)PaymentAttemptStatus.Succeeded}");
            });

            builder.Entity<Role>()
                .HasData(
                    new Role { Id = 1, Name = "Member", NormalizedName = "MEMBER" },
                    new Role { Id = 2, Name = "Admin", NormalizedName = "ADMIN" }
                    );

            // These two owned types used to be declared with EF's [Owned] attribute
            // on the entity classes themselves. Declaring the ownership here keeps
            // ReStore.Domain free of persistence concerns; the resulting model is
            // identical, which ModelSnapshotParityTests in the test project asserts
            // by diffing this model against the migration snapshot.
            builder.Entity<OrderEntity>()
                .OwnsOne(order => order.ShippingAddress);
            builder.Entity<OrderEntity>()
                .HasIndex(order => new { order.BuyerId, order.CheckoutRequestId }).IsUnique();

            builder.Entity<OrderItemEntity>()
                .OwnsOne(item => item.ItemOrdered);

            // Keep the existing table names. EF derives a table name from the DbSet
            // property where there is one (Products/Baskets/Orders) and from the CLR
            // type otherwise, so renaming OrderItem to OrderItemEntity would silently
            // rename this table. BasketItems already has [Table] on the entity.
            builder.Entity<OrderItemEntity>().ToTable("OrderItem");

            // Order → OrderItems has no explicit foreign key property, so EF creates a
            // shadow one and names it after the principal type — which after the rename
            // would be "OrderEntityOrderId". Pinning the name keeps the existing
            // OrderItem.OrderId column and its constraint untouched.
            builder.Entity<OrderEntity>()
                .HasMany(order => order.OrderItems)
                .WithOne()
                .HasForeignKey("OrderId");
        }
    }
}