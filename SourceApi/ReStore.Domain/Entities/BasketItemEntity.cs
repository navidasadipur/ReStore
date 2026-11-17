using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using ReStore.Domain.Common;

namespace ReStore.Domain.Entities
{
    [Table("BasketItems")]
    public class BasketItemEntity : BaseEntity
    {
        /// <summary>Key is "BasketItemId", not Id/BasketItemEntityId, so it needs [Key].</summary>
        [Key]
        public int BasketItemId { get; set; }

        public int Quantity { get; set; }

        //navigation properties
        // Names must stay Product/Basket: EF pairs a navigation with a foreign key
        // called <NavigationName>Id, so renaming them to ProductEntity/BasketEntity
        // would make ProductId/BasketId plain columns and add shadow keys instead.
        public int ProductId { get; set; }
        public ProductEntity Product { get; set; }

        public int BasketId { get; set; }
        public BasketEntity Basket { get; set; }
    }
}
