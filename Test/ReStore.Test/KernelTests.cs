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

    private BaseControls<Product> Controls()
        => new(new GenericQueryRepositoryBase<Product>(_store.Context, _store.Mapper));

    [Fact]
    public async Task CheckOnUpdate_reports_a_missing_row()
    {
        var result = await Controls().CheckOnUpdate(
            new Product { Id = 918273 },
            new[] { nameof(Product.Name) },
            new UpdateNameCommand());

        result.Result.Should().BeFalse();
        result.Error.Should().Contain("was found");
    }

    [Fact]
    public async Task CheckOnUpdate_requires_at_least_one_change_field()
    {
        var product = _store.SeedProduct(name: "NoFields");

        var result = await Controls().CheckOnUpdate(
            new Product { Id = product.Id },
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
            new Product { Id = product.Id },
            new[] { nameof(Product.Name) },
            new UpdateNameCommand());

        result.Result.Should().BeTrue();
        controls.LastEntityInfo.Should().NotBeNull();
        controls.LastEntityInfo!.Id.Should().Be(product.Id);
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

    private sealed class AddProductHandler : BaseAddCommandHandler<Product, AddProductCommand>
    {
        public AddProductHandler(
            IGenericCommandRepositoryBase<Product> repository,
            IControls<Product> controls,
            IMapperCustome mapper,
            IAppLogger logger)
            : base(repository, controls, mapper, logger)
        {
        }
    }

    private static (
        Mock<IGenericCommandRepositoryBase<Product>> Repository,
        Mock<IControls<Product>> Controls,
        IMapperCustome Mapper,
        IAppLogger Logger) Dependencies()
    {
        var repository = new Mock<IGenericCommandRepositoryBase<Product>>();
        repository.SetupGet(mock => mock.IdPropertyName).Returns(nameof(Product.Id));

        var controls = new Mock<IControls<Product>>();
        controls.Setup(mock => mock.CheckOnAdd(It.IsAny<Product>()))
            .ReturnsAsync(new DomainResponse());

        return (repository, controls, new MapsterCustome(MapsterConfig.Create()), new NoopAppLogger());
    }

    [Fact]
    public async Task A_successful_add_returns_the_new_key()
    {
        var (repository, controls, mapper, logger) = Dependencies();

        repository
            .Setup(mock => mock.AddAsync(It.IsAny<Product>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Product entity, CancellationToken _) =>
            {
                entity.Id = 42;
                return new DomainResponse(42, 1);
            });

        var handler = new AddProductHandler(repository.Object, controls.Object, mapper, logger);

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
        var (repository, controls, mapper, logger) = Dependencies();

        repository
            .Setup(mock => mock.AddAsync(It.IsAny<Product>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DomainResponse("Unique constraint violated."));

        var handler = new AddProductHandler(repository.Object, controls.Object, mapper, logger);

        var result = await handler.Handle(new AddProductCommand(), CancellationToken.None);

        result.Result.Should().Be(ReturnResult.Failed);
        result.Error.Should().Be("Unique constraint violated.");
    }

    [Fact]
    public async Task A_throwing_repository_is_caught_and_reported()
    {
        var (repository, controls, mapper, logger) = Dependencies();

        repository
            .Setup(mock => mock.AddAsync(It.IsAny<Product>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Database went away."));

        var handler = new AddProductHandler(repository.Object, controls.Object, mapper, logger);

        var result = await handler.Handle(new AddProductCommand(), CancellationToken.None);

        result.Result.Should().Be(ReturnResult.Failed);
        result.Error.Should().Be("Database went away.");
    }

    [Fact]
    public async Task A_failing_control_stops_the_add_before_the_repository_is_called()
    {
        var (repository, controls, mapper, logger) = Dependencies();

        controls.Setup(mock => mock.CheckOnAdd(It.IsAny<Product>()))
            .ReturnsAsync(new DomainResponse("Brand is not allowed."));

        var handler = new AddProductHandler(repository.Object, controls.Object, mapper, logger);

        var result = await handler.Handle(new AddProductCommand(), CancellationToken.None);

        result.Result.Should().Be(ReturnResult.Failed);
        result.Error.Should().Be("Brand is not allowed.");

        repository.Verify(
            mock => mock.AddAsync(It.IsAny<Product>(), It.IsAny<CancellationToken>()),
            Times.Never);
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
        var target = new Product { Id = 1, Name = "old", Price = 42 };

        mapper.Map(new PatchCommand { Name = "new" }, target);

        target.Name.Should().Be("new");
        target.Price.Should().Be(42, "a null in the command must not wipe the column");
    }

    [Fact]
    public void MapAnonymous_maps_a_projection_by_property_name()
    {
        var mapper = new MapsterCustome(MapsterConfig.Create());

        var product = mapper.MapAnonymous<Product>(new { Id = 7, Name = "projected", Price = 55L });

        product.Should().NotBeNull();
        product!.Id.Should().Be(7);
        product.Name.Should().Be("projected");
        product.Price.Should().Be(55);
    }

    [Fact]
    public void MapAnonymous_returns_null_for_a_null_source()
    {
        var mapper = new MapsterCustome(MapsterConfig.Create());

        mapper.MapAnonymous<Product>(null).Should().BeNull();
    }
}
