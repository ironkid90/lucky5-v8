namespace Lucky5.Api.Controllers;

using Lucky5.Api.Models;
using Lucky5.Application.Contracts;
using Lucky5.Application.Dtos;
using Lucky5.Application.Requests;
using Lucky5.Domain.Entities;
using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("api/[controller]")]
public sealed class AdminController(
    IAdminService adminService,
    IAdminAuditService auditService,
    ICabinetDeviceAuthService cabinetDeviceAuthService) : ControllerBase
{
    [HttpGet("dashboard")]
    public async Task<ActionResult<ApiResponse<AdminDashboardDto>>> GetDashboard(CancellationToken cancellationToken)
    {
        HttpContext.RequireAdminRole();
        var dashboard = await adminService.GetDashboardAsync(cancellationToken);
        return Ok(ApiResponse<AdminDashboardDto>.Ok(dashboard, traceId: HttpContext.TraceIdentifier));
    }

    [HttpGet("audit")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<AdminAuditEntryDto>>>> ListAudit([FromQuery] int take, CancellationToken cancellationToken)
    {
        HttpContext.RequireAdminRole();
        var records = await auditService.ListRecentAsync(take <= 0 ? 100 : take, cancellationToken);
        return Ok(ApiResponse<IReadOnlyList<AdminAuditEntryDto>>.Ok(records, traceId: HttpContext.TraceIdentifier));
    }

    [HttpGet("users")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<AdminUserDto>>>> ListUsers(CancellationToken cancellationToken)
    {
        HttpContext.RequireAdminRole();
        var users = await adminService.ListUsersAsync(cancellationToken);
        return Ok(ApiResponse<IReadOnlyList<AdminUserDto>>.Ok(users, traceId: HttpContext.TraceIdentifier));
    }

    [HttpGet("users/search")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<AdminUserDto>>>> SearchUsers([FromQuery] string q, CancellationToken cancellationToken)
    {
        HttpContext.RequireAdminRole();
        if (string.IsNullOrWhiteSpace(q)) return BadRequest(ApiResponse<IReadOnlyList<AdminUserDto>>.Fail("Query parameter 'q' is required", traceId: HttpContext.TraceIdentifier));
        var users = await adminService.SearchUsersAsync(q, cancellationToken);
        return Ok(ApiResponse<IReadOnlyList<AdminUserDto>>.Ok(users, traceId: HttpContext.TraceIdentifier));
    }

    [HttpGet("users/{userId:guid}")]
    public async Task<ActionResult<ApiResponse<AdminUserDto>>> GetUser(Guid userId, CancellationToken cancellationToken)
    {
        HttpContext.RequireAdminRole();
        var user = await adminService.GetUserAsync(userId, cancellationToken);
        return Ok(ApiResponse<AdminUserDto>.Ok(user, traceId: HttpContext.TraceIdentifier));
    }

    [HttpGet("users/{userId:guid}/detail")]
    public async Task<ActionResult<ApiResponse<AdminUserDetailDto>>> GetUserDetail(Guid userId, CancellationToken cancellationToken)
    {
        HttpContext.RequireAdminRole();
        var user = await adminService.GetUserDetailAsync(userId, cancellationToken);
        return Ok(ApiResponse<AdminUserDetailDto>.Ok(user, traceId: HttpContext.TraceIdentifier));
    }

    [HttpPost("users/credit")]
    public async Task<ActionResult<ApiResponse<WalletLedgerEntryDto>>> Credit([FromBody] AdminCreditRequest request, CancellationToken cancellationToken)
    {
        var adminId = HttpContext.RequireAdminRole();
        var row = await adminService.AdminCreditAsync(adminId, request, cancellationToken);
        await auditService.AppendAsync(new AdminAuditWriteDto(
            adminId,
            "admin",
            request.Amount > 0 ? "user.credit" : "user.debit",
            "user",
            request.TargetUserId.ToString("N"),
            Outcome: "succeeded",
            Reason: request.Reason,
            Metadata: new Dictionary<string, string>
            {
                ["amount"] = request.Amount.ToString("0.##"),
                ["ledger_entry_id"] = row.Id.ToString("N")
            }), cancellationToken);
        return Ok(ApiResponse<WalletLedgerEntryDto>.Ok(row, traceId: HttpContext.TraceIdentifier));
    }

    [HttpGet("cabinet-devices")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<CabinetDeviceDto>>>> ListCabinetDevices(CancellationToken cancellationToken)
    {
        HttpContext.RequireAdminRole();
        var devices = await cabinetDeviceAuthService.ListDevicesAsync(cancellationToken);
        return Ok(ApiResponse<IReadOnlyList<CabinetDeviceDto>>.Ok(devices, traceId: HttpContext.TraceIdentifier));
    }

    [HttpGet("cabinet-devices/{deviceId:guid}")]
    public async Task<ActionResult<ApiResponse<CabinetDeviceDto>>> GetCabinetDevice(Guid deviceId, CancellationToken cancellationToken)
    {
        HttpContext.RequireAdminRole();
        var device = await cabinetDeviceAuthService.GetDeviceAsync(deviceId, cancellationToken);
        return Ok(ApiResponse<CabinetDeviceDto>.Ok(device, traceId: HttpContext.TraceIdentifier));
    }

    [HttpPost("cabinet-devices")]
    public async Task<ActionResult<ApiResponse<CabinetDeviceProvisioningDto>>> ProvisionCabinetDevice([FromBody] ProvisionCabinetDeviceRequest request, CancellationToken cancellationToken)
    {
        var adminId = HttpContext.RequireAdminRole();
        var result = await cabinetDeviceAuthService.ProvisionDeviceAsync(adminId, request, cancellationToken);
        return Ok(ApiResponse<CabinetDeviceProvisioningDto>.Ok(result, "Cabinet device provisioned; store the one-time secret securely", HttpContext.TraceIdentifier));
    }

    [HttpPost("cabinet-devices/{deviceId:guid}/revoke")]
    public async Task<ActionResult<ApiResponse<CabinetDeviceDto>>> RevokeCabinetDevice(Guid deviceId, [FromBody] RevokeCabinetDeviceRequest request, CancellationToken cancellationToken)
    {
        var adminId = HttpContext.RequireAdminRole();
        var device = await cabinetDeviceAuthService.RevokeDeviceAsync(adminId, deviceId, request, cancellationToken);
        return Ok(ApiResponse<CabinetDeviceDto>.Ok(device, traceId: HttpContext.TraceIdentifier));
    }

    [HttpGet("machines")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<AdminMachineDto>>>> ListMachines(CancellationToken cancellationToken)
    {
        HttpContext.RequireAdminRole();
        var machines = await adminService.ListMachinesAsync(cancellationToken);
        return Ok(ApiResponse<IReadOnlyList<AdminMachineDto>>.Ok(machines, traceId: HttpContext.TraceIdentifier));
    }

    [HttpGet("machines/{machineId:int}")]
    public async Task<ActionResult<ApiResponse<AdminMachineDto>>> GetMachine(int machineId, CancellationToken cancellationToken)
    {
        HttpContext.RequireAdminRole();
        var machine = await adminService.GetMachineAsync(machineId, cancellationToken);
        return Ok(ApiResponse<AdminMachineDto>.Ok(machine, traceId: HttpContext.TraceIdentifier));
    }

    [HttpGet("machines/{machineId:int}/detail")]
    public async Task<ActionResult<ApiResponse<AdminMachineDetailDto>>> GetMachineDetail(int machineId, CancellationToken cancellationToken)
    {
        HttpContext.RequireAdminRole();
        var machine = await adminService.GetMachineDetailAsync(machineId, cancellationToken);
        return Ok(ApiResponse<AdminMachineDetailDto>.Ok(machine, traceId: HttpContext.TraceIdentifier));
    }

    [HttpPost("machines/{machineId:int}/reset")]
    public async Task<ActionResult<ApiResponse<AdminMachineDto>>> ResetMachine(int machineId, CancellationToken cancellationToken)
    {
        var adminId = HttpContext.RequireAdminRole();
        var machine = await adminService.ResetMachineAsync(adminId, machineId, cancellationToken);
        await auditService.AppendAsync(new AdminAuditWriteDto(
            adminId,
            "admin",
            "machine.reset",
            "machine",
            machineId.ToString(),
            MachineId: machineId), cancellationToken);
        return Ok(ApiResponse<AdminMachineDto>.Ok(machine, traceId: HttpContext.TraceIdentifier));
    }

    [HttpPost("machines/{machineId:int}/door-state")]
    public async Task<ActionResult<ApiResponse<DoorState>>> SetDoorState(int machineId, [FromBody] SetDoorStateRequest request, CancellationToken cancellationToken)
    {
        var adminId = HttpContext.RequireAdminRole();
        var doorState = await adminService.SetDoorStateAsync(machineId, request.DoorState, cancellationToken);
        await auditService.AppendAsync(new AdminAuditWriteDto(
            adminId,
            "admin",
            "machine.door_state.set",
            "machine",
            machineId.ToString(),
            MachineId: machineId,
            Metadata: new Dictionary<string, string> { ["door_state"] = doorState.ToString() }), cancellationToken);
        return Ok(ApiResponse<DoorState>.Ok(doorState, traceId: HttpContext.TraceIdentifier));
    }

    [HttpPost("users/recharge-bonus")]
    public async Task<ActionResult<ApiResponse<WalletLedgerEntryDto>>> RechargeBonus([FromBody] RechargeBonusRequest request, CancellationToken cancellationToken)
    {
        var adminId = HttpContext.RequireAdminRole();
        var row = await adminService.RechargeBonusAsync(request.UserId, request.RechargeAmount, cancellationToken);
        await auditService.AppendAsync(new AdminAuditWriteDto(
            adminId,
            "admin",
            "user.recharge_bonus",
            "user",
            request.UserId.ToString("N"),
            Metadata: new Dictionary<string, string>
            {
                ["recharge_amount"] = request.RechargeAmount.ToString("0.##"),
                ["ledger_entry_id"] = row.Id.ToString("N")
            }), cancellationToken);
        return Ok(ApiResponse<WalletLedgerEntryDto>.Ok(row, traceId: HttpContext.TraceIdentifier));
    }
}
