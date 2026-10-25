using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using ReStore.Domain.Entities;
using ReStore.Domain.Entities.OrderAggregare;
using ReStore.Infrastructure.Persistence;
using Xunit;

namespace ReStore.Test;

/// <summary>
/// The Phase 0 move must not change what EF persists. Two things in Phase 0 could
/// have affected the model:
/// <list type="bullet">
/// <item>the migrations and snapshot changed assembly and namespace;</item>
/// <item><c>[Owned]</c> on ShippingAddress and ProductItemOrdered was replaced by
/// fluent <c>OwnsOne</c> calls in StoreContext, so that ReStore.Domain needs no EF
/// reference at all.</item>
/// </list>
/// These tests compare the runtime model with the migration snapshot at the level
/// that matters — the set of tables, and which entity types are owned by which
/// owner. No database is involved: the model is built from metadata only.
///
/// Note on owned-type *names*: the snapshot (generated with EF 7) names an owned
/// type "Owner.Navigation#ClrType", while EF 9 names it by its plain CLR name. That
/// difference pre-dates this move and is not a schema difference, so these tests
/// deliberately compare table names rather than entity-type names.
/// </summary>
public class ModelSnapshotParityTests
{
    /// <summary>
    /// A connection string is required to construct the provider's model services,
    /// but nothing here opens a connection.
    /// </summary>
    private const string UnusedConnectionString =
        "Host=localhost;Port=5432;Database=restore;Username=restore;Password=restore";

    private static StoreContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<StoreContext>()
            .UseNpgsql(UnusedConnectionString)
            .Options;

        return new StoreContext(options);
    }

    private static string[] TablesOf(IModel model) => model
        .GetEntityTypes()
        .Select(entityType => entityType.GetTableName())
        .Where(tableName => tableName is not null)
        .Cast<string>()
        .Distinct(StringComparer.Ordinal)
        .OrderBy(tableName => tableName, StringComparer.Ordinal)
        .ToArray();

    [Fact]
    public void Runtime_model_maps_to_the_same_tables_as_the_migration_snapshot()
    {
        using var context = CreateContext();
        var snapshotModel = context.GetService<IMigrationsAssembly>().ModelSnapshot!.Model;

        var runtimeTables = TablesOf(context.Model);
        var snapshotTables = TablesOf(snapshotModel);

        runtimeTables.Should().BeEquivalentTo(snapshotTables,
            "a different table set means a real database would drift from the "
            + "migration snapshot after the project move");
    }

    [Fact]
    public void Shipping_address_is_owned_by_order_and_stored_in_the_orders_table()
    {
        using var context = CreateContext();

        var orderType = context.Model.FindEntityType(typeof(OrderEntity))!;
        var navigation = orderType.FindNavigation(nameof(OrderEntity.ShippingAddress));

        navigation.Should().NotBeNull("OrderEntity.ShippingAddress must still be discovered");
        navigation!.ForeignKey.IsOwnership.Should().BeTrue(
            "the [Owned] attribute was replaced by OwnsOne in StoreContext");
        navigation.ForeignKey.PrincipalEntityType.Should().Be(orderType);
        navigation.TargetEntityType.GetTableName().Should().Be(orderType.GetTableName());
    }

    [Fact]
    public void Product_item_ordered_is_owned_by_order_item_and_has_no_table_of_its_own()
    {
        using var context = CreateContext();

        var orderItemType = context.Model.FindEntityType(typeof(OrderItemEntity))!;
        var navigation = orderItemType.FindNavigation(nameof(OrderItemEntity.ItemOrdered));

        navigation.Should().NotBeNull("OrderItemEntity.ItemOrdered must still be discovered");
        navigation!.ForeignKey.IsOwnership.Should().BeTrue(
            "the [Owned] attribute was replaced by OwnsOne in StoreContext");
        navigation.ForeignKey.PrincipalEntityType.Should().Be(orderItemType);
        navigation.TargetEntityType.GetTableName().Should().Be(orderItemType.GetTableName());
    }

    [Fact]
    public void Domain_assembly_has_no_reference_to_entity_framework()
    {
        typeof(ProductEntity).Assembly.GetReferencedAssemblies()
            .Select(name => name.Name)
            .Should().NotContain(name => name != null && name.StartsWith("Microsoft.EntityFrameworkCore"),
                "keeping EF out of ReStore.Domain is the point of moving [Owned] into StoreContext");
    }
}
