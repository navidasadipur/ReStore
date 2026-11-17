using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;
using ReStore.Domain.Common;

namespace ReStore.Domain.Entities
{
    public class ProductEntity : BaseEntity
    {
        /// <summary>
        /// [Key] is required because the key is not called Id or ProductEntityId: EF's
        /// convention only recognises those two names. AccountsApi uses the same
        /// attribute for the same reason. The attribute is DataAnnotations, not EF, so
        /// this project still needs no persistence framework.
        ///
        /// Serialised as "id" on purpose. This entity is still returned directly by
        /// ProductsController, and the React client reads `product.id`; the attribute
        /// keeps that contract until the Products slice returns a DTO (Phase 3), at
        /// which point it moves to the DTO.
        /// </summary>
        [Key]
        [JsonPropertyName("id")]
        public int ProductId { get; set; }

        public string Name { get; set; }
        public string Description { get; set; }
        public long Price { get; set; }
        public string? PictureUrl { get; set; }
        public string Type { get; set; }
        public string Brand { get; set; }
        public int QuantityInStock { get; set; }
        public string? publicId { get; set; }
    }
}
