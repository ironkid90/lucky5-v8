namespace Lucky5.Infrastructure.Services;

using Lucky5.Application.Contracts;
using Lucky5.Application.Dtos;
using Lucky5.Application.Requests;
using Lucky5.Domain.Entities;

public sealed class GeneralService(InMemoryDataStore store) : IGeneralService
{
    public Task<IReadOnlyDictionary<string, string>> GetAppSettingsAsync(CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyDictionary<string, string>>(store.AppSettings);

    public Task<IReadOnlyList<OfferDto>> ListOffersAsync(CancellationToken cancellationToken)
    {
        var offers = store.Offers
            .OrderBy(o => o.Id)
            .Select(o => new OfferDto(o.Id, o.Title, o.Description, o.BonusAmount))
            .ToArray();
        return Task.FromResult<IReadOnlyList<OfferDto>>(offers);
    }

    public Task<OfferDto> CreateOfferAsync(string title, string description, decimal bonusAmount, CancellationToken cancellationToken)
    {
        var offer = new Offer { Title = title, Description = description, BonusAmount = bonusAmount };
        var created = store.CreateOfferAsync(offer).GetAwaiter().GetResult();
        return Task.FromResult(new OfferDto(created.Id, created.Title, created.Description, created.BonusAmount));
    }

    public Task<OfferDto> UpdateOfferAsync(int id, string title, string description, decimal bonusAmount, CancellationToken cancellationToken)
    {
        var existing = store.GetOfferAsync(id).GetAwaiter().GetResult();
        if (existing == null) throw new KeyNotFoundException("Offer not found");
        var updated = new Offer { Id = id, Title = title, Description = description, BonusAmount = bonusAmount };
        store.UpdateOfferAsync(updated).GetAwaiter().GetResult();
        return Task.FromResult(new OfferDto(id, title, description, bonusAmount));
    }

    public Task DeleteOfferAsync(int id, CancellationToken cancellationToken)
    {
        store.DeleteOfferAsync(id).GetAwaiter().GetResult();
        return Task.CompletedTask;
    }

    public Task<IReadOnlyDictionary<string, string>> GetContactInfoAsync(CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyDictionary<string, string>>(store.ContactInfo);

    public Task<IReadOnlyList<ContactTypeDto>> GetContactTypesAsync(CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<ContactTypeDto>>(store.ContactTypes.Select(x => new ContactTypeDto(x.Id, x.Name)).ToArray());

    public Task SubmitContactReportAsync(Guid userId, ContactReportRequest request, CancellationToken cancellationToken)
    {
        store.ContactReports.Add(new ContactReport
        {
            UserId = userId,
            ContactTypeId = request.ContactTypeId,
            Subject = request.Subject,
            Message = request.Message,
            CreatedUtc = DateTime.UtcNow
        });

        return Task.CompletedTask;
    }

    public Task<TermsResponseDto> GetTermsAsync(CancellationToken cancellationToken)
        => Task.FromResult(new TermsResponseDto(store.Terms.Version, store.Terms.BodyMarkdown, store.Terms.UpdatedUtc));

    public Task<TermsResponseDto> UpsertTermsAsync(string version, string bodyMarkdown, CancellationToken cancellationToken)
    {
        store.UpdateTermsAsync(new TermsDocument { Version = version, BodyMarkdown = bodyMarkdown, UpdatedUtc = DateTime.UtcNow }).GetAwaiter().GetResult();
        return Task.FromResult(new TermsResponseDto(version, bodyMarkdown, DateTime.UtcNow));
    }

    public Task DeleteTermsAsync(CancellationToken cancellationToken)
    {
        store.DeleteTermsAsync().GetAwaiter().GetResult();
        return Task.CompletedTask;
    }

    public Task<IReadOnlyDictionary<string, string>> UpsertAppSettingAsync(string key, string value, CancellationToken cancellationToken)
    {
        store.UpdateAppSettingAsync(key, value).GetAwaiter().GetResult();
        return Task.FromResult<IReadOnlyDictionary<string, string>>(store.AppSettings);
    }

    public Task DeleteAppSettingAsync(string key, CancellationToken cancellationToken)
    {
        store.DeleteAppSettingAsync(key).GetAwaiter().GetResult();
        return Task.CompletedTask;
    }
}
