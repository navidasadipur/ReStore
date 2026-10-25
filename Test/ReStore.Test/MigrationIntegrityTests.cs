using System.Reflection;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using ReStore.Infrastructure.Persistence;
using Xunit;

namespace ReStore.Test;

/// <summary>
/// Phase 0 gate. Moving StoreContext from API/Data to
/// ReStore.Infrastructure/Persistence renames three migrations and the model
/// snapshot. EF identifies a migration by its ID (the timestamp), not by its
/// namespace or assembly, so an existing database is unaffected — but that has
/// to be proven, not assumed. If a rename ever loses an ID, EF would treat a
/// fresh database as having no migrations applied and re-run the baseline, which
/// fails loudly in production and quietly in tests. These tests fail first, and
/// without needing a database.
/// </summary>
public class MigrationIntegrityTests
{
    /// <summary>
    /// The full migration list, in order. This is a tripwire: adding, removing or
    /// reordering a migration must be a deliberate act that updates this list. The
    /// first three are the original ReStore migrations and must never change, because
    /// they identify the schema of any database already deployed.
    /// </summary>
    private static readonly string[] ExpectedMigrationIds =
    [
        "20230515130717_PostGresInitial",
        "20230531143443_makePictureUrlNullable",
        "20230603090725_PublicIdAdded",
        "20261025091004_RenameEntityKeys"
    ];

    private static Type[] MigrationTypes => typeof(StoreContext).Assembly.GetTypes()
        .Where(type => !type.IsAbstract && typeof(Migration).IsAssignableFrom(type))
        .ToArray();

    private static string IdOf(Type migrationType) => migrationType
        .GetCustomAttributes(typeof(MigrationAttribute), false)
        .Cast<MigrationAttribute>()
        .Single()
        .Id;

    [Fact]
    public void StoreContext_exposes_exactly_the_expected_migrations()
    {
        var ids = MigrationTypes.Select(IdOf).OrderBy(id => id, StringComparer.Ordinal);

        ids.Should().Equal(ExpectedMigrationIds,
            "the project move and the key rename must preserve every migration ID so "
            + "an existing database is not treated as uninitialised");
    }

    [Fact]
    public void Migrations_live_in_the_infrastructure_assembly_and_namespace()
    {
        MigrationTypes.Should().NotBeEmpty();
        MigrationTypes.Should().OnlyContain(
            type => type.Namespace == "ReStore.Infrastructure.Persistence.Migrations");
    }

    [Fact]
    public void Model_snapshot_still_describes_the_store_model()
    {
        var snapshotType = typeof(StoreContext).Assembly.GetTypes()
            .SingleOrDefault(type => type.Name.EndsWith("ModelSnapshot", StringComparison.Ordinal));

        snapshotType.Should().NotBeNull("EF needs the snapshot to compute future diffs");

        var snapshot = (ModelSnapshot)Activator.CreateInstance(snapshotType!)!;

        // Entity type names are not compared here: the snapshot's owned types carry
        // the EF 7 "Owner.Navigation#ClrType" name while EF 9 uses the plain CLR
        // name. Tables are what the database actually sees.
        var tables = snapshot.Model.GetEntityTypes()
            .Select(entityType => entityType.GetTableName())
            .Where(tableName => tableName is not null)
            .Cast<string>()
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        tables.Should().Contain(["Products", "Baskets", "Orders", "AspNetUsers", "AspNetRoles"]);
    }

    [Fact]
    public void Baseline_migration_still_creates_the_store_and_identity_tables()
    {
        var baselineType = MigrationTypes
            .Single(type => type.Name.Contains("PostGresInitial", StringComparison.Ordinal));

        var baseline = (Migration)Activator.CreateInstance(baselineType)!;
        var builder = new MigrationBuilder("Npgsql.EntityFrameworkCore.PostgreSQL");

        // Up() is protected; reflection is the only way to read it without
        // editing the generated migration files (which we must not touch).
        baselineType
            .GetMethod("Up", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(baseline, [builder]);

        var tables = builder.Operations
            .OfType<CreateTableOperation>()
            .Select(operation => operation.Name)
            .ToArray();

        tables.Should().Contain(["Products", "Baskets", "Orders", "AspNetUsers", "AspNetRoles"]);
    }
}
