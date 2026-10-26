using MediatR;
using ReStore.Application.Common;
using ReStore.Application.DTOs;

namespace ReStore.Application.Queries;

public class OrderListQuery : BaseQuery, IRequest<PaginatedResponseListDto<OrderDto>>;

public class OrderDetailQuery : BaseDetailQuery, IRequest<DtoReturnOne<OrderDto>>;
