using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using MediatR;
using Moq;
using ReStore.Application.Common;
using ReStore.Application.Controls;
using ReStore.Application.Handlers.BaseHandlers;
using ReStore.Application.Interfaces;
using ReStore.Domain.Common;
using ReStore.Domain.Entities;
using ReStore.Domain.Interfaces.Common;
using ReStore.Domain.Interfaces.Repository;
using ReStore.Infrastructure.Mapping;
using ReStore.Infrastructure.Repositories;
using Xunit;

namespace ReStore.Test;

/// <summary>No-op logger: these tests assert behaviour, not what was logged.</summary>
public sealed class NoopAppLogger : IAppLogger
{
    public void LogError(Exception exception, string context) { }

    public void LogError(string message, string context) { }

    public void LogWarning(string message, string context) { }

    public void LogInformation(string message) { }
}

public class ControlAttributeValidatorTests
{
    private sealed class Form
    {
        [Control("Name", required: true, minLength: 3, maxLength: 5)]
        public string? Name { get; set; }

        [Control("Price", 100, 1000)]
        public long Price { get; set; }

        [Control("Code", true)]
        public string? Code { get; set; }
    }

    [Fact]
    public void A_valid_instance_passes()
    {
        var result = ControlAttributeValidator.Validate(new Form { Name = "abcd", Price = 500, Code = "OK" });

        result.Result.Should().BeTrue();
    }

    [Fact]
    public void Every_problem_is_reported_not_just_the_first()
    {
        var result = ControlAttributeValidator.Validate(new Form { Name = "ab", Price = 5, Code = null });

        result.Result.Should().BeFalse();
        result.Error.Should().Contain("at least 3 characters")
            .And.Contain("not be less than 100")
            .And.Contain("Code is required");
    }

    [Fact]
    public void Only_the_named_properties_are_validated()
    {
        var result = ControlAttributeValidator.Validate(
            new Form { Name = "ab", Price = 500, Code = "OK" },
            new[] { nameof(Form.Price) });

        result.Result.Should().BeTrue("Name was not part of the update");
    }

    [Fact]
    public void A_regex_attribute_reports_its_own_message()
    {
        var result = ControlAttributeValidator.Validate(new RegexForm { Code = "nope" });

        result.Result.Should().BeFalse();
        result.Error.Should().Be("Code must be three capitals.");
    }

    private sealed class RegexForm
    {
        [Control("Code", RegexPattern = "^[A-Z]{3}$", RegexErrorMessage = "Code must be three capitals.")]
        public string? Code { get; set; }
    }
}

public class BaseControlsTests : IClassFixture<SqliteStore>
{
    private readonly SqliteStore _store;

    public BaseControlsTests(SqliteStore store) => _store = store;

    private BaseControls<ProductEntity> Controls()
        => new(new GenericQueryRepositoryBase<ProductEntity>(_store.Context, _store.Mapper));

    [Fact]
    public async Task CheckOnUpdate_reports_a_missing_row()
    {
        var result = await Controls().CheckOnUpdate(
            new ProductEntity { ProductId = 918273 },
            new[] { nameof(ProductEntity.Name) },
            new UpdateNameCommand());

        result.Result.Should().BeFalse();
        result.Error.Should().Contain("was found");
    }

    [Fact]
    public async Task CheckOnUpdate_requires_at_least_one_change_field()
    {
        var product = _store.SeedProduct(name: "NoFields");

        var result = await Controls().CheckOnUpdate(
            new ProductEntity { ProductId = product.ProductId },
            Array.Empty<string>(),
            new UpdateNameCommand());

        result.Result.Should().BeFalse();
        result.Error.Should().Contain("No columns");
    }

    [Fact]
    public async Task CheckOnUpdate_loads_the_row_into_LastEntityInfo()
    {
        var product = _store.SeedProduct(name: "Loaded");

        var controls = Controls();
        var result = await controls.CheckOnUpdate(
            new ProductEntity { ProductId = product.ProductId },
            new[] { nameof(ProductEntity.Name) },
            new UpdateNameCommand());

        result.Result.Should().BeTrue();
        controls.LastEntityInfo.Should().NotBeNull();
        controls.LastEntityInfo!.ProductId.Should().Be(product.ProductId);
        controls.LastEntityInfo.Name.Should().Be("Loaded");
    }

    private sealed class UpdateNameCommand : BaseUpdateCommand
    {
    }
}

public class BaseAddCommandHandlerTests
{
    private sealed class AddProductCommand : BaseAddCommand, IRequest<InsertReturn>
    {
        public string Name { get; set; } = "Handler product";
        public long Price { get; set; } = 123;
        public string Brand { get; set; } = "Brand";
        public string Type { get; set; } = "Type";
        public string Description { get; set; } = "Description";
    }

    private sealed class AddProductHandler : BaseAddCommandHandler<ProductEntity, AddProductCommand>
    {
        public AddProductHandler(
            IGenericCommandRepositoryBase<ProductEntity> repository,
            IControls<ProductEntity> controls,
            IMapperCustome mapper,
            IAppLogger logger,
            IKeyExistenceChecker keyExistenceChecker)
            : base(repository, controls, mapper, logger, keyExistenceChecker)
        {
        }
    }

    /// <summary>A command whose foreign key must exist before the row is created.</summary>
    private sealed class AddBasketItemCommand : BaseAddCommand, IRequest<InsertReturn>
    {
        [CheckKeyExists(typeof(IGenericQueryRepositoryBase<ProductEntity>))]
        public int ProductId { get; set; } = 7;

        public string Name { get; set; } = "Basket line";
        public long Price { get; set; } = 1;
        public string Brand { get; set; } = "Brand";
        public string Type { get; set; } = "Type";
        public string Description { get; set; } = "Description";
    }

    private sealed class AddBasketItemHandler : BaseAddCommandHandler<ProductEntity, AddBasketItemCommand>
    {
        public AddBasketItemHandler(
            IGenericCommandRepositoryBase<ProductEntity> repository,
            IControls<ProductEntity> controls,
            IMapperCustome mapper,
            IAppLogger logger,
            IKeyExistenceChecker keyExistenceChecker)
            : base(repository, controls, mapper, logger, keyExistenceChecker)
        {
        }
    }

    private static (
        Mock<IGenericCommandRepositoryBase<ProductEntity>> Repository,
        Mock<IControls<ProductEntity>> Controls,
        IMapperCustome Mapper,
        IAppLogger Logger,
        Mock<IKeyExistenceChecker> KeyExistenceChecker) Dependencies()
    {
        var repository = new Mock<IGenericCommandRepositoryBase<ProductEntity>>();
        repository.SetupGet(mock => mock.IdPropertyName).Returns(nameof(ProductEntity.ProductId));

        var controls = new Mock<IControls<ProductEntity>>();
        controls.Setup(mock => mock.CheckOnAdd(It.IsAny<ProductEntity>()))
            .ReturnsAsync(new DomainResponse());

        var keyExistenceChecker = new Mock<IKeyExistenceChecker>();
        keyExistenceChecker
            .Setup(mock => mock.CheckAsync(It.IsAny<Type>(), It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DomainResponse());

        return (repository, controls, new MapsterCustome(MapsterConfig.Create()), new NoopAppLogger(),
                keyExistenceChecker);
    }

    [Fact]
    public async Task A_successful_add_returns_the_new_key()
    {
        var (repository, controls, mapper, logger, keyExistenceChecker) = Dependencies();

        repository
            .Setup(mock => mock.AddAsync(It.IsAny<ProductEntity>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ProductEntity entity, CancellationToken _) =>
            {
                entity.ProductId = 42;
                return new DomainResponse(42, 1);
            });

        var handler = new AddProductHandler(repository.Object, controls.Object, mapper, logger, keyExistenceChecker.Object);

        var result = await handler.Handle(new AddProductCommand(), CancellationToken.None);

        result.Result.Should().Be(ReturnResult.Success);
        result.Key.Should().Be(42);
    }

    /// <summary>
    /// The envelope contract in one test: a repository failure comes back as
    /// Result=Failed with the message, and no exception escapes the handler.
    /// </summary>
    [Fact]
    public async Task A_failing_add_becomes_a_failed_envelope_not_an_exception()
    {
        var (repository, controls, mapper, logger, keyExistenceChecker) = Dependencies();

        repository
            .Setup(mock => mock.AddAsync(It.IsAny<ProductEntity>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DomainResponse("Unique constraint violated."));

        var handler = new AddProductHandler(repository.Object, controls.Object, mapper, logger, keyExistenceChecker.Object);

        var result = await handler.Handle(new AddProductCommand(), CancellationToken.None);

        result.Result.Should().Be(ReturnResult.Failed);
        result.Error.Should().Be("Unique constraint violated.");
    }

    [Fact]
    public async Task A_throwing_repository_is_caught_and_reported()
    {
        var (repository, controls, mapper, logger, keyExistenceChecker) = Dependencies();

        repository
            .Setup(mock => mock.AddAsync(It.IsAny<ProductEntity>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Database went away."));

        var handler = new AddProductHandler(repository.Object, controls.Object, mapper, logger, keyExistenceChecker.Object);

        var result = await handler.Handle(new AddProductCommand(), CancellationToken.None);

        result.Result.Should().Be(ReturnResult.Failed);
        result.Error.Should().Be("Database went away.");
    }

    [Fact]
    public async Task A_failing_control_stops_the_add_before_the_repository_is_called()
    {
        var (repository, controls, mapper, logger, keyExistenceChecker) = Dependencies();

        controls.Setup(mock => mock.CheckOnAdd(It.IsAny<ProductEntity>()))
            .ReturnsAsync(new DomainResponse("Brand is not allowed."));

        var handler = new AddProductHandler(repository.Object, controls.Object, mapper, logger, keyExistenceChecker.Object);

        var result = await handler.Handle(new AddProductCommand(), CancellationToken.None);

        result.Result.Should().Be(ReturnResult.Failed);
        result.Error.Should().Be("Brand is not allowed.");

        repository.Verify(
            mock => mock.AddAsync(It.IsAny<ProductEntity>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    /// <summary>
    /// [CheckKeyExists] on a command: a foreign key the caller supplied must exist, and
    /// the failure names the property so the client knows which field to fix.
    /// </summary>
    [Fact]
    public async Task A_missing_foreign_key_fails_the_add_and_names_the_property()
    {
        var (repository, controls, mapper, logger, keyExistenceChecker) = Dependencies();

        keyExistenceChecker
            .Setup(mock => mock.CheckAsync(It.IsAny<Type>(), It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DomainResponse("No ProductEntity with key 7 was found."));

        var handler = new AddBasketItemHandler(repository.Object, controls.Object, mapper, logger,
            keyExistenceChecker.Object);

        var result = await handler.Handle(new AddBasketItemCommand(), CancellationToken.None);

        result.Result.Should().Be(ReturnResult.Failed);
        result.Error.Should().Contain("No ProductEntity with key 7 was found.")
            .And.Contain("{ProductId}", "the message must say which field was wrong");

        keyExistenceChecker.Verify(
            mock => mock.CheckAsync(
                typeof(IGenericQueryRepositoryBase<ProductEntity>),
                7,
                It.IsAny<CancellationToken>()),
            Times.Once);

        repository.Verify(
            mock => mock.AddAsync(It.IsAny<ProductEntity>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task An_existing_foreign_key_lets_the_add_proceed()
    {
        var (repository, controls, mapper, logger, keyExistenceChecker) = Dependencies();

        repository
            .Setup(mock => mock.AddAsync(It.IsAny<ProductEntity>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DomainResponse(11, 1));

        var handler = new AddBasketItemHandler(repository.Object, controls.Object, mapper, logger,
            keyExistenceChecker.Object);

        var result = await handler.Handle(new AddBasketItemCommand(), CancellationToken.None);

        result.Result.Should().Be(ReturnResult.Success);

        keyExistenceChecker.Verify(
            mock => mock.CheckAsync(
                typeof(IGenericQueryRepositoryBase<ProductEntity>),
                7,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }
}

public class MapsterCustomeTests
{
    private sealed class PatchCommand
    {
        public string? Name { get; set; }

        public long? Price { get; set; }
    }

    [Fact]
    public void IgnoreNullValues_keeps_the_existing_column()
    {
        var mapper = new MapsterCustome(MapsterConfig.Create());
        var target = new ProductEntity { ProductId = 1, Name = "old", Price = 42 };

        mapper.Map(new PatchCommand { Name = "new" }, target);

        target.Name.Should().Be("new");
        target.Price.Should().Be(42, "a null in the command must not wipe the column");
    }

    [Fact]
    public void MapAnonymous_maps_a_projection_by_property_name()
    {
        var mapper = new MapsterCustome(MapsterConfig.Create());

        var product = mapper.MapAnonymous<ProductEntity>(new { ProductId = 7, Name = "projected", Price = 55L });

        product.Should().NotBeNull();
        product!.ProductId.Should().Be(7);
        product.Name.Should().Be("projected");
        product.Price.Should().Be(55);
    }

    [Fact]
    public void MapAnonymous_returns_null_for_a_null_source()
    {
        var mapper = new MapsterCustome(MapsterConfig.Create());

        mapper.MapAnonymous<ProductEntity>(null).Should().BeNull();
    }
}
