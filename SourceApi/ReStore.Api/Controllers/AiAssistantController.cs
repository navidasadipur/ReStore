using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;

namespace ReStore.Api.Controllers;

public class AiAssistantController : BaseApiController
{
    [HttpPost("chat")]
    [HttpPost("/aiAssistant/chat")]
    public AiAssistantMessage Chat([FromBody] AiAssistantMessage request) => request;
}

public sealed record AiAssistantMessage([Required] string Message);
