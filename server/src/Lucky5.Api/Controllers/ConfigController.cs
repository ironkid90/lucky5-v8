using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;

namespace Lucky5.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ConfigController(IConfiguration configuration) : ControllerBase
{
    /// <summary>
    /// Returns public Firebase web config for the frontend SDK.
    /// Only exposes values that are safe to be public (no service account credentials).
    /// </summary>
    [HttpGet("firebase")]
    public IActionResult GetFirebaseConfig()
    {
        var config = new
        {
            apiKey = configuration["Firebase:WebApiKey"] ?? "",
            projectId = configuration["Firebase:ProjectId"] ?? "lucky5-v7",
            messagingSenderId = configuration["Firebase:MessagingSenderId"] ?? "",
            appId = configuration["Firebase:AppId"] ?? "",
            vapidKey = configuration["Firebase:VapidKey"] ?? ""
        };

        // Only return if actually configured
        if (string.IsNullOrWhiteSpace(config.apiKey))
            return Ok(new { configured = false });

        return Ok(new { configured = true, config });
    }

    /// <summary>
    /// Returns dynamic game rules and server limits to align the client.
    /// </summary>
    [HttpGet("rules")]
    public IActionResult GetGameRules()
    {
        var rules = new
        {
            maxStake = configuration.GetValue<int>("Game:MaxStake", 50000),
            minStake = configuration.GetValue<int>("Game:MinStake", 100),
            animationPaceMs = configuration.GetValue<int>("Game:AnimationPaceMs", 150),
            doubleUpMaxConsecutive = configuration.GetValue<int>("Game:DoubleUpMaxConsecutive", 5),
            rtpTarget = configuration.GetValue<string>("Game:RtpTarget", "96.5")
        };

        return Ok(new { configured = true, rules });
    }
}
