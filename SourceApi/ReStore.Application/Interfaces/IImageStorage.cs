namespace ReStore.Application.Interfaces
{
    /// <summary>
    /// Where uploaded images go. Application decides *when* to upload and what to store
    /// on the entity; Infrastructure decides *where* (Cloudinary today).
    ///
    /// Streams, not IFormFile: the controller converts the uploaded file, so nothing
    /// here references ASP.NET Core.
    /// </summary>
    public interface IImageStorage
    {
        /// <summary>Uploads a stream. <see cref="UploadedImage.Error"/> is set when the upload failed.</summary>
        Task<UploadedImage> AddImageAsync(
            Stream stream,
            string fileName,
            CancellationToken cancellationToken = default);

        /// <summary>Deletes by public id. False means it was not deleted; the row is still deleted.</summary>
        Task<bool> DeleteImageAsync(string publicId, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Result of an upload. Url + PublicId are what the entity stores. Named
    /// <c>UploadedImage</c> rather than "ImageUploadResult" so it cannot be confused with
    /// Cloudinary's own ImageUploadResult in the adapter.
    /// </summary>
    public sealed record UploadedImage(string? Url, string? PublicId, string? Error)
    {
        public bool Succeeded => Error is null && PublicId is not null;

        public static UploadedImage Failed(string error) => new(null, null, error);
    }
}
