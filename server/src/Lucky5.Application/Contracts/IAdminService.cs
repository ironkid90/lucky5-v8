namespace Lucky5.Application.Contracts;

using Lucky5.Application.Dtos;
using Lucky5.Application.Requests;
using Lucky5.Domain.Entities;

public interface IAdminService
{
    Task<AdminDashboardDto> GetDashboardAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<AdminUserDto>> ListUsersAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<AdminUserDto>> SearchUsersAsync(string query, CancellationToken cancellationToken);
    Task<AdminUserDto> GetUserAsync(Guid userId, CancellationToken cancellationToken);
    Task<AdminUserDetailDto> GetUserDetailAsync(Guid userId, CancellationToken cancellationToken);
    Task<WalletLedgerEntryDto> AdminCreditAsync(Guid adminId, AdminCreditRequest request, CancellationToken cancellationToken);
    Task<IReadOnlyList<AdminMachineDto>> ListMachinesAsync(CancellationToken cancellationToken);
    Task<AdminMachineDto> GetMachineAsync(int machineId, CancellationToken cancellationToken);
    Task<AdminMachineDetailDto> GetMachineDetailAsync(int machineId, CancellationToken cancellationToken);
    Task<AdminMachineDto> ResetMachineAsync(Guid adminId, int machineId, CancellationToken cancellationToken);
    Task<WalletLedgerEntryDto> RechargeBonusAsync(Guid userId, decimal rechargeAmount, CancellationToken cancellationToken);
    Task<DoorState> SetDoorStateAsync(int machineId, DoorState doorState, CancellationToken cancellationToken);
}
