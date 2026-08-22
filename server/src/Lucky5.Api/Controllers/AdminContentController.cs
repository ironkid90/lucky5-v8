namespace Lucky5.Api.Controllers;

using Lucky5.Api.Models;
using Lucky5.Application.Contracts;
using Lucky5.Application.Dtos;
using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("api/admin/content")]
public sealed class AdminContentController(IGeneralService generalService) : ControllerBase
{
    [HttpGet("offers")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<OfferDto>>>> ListOffers(CancellationToken cancellationToken)
    {
        HttpContext.RequireAdminRole();
        var offers = await generalService.ListOffersAsync(cancellationToken);
        return Ok(ApiResponse<IReadOnlyList<OfferDto>>.Ok(offers, traceId: HttpContext.TraceIdentifier));
    }

    [HttpPost("offers")]
    public async Task<ActionResult<ApiResponse<OfferDto>>> CreateOffer([FromBody] CreateOfferRequest request, CancellationToken cancellationToken)
    {
        HttpContext.RequireAdminRole();
        if (string.IsNullOrWhiteSpace(request.Title))
            return BadRequest(ApiResponse<OfferDto>.Fail("Title is required", traceId: HttpContext.TraceIdentifier));

        var offer = await generalService.CreateOfferAsync(request.Title, request.Description, request.BonusAmount, cancellationToken);
        return Ok(ApiResponse<OfferDto>.Ok(offer, traceId: HttpContext.TraceIdentifier));
    }

    [HttpPut("offers/{id:int}")]
    public async Task<ActionResult<ApiResponse<OfferDto>>> UpdateOffer(int id, [FromBody] UpdateOfferRequest request, CancellationToken cancellationToken)
    {
        HttpContext.RequireAdminRole();
        if (string.IsNullOrWhiteSpace(request.Title))
            return BadRequest(ApiResponse<OfferDto>.Fail("Title is required", traceId: HttpContext.TraceIdentifier));

        var offer = await generalService.UpdateOfferAsync(id, request.Title, request.Description, request.BonusAmount, cancellationToken);
        return Ok(ApiResponse<OfferDto>.Ok(offer, traceId: HttpContext.TraceIdentifier));
    }

    [HttpDelete("offers/{id:int}")]
    public async Task<ActionResult<ApiResponse<object>>> DeleteOffer(int id, CancellationToken cancellationToken)
    {
        HttpContext.RequireAdminRole();
        await generalService.DeleteOfferAsync(id, cancellationToken);
        return Ok(ApiResponse<object>.Ok(new { deleted = true }, traceId: HttpContext.TraceIdentifier));
    }

    [HttpGet("terms")]
    public async Task<ActionResult<ApiResponse<TermsResponseDto>>> GetTerms(CancellationToken cancellationToken)
    {
        HttpContext.RequireAdminRole();
        var terms = await generalService.GetTermsAsync(cancellationToken);
        return Ok(ApiResponse<TermsResponseDto>.Ok(terms, traceId: HttpContext.TraceIdentifier));
    }

    [HttpPut("terms")]
    public async Task<ActionResult<ApiResponse<TermsResponseDto>>> UpsertTerms([FromBody] UpsertTermsRequest request, CancellationToken cancellationToken)
    {
        HttpContext.RequireAdminRole();
        if (string.IsNullOrWhiteSpace(request.Version))
            return BadRequest(ApiResponse<TermsResponseDto>.Fail("Version is required", traceId: HttpContext.TraceIdentifier));

        var terms = await generalService.UpsertTermsAsync(request.Version, request.BodyMarkdown, cancellationToken);
        return Ok(ApiResponse<TermsResponseDto>.Ok(terms, traceId: HttpContext.TraceIdentifier));
    }

    [HttpDelete("terms")]
    public async Task<ActionResult<ApiResponse<object>>> DeleteTerms(CancellationToken cancellationToken)
    {
        HttpContext.RequireAdminRole();
        await generalService.DeleteTermsAsync(cancellationToken);
        return Ok(ApiResponse<object>.Ok(new { deleted = true }, traceId: HttpContext.TraceIdentifier));
    }

    [HttpGet("app-settings")]
    public async Task<ActionResult<ApiResponse<IReadOnlyDictionary<string, string>>>> ListAppSettings(CancellationToken cancellationToken)
    {
        HttpContext.RequireAdminRole();
        var settings = await generalService.GetAppSettingsAsync(cancellationToken);
        return Ok(ApiResponse<IReadOnlyDictionary<string, string>>.Ok(settings, traceId: HttpContext.TraceIdentifier));
    }

    [HttpPost("app-settings")]
    public async Task<ActionResult<ApiResponse<IReadOnlyDictionary<string, string>>>> UpsertAppSetting([FromBody] UpsertAppSettingRequest request, CancellationToken cancellationToken)
    {
        HttpContext.RequireAdminRole();
        if (string.IsNullOrWhiteSpace(request.Key))
            return BadRequest(ApiResponse<IReadOnlyDictionary<string, string>>.Fail("Key is required", traceId: HttpContext.TraceIdentifier));

        var settings = await generalService.UpsertAppSettingAsync(request.Key, request.Value, cancellationToken);
        return Ok(ApiResponse<IReadOnlyDictionary<string, string>>.Ok(settings, traceId: HttpContext.TraceIdentifier));
    }

    [HttpDelete("app-settings/{key}")]
    public async Task<ActionResult<ApiResponse<object>>> DeleteAppSetting(string key, CancellationToken cancellationToken)
    {
        HttpContext.RequireAdminRole();
        await generalService.DeleteAppSettingAsync(key, cancellationToken);
        return Ok(ApiResponse<object>.Ok(new { deleted = true }, traceId: HttpContext.TraceIdentifier));
    }

    public sealed record CreateOfferRequest(string Title, string Description, decimal BonusAmount);
    public sealed record UpdateOfferRequest(string Title, string Description, decimal BonusAmount);
    public sealed record UpsertTermsRequest(string Version, string BodyMarkdown);
    public sealed record UpsertAppSettingRequest(string Key, string Value);
}
