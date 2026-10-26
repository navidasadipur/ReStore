using MediatR;
using ReStore.Application.DTOs;

namespace ReStore.Application.Queries;

public class CurrentUserQuery : IRequest<DtoReturnOne<UserDto>>;
public class SavedAddressQuery : IRequest<DtoReturnOne<UserAddressDto>>;
