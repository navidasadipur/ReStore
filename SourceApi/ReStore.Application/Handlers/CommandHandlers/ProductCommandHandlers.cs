using ReStore.Application.Commands;
using ReStore.Application.Handlers.BaseHandlers;
using ReStore.Application.Interfaces;
using ReStore.Domain.Entities;
using ReStore.Domain.Interfaces.Common;
using ReStore.Domain.Interfaces.Repository;

namespace ReStore.Application.Handlers.CommandHandlers
{
    /// <summary>
    /// Creates a product and, when a file came with the command, uploads it and stores
    /// the returned url/public id on the entity.
    /// </summary>
    public class CreateProductCommandHandler : BaseAddCommandHandler<ProductEntity, CreateProductCommand>
    {
        private readonly IImageStorage _imageStorage;

        public CreateProductCommandHandler(
            IGenericCommandRepositoryBase<ProductEntity> repository,
            IControls<ProductEntity> controls,
            IMapperCustome mapper,
            IAppLogger logger,
            IKeyExistenceChecker keyExistenceChecker,
            IImageStorage imageStorage)
            : base(repository, controls, mapper, logger, keyExistenceChecker)
        {
            _imageStorage = imageStorage;
        }

        protected override async Task<ProductEntity> MapEntityFromRequest(CreateProductCommand command)
        {
            var product = await base.MapEntityFromRequest(command);

            if (!command.HasFile)
                return product;

            var upload = await _imageStorage.AddImageAsync(command.Stream, command.FileName);

            // There is no error channel on this hook, so a failed upload is raised as an
            // exception: BaseAddCommandHandler turns it into a failed InsertReturn with
            // this message, which is what the caller should see.
            if (!upload.Succeeded)
                throw new InvalidOperationException($"The image could not be uploaded: {upload.Error}");

            product.PictureUrl = upload.Url;
            product.publicId = upload.PublicId;

            return product;
        }
    }

    /// <summary>
    /// Updates a product. Sending a file replaces the image *and* deletes the previous
    /// one; sending none keeps the current image untouched.
    /// </summary>
    public class UpdateProductCommandHandler : BaseUpdateCommandHandler<ProductEntity, UpdateProductCommand>
    {
        private readonly IImageStorage _imageStorage;

        public UpdateProductCommandHandler(
            IGenericCommandRepositoryBase<ProductEntity> repository,
            IControls<ProductEntity> controls,
            IMapperCustome mapper,
            IAppLogger logger,
            IImageStorage imageStorage)
            : base(repository, controls, mapper, logger)
        {
            _imageStorage = imageStorage;
        }

        /// <summary>
        /// A new image means the two image columns must be written even if the caller did
        /// not list them — otherwise the upload would be paid for and discarded.
        /// </summary>
        protected override Task<DomainResponse> CheckCommand(UpdateProductCommand command)
        {
            if (command.HasFile)
            {
                command.ChangeFields ??= new List<string>();
                foreach (var column in new[] { nameof(ProductEntity.PictureUrl), nameof(ProductEntity.publicId) })
                {
                    if (!command.ChangeFields.Contains(column, StringComparer.OrdinalIgnoreCase))
                        command.ChangeFields.Add(column);
                }
            }

            return base.CheckCommand(command);
        }

        protected override async Task<ProductEntity> MapEntityFromRequest(UpdateProductCommand command)
            => _mapper.Map<UpdateProductCommand, ProductEntity>(command);

        protected override async Task<DomainResponse> Update(
            ProductEntity entity,
            UpdateProductCommand command,
            CancellationToken cancellationToken)
        {
            if (!command.HasFile)
                return await base.Update(entity, command, cancellationToken);

            var previousPublicId = RequestedPublicId();

            var upload = await _imageStorage.AddImageAsync(command.Stream, command.FileName);
            if (!upload.Succeeded)
                throw new InvalidOperationException($"The image could not be uploaded: {upload.Error}");

            entity.PictureUrl = upload.Url;
            entity.publicId = upload.PublicId;

            var result = await base.Update(entity, command, cancellationToken);

            // Only after the row is safely written: deleting first would leave the product
            // pointing at an image that no longer exists if the update then failed.
            if (result.Result && !string.IsNullOrWhiteSpace(previousPublicId))
                await _imageStorage.DeleteImageAsync(previousPublicId, cancellationToken);

            return result;
        }

        /// <summary>
        /// The old image id, read from the row the control loaded. ProductControls asks for
        /// that column in GetSelectForKey for exactly this reason.
        /// </summary>
        private string? RequestedPublicId() => _controls.LastEntityInfo?.publicId;
    }

    /// <summary>Deletes a product, then its image (a stray image is better than a dead row).</summary>
    public class DeleteProductCommandHandler : BaseDeleteCommandHandler<ProductEntity, DeleteProductCommand>
    {
        private readonly IImageStorage _imageStorage;

        public DeleteProductCommandHandler(
            IGenericCommandRepositoryBase<ProductEntity> repository,
            IControls<ProductEntity> controls,
            IAppLogger logger,
            IImageStorage imageStorage)
            : base(repository, controls, logger)
        {
            _imageStorage = imageStorage;
        }

        protected override async Task<DomainResponse> Delete(
            ProductEntity entity,
            DeleteProductCommand command,
            CancellationToken cancellationToken)
        {
            var publicId = entity.publicId;

            var result = await base.Delete(entity, command, cancellationToken);

            if (result.Result && !string.IsNullOrWhiteSpace(publicId))
                await _imageStorage.DeleteImageAsync(publicId, cancellationToken);

            return result;
        }
    }
}
