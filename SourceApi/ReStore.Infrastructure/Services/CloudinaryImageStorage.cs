using CloudinaryDotNet;
using CloudinaryDotNet.Actions;
using Microsoft.Extensions.Configuration;
using ReStore.Application.Interfaces;

namespace ReStore.Infrastructure.Services
{
    /// <summary>
    /// Cloudinary behind <see cref="IImageStorage"/>. Never throws: a failed upload
    /// comes back as <see cref="UploadedImage.Error"/>, a failed delete as false, so
    /// the caller decides whether a storage problem should fail the request.
    /// </summary>
    public sealed class CloudinaryImageStorage : IImageStorage
    {
        private readonly Cloudinary _cloudinary;
        private readonly IAppLogger _logger;

        public CloudinaryImageStorage(IConfiguration configuration, IAppLogger logger)
        {
            _logger = logger;

            var account = new Account(
                configuration["Cloudinary:CloudName"],
                configuration["Cloudinary:ApiKey"],
                configuration["Cloudinary:ApiSecret"]);

            _cloudinary = new Cloudinary(account);
        }

        public async Task<UploadedImage> AddImageAsync(
            Stream stream,
            string fileName,
            CancellationToken cancellationToken = default)
        {
            try
            {
                if (stream is null || stream == Stream.Null || string.IsNullOrWhiteSpace(fileName))
                    return UploadedImage.Failed("No file was supplied.");

                var uploadParams = new ImageUploadParams
                {
                    File = new FileDescription(fileName, stream)
                };

                var result = await _cloudinary.UploadAsync(uploadParams);

                if (result.Error is not null)
                    return UploadedImage.Failed(result.Error.Message);

                return new UploadedImage(result.SecureUrl?.ToString(), result.PublicId, null);
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, nameof(CloudinaryImageStorage));
                return UploadedImage.Failed(exception.Message);
            }
        }

        public async Task<bool> DeleteImageAsync(
            string publicId,
            CancellationToken cancellationToken = default)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(publicId))
                    return false;

                var result = await _cloudinary.DestroyAsync(new DeletionParams(publicId));
                return string.Equals(result.Result, "ok", StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, nameof(CloudinaryImageStorage));
                return false;
            }
        }
    }
}
