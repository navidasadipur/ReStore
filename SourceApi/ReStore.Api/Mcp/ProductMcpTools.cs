using System.ComponentModel;
using MediatR;
using ModelContextProtocol.Server;
using ReStore.Application.Commands;
using ReStore.Application.Common;
using ReStore.Application.DTOs;
using ReStore.Application.Queries;
using ReStore.Domain.Entities;

namespace ReStore.Api.Mcp;

/// <summary>
/// MCP tools for product administration.
///
/// They are pass-throughs: the same queries and commands, and therefore the *same
/// inputs and outputs*, as the HTTP product endpoints. There are no MCP-specific DTOs —
/// a tool returns exactly the envelope its endpoint returns, failures included
/// (succeeded=false with a message), so an assistant sees what a browser would.
///
/// This is deliberate: a second response shape is a second contract to keep in step,
/// and it would drift the first time a DTO changed.
/// </summary>
[McpServerToolType]
public sealed class ProductMcpTools(IMediator mediator)
{
    [McpServerTool(ReadOnly = true, UseStructuredContent = true)]
    [Description("یک اندپوینت جهت تست عملکرد mcp server.")]
    public string McpTest(
        [Description("یک ورودی استرینگ تست")] string? messageTest = null)
        => $"Hello. the meesage is '{messageTest}'";

    [McpServerTool(ReadOnly = true, UseStructuredContent = true)]
    [Description("فهرست محصولات با جستجو، فیلتر، مرتب‌سازی و صفحه‌بندی — همان خروجی GET /api/products.")]
    public async Task<PaginatedResponseListDto<ProductListDto>> ListProducts(
        [Description("بخشی از نام محصول؛ خالی یعنی همه محصولات.")] string? search = null,
        [Description("فیلتر برندها با کاما، مانند Nike,Adidas؛ خالی یعنی همه برندها.")] string? brands = null,
        [Description("فیلتر نوع‌ها با کاما، مانند Shoes,Clothing؛ خالی یعنی همه نوع‌ها.")] string? types = null,
        [Description("مرتب‌سازی: price، priceDesc یا خالی برای نام محصول.")] string? orderBy = null,
        [Description("شماره صفحه؛ پیش‌فرض ۱.")] int page = 1,
        [Description("تعداد رکورد در صفحه؛ پیش‌فرض ۱۰ و حداکثر ۵۰.")] int pageSize = 10,
        CancellationToken cancellationToken = default)
        => await mediator.Send(
            new ProductListQuery
            {
                FullSearchPhrase = search,
                Brands = brands,
                Types = types,
                OrderBy = orderBy,
                PageNumber = Math.Max(1, page),
                PageSize = Math.Clamp(pageSize, 1, 50)
            },
            cancellationToken);

    [McpServerTool(ReadOnly = true, UseStructuredContent = true)]
    [Description("جزئیات یک محصول را با شناسه آن برمی‌گرداند — همان خروجی GET /api/products/{id}.")]
    public async Task<DtoReturnOne<ProductDetailDto>> GetProduct(
        [Description("شناسه محصول.")] int id,
        CancellationToken cancellationToken = default)
        => await mediator.Send(new ProductDetailQuery { Id = id }, cancellationToken);

    [McpServerTool(ReadOnly = true, UseStructuredContent = true)]
    [Description("برندها و نوع‌های موجود برای فیلتر محصولات — همان خروجی GET /api/products/filters.")]
    public async Task<DtoReturnOne<ProductFilterDto>> GetProductFilters(
        CancellationToken cancellationToken = default)
        => await mediator.Send(new ProductFiltersQuery(), cancellationToken);

    [McpServerTool(ReadOnly = false, Destructive = false, Idempotent = false, UseStructuredContent = true)]
    [Description("یک محصول جدید بدون تصویر ایجاد می‌کند. برای تصویر باید از API مدیریت محصول استفاده شود.")]
    public async Task<InsertReturn> CreateProduct(
        [Description("نام محصول.")] string name,
        [Description("توضیحات محصول.")] string description,
        [Description("قیمت محصول؛ حداقل ۱۰۰.")] long price,
        [Description("نوع محصول.")] string type,
        [Description("برند محصول.")] string brand,
        [Description("تعداد موجودی؛ از ۰ تا ۲۰۰.")] int quantityInStock,
        CancellationToken cancellationToken = default)
        => await mediator.Send(
            new CreateProductCommand
            {
                Name = name,
                Description = description,
                Price = price,
                Type = type,
                Brand = brand,
                QuantityInStock = quantityInStock
            },
            cancellationToken);

    [McpServerTool(ReadOnly = false, Destructive = false, Idempotent = false, UseStructuredContent = true)]
    [Description("اطلاعات یک محصول را ویرایش می‌کند و تصویر فعلی آن را حفظ می‌کند.")]
    public async Task<UpdateReturn> UpdateProduct(
        [Description("شناسه محصول.")] int id,
        [Description("نام محصول.")] string name,
        [Description("توضیحات محصول.")] string description,
        [Description("قیمت محصول؛ حداقل ۱۰۰.")] long price,
        [Description("نوع محصول.")] string type,
        [Description("برند محصول.")] string brand,
        [Description("تعداد موجودی؛ از ۰ تا ۲۰۰.")] int quantityInStock,
        CancellationToken cancellationToken = default)
        => await mediator.Send(
            new UpdateProductCommand
            {
                Id = id,
                Name = name,
                Description = description,
                Price = price,
                Type = type,
                Brand = brand,
                QuantityInStock = quantityInStock,
                ChangeFields =
                [
                    nameof(ProductEntity.Name),
                    nameof(ProductEntity.Description),
                    nameof(ProductEntity.Price),
                    nameof(ProductEntity.Type),
                    nameof(ProductEntity.Brand),
                    nameof(ProductEntity.QuantityInStock)
                ]
            },
            cancellationToken);

    [McpServerTool(ReadOnly = false, Destructive = true, Idempotent = false, UseStructuredContent = true)]
    [Description("یک محصول را حذف می‌کند. اگر محصول تصویر Cloudinary داشته باشد، آن تصویر نیز حذف می‌شود.")]
    public async Task<DeleteReturn> DeleteProduct(
        [Description("شناسه محصول.")] int id,
        CancellationToken cancellationToken = default)
        => await mediator.Send(new DeleteProductCommand { Id = id }, cancellationToken);
}
