namespace Lucky5.Application.Contracts;

using Lucky5.Application.Dtos;
using Lucky5.Application.Requests;

public interface IAdminContentService
{
    Task<IReadOnlyList<AdminOfferDto>> ListOffersAsync(CancellationToken cancellationToken);
    Task<AdminOfferDto> GetOfferAsync(int id, CancellationToken cancellationToken);
    Task<AdminOfferDto> CreateOfferAsync(Guid adminId, CreateOfferRequest req, CancellationToken cancellationToken);
    Task<AdminOfferDto> UpdateOfferAsync(Guid adminId, int id, UpdateOfferRequest req, CancellationToken cancellationToken);
    Task DeleteOfferAsync(Guid adminId, int id, CancellationToken cancellationToken);
    Task<IReadOnlyList<AdminTermsDto>> ListTermsAsync(CancellationToken cancellationToken);
    Task<AdminTermsDto> UpsertTermsAsync(Guid adminId, UpsertTermsRequest req, CancellationToken cancellationToken);
    Task DeleteTermsAsync(Guid adminId, string version, CancellationToken cancellationToken);
    Task<IReadOnlyList<AdminAppSettingDto>> ListAppSettingsAsync(CancellationToken cancellationToken);
    Task<AdminAppSettingDto> UpsertAppSettingAsync(Guid adminId, UpsertAppSettingRequest req, CancellationToken cancellationToken);
    Task DeleteAppSettingAsync(Guid adminId, string key, CancellationToken cancellationToken);
}
