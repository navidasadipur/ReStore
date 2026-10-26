using System.Threading;
using System.Threading.Tasks;
using ReStore.Domain.Entities;
using ReStore.Domain.Interfaces.Common;

namespace ReStore.Application.Interfaces
{
    /// <summary>
    /// Basket access.
    ///
    /// The mutations are here rather than assembled in the handlers on purpose: adding a
    /// line means loading the *tracked* product entity (passing an untracked product into a
    /// tracked basket makes EF try to INSERT it) and the aggregate's own AddItem rule has
    /// to run against tracked instances. Keeping that in one place means a handler cannot
    /// get the tracking wrong.
    ///
    /// On success the returned response carries the basket, ready to map.
    /// </summary>
    public interface IBasketRepository
    {
        /// <summary>Reads a basket with items and products, untracked. Missing basket = failed response with no data.</summary>
        Task<DomainResponseOne<BasketEntity>> GetWithItemsAsync(
            string buyerId,
            CancellationToken cancellationToken = default);

        /// <summary>Adds a quantity of a product, creating the basket when the buyer has none.</summary>
        Task<DomainResponse> AddItemAsync(
            string buyerId,
            int productId,
            int quantity,
            CancellationToken cancellationToken = default);

        /// <summary>Removes a quantity of a product; removing more than present clears the line.</summary>
        Task<DomainResponse> RemoveItemAsync(
            string buyerId,
            int productId,
            int quantity,
            CancellationToken cancellationToken = default);
    }
}
