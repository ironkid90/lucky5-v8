namespace Lucky5.Tests;

using System.Text.Json;
using Lucky5.Application.Requests;
using Lucky5.Infrastructure.Services;

public static class AdminAuditAndCabinetDeviceTests
{
    public static async Task RunAsync(List<string> failures)
    {
        await CabinetDeviceProvisioningDoesNotLeakSecretsToOperatorViewsAsync(failures);
        await CabinetDeviceRevocationInvalidatesActiveTokensAndBlocksLoginAsync(failures);
        await AdminAuditRecordsAreAppendOnlyAndRedactSensitiveMetadataAsync(failures);
    }

    private static async Task CabinetDeviceProvisioningDoesNotLeakSecretsToOperatorViewsAsync(List<string> failures)
    {
        var store = new InMemoryDataStore();
        var audit = new AdminAuditService(store);
        var service = new CabinetDeviceAuthService(store, audit);
        var adminId = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var machine = store.Machines.Values.First(machine => machine.IsOpen);

        var provisioned = await service.ProvisionDeviceAsync(
            adminId,
            new ProvisionCabinetDeviceRequest(machine.Id, "Web Floor Cabinet A", "WEB-CAB-A-001"),
            CancellationToken.None);

        var secret = provisioned.DeviceSecret;
        var stored = store.CabinetDevices[provisioned.Device.DeviceId];
        var operatorDevices = await service.ListDevicesAsync(CancellationToken.None);
        var auditRecords = await audit.ListRecentAsync(20, CancellationToken.None);
        var operatorJson = JsonSerializer.Serialize(operatorDevices);
        var auditJson = JsonSerializer.Serialize(auditRecords);

        Assert(
            failures,
            "Cabinet provisioning should return a one-time opaque secret to the admin caller.",
            secret.StartsWith("l5cabsec_", StringComparison.Ordinal) && secret.Length > "l5cabsec_".Length);
        Assert(
            failures,
            "Cabinet device store should persist only a hash, never the raw provisioning secret.",
            stored.DeviceSecretHash.Length == 64 && !string.Equals(stored.DeviceSecretHash, secret, StringComparison.Ordinal));
        Assert(
            failures,
            "Safe cabinet device operator listing should not leak raw device secrets or stored secret hashes.",
            !operatorJson.Contains(secret, StringComparison.Ordinal) && !operatorJson.Contains(stored.DeviceSecretHash, StringComparison.Ordinal));
        Assert(
            failures,
            "Admin audit visibility should not leak raw cabinet device secrets or stored secret hashes.",
            !auditJson.Contains(secret, StringComparison.Ordinal) && !auditJson.Contains(stored.DeviceSecretHash, StringComparison.Ordinal));
    }

    private static async Task CabinetDeviceRevocationInvalidatesActiveTokensAndBlocksLoginAsync(List<string> failures)
    {
        var store = new InMemoryDataStore();
        var audit = new AdminAuditService(store);
        var service = new CabinetDeviceAuthService(store, audit);
        var adminId = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var machine = store.Machines.Values.First(machine => machine.IsOpen);

        var provisioned = await service.ProvisionDeviceAsync(
            adminId,
            new ProvisionCabinetDeviceRequest(machine.Id, "Web Floor Cabinet B", "WEB-CAB-B-001"),
            CancellationToken.None);
        var auth = await service.AuthenticateAsync(
            new CabinetDeviceAuthRequest(provisioned.Device.DeviceId, provisioned.DeviceSecret, "web-fw-1", "web-client-1"),
            CancellationToken.None);
        var contextBeforeRevoke = await service.ValidateAccessTokenAsync(auth.AccessToken, CancellationToken.None);

        var revoked = await service.RevokeDeviceAsync(
            adminId,
            provisioned.Device.DeviceId,
            new RevokeCabinetDeviceRequest("cabinet retired"),
            CancellationToken.None);
        var contextAfterRevoke = await service.ValidateAccessTokenAsync(auth.AccessToken, CancellationToken.None);

        var loginBlocked = false;
        try
        {
            await service.AuthenticateAsync(
                new CabinetDeviceAuthRequest(provisioned.Device.DeviceId, provisioned.DeviceSecret),
                CancellationToken.None);
        }
        catch (UnauthorizedAccessException ex) when (ex.Message.Contains("revoked", StringComparison.OrdinalIgnoreCase))
        {
            loginBlocked = true;
        }

        Assert(
            failures,
            "A valid cabinet device token should resolve to a cabinet device auth context before revocation.",
            contextBeforeRevoke is not null && contextBeforeRevoke.DeviceId == provisioned.Device.DeviceId && contextBeforeRevoke.MachineId == machine.Id);
        Assert(
            failures,
            "Revoking a cabinet device should mark it revoked and close active device sessions.",
            revoked.IsRevoked
            && revoked.ActiveSessionCount == 0
            && store.CabinetDeviceSessions.Values.Where(session => session.DeviceId == provisioned.Device.DeviceId).All(session => session.RevokedUtc is not null));
        Assert(
            failures,
            "Revoked cabinet device access tokens should stop validating immediately.",
            contextAfterRevoke is null);
        Assert(
            failures,
            "Revoked cabinet devices should not be able to authenticate again with the old device secret.",
            loginBlocked);
    }

    private static async Task AdminAuditRecordsAreAppendOnlyAndRedactSensitiveMetadataAsync(List<string> failures)
    {
        var store = new InMemoryDataStore();
        var audit = new AdminAuditService(store);
        var adminId = Guid.Parse("00000000-0000-0000-0000-000000000001");

        var first = await audit.AppendAsync(new(
            adminId,
            "admin",
            "operator.test.first",
            "cabinet_device",
            "device-a",
            Metadata: new Dictionary<string, string>
            {
                ["plain"] = "visible",
                ["access_token"] = "should-not-leak",
                ["device_secret"] = "should-not-leak-either"
            }), CancellationToken.None);
        var second = await audit.AppendAsync(new(
            adminId,
            "admin",
            "operator.test.second",
            "machine",
            "1",
            MachineId: 1), CancellationToken.None);

        if (first.Metadata is IDictionary<string, string> firstMetadata)
        {
            firstMetadata["plain"] = "tampered outside store";
        }

        var recent = await audit.ListRecentAsync(10, CancellationToken.None);
        var firstFromStore = recent.Single(record => record.Id == first.Id);
        var secondFromStore = recent.Single(record => record.Id == second.Id);

        Assert(
            failures,
            "Admin audit service should append monotonic sequence records without replacing earlier records.",
            store.AdminAuditRecords.Count == 2
            && first.SequenceNumber == 1
            && second.SequenceNumber == 2
            && firstFromStore.SequenceNumber == 1
            && secondFromStore.SequenceNumber == 2);
        Assert(
            failures,
            "Admin audit DTO mutation should not modify already-appended audit records.",
            firstFromStore.Metadata.TryGetValue("plain", out var plain) && plain == "visible");
        Assert(
            failures,
            "Admin audit metadata should redact secret-bearing keys before operator visibility.",
            firstFromStore.Metadata.TryGetValue("access_token", out var accessToken)
            && firstFromStore.Metadata.TryGetValue("device_secret", out var deviceSecret)
            && accessToken == "<redacted>"
            && deviceSecret == "<redacted>");
    }

    private static void Assert(List<string> failures, string message, bool condition)
    {
        if (!condition)
        {
            failures.Add(message);
        }
    }
}
