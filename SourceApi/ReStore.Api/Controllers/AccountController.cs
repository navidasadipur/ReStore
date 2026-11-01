using MediatR;
using ReStore.Api.Filters;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ReStore.Application.Commands;
using ReStore.Application.Common;
using ReStore.Application.DTOs;
using ReStore.Application.Queries;

namespace ReStore.Api.Controllers;

[EnvelopeResponse]
public class AccountController(IMediator mediator) : BaseApiController
{
    [HttpPost("login")]
    public async Task<DtoReturnOne<UserDto>> Login([FromBody] LoginCommand command, CancellationToken cancellationToken)
        => await mediator.Send(command, cancellationToken);

    [HttpPost("register")]
    public async Task<InsertReturn> Register([FromBody] RegisterCommand command, CancellationToken cancellationToken)
        => await mediator.Send(command, cancellationToken);

    [Authorize]
    [HttpGet("currentUser")]
    public async Task<DtoReturnOne<UserDto>> GetCurrentUser(CancellationToken cancellationToken)
        => await mediator.Send(new CurrentUserQuery(), cancellationToken);

    [Authorize]
    [HttpGet("savedAddress")]
    public async Task<DtoReturnOne<UserAddressDto>> GetSavedAddress(CancellationToken cancellationToken)
        => await mediator.Send(new SavedAddressQuery(), cancellationToken);
}
