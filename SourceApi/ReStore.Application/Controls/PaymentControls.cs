using ReStore.Domain.Entities;
using ReStore.Domain.Interfaces.Common;
using ReStore.Domain.Interfaces.Repository;

namespace ReStore.Application.Controls;

public class PaymentControls(IGenericQueryRepositoryBase<BasketEntity> repository)
    : BaseControls<BasketEntity>(repository)
{
    public DomainResponse CheckForPayment(BasketEntity? basket)
    {
        if (basket is null || basket.Items.Count == 0)
            return new DomainResponse("The basket is empty.");
        if (basket.Items.Any(item => item.Product is null || item.Quantity <= 0 || item.Product.Price <= 0))
            return new DomainResponse("The basket contains an invalid product or quantity.");
        return new DomainResponse();
    }

    // Uses database prices and the existing checkout delivery rule, never a
    // client-supplied amount. Checked arithmetic prevents a wrapped payment total.
    public long GetAmount(BasketEntity basket)
    {
        var subtotal = basket.Items.Sum(item => checked(item.Product.Price * item.Quantity));
        return checked(subtotal + (subtotal > 10000 ? 0 : 500));
    }
}
