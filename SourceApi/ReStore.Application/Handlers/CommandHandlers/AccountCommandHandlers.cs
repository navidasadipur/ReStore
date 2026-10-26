using MediatR;
using ReStore.Application.Commands;
using ReStore.Application.Common;
using ReStore.Application.DTOs;
using ReStore.Application.Handlers.QueryHandlers;
using ReStore.Application.Interfaces;

namespace ReStore.Application.Handlers.CommandHandlers;

public class LoginCommandHandler(IIdentityService identity, ITokenGenerator tokens,
    IAccountBasketRepository baskets, IBuyerIdentity buyer, IAppLogger logger)
    : IRequestHandler<LoginCommand, DtoReturnOne<UserDto>>
{
    public async Task<DtoReturnOne<UserDto>> Handle(LoginCommand request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
            return AccountResults.Failure<UserDto>("Username and password are required.");

        try
        {
            var authenticated = await identity.AuthenticateAsync(request.Username, request.Password, cancellationToken);
            if (!authenticated.Result || authenticated.Data is null)
                return AccountResults.Failure<UserDto>(authenticated, logger);

            var user = authenticated.Data;
            // Produce the token before changing baskets: a signing/configuration
            // failure must not consume the guest basket or clear the cookie.
            var token = tokens.GenerateToken(user, await identity.GetRolesAsync(user, cancellationToken));
            var basket = await baskets.TransferGuestBasketAsync(user.UserName!, buyer.AnonymousBuyerId, cancellationToken);
            if (!basket.Result)
                return AccountResults.Failure<UserDto>(basket, logger);

            var result = AccountResults.Success(new UserDto
            {
                Email = user.Email ?? string.Empty,
                Token = token,
                Basket = basket.Data is null ? null : BasketDto.FromEntity(basket.Data)
            });
            if (buyer.AnonymousBuyerId is not null)
                buyer.ClearAnonymousBuyerId();
            return result;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, nameof(LoginCommandHandler));
            return AccountResults.Failure<UserDto>(exception.Message);
        }
    }
}

public class RegisterCommandHandler(IIdentityService identity, IAppLogger logger)
    : IRequestHandler<RegisterCommand, InsertReturn>
{
    public async Task<InsertReturn> Handle(RegisterCommand request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Email)
            || string.IsNullOrWhiteSpace(request.Password))
            return new InsertReturn(("Username, email and password are required.", "Required"));

        try
        {
            var response = await identity.RegisterAsync(request.Username, request.Email, request.Password, cancellationToken);
            if (response.Exception is not null)
                logger.LogError(response.Exception, nameof(RegisterCommandHandler));
            return response.Result ? new InsertReturn(response.LastKey, response)
                : new InsertReturn((response.Error ?? "The account could not be registered.", response.ErrorCode ?? string.Empty));
        }
        catch (Exception exception)
        {
            logger.LogError(exception, nameof(RegisterCommandHandler));
            return new InsertReturn(exception);
        }
    }
}
