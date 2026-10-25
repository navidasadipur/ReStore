using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using FluentAssertions;
using ReStore.Application.Common;
using ReStore.Application.Filters;
using ReStore.Domain.Entities;
using ReStore.Domain.Interfaces.Common;
using ReStore.Infrastructure.Repositories;
using Xunit;

namespace ReStore.Test;

/// <summary>
/// The filter engine on its own: no database, no EF — just "does this filter list become
/// the predicate the client meant, and does a bad filter say what is wrong".
/// </summary>
public class FilterExpressionBuilderTests
{
    private static Expression<Func<Product, bool>> Build(params FilterInfo[] filters)
    {
        var (expression, error) = FilterExpressionBuilder.Build<Product>(filters);

        error.Should().BeNull();
        expression.Should().NotBeNull();

        return expression!;
    }

    private static string? ErrorOf(params FilterInfo[] filters)
        => FilterExpressionBuilder.Build<Product>(filters).Error;

    private static Product Sample(string brand = "Nike", long price = 150, bool hasPicture = false)
        => new()
        {
            Id = 1,
            Name = "Runner",
            Description = "A shoe",
            Brand = brand,
            Type = "Shoes",
            Price = price,
            QuantityInStock = 5,
            PictureUrl = hasPicture ? "http://image" : null
        };

    [Fact]
    public void An_unknown_column_fails_and_names_itself()
    {
        ErrorOf(new FilterInfo { ColName = "NotAColumn", Value = "x" })
            .Should().Be("Unknown filter column 'NotAColumn' for Product.");
    }

    [Fact]
    public void Equal_matches_only_the_same_value()
    {
        var predicate = Build(new FilterInfo
        {
            ColName = nameof(Product.Brand),
            FilterType = eFilterType.ftEqual,
            Value = "Nike"
        }).Compile();

        predicate(Sample(brand: "Nike")).Should().BeTrue();
        predicate(Sample(brand: "Adidas")).Should().BeFalse();
    }

    [Fact]
    public void Contains_is_a_case_sensitive_substring_match()
    {
        // Worth knowing before Phase 2: this predicate compiles to a plain Contains, and
        // both .NET and Npgsql's LIKE are case-sensitive. A search box therefore needs an
        // explicit case-insensitive path (ProductExtensions.Search lower-cases both sides)
        // rather than relying on a database collation.
        var matchingCase = Build(new FilterInfo
        {
            ColName = nameof(Product.Name),
            FilterType = eFilterType.ftContains,
            Value = "Run"
        }).Compile();

        var otherCase = Build(new FilterInfo
        {
            ColName = nameof(Product.Name),
            FilterType = eFilterType.ftContains,
            Value = "run"
        }).Compile();

        matchingCase(Sample()).Should().BeTrue("Runner contains Run");
        otherCase(Sample()).Should().BeFalse("Contains is case-sensitive");
    }

    [Fact]
    public void Greater_than_converts_a_json_number_to_the_column_type()
    {
        var predicate = Build(new FilterInfo
        {
            ColName = nameof(Product.Price),
            FilterType = eFilterType.ftGreaterThan,
            Value = 100L
        }).Compile();

        predicate(Sample(price: 150)).Should().BeTrue();
        predicate(Sample(price: 50)).Should().BeFalse();
    }

    [Fact]
    public void Between_is_inclusive_on_both_ends()
    {
        var predicate = Build(new FilterInfo
        {
            ColName = nameof(Product.Price),
            FilterType = eFilterType.ftBetween,
            Value = new object[] { 100L, 200L }
        }).Compile();

        predicate(Sample(price: 100)).Should().BeTrue();
        predicate(Sample(price: 200)).Should().BeTrue();
        predicate(Sample(price: 201)).Should().BeFalse();
    }

    [Fact]
    public void In_matches_any_of_the_listed_values()
    {
        var predicate = Build(new FilterInfo
        {
            ColName = nameof(Product.Brand),
            FilterType = eFilterType.ftIn,
            Value = new object[] { "Nike", "Puma" }
        }).Compile();

        predicate(Sample(brand: "Nike")).Should().BeTrue();
        predicate(Sample(brand: "Puma")).Should().BeTrue();
        predicate(Sample(brand: "Adidas")).Should().BeFalse();
    }

    [Fact]
    public void IsNull_finds_the_empty_column()
    {
        var predicate = Build(new FilterInfo
        {
            ColName = nameof(Product.PictureUrl),
            FilterType = eFilterType.ftIsNull
        }).Compile();

        predicate(Sample(hasPicture: false)).Should().BeTrue();
        predicate(Sample(hasPicture: true)).Should().BeFalse();
    }

    [Fact]
    public void Filters_are_ANDed_unless_they_share_an_inner_filter_id()
    {
        var anded = Build(
            new FilterInfo { ColName = nameof(Product.Brand), FilterType = eFilterType.ftEqual, Value = "Nike" },
            new FilterInfo { ColName = nameof(Product.Price), FilterType = eFilterType.ftGreaterThan, Value = 100L })
            .Compile();

        anded(Sample(brand: "Nike", price: 150)).Should().BeTrue();
        anded(Sample(brand: "Nike", price: 50)).Should().BeFalse("ANDed with the price filter");

        var ored = Build(
            new FilterInfo { ColName = nameof(Product.Brand), FilterType = eFilterType.ftEqual, Value = "Nike", InnerFilterId = 1 },
            new FilterInfo { ColName = nameof(Product.Brand), FilterType = eFilterType.ftEqual, Value = "Puma", InnerFilterId = 1 },
            new FilterInfo { ColName = nameof(Product.Price), FilterType = eFilterType.ftGreaterThan, Value = 100L })
            .Compile();

        ored(Sample(brand: "Nike", price: 150)).Should().BeTrue();
        ored(Sample(brand: "Puma", price: 150)).Should().BeTrue();
        ored(Sample(brand: "Adidas", price: 150)).Should().BeFalse();
        ored(Sample(brand: "Nike", price: 50)).Should().BeFalse("the group is still ANDed with the price filter");
    }

    [Fact]
    public void A_range_operator_on_text_is_rejected_rather_than_thrown()
    {
        ErrorOf(new FilterInfo
        {
            ColName = nameof(Product.Name),
            FilterType = eFilterType.ftGreaterThan,
            Value = "M"
        }).Should().Contain("does not support a range comparison");
    }

    [Fact]
    public void A_value_that_does_not_parse_names_the_column_and_the_type()
    {
        ErrorOf(new FilterInfo
        {
            ColName = nameof(Product.Price),
            FilterType = eFilterType.ftEqual,
            Value = "not-a-number"
        }).Should().Contain("Price").And.Contain("Int64");
    }

    [Fact]
    public void Too_many_values_for_Between_is_an_error()
    {
        ErrorOf(new FilterInfo
        {
            ColName = nameof(Product.Price),
            FilterType = eFilterType.ftBetween,
            Value = new object[] { 1L, 2L, 3L }
        }).Should().Contain("exactly two values");
    }

    [Fact]
    public void No_filters_means_no_predicate()
    {
        var (expression, error) = FilterExpressionBuilder.Build<Product>(null);

        expression.Should().BeNull();
        error.Should().BeNull();
    }
}

/// <summary>
/// The filter engine against real SQL: this is where a predicate that compiles but that
/// EF cannot translate would show up.
/// </summary>
public class FilterTranslationTests : IClassFixture<SqliteStore>
{
    private readonly SqliteStore _store;

    public FilterTranslationTests(SqliteStore store) => _store = store;

    [Fact]
    public async Task Filters_reach_the_database_and_compose_with_each_other()
    {
        _store.SeedProduct(name: "Cheap Nike", price: 50, brand: "FilterNike");
        _store.SeedProduct(name: "Pricey Nike", price: 500, brand: "FilterNike");
        _store.SeedProduct(name: "Pricey Puma", price: 500, brand: "FilterPuma");

        var (filterExpression, filterError) = FilterExpressionBuilder.Build<Product>(new List<FilterInfo>
        {
            new() { ColName = nameof(Product.Brand), FilterType = eFilterType.ftIn, Value = new object[] { "FilterNike", "FilterPuma" } },
            new() { ColName = nameof(Product.Price), FilterType = eFilterType.ftGreaterThan, Value = 100L }
        });

        filterError.Should().BeNull();

        var repository = new GenericQueryRepositoryBase<Product>(_store.Context, _store.Mapper);
        var config = new RequestConfig<Product>(product => new { product.Id, product.Name, product.Brand, product.Price });
        config.Specification.Criteria = filterExpression;

        var result = await repository.ListAsync(config);

        result.Result.Should().BeTrue();
        result.TotalCount.Should().Be(2);
        result.Data.Select(row => row.Name)
            .Should().BeEquivalentTo(new[] { "Pricey Nike", "Pricey Puma" });
    }
}
