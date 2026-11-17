using MediatR;
using ReStore.Application.DTOs;

namespace ReStore.Application.Commands
{
    /// <summary>
    /// Adds a product to the current visitor's basket, creating the basket on first add.
    /// Returns the whole basket because that is what the client renders afterwards.
    /// </summary>
    public class AddBasketItemCommand : IRequest<DtoReturnOne<BasketDto>>
    {
        public int ProductId { get; set; }

        public int Quantity { get; set; } = 1;
    }

    /// <summary>Removes a quantity of a product; removing more than present clears the line.</summary>
    public class RemoveBasketItemCommand : IRequest<DtoReturnOne<BasketDto>>
    {
        public int ProductId { get; set; }

        public int Quantity { get; set; } = 1;
    }
}
