using System.Collections.Generic;
using ReStore.Application.Commands;

namespace ReStore.Api.DTOs
{
    /// <summary>
    /// The multipart form for updating a product. Same reason as
    /// <see cref="CreateProductDto"/> for existing: the file is an IFormFile here and a
    /// Stream in the command.
    ///
    /// No DataAnnotations on purpose — failures come back as envelopes (decision D1), not
    /// automatic 400s. ProductControls owns the rules.
    /// </summary>
    public class UpdateProductDto
    {
        public int Id { get; set; }

        public string? Name { get; set; }

        public string? Description { get; set; }

        public long Price { get; set; }

        public IFormFile? File { get; set; }

        public string? Type { get; set; }

        public string? Brand { get; set; }

        public int QuantityInStock { get; set; }

        /// <summary>
        /// <paramref name="editableColumns"/> is the controller's whitelist, not something
        /// the caller sends: the client says *what* the values are, the server decides
        /// which columns may be written.
        /// </summary>
        public UpdateProductCommand ToCommand(IReadOnlyCollection<string> editableColumns) => new()
        {
            Id = Id,
            Name = Name,
            Description = Description,
            Price = Price,
            Type = Type,
            Brand = Brand,
            QuantityInStock = QuantityInStock,
            ChangeFields = new List<string>(editableColumns),
            Stream = File is { Length: > 0 } ? File.OpenReadStream() : Stream.Null,
            ContentType = File?.ContentType ?? string.Empty,
            FileName = File?.FileName ?? string.Empty
        };
    }
}
