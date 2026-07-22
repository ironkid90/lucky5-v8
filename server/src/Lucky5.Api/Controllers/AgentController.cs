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
        
            var dataStore = HttpContext.RequestServices.GetRequiredService<Lucky5.Application.Interfaces.IDataStore>();
        
            // Find the Agent record corresponding to the logged-in agent user
            var agent = await dataStore.GetAgentByUserIdAsync(agentUserId);
            if (agent == null && role != "Admin")
            {
                return Forbid("A valid agent account is required to create subordinate users.");
            }

            var authService = HttpContext.RequestServices.GetRequiredService<IAuthService>();
            var (profile, challenge) = await authService.SignupAsync(request, cancellationToken);
        
            // Securely bind the new user profile to this agent's subtree
            if (agent != null)
            {
                var dbProfile = await dataStore.GetProfileAsync(profile.UserId);
                if (dbProfile != null)
                {
                    dbProfile.AgentId = agent.Id;
                    await dataStore.UpdateProfileAsync(dbProfile);
                
                    // Return updated profile with Agent association
                    profile = new Lucky5.Application.Dtos.MemberProfileDto(
                        dbProfile.UserId,
                        dbProfile.Username,
                        dbProfile.DisplayName,
                        dbProfile.FullName,
                        dbProfile.Email,
                        dbProfile.PhoneNumber,
                        dbProfile.DateOfBirth,
                        dbProfile.WalletBalance,
                        dbProfile.Credit,
                        dbProfile.TotalWins,
                        dbProfile.AgentId,
                        dbProfile.GeneratedID,
                        dbProfile.MinimumOut,
                        dbProfile.BonusDate,
                        dbProfile.BonusRechargeCount,
                        dbProfile.LastSeenUtc,
                        "player"
                    );
                }
            }
        
            return Ok(ApiResponse<object>.Ok(
                new { profile, otp = challenge.OtpCode }, 
                "User successfully created and registered under your agent subtree.", 
                HttpContext.TraceIdentifier));
        }

    public sealed record LoadCreditRequest(decimal Amount);
}
