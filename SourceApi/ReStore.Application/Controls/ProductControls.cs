using ReStore.Domain.Entities;
using ReStore.Domain.Interfaces.Common;
using ReStore.Domain.Interfaces.Repository;

namespace ReStore.Application.Controls
{
    /// <summary>
    /// The rules a product must satisfy, in one place and independent of the transport.
    ///
    /// They are written here rather than as [Control] attributes on ProductEntity for a
    /// reason: price has a lower bound and stock has a range, and keeping them next to
    /// the aggregate's own behaviour makes them easy to find and easy to change without
    /// touching the entity.
    /// </summary>
    public class ProductControls : BaseControls<ProductEntity>
    {
        public const long MinimumPrice = 100;
        public const int MaximumStock = 200;

        public ProductControls(IGenericQueryRepositoryBase<ProductEntity> repository)
            : base(repository)
        {
        }

        protected override Task<DomainResponse> CheckOnAddInternal(ProductEntity entity)
            => Task.FromResult(ValidateProduct(entity));

        /// <summary>
        /// The update flow needs the current image id to delete it after a successful
        /// replacement, so the row loaded for the key must include that column.
        /// </summary>
        protected override System.Linq.Expressions.Expression<System.Func<ProductEntity, object>>? GetSelectForKey()
            => product => new { product.ProductId, product.publicId };

        protected override Task<DomainResponse> CheckOnUpdateInternal<Command>(
            ProductEntity entity,
            System.Collections.Generic.IReadOnlyCollection<string> changeFields,
            Command command)
        {
            // Only the columns being written are validated: an update that touches the
            // name must not fail because the row's stored price happens to be odd.
            var check = new DomainResponse();

            if (Writes(changeFields, nameof(ProductEntity.Price)))
                check = ValidatePrice(entity.Price).AddTo(check);

            if (Writes(changeFields, nameof(ProductEntity.QuantityInStock)))
                check = ValidateStock(entity.QuantityInStock).AddTo(check);

            return Task.FromResult(check);
        }

        private static bool Writes(System.Collections.Generic.IReadOnlyCollection<string> changeFields, string column)
            => changeFields.Contains(column, System.StringComparer.OrdinalIgnoreCase);

        private static DomainResponse ValidateProduct(ProductEntity product)
        {
            var check = new DomainResponse();

            if (string.IsNullOrWhiteSpace(product.Name))
                check = new DomainResponse("Name is required.").AddTo(check);

            if (string.IsNullOrWhiteSpace(product.Description))
                check = new DomainResponse("Description is required.").AddTo(check);

            if (string.IsNullOrWhiteSpace(product.Brand))
                check = new DomainResponse("Brand is required.").AddTo(check);

            if (string.IsNullOrWhiteSpace(product.Type))
                check = new DomainResponse("Type is required.").AddTo(check);

            check = ValidatePrice(product.Price).AddTo(check);
            check = ValidateStock(product.QuantityInStock).AddTo(check);

            return check;
        }

        private static DomainResponse ValidatePrice(long price)
            => price < MinimumPrice
                ? new DomainResponse($"Price must be at least {MinimumPrice}.")
                : new DomainResponse();

        private static DomainResponse ValidateStock(int quantityInStock)
            => quantityInStock is < 0 or > MaximumStock
                ? new DomainResponse($"Quantity in stock must be between 0 and {MaximumStock}.")
                : new DomainResponse();
    }
}
