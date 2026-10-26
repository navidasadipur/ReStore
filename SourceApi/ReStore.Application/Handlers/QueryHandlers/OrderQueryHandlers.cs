using System.Linq.Expressions;
using ReStore.Application.Common;
using ReStore.Application.DTOs;
using ReStore.Application.Handlers.BaseHandlers;
using ReStore.Application.Interfaces;
using ReStore.Application.Queries;
using ReStore.Domain.Entities.OrderAggregare;
using ReStore.Domain.Interfaces.Common;
using ReStore.Domain.Interfaces.Repository;

namespace ReStore.Application.Handlers.QueryHandlers;

internal static class OrderReads
{
    internal static readonly Expression<Func<OrderEntity, object>> Select = order => new
    {
        order.OrderId, order.BuyerId, order.ShippingAddress, order.OrderDate,
        order.OrderItems, order.SubTotal, order.DeliveryFee, order.OrderStatus
    };

    internal static DomainResponse? CheckBuyer(IBuyerIdentity buyer)
        => !buyer.IsAuthenticated || string.IsNullOrWhiteSpace(buyer.BuyerId)
            ? new DomainResponse("An authenticated buyer is required.") : null;

    internal static Task<List<OrderDto>> Map(List<OrderEntity> orders)
        => Task.FromResult(orders.Select(OrderDto.FromEntity).ToList());
}

public class OrderListQueryHandler(
    IGenericQueryRepositoryBase<OrderEntity> repository, IMapperCustome mapper,
    IAppLogger logger, IBuyerIdentity buyer)
    : BaseQueryHandler<OrderEntity, OrderListQuery, PaginatedResponseListDto<OrderDto>, OrderDto>(repository, mapper, logger)
{
    protected override Expression<Func<OrderEntity, object>> SelectExpr => OrderReads.Select;
    protected override DomainResponse? ValidateQuery(OrderListQuery query) => OrderReads.CheckBuyer(buyer);
    protected override Expression<Func<OrderEntity, bool>> FullSearchExpr(OrderListQuery query)
    {
        var phrase = (query.FullSearchPhrase ?? string.Empty).Trim().ToLowerInvariant();
        return order => order.ShippingAddress.FullName.ToLower().Contains(phrase);
    }

    protected override Task<Expression<Func<OrderEntity, bool>>?> GetAdditionalCriteria(OrderListQuery query)
    {
        var buyerId = buyer.BuyerId;
        return Task.FromResult<Expression<Func<OrderEntity, bool>>?>(order => order.BuyerId == buyerId);
    }

    protected override IReadOnlyCollection<Common.OrderInfo>? GetOrdering(OrderListQuery query)
        => query.OrderInfo is { Count: > 0 } ? query.OrderInfo : new[]
        {
            new Common.OrderInfo { ColumnName = nameof(OrderEntity.OrderDate), Ascending = false },
            new Common.OrderInfo { ColumnName = nameof(OrderEntity.OrderId), Ascending = false }
        };

    protected override Task<List<OrderDto>> MapDto(List<OrderEntity> data, OrderListQuery query) => OrderReads.Map(data);
}

public class OrderDetailQueryHandler(
    IGenericQueryRepositoryBase<OrderEntity> repository, IMapperCustome mapper,
    IAppLogger logger, IBuyerIdentity buyer)
    : BaseDetailQueryHandler<OrderEntity, OrderDetailQuery, DtoReturnOne<OrderDto>, OrderDto>(repository, mapper, logger)
{
    protected override Expression<Func<OrderEntity, object>> SelectExpr => OrderReads.Select;
    protected override DomainResponse? ValidateQuery(OrderDetailQuery query)
        => OrderReads.CheckBuyer(buyer) ?? base.ValidateQuery(query);

    protected override Task<Expression<Func<OrderEntity, bool>>?> GetAdditionalCriteria(OrderDetailQuery query)
    {
        var buyerId = buyer.BuyerId;
        return Task.FromResult<Expression<Func<OrderEntity, bool>>?>(order => order.BuyerId == buyerId);
    }

    protected override Task<List<OrderDto>> MapDto(List<OrderEntity> data, OrderDetailQuery query) => OrderReads.Map(data);

    protected override DtoReturnOne<OrderDto> GetResult(List<OrderDto> data, int totalCount, int returnCount)
    {
        var result = base.GetResult(data, totalCount, returnCount);
        if (data.Count == 0)
            result.SetErrorResult("The order could not be found.");
        return result;
    }
}
