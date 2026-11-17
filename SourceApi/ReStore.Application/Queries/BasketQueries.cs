using MediatR;
using ReStore.Application.DTOs;

namespace ReStore.Application.Queries
{
    /// <summary>
    /// The current visitor's basket. No paging and no filters: there is exactly one.
    /// </summary>
    public class BasketQuery : IRequest<DtoReturnOne<BasketDto>>
    {
    }
}
