var failures = new List<string>();

await Lucky5.Tests.ReplayTests.RunAsync(failures);
await Lucky5.Tests.CabinetCommandIntegrityTests.RunAsync(failures);
await Lucky5.Tests.AdminAuditAndCabinetDeviceTests.RunAsync(failures);
await Lucky5.Tests.WebCabinetRegressionTests.RunAsync(failures);
await Lucky5.Tests.GameServiceRegressionTests.RunAsync(failures);
await Lucky5.Tests.CleanRoomEngineTests.RunAsync(failures);
await Lucky5.Tests.AuthSecurityRegressionTests.RunAsync(failures);
await Lucky5.Tests.FilePersistentStateStoreTests.RunAsync(failures);
await Lucky5.Tests.PersistentStateRecoveryTests.RunAsync(failures);
await Lucky5.Tests.HubTests.RunAsync(failures);

if (failures.Count > 0)
{
    Console.Error.WriteLine("Lucky5 regression suite failed:");
    foreach (var failure in failures)
    {
        Console.Error.WriteLine($"- {failure}");
    }

    Environment.Exit(1);
}

Console.WriteLine("Lucky5 regression suite passed.");
