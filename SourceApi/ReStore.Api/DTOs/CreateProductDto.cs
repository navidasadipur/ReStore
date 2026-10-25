using ReStore.Application.Commands;

namespace ReStore.Api.DTOs
{
    /// <summary>
    /// The multipart form for creating a product. It exists for exactly one reason: the
    /// uploaded file arrives as <see cref="IFormFile"/>, and the command it becomes
    /// carries a Stream, so ReStore.Application stays free of ASP.NET Core (plan §7.6).
    /// Everything else is straight from the form.
    ///
    /// Deliberately no DataAnnotations: [ApiController] would turn a missing field into an
    /// HTTP 400 ProblemDetails, while every other failure comes back as a 200 envelope
    /// (decision D1). The rules live in ProductControls, so there is one error channel and
    /// one set of messages.
    /// </summary>
    public class CreateProductDto
    {
        public string? Name { get; set; }

        public string? Description { get; set; }

        public long Price { get; set; }

        public IFormFile? File { get; set; }

        public string? Type { get; set; }

        public string? Brand { get; set; }

        public int QuantityInStock { get; set; }

        public CreateProductCommand ToCommand() => new()
        {
            Name = Name ?? string.Empty,
            Description = Description ?? string.Empty,
            Price = Price,
            Type = Type ?? string.Empty,
            Brand = Brand ?? string.Empty,
            QuantityInStock = QuantityInStock,
            Stream = File is { Length: > 0 } ? File.OpenReadStream() : Stream.Null,
            ContentType = File?.ContentType ?? string.Empty,
            FileName = File?.FileName ?? string.Empty
        };
    }
}
