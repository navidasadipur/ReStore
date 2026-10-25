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

        public DbSet<Product> Products { get; set; }
        public DbSet<Basket> Baskets { get; set; }
        public DbSet<Order> Orders { get; set; }

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);

            builder.Entity<User>()
                .HasOne(a => a.Address)
                .WithOne()
                .HasForeignKey<UserAddress>(a => a.Id)
                .OnDelete(DeleteBehavior.Cascade);

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
            builder.Entity<Order>()
                .OwnsOne(order => order.ShippingAddress);

            builder.Entity<OrderItem>()
                .OwnsOne(item => item.ItemOrdered);
        }
    }
}