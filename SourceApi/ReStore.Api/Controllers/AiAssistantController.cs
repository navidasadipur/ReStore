using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Refit;
using ReStore.Api.Services;

namespace ReStore.Api.Controllers;

public class AiAssistantController(IMcpChatClient client) : BaseApiController
{
    [HttpPost("chat")]
    [HttpPost("/aiAssistant/chat")]
    public async Task<ActionResult<AiAssistantMessage>> Chat([FromBody] AiAssistantMessage request, CancellationToken cancellationToken)
    {
        try
        {
            var reply = await client.ChatAsync(request, cancellationToken);
            if (string.IsNullOrWhiteSpace(reply?.Answer)) return StatusCode(StatusCodes.Status502BadGateway);
            return new AiAssistantMessage(reply.Answer);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return StatusCode(StatusCodes.Status504GatewayTimeout);
        }
        catch (ApiException error)
        {
            return StatusCode(error.StatusCode == System.Net.HttpStatusCode.GatewayTimeout
                ? StatusCodes.Status504GatewayTimeout : StatusCodes.Status502BadGateway);
        }
        catch (HttpRequestException)
        {
            return StatusCode(StatusCodes.Status502BadGateway);
        }
    }
}

public sealed record AiAssistantMessage([Required] string Message);
