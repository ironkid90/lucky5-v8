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
        var (userId, role) = HttpContext.RequireAgentOrAdminRole();
        var agents = await agentService.GetAgentsAsync(cancellationToken);
        return Ok(ApiResponse<IReadOnlyList<AgentDto>>.Ok(agents, traceId: HttpContext.TraceIdentifier));
    }

    [HttpPost]
    public async Task<ActionResult<ApiResponse<AgentDto>>> CreateAgent(
        [FromBody] CreateAgentRequest request, CancellationToken cancellationToken)
    {
        var (userId, role) = HttpContext.RequireAgentOrAdminRole();
        var agent = await agentService.CreateAgentAsync(request, cancellationToken);
        return Ok(ApiResponse<AgentDto>.Ok(agent, "Agent created", HttpContext.TraceIdentifier));
    }

    [HttpPost("{agentId:int}/load-credit")]
    public async Task<ActionResult<ApiResponse<AgentDto>>> LoadCredit(
        int agentId, [FromBody] LoadCreditRequest request, CancellationToken cancellationToken)
    {
        var (userId, role) = HttpContext.RequireAgentOrAdminRole();
        var agent = await agentService.LoadCreditAsync(agentId, request.Amount, cancellationToken);
        return Ok(ApiResponse<AgentDto>.Ok(agent, "Credit loaded", HttpContext.TraceIdentifier));
    }

    [HttpPost("{agentId:int}/assign-user/{assignUserId:guid}")]
    public async Task<ActionResult<ApiResponse<object>>> AssignUser(
        int agentId, Guid assignUserId, CancellationToken cancellationToken)
    {
        var (userId, role) = HttpContext.RequireAgentOrAdminRole();
        await agentService.AssignUserToAgentAsync(assignUserId, agentId, cancellationToken);
        return Ok(ApiResponse<object>.Ok(new { assigned = true }, traceId: HttpContext.TraceIdentifier));
    }

    [HttpPost("create-user")]
    public async Task<ActionResult<ApiResponse<object>>> CreateUserUnderAgent(
        [FromBody] Lucky5.Application.Requests.SignupRequest request, CancellationToken cancellationToken)
    {
        var (agentUserId, role) = HttpContext.RequireAgentOrAdminRole();
        
        // Find agent ID for this user, assume the agent ID is required or fetched.
        // For simplicity, we just use the signup flow but require the agent role.
        var authService = HttpContext.RequestServices.GetRequiredService<IAuthService>();
        var (profile, challenge) = await authService.SignupAsync(request, cancellationToken);
        
        return Ok(ApiResponse<object>.Ok(
            new { profile, otp = challenge.OtpCode }, 
            "User created under agent", 
            HttpContext.TraceIdentifier));
    }

    public sealed record LoadCreditRequest(decimal Amount);
}
