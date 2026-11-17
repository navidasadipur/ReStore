using MediatR;
using ReStore.Application.DTOs;
using ReStore.Application.Interfaces;
using ReStore.Application.Queries;
using ReStore.Domain.Interfaces.Common;

namespace ReStore.Application.Handlers.QueryHandlers;

internal static class AccountResults
{
    internal static DtoReturnOne<T> Success<T>(T? data) where T : BaseDto
    {
        var result = new DtoReturnOne<T>();
        var rows = data is null ? new List<T>() : new List<T> { data };
        result.SetResultFrom(rows, rows.Count, rows.Count);
        return result;
    }

    internal static DtoReturnOne<T> Failure<T>(string? message) where T : BaseDto
    {
        var result = new DtoReturnOne<T>();
        result.SetErrorResult(message ?? "The account request could not be completed.");
        return result;
    }

    internal static DtoReturnOne<T> Failure<T>(BaseDomainResponse response, IAppLogger logger) where T : BaseDto
    {
        if (response.Exception is not null)
            logger.LogError(response.Exception, "Account");
        return Failure<T>(response.Error);
    }
}

public class CurrentUserQueryHandler(IIdentityService identity, ITokenGenerator tokens,
    IBasketRepository baskets, IBuyerIdentity buyer, IAppLogger logger)
    : IRequestHandler<CurrentUserQuery, DtoReturnOne<UserDto>>
{
    public async Task<DtoReturnOne<UserDto>> Handle(CurrentUserQuery request, CancellationToken cancellationToken)
    {
        if (!buyer.IsAuthenticated || string.IsNullOrWhiteSpace(buyer.BuyerId))
            return AccountResults.Failure<UserDto>("An authenticated buyer is required.");

        try
        {
            var found = await identity.FindByNameAsync(buyer.BuyerId, cancellationToken);
            if (!found.Result || found.Data is null)
                return AccountResults.Failure<UserDto>(found, logger);

            var user = found.Data;
            var basket = await baskets.GetWithItemsAsync(user.UserName!, cancellationToken);
            if (!basket.Result)
                return AccountResults.Failure<UserDto>(basket, logger);

            return AccountResults.Success(new UserDto
            {
                Email = user.Email ?? string.Empty,
                Token = tokens.GenerateToken(user, await identity.GetRolesAsync(user, cancellationToken)),
                Basket = basket.Data is null ? null : BasketDto.FromEntity(basket.Data)
            });
        }
        catch (Exception exception)
        {
            logger.LogError(exception, nameof(CurrentUserQueryHandler));
            return AccountResults.Failure<UserDto>(exception.Message);
        }
    }
}

public class SavedAddressQueryHandler(IIdentityService identity, IBuyerIdentity buyer, IAppLogger logger)
    : IRequestHandler<SavedAddressQuery, DtoReturnOne<UserAddressDto>>
{
    public async Task<DtoReturnOne<UserAddressDto>> Handle(SavedAddressQuery request, CancellationToken cancellationToken)
    {
        if (!buyer.IsAuthenticated || string.IsNullOrWhiteSpace(buyer.BuyerId))
            return AccountResults.Failure<UserAddressDto>("An authenticated buyer is required.");

        try
        {
            var address = await identity.GetAddressAsync(buyer.BuyerId, cancellationToken);
            return address.Result
                ? AccountResults.Success(address.Data is null ? null : UserAddressDto.FromEntity(address.Data))
                : AccountResults.Failure<UserAddressDto>(address, logger);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, nameof(SavedAddressQueryHandler));
            return AccountResults.Failure<UserAddressDto>(exception.Message);
        }
    }
}
