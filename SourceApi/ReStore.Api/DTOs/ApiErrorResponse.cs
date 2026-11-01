using System.Text.Json.Serialization;
using ReStore.Application.Common;

namespace ReStore.Api.DTOs;

public sealed class ApiErrorResponse : BaseReturn
{
    public ApiErrorResponse(string error, string errorCode, IDictionary<string, string[]>? errors = null)
        : base((error, errorCode)) => Errors = errors;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IDictionary<string, string[]>? Errors { get; }
}
