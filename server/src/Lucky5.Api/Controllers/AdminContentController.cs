namespace Lucky5.Api.Controllers;

using Lucky5.Api.Models;
using Lucky5.Application.Contracts;
using Lucky5.Application.Dtos;
using Lucky5.Application.Requests;
using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("api/admin/content")]
public sealed class AdminContentController(
    IAdminContentService contentService,
    IAdminAuditService auditService) : ControllerBase
{
    [HttpGet("offers")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<AdminOfferDto>>>> ListOffers(CancellationToken cancellationToken)
    {
        HttpContext.RequireAdminRole();
        var offers = await contentService.ListOffersAsync(cancellationToken);
        return Ok(ApiResponse<IReadOnlyList<AdminOfferDto>>.Ok(offers, traceId: HttpContext.TraceIdentifier));
    }

    [HttpGet("offers/{id:int}")]
    public async Task<ActionResult<ApiResponse<AdminOfferDto>>> GetOffer(int id, CancellationToken cancellationToken)
    {
        HttpContext.RequireAdminRole();
        var offer = await contentService.GetOfferAsync(id, cancellationToken);
        return Ok(ApiResponse<AdminOfferDto>.Ok(offer, traceId: HttpContext.TraceIdentifier));
    }

    [HttpPost("offers")]
    public async Task<ActionResult<ApiResponse<AdminOfferDto>>> CreateOffer([FromBody] CreateOfferRequest request, CancellationToken cancellationToken)
    {
        var adminId = HttpContext.RequireAdminRole();
        if (string.IsNullOrWhiteSpace(request.Title))
            return BadRequest(ApiResponse<AdminOfferDto>.Fail("Title is required", traceId: HttpContext.TraceIdentifier));

        var offer = await contentService.CreateOfferAsync(adminId, request, cancellationToken);
        await auditService.AppendAsync(new AdminAuditWriteDto(
            adminId,
            "admin",
            "content.offer.create",
            "offer",
            offer.Id.ToString(),
            Outcome: "succeeded",
            Reason: $"Created offer {offer.Title}",
            Metadata: new Dictionary<string, string> { { "title", offer.Title }, { "bonus_amount", offer.BonusAmount.ToString() } }), cancellationToken);

        return Ok(ApiResponse<AdminOfferDto>.Ok(offer, traceId: HttpContext.TraceIdentifier));
    }

    [HttpPut("offers/{id:int}")]
    public async Task<ActionResult<ApiResponse<AdminOfferDto>>> UpdateOffer(int id, [FromBody] UpdateOfferRequest request, CancellationToken cancellationToken)
    {
        var adminId = HttpContext.RequireAdminRole();
        if (string.IsNullOrWhiteSpace(request.Title))
            return BadRequest(ApiResponse<AdminOfferDto>.Fail("Title is required", traceId: HttpContext.TraceIdentifier));

        var offer = await contentService.UpdateOfferAsync(adminId, id, request, cancellationToken);
        await auditService.AppendAsync(new AdminAuditWriteDto(
            adminId,
            "admin",
            "content.offer.update",
            "offer",
            id.ToString(),
            Outcome: "succeeded",
            Reason: $"Updated offer {offer.Title}",
            Metadata: new Dictionary<string, string> { { "title", offer.Title }, { "bonus_amount", offer.BonusAmount.ToString() } }), cancellationToken);

        return Ok(ApiResponse<AdminOfferDto>.Ok(offer, traceId: HttpContext.TraceIdentifier));
    }

    [HttpDelete("offers/{id:int}")]
    public async Task<ActionResult<ApiResponse<object>>> DeleteOffer(int id, CancellationToken cancellationToken)
    {
        var adminId = HttpContext.RequireAdminRole();
        await contentService.DeleteOfferAsync(adminId, id, cancellationToken);
        await auditService.AppendAsync(new AdminAuditWriteDto(
            adminId,
            "admin",
            "content.offer.delete",
            "offer",
            id.ToString(),
            Outcome: "succeeded",
            Reason: $"Deleted offer {id}"), cancellationToken);

        return Ok(ApiResponse<object>.Ok(new { deleted = true }, traceId: HttpContext.TraceIdentifier));
    }

    [HttpGet("terms")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<AdminTermsDto>>>> ListTerms(CancellationToken cancellationToken)
    {
        HttpContext.RequireAdminRole();
        var terms = await contentService.ListTermsAsync(cancellationToken);
        return Ok(ApiResponse<IReadOnlyList<AdminTermsDto>>.Ok(terms, traceId: HttpContext.TraceIdentifier));
    }

    [HttpPut("terms")]
    public async Task<ActionResult<ApiResponse<AdminTermsDto>>> UpsertTerms([FromBody] UpsertTermsRequest request, CancellationToken cancellationToken)
    {
        var adminId = HttpContext.RequireAdminRole();
        if (string.IsNullOrWhiteSpace(request.Version))
            return BadRequest(ApiResponse<AdminTermsDto>.Fail("Version is required", traceId: HttpContext.TraceIdentifier));

        var terms = await contentService.UpsertTermsAsync(adminId, request, cancellationToken);
        await auditService.AppendAsync(new AdminAuditWriteDto(
            adminId,
            "admin",
            "content.terms.upsert",
            "terms",
            terms.Version,
            Outcome: "succeeded",
            Reason: $"Upserted terms {terms.Version}",
            Metadata: new Dictionary<string, string> { { "version", terms.Version } }), cancellationToken);

        return Ok(ApiResponse<AdminTermsDto>.Ok(terms, traceId: HttpContext.TraceIdentifier));
    }

    [HttpDelete("terms")]
    public async Task<ActionResult<ApiResponse<object>>> DeleteTerms(CancellationToken cancellationToken)
    {
        var adminId = HttpContext.RequireAdminRole();
        var currentTerms = await contentService.ListTermsAsync(cancellationToken);
        var version = currentTerms.Count > 0 ? currentTerms[0].Version : "1.0.0";
        await contentService.DeleteTermsAsync(adminId, version, cancellationToken);
        await auditService.AppendAsync(new AdminAuditWriteDto(
            adminId,
            "admin",
            "content.terms.delete",
            "terms",
            version,
            Outcome: "succeeded",
            Reason: $"Deleted terms {version}"), cancellationToken);

        return Ok(ApiResponse<object>.Ok(new { deleted = true }, traceId: HttpContext.TraceIdentifier));
    }

    [HttpGet("app-settings")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<AdminAppSettingDto>>>> ListAppSettings(CancellationToken cancellationToken)
    {
        HttpContext.RequireAdminRole();
        var settings = await contentService.ListAppSettingsAsync(cancellationToken);
        return Ok(ApiResponse<IReadOnlyList<AdminAppSettingDto>>.Ok(settings, traceId: HttpContext.TraceIdentifier));
    }

    [HttpPost("app-settings")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<AdminAppSettingDto>>>> UpsertAppSetting([FromBody] UpsertAppSettingRequest request, CancellationToken cancellationToken)
    {
        var adminId = HttpContext.RequireAdminRole();
        if (string.IsNullOrWhiteSpace(request.Key))
            return BadRequest(ApiResponse<IReadOnlyList<AdminAppSettingDto>>.Fail("Key is required", traceId: HttpContext.TraceIdentifier));

        await contentService.UpsertAppSettingAsync(adminId, request, cancellationToken);
        await auditService.AppendAsync(new AdminAuditWriteDto(
            adminId,
            "admin",
            "content.app_setting.upsert",
            "app_setting",
            request.Key,
            Outcome: "succeeded",
            Reason: $"Upserted app setting {request.Key}",
            Metadata: new Dictionary<string, string> { { "key", request.Key } }), cancellationToken);

        var settings = await contentService.ListAppSettingsAsync(cancellationToken);
        return Ok(ApiResponse<IReadOnlyList<AdminAppSettingDto>>.Ok(settings, traceId: HttpContext.TraceIdentifier));
    }

    [HttpDelete("app-settings/{key}")]
    public async Task<ActionResult<ApiResponse<object>>> DeleteAppSetting(string key, CancellationToken cancellationToken)
    {
        var adminId = HttpContext.RequireAdminRole();
        await contentService.DeleteAppSettingAsync(adminId, key, cancellationToken);
        await auditService.AppendAsync(new AdminAuditWriteDto(
            adminId,
            "admin",
            "content.app_setting.delete",
            "app_setting",
            key,
            Outcome: "succeeded",
            Reason: $"Deleted app setting {key}"), cancellationToken);

        return Ok(ApiResponse<object>.Ok(new { deleted = true }, traceId: HttpContext.TraceIdentifier));
    }
}
