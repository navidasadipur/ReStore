using System.Text.Json.Serialization;

namespace ReStore.Application.Common
{
    /// <summary>
    /// A command that carries an uploaded file. Streams stay in Application: the
    /// controller converts IFormFile, so no ASP.NET type leaks into this project.
    /// </summary>
    public interface IFileCommand
    {
        Stream Stream { get; }

        string ContentType { get; }

        string FileName { get; }

        bool HasFile { get; }
    }

    /// <summary>The update counterpart of <see cref="AddFileCommand"/>: replace the image, keep the rest.</summary>
    public abstract class UpdateFileCommand : BaseUpdateCommand, IFileCommand
    {
        [JsonIgnore]
        public Stream Stream { get; set; } = Stream.Null;

        [JsonIgnore]
        public string ContentType { get; set; } = string.Empty;

        public string FileName { get; set; } = string.Empty;

        public bool HasFile => Stream != Stream.Null && !string.IsNullOrWhiteSpace(FileName);
    }
}
