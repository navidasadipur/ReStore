using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using ReStore.Domain.Entities;
using ReStore.Domain.Interfaces.Common;
using ReStore.Infrastructure.Mapping;
using ReStore.Infrastructure.Persistence;
using ReStore.Infrastructure.Repositories;
using Xunit;

namespace ReStore.Test;

/// <summary>
/// A real (in-memory) SQLite database per test class. SQLite rather than the InMemory
/// provider on purpose: these tests exercise partial updates, projections and
/// transaction rollback, and InMemory supports none of them faithfully.
/// </summary>
public sealed class SqliteStore : IDisposable
{
    private readonly SqliteConnection _connection;

    public SqliteStore()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<StoreContext>()
            .UseSqlite(_connection)
            .Options;

        Context = new StoreContext(options);
        Context.Database.EnsureCreated();

        Mapper = new MapsterCustome(MapsterConfig.Create());
    }

    public StoreContext Context { get; }

    public MapsterCustome Mapper { get; }

    public ProductEntity SeedProduct(string name = "Shoe", long price = 100, string brand = "Nike")
    {
        var product = new ProductEntity
        {
            Name = name,
            Description = $"{name} description",
            Price = price,
            Brand = brand,
            Type = "Shoes",
            QuantityInStock = 10
        };

        Context.Products.Add(product);
        Context.SaveChanges();
        Context.ChangeTracker.Clear();

        return product;
    }

    public void Dispose()
    {
        Context.Dispose();
        _connection.Dispose();
    }
}

public class GenericCommandRepositoryTests : IClassFixture<SqliteStore>
{
    private readonly SqliteStore _store;

    public GenericCommandRepositoryTests(SqliteStore store) => _store = store;

    [Fact]
    public async Task Add_returns_the_generated_key_and_persists_the_row()
    {
        var repository = new GenericCommandRepositoryBase<ProductEntity>(_store.Context);

        var result = await repository.AddAsync(new ProductEntity
        {
            Name = "Hat",
            Description = "A hat",
            Price = 250,
            Brand = "Adidas",
            Type = "Clothing",
            QuantityInStock = 3
        });

        result.Result.Should().BeTrue();
        result.LastKey.Should().BeGreaterThan(0);

        _store.Context.ChangeTracker.Clear();
        (await _store.Context.Products.CountAsync(product => product.Name == "Hat")).Should().Be(1);
    }

    /// <summary>
    /// The behaviour the whole ChangeFields design exists for: a command carrying a
    /// full form writes only the columns it declared.
    /// </summary>
    [Fact]
    public async Task Update_writes_only_the_columns_named_in_changeFields()
    {
        var original = _store.SeedProduct(name: "Boot", price: 150);
        var repository = new GenericCommandRepositoryBase<ProductEntity>(_store.Context);

        var detached = new ProductEntity
        {
            ProductId = original.ProductId,
            Name = "Boot v2",
            Price = 9999,
            Description = "should not be written"
        };

        var result = await repository.UpdateAsync(detached, new[] { nameof(ProductEntity.Name) });

        result.Result.Should().BeTrue();

        _store.Context.ChangeTracker.Clear();
        var reloaded = await _store.Context.Products.AsNoTracking()
            .SingleAsync(product => product.ProductId == original.ProductId);

        reloaded.Name.Should().Be("Boot v2");
        reloaded.Price.Should().Be(150, "Price was not in changeFields");
        reloaded.Description.Should().NotBe("should not be written");
    }

    [Fact]
    public async Task Update_rejects_an_unknown_column_instead_of_writing_anything()
    {
        var original = _store.SeedProduct(name: "Cap");

        var repository = new GenericCommandRepositoryBase<ProductEntity>(_store.Context);
        var result = await repository.UpdateAsync(
            new ProductEntity { ProductId = original.ProductId, Name = "Cap v2" },
            new[] { "NotAColumn" });

        result.Result.Should().BeFalse();
        result.Error.Should().Contain("NotAColumn");
    }

    [Fact]
    public async Task Update_with_no_changeFields_fails_explicitly()
    {
        var original = _store.SeedProduct(name: "Sock");
        var repository = new GenericCommandRepositoryBase<ProductEntity>(_store.Context);

        var result = await repository.UpdateAsync(new ProductEntity { ProductId = original.ProductId }, Array.Empty<string>());

        result.Result.Should().BeFalse();
        result.Error.Should().Contain("No columns");
    }

    [Fact]
    public async Task A_failing_afterSave_callback_rolls_the_delete_back()
    {
        var original = _store.SeedProduct(name: "Belt");
        var repository = new GenericCommandRepositoryBase<ProductEntity>(_store.Context);

        var result = await repository.DeleteAsync(
            new ProductEntity { ProductId = original.ProductId },
            (_, _) => Task.FromResult(new DomainResponse("Blocked by a business rule.")));

        result.Result.Should().BeFalse();
        result.Error.Should().Be("Blocked by a business rule.");

        _store.Context.ChangeTracker.Clear();
        (await _store.Context.Products.AnyAsync(product => product.ProductId == original.ProductId))
            .Should().BeTrue("the transaction must have been rolled back");
    }

    [Fact]
    public async Task Delete_removes_the_row_when_nothing_objects()
    {
        var original = _store.SeedProduct(name: "Scarf");
        var repository = new GenericCommandRepositoryBase<ProductEntity>(_store.Context);

        var result = await repository.DeleteAsync(new ProductEntity { ProductId = original.ProductId });

        result.Result.Should().BeTrue();

        _store.Context.ChangeTracker.Clear();
        (await _store.Context.Products.AnyAsync(product => product.ProductId == original.ProductId)).Should().BeFalse();
    }
}

public class GenericQueryRepositoryTests : IClassFixture<SqliteStore>
{
    private readonly SqliteStore _store;

    public GenericQueryRepositoryTests(SqliteStore store) => _store = store;

    private GenericQueryRepositoryBase<ProductEntity> Repository()
        => new(_store.Context, _store.Mapper);

    [Fact]
    public async Task ListAsync_projects_only_the_selected_columns()
    {
        _store.SeedProduct(name: "Projection", price: 777);

        var repository = Repository();
        var config = new RequestConfig<ProductEntity>(product => new { product.ProductId, product.Name })
        {
            Specification = { Criteria = product => product.Name == "Projection" }
        };

        var result = await repository.ListAsync(config);

        result.Result.Should().BeTrue();
        result.TotalCount.Should().Be(1);
        result.Data.Should().HaveCount(1);

        var row = result.Data[0];
        row.Name.Should().Be("Projection");
        row.Price.Should().Be(0, "Price was not part of the projection");
    }

    [Fact]
    public async Task ListAsync_returns_TotalCount_for_the_unpaged_set_and_one_page_of_rows()
    {
        for (var index = 0; index < 5; index++)
            _store.SeedProduct(name: $"Paged {index}", brand: "PagedBrand");

        var repository = Repository();
        var config = new RequestConfig<ProductEntity>(product => new { product.ProductId, product.Name, product.Price });
        config.Specification.Criteria = product => product.Brand == "PagedBrand";
        config.PagingRequest.PageNum = 2;
        config.PagingRequest.PageCount = 2;

        var result = await repository.ListAsync(config);

        result.TotalCount.Should().Be(5, "the count ignores paging");
        result.Data.Should().HaveCount(2, "page 2 of size 2");
    }

    [Fact]
    public async Task ListAsync_applies_the_requested_ordering()
    {
        _store.SeedProduct(name: "A item", brand: "OrderBrand");
        _store.SeedProduct(name: "C item", brand: "OrderBrand");
        _store.SeedProduct(name: "B item", brand: "OrderBrand");

        var repository = Repository();
        var config = new RequestConfig<ProductEntity>(product => new { product.ProductId, product.Name });
        config.Specification.Criteria = product => product.Brand == "OrderBrand";
        config.PagingRequest.OrderCols = new()
        {
            new Domain.Interfaces.Common.OrderInfo(nameof(ProductEntity.Name), true)
        };

        var result = await repository.ListAsync(config);

        result.Data.Select(row => row.Name)
            .Should().ContainInOrder("A item", "B item", "C item");
    }

    [Fact]
    public async Task ListAsync_fails_with_the_name_of_an_unknown_ordering_column()
    {
        _store.SeedProduct(name: "BadSort", brand: "BadSortBrand");

        var repository = Repository();
        var config = new RequestConfig<ProductEntity>(product => new { product.ProductId, product.Name });
        config.Specification.Criteria = product => product.Brand == "BadSortBrand";
        config.PagingRequest.OrderCols = new()
        {
            new Domain.Interfaces.Common.OrderInfo("NotAColumn", true)
        };

        var result = await repository.ListAsync(config);

        result.Result.Should().BeFalse("an unknown sort column is the caller's mistake, not something to ignore");
        result.Error.Should().Contain("NotAColumn");
    }

    [Fact]
    public async Task GetByIdAsync_reports_a_missing_row_as_a_failed_response()
    {
        var repository = Repository();

        var result = await repository.GetByIdAsync(987654);

        result.Result.Should().BeFalse();
        result.Error.Should().Contain("was found");
    }

    [Fact]
    public async Task IsKeyExistsAsync_tells_present_from_absent()
    {
        var product = _store.SeedProduct(name: "Exists");
        var repository = Repository();

        (await repository.IsKeyExistsAsync(product.ProductId)).Result.Should().BeTrue();
        (await repository.IsKeyExistsAsync(product.ProductId + 10000)).Result.Should().BeFalse();
    }

    [Fact]
    public void IdPropertyName_comes_from_the_ef_model()
    {
        Repository().IdPropertyName.Should().Be(nameof(ProductEntity.ProductId));
    }
}
