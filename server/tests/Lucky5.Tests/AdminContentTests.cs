namespace Lucky5.Tests;

using Lucky5.Application.Requests;
using Lucky5.Infrastructure.Services;

public static class AdminContentTests
{
    public static async Task RunAsync(List<string> failures)
    {
        await OffersCrudAsync(failures);
        await TermsUpsertListDeleteAsync(failures);
        await AppSettingsUpsertListDeleteAsync(failures);
        await ServiceLayerDoesNotEnforceAdminRoleAsync(failures);
    }

    private static async Task OffersCrudAsync(List<string> failures)
    {
        var store = new InMemoryDataStore();
        var service = new AdminContentService(store);
        var adminId = Guid.Parse("00000000-0000-0000-0000-000000000001");

        var initial = await service.ListOffersAsync(CancellationToken.None);
        Assert(failures, "ListOffersAsync should return pre-seeded offers.", initial.Count >= 2);

        var created = await service.CreateOfferAsync(adminId, new CreateOfferRequest("Test Offer", "Test Desc", 25m), CancellationToken.None);
        Assert(failures, "CreateOfferAsync should return the created offer.", created.Title == "Test Offer" && created.BonusAmount == 25m && created.Id > 0);

        var get = await service.GetOfferAsync(created.Id, CancellationToken.None);
        Assert(failures, "GetOfferAsync should return the created offer.", get.Id == created.Id && get.Title == "Test Offer");

        var updated = await service.UpdateOfferAsync(adminId, created.Id, new UpdateOfferRequest("Updated Offer", "Updated Desc", 50m), CancellationToken.None);
        Assert(failures, "UpdateOfferAsync should return the updated offer.", updated.Title == "Updated Offer" && updated.BonusAmount == 50m);

        await service.DeleteOfferAsync(adminId, created.Id, CancellationToken.None);
        Assert(failures, "DeleteOfferAsync should remove the offer.", ThrowsKeyNotFound(() => service.GetOfferAsync(created.Id, CancellationToken.None).GetAwaiter().GetResult()));
    }

    private static async Task TermsUpsertListDeleteAsync(List<string> failures)
    {
        var store = new InMemoryDataStore();
        var service = new AdminContentService(store);
        var adminId = Guid.Parse("00000000-0000-0000-0000-000000000001");

        var list = await service.ListTermsAsync(CancellationToken.None);
        Assert(failures, "ListTermsAsync should return the pre-seeded terms.", list.Count == 1 && list[0].Version == "1.0.0");

        var upserted = await service.UpsertTermsAsync(adminId, new UpsertTermsRequest("2.0", "# New Terms\n\nBody here."), CancellationToken.None);
        Assert(failures, "UpsertTermsAsync should return the upserted terms.", upserted.Version == "2.0" && upserted.BodyMarkdown == "# New Terms\n\nBody here.");

        var afterUpsert = await service.ListTermsAsync(CancellationToken.None);
        Assert(failures, "ListTermsAsync should reflect the upserted terms.", afterUpsert.Count == 1 && afterUpsert[0].Version == "2.0");

        await service.DeleteTermsAsync(adminId, "2.0", CancellationToken.None);
        var afterDelete = await service.ListTermsAsync(CancellationToken.None);
        Assert(failures, "DeleteTermsAsync should reset terms to default.", afterDelete.Count == 1 && afterDelete[0].Version == "1.0.0");
    }

    private static async Task AppSettingsUpsertListDeleteAsync(List<string> failures)
    {
        var store = new InMemoryDataStore();
        var service = new AdminContentService(store);
        var adminId = Guid.Parse("00000000-0000-0000-0000-000000000001");

        var initial = await service.ListAppSettingsAsync(CancellationToken.None);
        Assert(failures, "ListAppSettingsAsync should return pre-seeded app settings.", initial.Any(s => s.Key == "game.houseRulesetVersion"));

        var upserted = await service.UpsertAppSettingAsync(adminId, new UpsertAppSettingRequest("test.key", "test-value"), CancellationToken.None);
        Assert(failures, "UpsertAppSettingAsync should return the upserted setting.", upserted.Key == "test.key" && upserted.Value == "test-value");

        var afterUpsert = await service.ListAppSettingsAsync(CancellationToken.None);
        Assert(failures, "ListAppSettingsAsync should include the new setting.", afterUpsert.Any(s => s.Key == "test.key" && s.Value == "test-value"));

        await service.DeleteAppSettingAsync(adminId, "test.key", CancellationToken.None);
        var afterDelete = await service.ListAppSettingsAsync(CancellationToken.None);
        Assert(failures, "DeleteAppSettingAsync should remove the setting.", !afterDelete.Any(s => s.Key == "test.key"));
    }

    private static async Task ServiceLayerDoesNotEnforceAdminRoleAsync(List<string> failures)
    {
        var store = new InMemoryDataStore();
        var service = new AdminContentService(store);
        var playerId = Guid.Parse("00000000-0000-0000-0000-000000000002");

        var offers = await service.ListOffersAsync(CancellationToken.None);
        Assert(failures, "ListOffersAsync should work for any caller (role enforcement is at controller layer).", offers.Count >= 2);

        var created = await service.CreateOfferAsync(playerId, new CreateOfferRequest("Player Offer", "Player Desc", 5m), CancellationToken.None);
        Assert(failures, "CreateOfferAsync should accept non-admin adminId (role enforcement is at controller layer).", created.Title == "Player Offer");

        await service.DeleteOfferAsync(playerId, created.Id, CancellationToken.None);
        Assert(failures, "DeleteOfferAsync should accept non-admin adminId (role enforcement is at controller layer).", ThrowsKeyNotFound(() => service.GetOfferAsync(created.Id, CancellationToken.None).GetAwaiter().GetResult()));
    }

    private static bool ThrowsKeyNotFound(Func<AdminOfferDto> func)
    {
        try
        {
            func();
            return false;
        }
        catch (KeyNotFoundException)
        {
            return true;
        }
    }

    private static void Assert(List<string> failures, string message, bool condition)
    {
        if (!condition)
        {
            failures.Add(message);
        }
    }
}
