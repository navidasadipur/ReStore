using Refit;
using ReStore.Api.Controllers;

namespace ReStore.Api.Services;

public interface IMcpChatClient
{
    [Post("/api/chat")]
    Task<McpChatReply> ChatAsync([Body] AiAssistantMessage request, CancellationToken cancellationToken);
}

public sealed record McpChatReply(string Answer);
