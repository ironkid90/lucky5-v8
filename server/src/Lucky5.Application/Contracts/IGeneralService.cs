namespace Lucky5.Application.Contracts;

using Lucky5.Application.Dtos;
using Lucky5.Application.Requests;

public interface IGeneralService
{
    Task<IReadOnlyDictionary<string, string>> GetAppSettingsAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<OfferDto>> ListOffersAsync(CancellationToken cancellationToken);
    Task<OfferDto> CreateOfferAsync(string title, string description, decimal bonusAmount, CancellationToken cancellationToken);
    Task<OfferDto> UpdateOfferAsync(int id, string title, string description, decimal bonusAmount, CancellationToken cancellationToken);
    Task DeleteOfferAsync(int id, CancellationToken cancellationToken);
    Task<IReadOnlyDictionary<string, string>> GetContactInfoAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<ContactTypeDto>> GetContactTypesAsync(CancellationToken cancellationToken);
    Task SubmitContactReportAsync(Guid userId, ContactReportRequest request, CancellationToken cancellationToken);
    Task<TermsResponseDto> GetTermsAsync(CancellationToken cancellationToken);
    Task<TermsResponseDto> UpsertTermsAsync(string version, string bodyMarkdown, CancellationToken cancellationToken);
    Task DeleteTermsAsync(CancellationToken cancellationToken);
    Task<IReadOnlyDictionary<string, string>> UpsertAppSettingAsync(string key, string value, CancellationToken cancellationToken);
    Task DeleteAppSettingAsync(string key, CancellationToken cancellationToken);
}
