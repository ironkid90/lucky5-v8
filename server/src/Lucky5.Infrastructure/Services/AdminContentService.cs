namespace Lucky5.Infrastructure.Services;

using Lucky5.Application.Contracts;
using Lucky5.Application.Dtos;
using Lucky5.Application.Requests;
using Lucky5.Domain.Entities;

public sealed class AdminContentService(InMemoryDataStore store) : IAdminContentService
{
    public Task<IReadOnlyList<AdminOfferDto>> ListOffersAsync(CancellationToken cancellationToken)
    {
        var offers = store.Offers
            .OrderBy(o => o.Id)
            .Select(o => new AdminOfferDto(o.Id, o.Title, o.Description, o.BonusAmount))
            .ToArray();
        return Task.FromResult<IReadOnlyList<AdminOfferDto>>(offers);
    }

    public Task<AdminOfferDto> GetOfferAsync(int id, CancellationToken cancellationToken)
    {
        var offer = store.Offers.FirstOrDefault(o => o.Id == id) ?? throw new KeyNotFoundException("Offer not found");
        return Task.FromResult(new AdminOfferDto(offer.Id, offer.Title, offer.Description, offer.BonusAmount));
    }

    public Task<AdminOfferDto> CreateOfferAsync(Guid adminId, CreateOfferRequest req, CancellationToken cancellationToken)
    {
        var offer = new Offer { Title = req.Title, Description = req.Description, BonusAmount = req.BonusAmount };
        var created = store.CreateOfferAsync(offer).GetAwaiter().GetResult();
        return Task.FromResult(new AdminOfferDto(created.Id, created.Title, created.Description, created.BonusAmount));
    }

    public Task<AdminOfferDto> UpdateOfferAsync(Guid adminId, int id, UpdateOfferRequest req, CancellationToken cancellationToken)
    {
        var existing = store.Offers.FirstOrDefault(o => o.Id == id) ?? throw new KeyNotFoundException("Offer not found");
        var updated = new Offer { Id = id, Title = req.Title, Description = req.Description, BonusAmount = req.BonusAmount };
        store.UpdateOfferAsync(updated).GetAwaiter().GetResult();
        return Task.FromResult(new AdminOfferDto(id, req.Title, req.Description, req.BonusAmount));
    }

    public Task DeleteOfferAsync(Guid adminId, int id, CancellationToken cancellationToken)
    {
        store.DeleteOfferAsync(id).GetAwaiter().GetResult();
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<AdminTermsDto>> ListTermsAsync(CancellationToken cancellationToken)
    {
        var terms = store.GetTermsAsync().GetAwaiter().GetResult() ?? new TermsDocument();
        return Task.FromResult<IReadOnlyList<AdminTermsDto>>(new[] { new AdminTermsDto(terms.Version, terms.BodyMarkdown, terms.UpdatedUtc) });
    }

    public Task<AdminTermsDto> UpsertTermsAsync(Guid adminId, UpsertTermsRequest req, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        store.UpdateTermsAsync(new TermsDocument { Version = req.Version, BodyMarkdown = req.BodyMarkdown, UpdatedUtc = now }).GetAwaiter().GetResult();
        return Task.FromResult(new AdminTermsDto(req.Version, req.BodyMarkdown, now));
    }

    public Task DeleteTermsAsync(Guid adminId, string version, CancellationToken cancellationToken)
    {
        store.DeleteTermsAsync().GetAwaiter().GetResult();
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<AdminAppSettingDto>> ListAppSettingsAsync(CancellationToken cancellationToken)
    {
        var settings = store.GetAppSettingsAsync().GetAwaiter().GetResult();
        var result = settings
            .Select(kvp => new AdminAppSettingDto(kvp.Key, kvp.Value))
            .ToArray();
        return Task.FromResult<IReadOnlyList<AdminAppSettingDto>>(result);
    }

    public Task<AdminAppSettingDto> UpsertAppSettingAsync(Guid adminId, UpsertAppSettingRequest req, CancellationToken cancellationToken)
    {
        store.UpdateAppSettingAsync(req.Key, req.Value).GetAwaiter().GetResult();
        return Task.FromResult(new AdminAppSettingDto(req.Key, req.Value));
    }

    public Task DeleteAppSettingAsync(Guid adminId, string key, CancellationToken cancellationToken)
    {
        store.DeleteAppSettingAsync(key).GetAwaiter().GetResult();
        return Task.CompletedTask;
    }
}
