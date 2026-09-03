using System.Text.Json;
using API.RequestHelpers;

namespace API.Extensions
{
    public static class HttpExtensions
    {
        public static void AddPaginationHeader(this HttpResponse response, MetaData metaData)
        {
            var options = new JsonSerializerOptions{PropertyNamingPolicy = JsonNamingPolicy.CamelCase};

            // Assign rather than Add: Add throws if the header is already present,
            // which the CORS middleware can do for Access-Control-Expose-Headers.
            response.Headers["Pagination"] = JsonSerializer.Serialize(metaData, options);
            response.Headers["Access-Control-Expose-Headers"] = "Pagination";
        }
    }
}