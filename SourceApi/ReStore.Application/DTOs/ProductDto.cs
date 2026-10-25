using System.Text.Json.Serialization;

namespace ReStore.Application.DTOs
{
    /// <summary>
    /// One row of the catalog list. Only the columns the list needs, so the projection
    /// stays narrow.
    /// </summary>
    public class ProductListDto : BaseDto
    {
        /// <summary>
        /// Serialised as "id" so the product shape the React client consumes is
        /// unchanged — only the envelope around it is new.
        /// </summary>
        [JsonPropertyName("id")]
        public int ProductId { get; set; }

        public string Name { get; set; } = string.Empty;

        public long Price { get; set; }

        public string? PictureUrl { get; set; }

        public string Brand { get; set; } = string.Empty;

        public string Type { get; set; } = string.Empty;

        /// <summary>
        /// Included because the admin inventory grid renders it from this same list. The
        /// description is deliberately absent: only the detail query reads it.
        /// </summary>
        public int QuantityInStock { get; set; }
    }

    public class ProductDetailDto : BaseDto
    {
        [JsonPropertyName("id")]
        public int ProductId { get; set; }

        public string Name { get; set; } = string.Empty;

        public string Description { get; set; } = string.Empty;

        public long Price { get; set; }

        public string? PictureUrl { get; set; }

        public string Brand { get; set; } = string.Empty;

        public string Type { get; set; } = string.Empty;

        public int QuantityInStock { get; set; }
    }

    /// <summary>The distinct values the catalog's filter controls are built from.</summary>
    public class ProductFilterDto : BaseDto
    {
        public IReadOnlyList<string> Brands { get; set; } = new List<string>();

        public IReadOnlyList<string> Types { get; set; } = new List<string>();
    }
}
