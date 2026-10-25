using MediatR;
using ReStore.Application.Common;

namespace ReStore.Application.Commands
{
    /// <summary>
    /// Creates a product, optionally with an image. The price/stock bounds are enforced
    /// by ProductControls, not here — a command is data, the control is the rule.
    /// </summary>
    public class CreateProductCommand : AddFileCommand, IRequest<InsertReturn>
    {
        public string Name { get; set; } = string.Empty;

        public string Description { get; set; } = string.Empty;

        public long Price { get; set; }

        public string Type { get; set; } = string.Empty;

        public string Brand { get; set; } = string.Empty;

        public int QuantityInStock { get; set; }
    }

    /// <summary>
    /// Updates a product. A new image replaces the old one (and deletes it);
    /// no image means the current one is kept.
    /// </summary>
    public class UpdateProductCommand : UpdateFileCommand, IRequest<UpdateReturn>
    {
        public string? Name { get; set; }

        public string? Description { get; set; }

        public long? Price { get; set; }

        public string? Type { get; set; }

        public string? Brand { get; set; }

        public int? QuantityInStock { get; set; }
    }

    public class DeleteProductCommand : BaseDeleteCommand, IRequest<DeleteReturn>
    {
    }
}
