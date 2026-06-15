using Lucky5.Api.Models;
using Lucky5.Application.Contracts;
using Lucky5.Application.Dtos;
using Microsoft.AspNetCore.Mvc;

namespace Lucky5.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AgentController(IAgentService agentService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<AgentDto>>>> GetAgents(CancellationToken cancellationToken)
    {
        HttpContext.RequireAdminRole();
        var agents = await agentService.GetAgentsAsync(cancellationToken);
        return Ok(ApiResponse<IReadOnlyList<AgentDto>>.Ok(agents, traceId: HttpContext.TraceIdentifier));
    }

    [HttpPost]
    public async Task<ActionResult<ApiResponse<AgentDto>>> CreateAgent(
        [FromBody] CreateAgentRequest request, CancellationToken cancellationToken)
    {
        HttpContext.RequireAdminRole();
        var agent = await agentService.CreateAgentAsync(request, cancellationToken);
        return Ok(ApiResponse<AgentDto>.Ok(agent, "Agent created", HttpContext.TraceIdentifier));
    }

    [HttpPost("{agentId:int}/load-credit")]
    public async Task<ActionResult<ApiResponse<AgentDto>>> LoadCredit(
        int agentId, [FromBody] LoadCreditRequest request, CancellationToken cancellationToken)
    {
        HttpContext.RequireAdminRole();
        var agent = await agentService.LoadCreditAsync(agentId, request.Amount, cancellationToken);
        return Ok(ApiResponse<AgentDto>.Ok(agent, "Credit loaded", HttpContext.TraceIdentifier));
    }

    [HttpPost("{agentId:int}/assign-user/{userId:guid}")]
    public async Task<ActionResult<ApiResponse<object>>> AssignUser(
        int agentId, Guid userId, CancellationToken cancellationToken)
    {
        HttpContext.RequireAdminRole();
        await agentService.AssignUserToAgentAsync(userId, agentId, cancellationToken);
        return Ok(ApiResponse<object>.Ok(new { assigned = true }, traceId: HttpContext.TraceIdentifier));
    }

    public sealed record LoadCreditRequest(decimal Amount);
}
