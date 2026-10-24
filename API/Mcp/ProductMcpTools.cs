using System.ComponentModel;
using API.Data;
using API.Entities;
using API.Extensions;
using API.RequestHelpers;
using API.Services;
using Microsoft.EntityFrameworkCore;
using ModelContextProtocol.Server;

namespace API.Mcp;

/// <summary>
/// MCP tools for product administration. This first version deliberately has
/// no caller authentication and does not accept file uploads.
/// </summary>
[McpServerToolType]
public sealed class ProductMcpTools(StoreContext context, ImageService imageService)
{
    [McpServerTool(ReadOnly = true, UseStructuredContent = true)]
    [Description("یک اندپوینت جهت تست عملکرد mcp server.")]
    public string McpTest(
    [Description("یک ورودی استرینگ تست")] string? messageTest = null)
    {
        return $"Hello. the meesage is '{messageTest}'";
    }

    [McpServerTool(ReadOnly = true, UseStructuredContent = true)]
    [Description("فهرست محصولات با جستجو، فیلتر، مرتب‌سازی و صفحه‌بندی.")]
    public async Task<PagedProductsMcpResult> ListProducts(
        [Description("بخشی از نام محصول؛ خالی یعنی همه محصولات.")] string? search = null,
        [Description("فیلتر برندها با کاما، مانند Nike,Adidas؛ خالی یعنی همه برندها.")] string? brands = null,
        [Description("فیلتر نوع‌ها با کاما، مانند Shoes,Clothing؛ خالی یعنی همه نوع‌ها.")] string? types = null,
        [Description("مرتب‌سازی: price، priceDesc یا خالی برای نام محصول.")] string? orderBy = null,
        [Description("شماره صفحه؛ پیش‌فرض ۱.")] int page = 1,
        [Description("تعداد رکورد در صفحه؛ پیش‌فرض 10 و حداکثر ۵۰.")] int pageSize = 10,
        CancellationToken cancellationToken = default)
    {
        var currentPage = Math.Max(1, page);
        var currentPageSize = Math.Clamp(pageSize, 1, 50);

        var query = context.Products
            .AsNoTracking()
            .Sort(orderBy ?? string.Empty)
            .Search(search ?? string.Empty)
            .Filter(brands ?? string.Empty, types ?? string.Empty);

        var totalCount = await query.CountAsync(cancellationToken);
        var products = await query
            .Skip((currentPage - 1) * currentPageSize)
            .Take(currentPageSize)
            .Select(product => ProductMcpDto.FromProduct(product))
            .ToListAsync(cancellationToken);

        return new PagedProductsMcpResult(
            products,
            new MetaData
            {
                CurrentPage = currentPage,
                PageSize = currentPageSize,
                TotalCount = totalCount,
                TotalPages = (int)Math.Ceiling(totalCount / (double)currentPageSize)
            });
    }

    [McpServerTool(ReadOnly = true, UseStructuredContent = true)]
    [Description("جزئیات یک محصول را با شناسه آن برمی‌گرداند.")]
    public async Task<ProductMcpDto> GetProduct(
        [Description("شناسه محصول.")] int id,
        CancellationToken cancellationToken = default)
    {
        var product = await context.Products
            .AsNoTracking()
            .SingleOrDefaultAsync(product => product.Id == id, cancellationToken)
            ?? throw new InvalidOperationException($"محصولی با شناسه {id} پیدا نشد.");

        return ProductMcpDto.FromProduct(product);
    }

    [McpServerTool(ReadOnly = true, UseStructuredContent = true)]
    [Description("برندها و نوع‌های موجود برای فیلتر محصولات را برمی‌گرداند.")]
    public async Task<ProductFiltersMcpResult> GetProductFilters(CancellationToken cancellationToken = default)
    {
        var brands = await context.Products
            .AsNoTracking()
            .Select(product => product.Brand)
            .Distinct()
            .OrderBy(brand => brand)
            .ToListAsync(cancellationToken);

        var types = await context.Products
            .AsNoTracking()
            .Select(product => product.Type)
            .Distinct()
            .OrderBy(type => type)
            .ToListAsync(cancellationToken);

        return new ProductFiltersMcpResult(brands, types);
    }

    [McpServerTool(ReadOnly = false, Destructive = false, Idempotent = false, UseStructuredContent = true)]
    [Description("یک محصول جدید بدون تصویر ایجاد می‌کند. برای تصویر، بعداً باید از API مدیریت محصول استفاده شود.")]
    public async Task<ProductMcpDto> CreateProduct(
        [Description("نام محصول.")] string name,
        [Description("توضیحات محصول.")] string description,
        [Description("قیمت محصول؛ حداقل ۱۰۰.")] long price,
        [Description("نوع محصول.")] string type,
        [Description("برند محصول.")] string brand,
        [Description("تعداد موجودی؛ از ۰ تا ۲۰۰.")] int quantityInStock,
        CancellationToken cancellationToken = default)
    {
        ValidateProductInput(name, description, price, type, brand, quantityInStock);

        var product = new Product
        {
            Name = name.Trim(),
            Description = description.Trim(),
            Price = price,
            Type = type.Trim(),
            Brand = brand.Trim(),
            QuantityInStock = quantityInStock
        };

        context.Products.Add(product);
        await context.SaveChangesAsync(cancellationToken);

        return ProductMcpDto.FromProduct(product);
    }

    [McpServerTool(ReadOnly = false, Destructive = false, Idempotent = false, UseStructuredContent = true)]
    [Description("اطلاعات یک محصول را ویرایش می‌کند و تصویر فعلی آن را حفظ می‌کند.")]
    public async Task<ProductMcpDto> UpdateProduct(
        [Description("شناسه محصول.")] int id,
        [Description("نام محصول.")] string name,
        [Description("توضیحات محصول.")] string description,
        [Description("قیمت محصول؛ حداقل ۱۰۰.")] long price,
        [Description("نوع محصول.")] string type,
        [Description("برند محصول.")] string brand,
        [Description("تعداد موجودی؛ از ۰ تا ۲۰۰.")] int quantityInStock,
        CancellationToken cancellationToken = default)
    {
        ValidateProductInput(name, description, price, type, brand, quantityInStock);

        var product = await context.Products
            .SingleOrDefaultAsync(product => product.Id == id, cancellationToken)
            ?? throw new InvalidOperationException($"محصولی با شناسه {id} پیدا نشد.");

        product.Name = name.Trim();
        product.Description = description.Trim();
        product.Price = price;
        product.Type = type.Trim();
        product.Brand = brand.Trim();
        product.QuantityInStock = quantityInStock;

        await context.SaveChangesAsync(cancellationToken);

        return ProductMcpDto.FromProduct(product);
    }

    [McpServerTool(ReadOnly = false, Destructive = true, Idempotent = false, UseStructuredContent = true)]
    [Description("یک محصول را حذف می‌کند. اگر محصول تصویر Cloudinary داشته باشد، آن تصویر نیز حذف می‌شود.")]
    public async Task<DeleteProductMcpResult> DeleteProduct(
        [Description("شناسه محصول.")] int id,
        CancellationToken cancellationToken = default)
    {
        var product = await context.Products
            .SingleOrDefaultAsync(product => product.Id == id, cancellationToken)
            ?? throw new InvalidOperationException($"محصولی با شناسه {id} پیدا نشد.");

        if (!string.IsNullOrWhiteSpace(product.publicId))
            await imageService.DeleteImageAsync(product.publicId);

        context.Products.Remove(product);
        await context.SaveChangesAsync(cancellationToken);

        return new DeleteProductMcpResult(id, true);
    }

    private static void ValidateProductInput(
        string name,
        string description,
        long price,
        string type,
        string brand,
        int quantityInStock)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("نام محصول الزامی است.", nameof(name));
        if (string.IsNullOrWhiteSpace(description))
            throw new ArgumentException("توضیحات محصول الزامی است.", nameof(description));
        if (price < 100)
            throw new ArgumentOutOfRangeException(nameof(price), "قیمت محصول باید حداقل ۱۰۰ باشد.");
        if (string.IsNullOrWhiteSpace(type))
            throw new ArgumentException("نوع محصول الزامی است.", nameof(type));
        if (string.IsNullOrWhiteSpace(brand))
            throw new ArgumentException("برند محصول الزامی است.", nameof(brand));
        if (quantityInStock is < 0 or > 200)
            throw new ArgumentOutOfRangeException(nameof(quantityInStock), "موجودی باید بین ۰ تا ۲۰۰ باشد.");
    }
}

public sealed record PagedProductsMcpResult(IReadOnlyList<ProductMcpDto> Items, MetaData MetaData);

public sealed record ProductFiltersMcpResult(IReadOnlyList<string> Brands, IReadOnlyList<string> Types);

public sealed record DeleteProductMcpResult(int Id, bool Deleted);

public sealed record ProductMcpDto(
    int Id,
    string Name,
    string Description,
    long Price,
    string? PictureUrl,
    string Type,
    string Brand,
    int QuantityInStock)
{
    public static ProductMcpDto FromProduct(Product product) => new(
        product.Id,
        product.Name,
        product.Description,
        product.Price,
        product.PictureUrl,
        product.Type,
        product.Brand,
        product.QuantityInStock);
}
