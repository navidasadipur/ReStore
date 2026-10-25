using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using ReStore.Application.Commands;
using ReStore.Application.Common;
using ReStore.Application.Controls;
using ReStore.Application.Handlers.CommandHandlers;
using ReStore.Application.Handlers.QueryHandlers;
using ReStore.Application.Interfaces;
using ReStore.Application.Queries;
using ReStore.Domain.Entities;
using ReStore.Domain.Interfaces.Common;
using ReStore.Infrastructure.Repositories;
using Xunit;

namespace ReStore.Test;

/// <summary>Records what the slice asked the image store to do, without touching Cloudinary.</summary>
public sealed class FakeImageStorage : IImageStorage
{
    public List<string> Uploaded { get; } = new();

    public List<string> Deleted { get; } = new();

    public bool FailUploads { get; set; }

    public Task<UploadedImage> AddImageAsync(
        Stream stream,
        string fileName,
        CancellationToken cancellationToken = default)
    {
        if (FailUploads)
            return Task.FromResult(UploadedImage.Failed("storage is down"));

        Uploaded.Add(fileName);
        return Task.FromResult(new UploadedImage($"https://img/{fileName}", $"public-{fileName}", null));
    }

    public Task<bool> DeleteImageAsync(string publicId, CancellationToken cancellationToken = default)
    {
        Deleted.Add(publicId);
        return Task.FromResult(true);
    }
}

/// <summary>No foreign keys in this slice, so every check passes.</summary>
public sealed class StubKeyExistenceChecker : IKeyExistenceChecker
{
    public Task<DomainResponse> CheckAsync(
        Type repositoryType,
        object value,
        CancellationToken cancellationToken = default)
        => Task.FromResult(new DomainResponse());
}

public class ProductSliceTests : IClassFixture<SqliteStore>
{
    private readonly SqliteStore _store;

    public ProductSliceTests(SqliteStore store) => _store = store;

    private ProductQueryRepository Repository() => new(_store.Context, _store.Mapper);

    private GenericCommandRepositoryBase<ProductEntity> CommandRepository() => new(_store.Context);

    private ProductControls Controls() => new(Repository());

    private CreateProductCommandHandler CreateHandler(FakeImageStorage images)
        => new(CommandRepository(), Controls(), _store.Mapper, new NoopAppLogger(),
            new StubKeyExistenceChecker(), images);

    private UpdateProductCommandHandler UpdateHandler(FakeImageStorage images)
        => new(CommandRepository(), Controls(), _store.Mapper, new NoopAppLogger(), images);

    private DeleteProductCommandHandler DeleteHandler(FakeImageStorage images)
        => new(CommandRepository(), Controls(), new NoopAppLogger(), images);

    private ProductListQueryHandler ListHandler()
        => new(Repository(), _store.Mapper, new NoopAppLogger());

    private static CreateProductCommand NewProduct(string brand, long price = 250)
        => new()
        {
            Name = $"{brand} shoe",
            Description = "a shoe",
            Price = price,
            Type = "Shoes",
            Brand = brand,
            QuantityInStock = 5
        };

    [Fact]
    public async Task List_returns_a_page_and_the_unpaged_total()
    {
        for (var index = 0; index < 3; index++)
        {
            _store.SeedProduct(name: $"Paged {index}", brand: "ListBrand", price: 100 + index);
        }

        var result = await ListHandler().Handle(
            new ProductListQuery
            {
                Brands = "ListBrand",
                PageNumber = 1,
                PageSize = 2
            },
            CancellationToken.None);

        result.Result.Should().BeTrue();
        result.TotalCount.Should().Be(3, "the count ignores paging");
        result.Data.Should().HaveCount(2, "page 1 of size 2");
        result.Data[0].ProductId.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task List_filters_by_brand_and_type_together()
    {
        _store.SeedProduct(name: "Wanted", brand: "BothBrand");
        _store.SeedProduct(name: "Wrong type", brand: "BothBrand");

        // Give the second row a different type so the type filter can exclude it.
        var other = await _store.Context.Products.FirstAsync(product => product.Name == "Wrong type");
        other.Type = "Hats";
        await _store.Context.SaveChangesAsync();
        _store.Context.ChangeTracker.Clear();

        var result = await ListHandler().Handle(
            new ProductListQuery { Brands = "BothBrand", Types = "Shoes" },
            CancellationToken.None);

        result.Data.Select(row => row.Name).Should().ContainSingle().Which.Should().Be("Wanted");
    }

    [Fact]
    public async Task List_searches_without_caring_about_case()
    {
        _store.SeedProduct(name: "UPPER Case Runner", brand: "SearchBrand");

        var result = await ListHandler().Handle(
            new ProductListQuery { FullSearchPhrase = "upper case", Brands = "SearchBrand" },
            CancellationToken.None);

        result.Data.Should().ContainSingle();
        result.Data[0].Name.Should().Be("UPPER Case Runner");
    }

    [Fact]
    public async Task List_translates_the_priceDesc_sort_key()
    {
        _store.SeedProduct(name: "Cheap", brand: "SortBrand", price: 100);
        _store.SeedProduct(name: "Dear", brand: "SortBrand", price: 900);

        var descending = await ListHandler().Handle(
            new ProductListQuery { Brands = "SortBrand", OrderBy = "priceDesc" },
            CancellationToken.None);

        descending.Data.Select(row => row.Name).Should().ContainInOrder("Dear", "Cheap");
    }

    [Fact]
    public async Task Detail_returns_the_row_and_fails_for_a_missing_id()
    {
        var product = _store.SeedProduct(name: "Detailed", brand: "DetailBrand");

        var handler = new ProductDetailQueryHandler(Repository(), _store.Mapper, new NoopAppLogger());

        var found = await handler.Handle(new ProductDetailQuery { Id = product.ProductId }, CancellationToken.None);
        found.Result.Should().BeTrue();
        found.Data!.Name.Should().Be("Detailed");

        var missing = await handler.Handle(new ProductDetailQuery { Id = product.ProductId + 9999 }, CancellationToken.None);
        missing.Data.Should().BeNull();
    }

    [Fact]
    public async Task Filters_returns_distinct_brands_and_types()
    {
        _store.SeedProduct(name: "One", brand: "DistinctBrand");
        _store.SeedProduct(name: "Two", brand: "DistinctBrand");

        var handler = new ProductFiltersQueryHandler(Repository(), new NoopAppLogger());
        var result = await handler.Handle(new ProductFiltersQuery(), CancellationToken.None);

        result.Result.Should().BeTrue();
        result.Data!.Brands.Should().Contain("DistinctBrand").And.OnlyHaveUniqueItems();
    }

    [Fact]
    public async Task Create_uploads_the_image_and_stores_its_url_and_public_id()
    {
        var images = new FakeImageStorage();
        var command = NewProduct("CreateBrand");
        command.Stream = new MemoryStream([1, 2, 3]);
        command.FileName = "create.png";

        var result = await CreateHandler(images).Handle(command, CancellationToken.None);

        result.Result.Should().Be(ReturnResult.Success);
        result.Key.Should().BeGreaterThan(0);
        images.Uploaded.Should().ContainSingle().Which.Should().Be("create.png");

        _store.Context.ChangeTracker.Clear();
        var saved = await _store.Context.Products.AsNoTracking().SingleAsync(p => p.ProductId == result.Key);
        saved.PictureUrl.Should().Be("https://img/create.png");
        saved.publicId.Should().Be("public-create.png");
    }

    [Fact]
    public async Task Create_rejects_a_price_below_the_control_minimum()
    {
        var images = new FakeImageStorage();

        var result = await CreateHandler(images).Handle(NewProduct("CheapBrand", price: 50), CancellationToken.None);

        result.Result.Should().Be(ReturnResult.Failed);
        result.Error.Should().Contain("at least 100");
        images.Uploaded.Should().BeEmpty("nothing should be uploaded for an invalid command");
    }

    [Fact]
    public async Task A_failing_upload_fails_the_create_with_the_storage_message()
    {
        var images = new FakeImageStorage { FailUploads = true };
        var command = NewProduct("BrokenUploadBrand");
        command.Stream = new MemoryStream([1]);
        command.FileName = "broken.png";

        var result = await CreateHandler(images).Handle(command, CancellationToken.None);

        result.Result.Should().Be(ReturnResult.Failed);
        result.Error.Should().Contain("storage is down");
    }

    [Fact]
    public async Task Update_replaces_the_image_and_deletes_the_previous_one()
    {
        var images = new FakeImageStorage();
        var createCommand = NewProduct("UpdateBrand", price: 300);
        createCommand.Stream = new MemoryStream([1]);
        createCommand.FileName = "first.png";

        var created = await CreateHandler(images).Handle(createCommand, CancellationToken.None);
        created.Result.Should().Be(ReturnResult.Success);

        images.Uploaded.Clear();

        var updateCommand = new UpdateProductCommand
        {
            Id = created.Key,
            Name = "Renamed",
            Price = 350,
            ChangeFields = [nameof(ProductEntity.Name), nameof(ProductEntity.Price)],
            Stream = new MemoryStream([2]),
            FileName = "second.png"
        };

        var updated = await UpdateHandler(images).Handle(updateCommand, CancellationToken.None);

        updated.Result.Should().Be(ReturnResult.Success);
        images.Uploaded.Should().ContainSingle().Which.Should().Be("second.png");
        images.Deleted.Should().ContainSingle().Which.Should().Be("public-first.png");

        _store.Context.ChangeTracker.Clear();
        var saved = await _store.Context.Products.AsNoTracking().SingleAsync(p => p.ProductId == created.Key);
        saved.Name.Should().Be("Renamed");
        saved.Price.Should().Be(350);
        saved.publicId.Should().Be("public-second.png");
    }

    [Fact]
    public async Task Update_without_a_file_keeps_the_current_image()
    {
        var images = new FakeImageStorage();
        var createCommand = NewProduct("KeepImageBrand");
        createCommand.Stream = new MemoryStream([1]);
        createCommand.FileName = "keep.png";

        var created = await CreateHandler(images).Handle(createCommand, CancellationToken.None);
        images.Uploaded.Clear();
        images.Deleted.Clear();

        var updated = await UpdateHandler(images).Handle(
            new UpdateProductCommand
            {
                Id = created.Key,
                Name = "Still here",
                ChangeFields = [nameof(ProductEntity.Name)]
            },
            CancellationToken.None);

        updated.Result.Should().Be(ReturnResult.Success);
        images.Uploaded.Should().BeEmpty();
        images.Deleted.Should().BeEmpty();

        _store.Context.ChangeTracker.Clear();
        var saved = await _store.Context.Products.AsNoTracking().SingleAsync(p => p.ProductId == created.Key);
        saved.publicId.Should().Be("public-keep.png");
    }

    [Fact]
    public async Task Delete_removes_the_image_after_the_row()
    {
        var images = new FakeImageStorage();
        var createCommand = NewProduct("DeleteBrand");
        createCommand.Stream = new MemoryStream([1]);
        createCommand.FileName = "delete.png";

        var created = await CreateHandler(images).Handle(createCommand, CancellationToken.None);

        var deleted = await DeleteHandler(images).Handle(
            new DeleteProductCommand { Id = created.Key },
            CancellationToken.None);

        deleted.Result.Should().Be(ReturnResult.Success);
        images.Deleted.Should().ContainSingle().Which.Should().Be("public-delete.png");

        _store.Context.ChangeTracker.Clear();
        (await _store.Context.Products.AsNoTracking().AnyAsync(p => p.ProductId == created.Key))
            .Should().BeFalse();
    }
}
