using MediatR;
using ReStore.Application.Common;
using ReStore.Application.DTOs;

namespace ReStore.Application.Commands;

public class LoginCommand : IRequest<DtoReturnOne<UserDto>>
{
    public string? Username { get; set; }
    public string? Password { get; set; }
}

public class RegisterCommand : BaseAddCommand, IRequest<InsertReturn>
{
    public string? Username { get; set; }
    public string? Email { get; set; }
    public string? Password { get; set; }
}
