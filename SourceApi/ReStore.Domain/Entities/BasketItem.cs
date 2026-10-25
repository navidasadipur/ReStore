using System.ComponentModel.DataAnnotations.Schema;
using ReStore.Domain.Common;

namespace ReStore.Domain.Entities
{
    [Table("BasketItems")]
    public class BasketItem : BaseEntity
    {
        public int Id { get; set; }
        public int Quantity { get; set; }

        //navigation properties
        public int ProductId { get; set; }
        public Product Product { get; set; }

        public int BasketId { get; set; }
        public Basket Basket { get; set; }
    }
}