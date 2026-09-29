namespace Lucky5.Tests;

using System.Collections.Concurrent;
using System.Security.Claims;
using System.Threading;
using Lucky5.Application.Contracts;
using Lucky5.Application.Dtos;
using Lucky5.Application.Requests;
using Lucky5.Realtime;
using Lucky5.Realtime.Services;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Moq;

public static class HubTests
{
    // Hubs are transient; the disconnect grace-period timer only touches these
    // singleton-typed dependencies after the hub is disposed, and the 5-minute
    // timer never fires inside a test run — bare mocks are sufficient.
    private static CarrePokerGameHub CreateHub(IGameService gameService, ConnectionRegistry registry, ISpectatorTracker spectatorTracker)
        => new(gameService, registry, spectatorTracker, new Mock<IServiceScopeFactory>().Object, new Mock<IHubContext<CarrePokerGameHub>>().Object);

    public static async Task RunAsync(List<string> failures)
    {
        await GetAvailableMachinesReturnsMachineListAsync(failures);
        await MachineStatusChangedEmittedOnJoinMachineAsync(failures);
        await MachineStatusChangedEmittedOnLeaveMachineAsync(failures);
        await UserStatusChangedEmittedOnConnectAsync(failures);
        await UserStatusChangedEmittedOnDisconnectAsync(failures);
        await BetPlacedEmittedOnDealAsync(failures);
        await HoldCardUpdatedEmittedOnDrawAsync(failures);
        await DoubleUpWinEmittedOnDoubleUpAsync(failures);
        await LobbyMachinesUpdatedEmittedOnJoinMachineAsync(failures);
        await LobbyMachinesUpdatedEmittedOnLeaveMachineAsync(failures);
        await LobbyMachinesUpdatedEmittedOnSpectatorJoinAsync(failures);
        await JoinMachineReclaimsSeatForPendingDisconnectOwnerAsync(failures);
        await JoinMachineDoesNotClearOtherUsersPendingDisconnectAsync(failures);
        await ForceReleaseSettlesStaleSessionAndReleasesStakeAsync(failures);
        await ForceReleaseEmitsMachineStatusChangedFreeAsync(failures);
        await ForceReleaseIsIdempotentThroughGenerationGuardAsync(failures);
        await HandleStaleConnectionArmsGraceForKnownOccupantAsync(failures);
        await HandleStaleConnectionSettlesImmediatelyForUnknownOccupantAsync(failures);
        LobbyPayloadsCarryNoOccupantIdentity(failures);
    }

    private static async Task GetAvailableMachinesReturnsMachineListAsync(List<string> failures)
    {
        var gameServiceMock = new Mock<IGameService>();
        var registry = new ConnectionRegistry();
        var hub = CreateHub(gameServiceMock.Object, registry, new Mock<ISpectatorTracker>().Object);

        var hubClientsMock = new Mock<IHubCallerClients>();
        var callerMock = new Mock<ISingleClientProxy>();
        hubClientsMock.Setup(x => x.Caller).Returns(callerMock.Object);

        var hubContextMock = new Mock<HubCallerContext>();
        hubContextMock.Setup(x => x.ConnectionAborted).Returns(CancellationToken.None);
        hubContextMock.Setup(x => x.Items).Returns(new Dictionary<object, object?> { ["current-machine-id"] = 1 });

        var contextProperty = typeof(Hub).GetProperty("Context");
        contextProperty?.SetValue(hub, hubContextMock.Object);

        var clientsProperty = typeof(Hub).GetProperty("Clients");
        clientsProperty?.SetValue(hub, hubClientsMock.Object);

        var expectedMachines = new[]
        {
            new MachineListingDto(1, "Machine 1", true, 1000, 10000),
            new MachineListingDto(2, "Machine 2", true, 2000, 20000)
        };

        gameServiceMock
            .Setup(x => x.GetMachinesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedMachines);

        await hub.GetAvailableMachines(0);

        callerMock.Verify(
            x => x.SendCoreAsync("AvailableMachines", It.Is<object[]>(args => args.Length == 1 && ReferenceEquals(args[0], expectedMachines)), It.IsAny<CancellationToken>()),
            Times.Once);

        Assert(failures, "GetAvailableMachines should emit AvailableMachines event with machine list", true);
    }

    private static async Task MachineStatusChangedEmittedOnJoinMachineAsync(List<string> failures)
    {
        var gameServiceMock = new Mock<IGameService>();
        var registry = new ConnectionRegistry();
        var spectatorTrackerMock = new Mock<ISpectatorTracker>();
        spectatorTrackerMock.Setup(x => x.GetLobbySnapshot()).Returns(new List<LobbyMachineInfo>());
        var hub = CreateHub(gameServiceMock.Object, registry, spectatorTrackerMock.Object);

        var hubClientsMock = new Mock<IHubCallerClients>();
        var allMock = new Mock<IClientProxy>();
        var callerMock = new Mock<ISingleClientProxy>();
        var groupClientMock = new Mock<IClientProxy>();
        var groupManagerMock = new Mock<IGroupManager>();

        hubClientsMock.Setup(x => x.All).Returns(allMock.Object);
        hubClientsMock.Setup(x => x.Caller).Returns(callerMock.Object);
        hubClientsMock.Setup(x => x.Group(It.IsAny<string>())).Returns(groupClientMock.Object);

        var hubContextMock = new Mock<HubCallerContext>();
        var userId = Guid.NewGuid();
        var user = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, userId.ToString())
        }));
        hubContextMock.Setup(x => x.User).Returns(user);
        hubContextMock.Setup(x => x.ConnectionAborted).Returns(CancellationToken.None);
        hubContextMock.Setup(x => x.Items).Returns(new Dictionary<object, object?> { ["current-machine-id"] = 1 });

        var contextProperty = typeof(Hub).GetProperty("Context");
        contextProperty?.SetValue(hub, hubContextMock.Object);

        var clientsProperty = typeof(Hub).GetProperty("Clients");
        clientsProperty?.SetValue(hub, hubClientsMock.Object);

        var groupsProperty = typeof(Hub).GetProperty("Groups");
        groupsProperty?.SetValue(hub, groupManagerMock.Object);

        gameServiceMock
            .Setup(x => x.GetLobbyMachinesAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<MachineListingDto>());

        await hub.JoinMachine(1);

        allMock.Verify(
            x => x.SendCoreAsync("MachineStatusChanged", It.Is<object[]>(args => args.Length == 1), It.IsAny<CancellationToken>()),
            Times.Once);

        Assert(failures, "JoinMachine should emit MachineStatusChanged event", true);
    }

    private static async Task MachineStatusChangedEmittedOnLeaveMachineAsync(List<string> failures)
    {
        var gameServiceMock = new Mock<IGameService>();
        var registry = new ConnectionRegistry();
        var spectatorTrackerMock = new Mock<ISpectatorTracker>();
        spectatorTrackerMock.Setup(x => x.GetLobbySnapshot()).Returns(new List<LobbyMachineInfo>());
        var hub = CreateHub(gameServiceMock.Object, registry, spectatorTrackerMock.Object);

        var hubClientsMock = new Mock<IHubCallerClients>();
        var allMock = new Mock<IClientProxy>();
        var groupClientMock = new Mock<IClientProxy>();
        var groupManagerMock = new Mock<IGroupManager>();

        hubClientsMock.Setup(x => x.All).Returns(allMock.Object);
        hubClientsMock.Setup(x => x.Group(It.IsAny<string>())).Returns(groupClientMock.Object);

        var hubContextMock = new Mock<HubCallerContext>();
        hubContextMock.Setup(x => x.ConnectionAborted).Returns(CancellationToken.None);
        hubContextMock.Setup(x => x.Items).Returns(new Dictionary<object, object?> { ["machine-id"] = 1 });

        var contextProperty = typeof(Hub).GetProperty("Context");
        contextProperty?.SetValue(hub, hubContextMock.Object);

        var clientsProperty = typeof(Hub).GetProperty("Clients");
        clientsProperty?.SetValue(hub, hubClientsMock.Object);

        var groupsProperty = typeof(Hub).GetProperty("Groups");
        groupsProperty?.SetValue(hub, groupManagerMock.Object);

        gameServiceMock
            .Setup(x => x.GetLobbyMachinesAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<MachineListingDto>());

        await hub.LeaveMachine(1);

        allMock.Verify(
            x => x.SendCoreAsync("MachineStatusChanged", It.Is<object[]>(args => args.Length == 1), It.IsAny<CancellationToken>()),
            Times.Once);

        Assert(failures, "LeaveMachine should emit MachineStatusChanged event", true);
    }

    private static async Task UserStatusChangedEmittedOnConnectAsync(List<string> failures)
    {
        var gameServiceMock = new Mock<IGameService>();
        var registry = new ConnectionRegistry();
        var hub = CreateHub(gameServiceMock.Object, registry, new Mock<ISpectatorTracker>().Object);

        var hubClientsMock = new Mock<IHubCallerClients>();
        var allMock = new Mock<IClientProxy>();

        hubClientsMock.Setup(x => x.All).Returns(allMock.Object);

        var hubContextMock = new Mock<HubCallerContext>();
        var userId = Guid.NewGuid();
        var user = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, userId.ToString())
        }));
        hubContextMock.Setup(x => x.User).Returns(user);
        hubContextMock.Setup(x => x.ConnectionAborted).Returns(CancellationToken.None);
        hubContextMock.Setup(x => x.Items).Returns(new Dictionary<object, object?> { ["machine-id"] = 1 });
        hubContextMock.Setup(x => x.ConnectionId).Returns("test-connection");

        var contextProperty = typeof(Hub).GetProperty("Context");
        contextProperty?.SetValue(hub, hubContextMock.Object);

        var clientsProperty = typeof(Hub).GetProperty("Clients");
        clientsProperty?.SetValue(hub, hubClientsMock.Object);

        await hub.OnConnectedAsync();

        allMock.Verify(
            x => x.SendCoreAsync("UserStatusChanged", It.Is<object[]>(args => args.Length == 1), It.IsAny<CancellationToken>()),
            Times.Once);

        Assert(failures, "OnConnectedAsync should emit UserStatusChanged event", true);
    }

    private static async Task UserStatusChangedEmittedOnDisconnectAsync(List<string> failures)
    {
        var gameServiceMock = new Mock<IGameService>();
        var registry = new ConnectionRegistry();
        var hub = CreateHub(gameServiceMock.Object, registry, new Mock<ISpectatorTracker>().Object);

        var hubClientsMock = new Mock<IHubCallerClients>();
        var allMock = new Mock<IClientProxy>();

        hubClientsMock.Setup(x => x.All).Returns(allMock.Object);

        var hubContextMock = new Mock<HubCallerContext>();
        var userId = Guid.NewGuid();
        var user = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, userId.ToString())
        }));
        hubContextMock.Setup(x => x.User).Returns(user);
        hubContextMock.Setup(x => x.ConnectionAborted).Returns(CancellationToken.None);
        hubContextMock.Setup(x => x.Items).Returns(new Dictionary<object, object?>());
        hubContextMock.Setup(x => x.ConnectionId).Returns("test-connection");

        var contextProperty = typeof(Hub).GetProperty("Context");
        contextProperty?.SetValue(hub, hubContextMock.Object);

        var clientsProperty = typeof(Hub).GetProperty("Clients");
        clientsProperty?.SetValue(hub, hubClientsMock.Object);

        await hub.OnDisconnectedAsync(null);

        allMock.Verify(
            x => x.SendCoreAsync("UserStatusChanged", It.Is<object[]>(args => args.Length == 1), It.IsAny<CancellationToken>()),
            Times.Once);

        Assert(failures, "OnDisconnectedAsync should emit UserStatusChanged event", true);
    }

    private static async Task BetPlacedEmittedOnDealAsync(List<string> failures)
    {
        var gameServiceMock = new Mock<IGameService>();
        var registry = new ConnectionRegistry();
        var hub = CreateHub(gameServiceMock.Object, registry, new Mock<ISpectatorTracker>().Object);

        var hubClientsMock = new Mock<IHubCallerClients>();
        var callerMock = new Mock<ISingleClientProxy>();
        var groupClientMock = new Mock<IClientProxy>();

        hubClientsMock.Setup(x => x.Caller).Returns(callerMock.Object);
        hubClientsMock.Setup(x => x.Group(It.IsAny<string>())).Returns(groupClientMock.Object);
        hubClientsMock.Setup(x => x.Groups(It.IsAny<IReadOnlyList<string>>())).Returns(groupClientMock.Object);

        var hubContextMock = new Mock<HubCallerContext>();
        var userId = Guid.NewGuid();
        var user = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, userId.ToString())
        }));
        hubContextMock.Setup(x => x.User).Returns(user);
        hubContextMock.Setup(x => x.ConnectionAborted).Returns(CancellationToken.None);
        hubContextMock.Setup(x => x.Items).Returns(new Dictionary<object, object?>());

        var contextProperty = typeof(Hub).GetProperty("Context");
        contextProperty?.SetValue(hub, hubContextMock.Object);

        var clientsProperty = typeof(Hub).GetProperty("Clients");
        clientsProperty?.SetValue(hub, hubClientsMock.Object);

        var dealResult = new DealResultDto(Guid.NewGuid(), [], 1000, 50000);
        gameServiceMock
            .Setup(x => x.DealAsync(It.IsAny<Guid>(), It.IsAny<DealRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(dealResult);

        await hub.Deal(1, 1000);

        groupClientMock.Verify(
            x => x.SendCoreAsync("BetPlaced", It.Is<object[]>(args => args.Length == 1), It.IsAny<CancellationToken>()),
            Times.Once);

        Assert(failures, "Deal should emit BetPlaced event", true);
    }

    private static async Task HoldCardUpdatedEmittedOnDrawAsync(List<string> failures)
    {
        var gameServiceMock = new Mock<IGameService>();
        var registry = new ConnectionRegistry();
        var hub = CreateHub(gameServiceMock.Object, registry, new Mock<ISpectatorTracker>().Object);

        var hubClientsMock = new Mock<IHubCallerClients>();
        var callerMock = new Mock<ISingleClientProxy>();
        var groupClientMock = new Mock<IClientProxy>();

        hubClientsMock.Setup(x => x.Caller).Returns(callerMock.Object);
        hubClientsMock.Setup(x => x.Group(It.IsAny<string>())).Returns(groupClientMock.Object);
        hubClientsMock.Setup(x => x.Groups(It.IsAny<IReadOnlyList<string>>())).Returns(groupClientMock.Object);

        var hubContextMock = new Mock<HubCallerContext>();
        var userId = Guid.NewGuid();
        var user = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, userId.ToString())
        }));
        hubContextMock.Setup(x => x.User).Returns(user);
        hubContextMock.Setup(x => x.ConnectionAborted).Returns(CancellationToken.None);
        hubContextMock.Setup(x => x.Items).Returns(new Dictionary<object, object?> { ["machine-id"] = 1 });

        var contextProperty = typeof(Hub).GetProperty("Context");
        contextProperty?.SetValue(hub, hubContextMock.Object);

        var clientsProperty = typeof(Hub).GetProperty("Clients");
        clientsProperty?.SetValue(hub, hubClientsMock.Object);

        var drawResult = new DrawResultDto(Guid.NewGuid(), [], "Two Pair", 5000, 45000);
        gameServiceMock
            .Setup(x => x.DrawAsync(It.IsAny<Guid>(), It.IsAny<DrawRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(drawResult);

        await hub.Draw(Guid.NewGuid(), [0, 2]);

        groupClientMock.Verify(
            x => x.SendCoreAsync("HoldCardUpdated", It.Is<object[]>(args => args.Length == 1), It.IsAny<CancellationToken>()),
            Times.Once);

        Assert(failures, "Draw should emit HoldCardUpdated event", true);
    }

    private static async Task DoubleUpWinEmittedOnDoubleUpAsync(List<string> failures)
    {
        var gameServiceMock = new Mock<IGameService>();
        var registry = new ConnectionRegistry();
        var hub = CreateHub(gameServiceMock.Object, registry, new Mock<ISpectatorTracker>().Object);

        var hubClientsMock = new Mock<IHubCallerClients>();
        var callerMock = new Mock<ISingleClientProxy>();

        hubClientsMock.Setup(x => x.Caller).Returns(callerMock.Object);

        var hubContextMock = new Mock<HubCallerContext>();
        var userId = Guid.NewGuid();
        var user = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, userId.ToString())
        }));
        hubContextMock.Setup(x => x.User).Returns(user);
        hubContextMock.Setup(x => x.ConnectionAborted).Returns(CancellationToken.None);
        hubContextMock.Setup(x => x.Items).Returns(new Dictionary<object, object?>());

        var contextProperty = typeof(Hub).GetProperty("Context");
        contextProperty?.SetValue(hub, hubContextMock.Object);

        var clientsProperty = typeof(Hub).GetProperty("Clients");
        clientsProperty?.SetValue(hub, hubClientsMock.Object);

        var doubleUpResult = new DoubleUpResultDto(Guid.NewGuid(), "Win", 10000, 50000);
        gameServiceMock
            .Setup(x => x.GuessDoubleUpAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(doubleUpResult);

        await hub.DoubleUp(Guid.NewGuid(), "big");

        callerMock.Verify(
            x => x.SendCoreAsync(
                "DoubleUpWin",
                It.Is<object[]>(args => args.Length == 1 && args[0] is DoubleUpResultDto),
                It.IsAny<CancellationToken>()),
            Times.Once);

        Assert(failures, "DoubleUp should emit DoubleUpWin event with result", true);
    }

    private static async Task LobbyMachinesUpdatedEmittedOnJoinMachineAsync(List<string> failures)
    {
        var gameServiceMock = new Mock<IGameService>();
        var registry = new ConnectionRegistry();
        var spectatorTrackerMock = new Mock<ISpectatorTracker>();
        spectatorTrackerMock.Setup(x => x.GetLobbySnapshot()).Returns(new List<LobbyMachineInfo>());
        var hub = CreateHub(gameServiceMock.Object, registry, spectatorTrackerMock.Object);

        var hubClientsMock = new Mock<IHubCallerClients>();
        var allMock = new Mock<IClientProxy>();
        var callerMock = new Mock<ISingleClientProxy>();
        var groupClientMock = new Mock<IClientProxy>();
        var groupManagerMock = new Mock<IGroupManager>();

        hubClientsMock.Setup(x => x.All).Returns(allMock.Object);
        hubClientsMock.Setup(x => x.Caller).Returns(callerMock.Object);
        hubClientsMock.Setup(x => x.Group(It.IsAny<string>())).Returns(groupClientMock.Object);

        var hubContextMock = new Mock<HubCallerContext>();
        var userId = Guid.NewGuid();
        var user = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, userId.ToString())
        }));
        hubContextMock.Setup(x => x.User).Returns(user);
        hubContextMock.Setup(x => x.ConnectionAborted).Returns(CancellationToken.None);
        hubContextMock.Setup(x => x.Items).Returns(new Dictionary<object, object?> { ["machine-id"] = 1 });

        var contextProperty = typeof(Hub).GetProperty("Context");
        contextProperty?.SetValue(hub, hubContextMock.Object);

        var clientsProperty = typeof(Hub).GetProperty("Clients");
        clientsProperty?.SetValue(hub, hubClientsMock.Object);

        var groupsProperty = typeof(Hub).GetProperty("Groups");
        groupsProperty?.SetValue(hub, groupManagerMock.Object);

        gameServiceMock
            .Setup(x => x.GetLobbyMachinesAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<MachineListingDto>());

        await hub.JoinMachine(1);

        allMock.Verify(
            x => x.SendCoreAsync("LobbyMachinesUpdated", It.Is<object[]>(args => args.Length == 1), It.IsAny<CancellationToken>()),
            Times.Once);

        Assert(failures, "JoinMachine should emit LobbyMachinesUpdated event", true);
    }

    private static async Task LobbyMachinesUpdatedEmittedOnLeaveMachineAsync(List<string> failures)
    {
        var gameServiceMock = new Mock<IGameService>();
        var registry = new ConnectionRegistry();
        var spectatorTrackerMock = new Mock<ISpectatorTracker>();
        spectatorTrackerMock.Setup(x => x.GetLobbySnapshot()).Returns(new List<LobbyMachineInfo>());
        var hub = CreateHub(gameServiceMock.Object, registry, spectatorTrackerMock.Object);

        var hubClientsMock = new Mock<IHubCallerClients>();
        var allMock = new Mock<IClientProxy>();
        var groupClientMock = new Mock<IClientProxy>();
        var groupManagerMock = new Mock<IGroupManager>();

        hubClientsMock.Setup(x => x.All).Returns(allMock.Object);
        hubClientsMock.Setup(x => x.Group(It.IsAny<string>())).Returns(groupClientMock.Object);

        var hubContextMock = new Mock<HubCallerContext>();
        hubContextMock.Setup(x => x.ConnectionAborted).Returns(CancellationToken.None);
        hubContextMock.Setup(x => x.Items).Returns(new Dictionary<object, object?> { ["machine-id"] = 1 });

        var contextProperty = typeof(Hub).GetProperty("Context");
        contextProperty?.SetValue(hub, hubContextMock.Object);

        var clientsProperty = typeof(Hub).GetProperty("Clients");
        clientsProperty?.SetValue(hub, hubClientsMock.Object);

        var groupsProperty = typeof(Hub).GetProperty("Groups");
        groupsProperty?.SetValue(hub, groupManagerMock.Object);

        gameServiceMock
            .Setup(x => x.GetLobbyMachinesAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<MachineListingDto>());

        await hub.LeaveMachine(1);

        allMock.Verify(
            x => x.SendCoreAsync("LobbyMachinesUpdated", It.Is<object[]>(args => args.Length == 1), It.IsAny<CancellationToken>()),
            Times.Once);

        Assert(failures, "LeaveMachine should emit LobbyMachinesUpdated event", true);
    }

    private static async Task LobbyMachinesUpdatedEmittedOnSpectatorJoinAsync(List<string> failures)
    {
        var gameServiceMock = new Mock<IGameService>();
        var registry = new ConnectionRegistry();
        var spectatorTrackerMock = new Mock<ISpectatorTracker>();
        spectatorTrackerMock.Setup(x => x.GetLobbySnapshot()).Returns(new List<LobbyMachineInfo>());
        var hub = CreateHub(gameServiceMock.Object, registry, spectatorTrackerMock.Object);

        var hubClientsMock = new Mock<IHubCallerClients>();
        var allMock = new Mock<IClientProxy>();
        var callerMock = new Mock<ISingleClientProxy>();
        var groupClientMock = new Mock<IClientProxy>();
        var groupManagerMock = new Mock<IGroupManager>();

        hubClientsMock.Setup(x => x.All).Returns(allMock.Object);
        hubClientsMock.Setup(x => x.Caller).Returns(callerMock.Object);
        hubClientsMock.Setup(x => x.Group(It.IsAny<string>())).Returns(groupClientMock.Object);

        var hubContextMock = new Mock<HubCallerContext>();
        hubContextMock.Setup(x => x.ConnectionAborted).Returns(CancellationToken.None);
        hubContextMock.Setup(x => x.Items).Returns(new Dictionary<object, object?>());

        var contextProperty = typeof(Hub).GetProperty("Context");
        contextProperty?.SetValue(hub, hubContextMock.Object);

        var clientsProperty = typeof(Hub).GetProperty("Clients");
        clientsProperty?.SetValue(hub, hubClientsMock.Object);

        var groupsProperty = typeof(Hub).GetProperty("Groups");
        groupsProperty?.SetValue(hub, groupManagerMock.Object);

        gameServiceMock
            .Setup(x => x.GetLobbyMachinesAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<MachineListingDto>());

        await hub.JoinMachineAsSpectator(1);

        allMock.Verify(
            x => x.SendCoreAsync("LobbyMachinesUpdated", It.Is<object[]>(args => args.Length == 1), It.IsAny<CancellationToken>()),
            Times.Once);

        Assert(failures, "JoinMachineAsSpectator should emit LobbyMachinesUpdated event", true);
    }

    private static void Assert(List<string> failures, string message, bool condition)
    {
        if (!condition)
        {
            failures.Add(message);
        }
    }

    private static async Task JoinMachineReclaimsSeatForPendingDisconnectOwnerAsync(List<string> failures)
    {
        var gameServiceMock = new Mock<IGameService>();
        gameServiceMock.Setup(x => x.GetLobbyMachinesAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<MachineListingDto>());

        var registry = new ConnectionRegistry();
        var hub = CreateHub(gameServiceMock.Object, registry, new Mock<ISpectatorTracker>().Object);

        var hubClientsMock = new Mock<IHubCallerClients>();
        var allMock = new Mock<IClientProxy>();
        var callerMock = new Mock<ISingleClientProxy>();
        var groupClientMock = new Mock<IClientProxy>();
        var groupManagerMock = new Mock<IGroupManager>();
        hubClientsMock.Setup(x => x.All).Returns(allMock.Object);
        hubClientsMock.Setup(x => x.Caller).Returns(callerMock.Object);
        hubClientsMock.Setup(x => x.Group(It.IsAny<string>())).Returns(groupClientMock.Object);

        var reconnectingUserId = Guid.NewGuid();
        var reconnectingConnectionId = "reconnect-connection";
        var hubContextMock = new Mock<HubCallerContext>();
        var user = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, reconnectingUserId.ToString())
        }));
        hubContextMock.Setup(x => x.User).Returns(user);
        hubContextMock.Setup(x => x.ConnectionId).Returns(reconnectingConnectionId);
        hubContextMock.Setup(x => x.ConnectionAborted).Returns(CancellationToken.None);
        hubContextMock.Setup(x => x.Items).Returns(new Dictionary<object, object?>());

        typeof(Hub).GetProperty("Context")?.SetValue(hub, hubContextMock.Object);
        typeof(Hub).GetProperty("Clients")?.SetValue(hub, hubClientsMock.Object);
        typeof(Hub).GetProperty("Groups")?.SetValue(hub, groupManagerMock.Object);

        const int machineId = 9901;
        var occupancyField = typeof(CarrePokerGameHub).GetField("MachineOccupancy", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        var pendingField = typeof(CarrePokerGameHub).GetField("PendingDisconnects", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        var occupancy = (ConcurrentDictionary<int, string>)occupancyField!.GetValue(null)!;
        var pendingDisconnects = (ConcurrentDictionary<int, (Guid UserId, Timer Timer)>)pendingField!.GetValue(null)!;

        var timer = new Timer(_ => { }, null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        occupancy[machineId] = "old-connection";
        pendingDisconnects[machineId] = (reconnectingUserId, timer);

        try
        {
            await hub.JoinMachine(machineId);
            var claimed = occupancy.TryGetValue(machineId, out var currentConnectionId) && currentConnectionId == reconnectingConnectionId;
            // The grace countdown is cancelled, and JoinMachine installs a dormant
            // settlement token for the live connection (the mutual-exclusion guard
            // against in-flight auto-cashout timers) — the entry is expected to
            // remain, now owned by this connection.
            var tokenInstalledForReconnector = pendingDisconnects.TryGetValue(machineId, out var token)
                && token.UserId == reconnectingUserId
                && !ReferenceEquals(token.Timer, timer);
            Assert(failures, "JoinMachine should reclaim lock when same user reconnects during grace period", claimed && tokenInstalledForReconnector);
        }
        catch
        {
            Assert(failures, "JoinMachine should reclaim lock when same user reconnects during grace period", false);
        }
        finally
        {
            occupancy.TryRemove(machineId, out _);
            if (pendingDisconnects.TryRemove(machineId, out var leftover))
            {
                leftover.Timer.Dispose();
            }
            timer.Dispose();
        }
    }

    private static async Task JoinMachineDoesNotClearOtherUsersPendingDisconnectAsync(List<string> failures)
    {
        var gameServiceMock = new Mock<IGameService>();
        gameServiceMock.Setup(x => x.GetLobbyMachinesAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<MachineListingDto>());

        var registry = new ConnectionRegistry();
        var hub = CreateHub(gameServiceMock.Object, registry, new Mock<ISpectatorTracker>().Object);

        var hubClientsMock = new Mock<IHubCallerClients>();
        var allMock = new Mock<IClientProxy>();
        var callerMock = new Mock<ISingleClientProxy>();
        var groupClientMock = new Mock<IClientProxy>();
        var groupManagerMock = new Mock<IGroupManager>();
        hubClientsMock.Setup(x => x.All).Returns(allMock.Object);
        hubClientsMock.Setup(x => x.Caller).Returns(callerMock.Object);
        hubClientsMock.Setup(x => x.Group(It.IsAny<string>())).Returns(groupClientMock.Object);

        var currentUserId = Guid.NewGuid();
        var hubContextMock = new Mock<HubCallerContext>();
        var user = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, currentUserId.ToString())
        }));
        hubContextMock.Setup(x => x.User).Returns(user);
        hubContextMock.Setup(x => x.ConnectionId).Returns("different-user-connection");
        hubContextMock.Setup(x => x.ConnectionAborted).Returns(CancellationToken.None);
        hubContextMock.Setup(x => x.Items).Returns(new Dictionary<object, object?>());

        typeof(Hub).GetProperty("Context")?.SetValue(hub, hubContextMock.Object);
        typeof(Hub).GetProperty("Clients")?.SetValue(hub, hubClientsMock.Object);
        typeof(Hub).GetProperty("Groups")?.SetValue(hub, groupManagerMock.Object);

        const int machineId = 9902;
        var occupancyField = typeof(CarrePokerGameHub).GetField("MachineOccupancy", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        var pendingField = typeof(CarrePokerGameHub).GetField("PendingDisconnects", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        var occupancy = (ConcurrentDictionary<int, string>)occupancyField!.GetValue(null)!;
        var pendingDisconnects = (ConcurrentDictionary<int, (Guid UserId, Timer Timer)>)pendingField!.GetValue(null)!;

        var ownerUserId = Guid.NewGuid();
        var timer = new Timer(_ => { }, null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        occupancy[machineId] = "owner-connection";
        pendingDisconnects[machineId] = (ownerUserId, timer);

        var blocked = false;
        Exception? unexpected = null;
        try
        {
            await hub.JoinMachine(machineId);
        }
        catch (HubException)
        {
            blocked = true;
        }
        catch (Exception ex)
        {
            unexpected = ex;
        }
        finally
        {
            var pendingPreserved = pendingDisconnects.TryGetValue(machineId, out var pending) && pending.UserId == ownerUserId;
            Assert(failures, "JoinMachine should keep pending disconnect for original owner when another user attempts join", blocked && pendingPreserved && unexpected is null);
            occupancy.TryRemove(machineId, out _);
            if (pendingDisconnects.TryRemove(machineId, out var leftover))
            {
                leftover.Timer.Dispose();
            }
            timer.Dispose();
        }
    }

    // ---------------------------------------------------------------------
    // AI9 gamestate hardening (PR1) — the settle-and-release core path and the
    // lobby identity redaction. These tests build a real ServiceProvider so the
    // force-release path can resolve its scoped IGameService + IDataStore.
    // ---------------------------------------------------------------------

    private static (ServiceProvider Provider, Mock<IGameService> GameService, Mock<IClientProxy> AllClients, Mock<Lucky5.Application.Interfaces.IDataStore> Store)
        BuildForceReleaseFixture(out ConnectionRegistry registry)
    {
        registry = new ConnectionRegistry();
        var gameServiceMock = new Mock<IGameService>();
        gameServiceMock
            .Setup(x => x.GetLobbyMachinesAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<MachineListingDto>());
        gameServiceMock
            .Setup(x => x.CashOutAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<CancellationToken>(), true))
            .ReturnsAsync((MachineSessionDto?)null!);

        var storeMock = new Mock<Lucky5.Application.Interfaces.IDataStore>();
        storeMock
            .Setup(x => x.GetMachineSessionAsync(It.IsAny<Guid>(), It.IsAny<int>()))
            .ReturnsAsync((Lucky5.Domain.Entities.MachineSessionState?)null);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped(_ => gameServiceMock.Object);
        services.AddScoped(_ => storeMock.Object);
        var provider = services.BuildServiceProvider();

        var allMock = new Mock<IClientProxy>();
        var hubContextMock = new Mock<IHubContext<CarrePokerGameHub>>();
        hubContextMock.Setup(x => x.Clients.All).Returns(allMock.Object);

        return (provider, gameServiceMock, allMock, storeMock);
    }

    private static async Task ForceReleaseSettlesStaleSessionAndReleasesStakeAsync(List<string> failures)
    {
        const int machineId = 9910;
        var userId = Guid.NewGuid();
        var (provider, gameServiceMock, allMock, storeMock) = BuildForceReleaseFixture(out var registry);
        await using (provider)
        {
            var hubContextMock = new Mock<IHubContext<CarrePokerGameHub>>();
            hubContextMock.Setup(x => x.Clients.All).Returns(allMock.Object);

            // Stale session: credits + reserved stake, no active round, old LastUpdatedUtc.
            var staleSession = new Lucky5.Domain.Entities.MachineSessionState
            {
                UserId = userId,
                MachineId = machineId,
                MachineCredits = 2500m,
                ReservedStake = 500m,
                LastUpdatedUtc = DateTime.UtcNow.AddMinutes(-30)
            };
            storeMock
                .Setup(x => x.GetMachineSessionAsync(userId, machineId))
                .ReturnsAsync(staleSession);

            var occupancyField = typeof(CarrePokerGameHub).GetField("MachineOccupancy", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            var pendingField = typeof(CarrePokerGameHub).GetField("PendingDisconnects", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            var occupancy = (ConcurrentDictionary<int, string>)occupancyField!.GetValue(null)!;
            var pendingDisconnects = (ConcurrentDictionary<int, (Guid UserId, Timer Timer)>)pendingField!.GetValue(null)!;

            // Seat held by a ghost occupancy entry (server restarted before the
            // disconnect was ever seen, so the in-memory registry has no entry for
            // this connection and the user's session row is the authoritative owner).
            occupancy[machineId] = "dead-connection";
            try
            {
                var result = await CarrePokerGameHub.ForceReleaseMachineCoreAsync(
                    machineId,
                    provider.GetRequiredService<IServiceScopeFactory>(),
                    hubContextMock.Object,
                    registry,
                    CarrePokerGameHub.ForceReleaseReason.StaleSessionSweep,
                    expectedUserId: userId);

                // CashOut must have been invoked with bypassRules: true so reserved
                // stake is released and residue credits settle to the wallet.
                gameServiceMock.Verify(
                    x => x.CashOutAsync(userId, machineId, It.IsAny<CancellationToken>(), true),
                    Times.Once);

                var seatReleased = !occupancy.ContainsKey(machineId);
                Assert(failures,
                    "Stale session (credits + reserved stake, no round) should force-settle: CashOutAsync(bypassRules: true) called, seat released",
                    result.Settled && result.WasOccupied && seatReleased);
            }
            finally
            {
                occupancy.TryRemove(machineId, out _);
                if (pendingDisconnects.TryRemove(machineId, out var leftover)) leftover.Timer.Dispose();
            }
        }
    }

    private static async Task ForceReleaseEmitsMachineStatusChangedFreeAsync(List<string> failures)
    {
        const int machineId = 9911;
        var userId = Guid.NewGuid();
        var (provider, gameServiceMock, allMock, _) = BuildForceReleaseFixture(out var registry);
        await using (provider)
        {
            var hubContextMock = new Mock<IHubContext<CarrePokerGameHub>>();
            hubContextMock.Setup(x => x.Clients.All).Returns(allMock.Object);

            var occupancyField = typeof(CarrePokerGameHub).GetField("MachineOccupancy", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            var pendingField = typeof(CarrePokerGameHub).GetField("PendingDisconnects", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            var occupancy = (ConcurrentDictionary<int, string>)occupancyField!.GetValue(null)!;
            var pendingDisconnects = (ConcurrentDictionary<int, (Guid UserId, Timer Timer)>)pendingField!.GetValue(null)!;

            occupancy[machineId] = "dead-connection";
            try
            {
                await CarrePokerGameHub.ForceReleaseMachineCoreAsync(
                    machineId,
                    provider.GetRequiredService<IServiceScopeFactory>(),
                    hubContextMock.Object,
                    registry,
                    CarrePokerGameHub.ForceReleaseReason.StaleSessionSweep);

                allMock.Verify(
                    x => x.SendCoreAsync(
                        "MachineStatusChanged",
                        It.Is<object[]>(args => args.Length == 1
                            && args[0]!.GetType().GetProperty("isOccupied")!.GetValue(args[0])!.Equals(false)
                            && args[0]!.GetType().GetProperty("machineId")!.GetValue(args[0])!.Equals(machineId)),
                        It.IsAny<CancellationToken>()),
                    Times.Once);

                Assert(failures, "Force-release should emit MachineStatusChanged(isOccupied=false) for the released machine", true);
            }
            finally
            {
                occupancy.TryRemove(machineId, out _);
                if (pendingDisconnects.TryRemove(machineId, out var leftover)) leftover.Timer.Dispose();
            }
        }
    }

    private static async Task ForceReleaseIsIdempotentThroughGenerationGuardAsync(List<string> failures)
    {
        const int machineId = 9912;
        var userId = Guid.NewGuid();
        var (provider, gameServiceMock, allMock, _) = BuildForceReleaseFixture(out var registry);
        await using (provider)
        {
            var hubContextMock = new Mock<IHubContext<CarrePokerGameHub>>();
            hubContextMock.Setup(x => x.Clients.All).Returns(allMock.Object);

            var occupancyField = typeof(CarrePokerGameHub).GetField("MachineOccupancy", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            var pendingField = typeof(CarrePokerGameHub).GetField("PendingDisconnects", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            var occupancy = (ConcurrentDictionary<int, string>)occupancyField!.GetValue(null)!;
            var pendingDisconnects = (ConcurrentDictionary<int, (Guid UserId, Timer Timer)>)pendingField!.GetValue(null)!;

            // Install a pending entry, then remove it — simulating a reconnect that
            // consumed the entry before the stale grace timer fired.
            var timer = new Timer(_ => { }, null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            var entry = new KeyValuePair<int, (Guid UserId, Timer Timer)>(machineId, (userId, timer));
            pendingDisconnects[machineId] = (userId, timer);
            occupancy[machineId] = "live-reconnected-connection";
            registry.Add("live-reconnected-connection", userId);

            // Reconnect consumed the pending entry.
            pendingDisconnects.TryRemove(machineId, out _);

            try
            {
                var result = await CarrePokerGameHub.ForceReleaseMachineCoreAsync(
                    machineId,
                    provider.GetRequiredService<IServiceScopeFactory>(),
                    hubContextMock.Object,
                    registry,
                    CarrePokerGameHub.ForceReleaseReason.GraceTimeout,
                    expectedPendingEntry: entry);

                // The stale caller must NOT have cashed out: the generation guard
                // rejected its ownership claim.
                gameServiceMock.Verify(
                    x => x.CashOutAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()),
                    Times.Never);

                Assert(failures,
                    "Grace auto-cashout must be idempotent: a stale generation-guard entry must abort the settle with no CashOut",
                    !result.Settled && !result.PendingEntryRemoved);
            }
            finally
            {
                occupancy.TryRemove(machineId, out _);
                pendingDisconnects.TryRemove(machineId, out _);
                timer.Dispose();
                registry.Remove("live-reconnected-connection");
            }
        }
    }

    private static Task HandleStaleConnectionArmsGraceForKnownOccupantAsync(List<string> failures)
    {
        const int machineId = 9913;
        var userId = Guid.NewGuid();
        var registry = new ConnectionRegistry();
        var deadConnectionId = "stale-occupant-connection";
        registry.Add(deadConnectionId, userId);

        var occupancyField = typeof(CarrePokerGameHub).GetField("MachineOccupancy", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        var pendingField = typeof(CarrePokerGameHub).GetField("PendingDisconnects", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        var occupancy = (ConcurrentDictionary<int, string>)occupancyField!.GetValue(null)!;
        var pendingDisconnects = (ConcurrentDictionary<int, (Guid UserId, Timer Timer)>)pendingField!.GetValue(null)!;

        occupancy[machineId] = deadConnectionId;
        try
        {
            var services = new ServiceCollection();
            var gameServiceMock = new Mock<IGameService>();
            gameServiceMock
                .Setup(x => x.GetLobbyMachinesAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<MachineListingDto>());
            services.AddScoped(_ => gameServiceMock.Object);
            var provider = services.BuildServiceProvider();
            var hubContextMock = new Mock<IHubContext<CarrePokerGameHub>>();

            var armed = CarrePokerGameHub.HandleStaleConnection(
                deadConnectionId,
                provider.GetRequiredService<IServiceScopeFactory>(),
                hubContextMock.Object,
                registry);

            // The seat is NOT killed instantly: a pending grace entry must now exist
            // for this machine, owned by the stale connection's user.
            var graceArmed = pendingDisconnects.TryGetValue(machineId, out var entry) && entry.UserId == userId;
            var seatStillHeld = occupancy.TryGetValue(machineId, out var occ) && occ == deadConnectionId;

            Assert(failures,
                "Heartbeat-pruned occupant should arm the 5-min grace timer (not instant-settle)",
                armed && graceArmed && seatStillHeld);

            provider.Dispose();
        }
        finally
        {
            occupancy.TryRemove(machineId, out _);
            if (pendingDisconnects.TryRemove(machineId, out var leftover)) leftover.Timer.Dispose();
            registry.Remove(deadConnectionId);
        }
        return Task.CompletedTask;
    }

    private static async Task HandleStaleConnectionSettlesImmediatelyForUnknownOccupantAsync(List<string> failures)
    {
        const int machineId = 9914;
        var registry = new ConnectionRegistry();
        var deadConnectionId = "pruned-unknown-connection";
        // Connection was already removed from the registry (heartbeat prune).

        var occupancyField = typeof(CarrePokerGameHub).GetField("MachineOccupancy", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        var pendingField = typeof(CarrePokerGameHub).GetField("PendingDisconnects", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        var occupancy = (ConcurrentDictionary<int, string>)occupancyField!.GetValue(null)!;
        var pendingDisconnects = (ConcurrentDictionary<int, (Guid UserId, Timer Timer)>)pendingField!.GetValue(null)!;

        occupancy[machineId] = deadConnectionId;
        try
        {
            var services = new ServiceCollection();
            var gameServiceMock = new Mock<IGameService>();
            gameServiceMock
                .Setup(x => x.GetLobbyMachinesAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<MachineListingDto>());
            gameServiceMock
                .Setup(x => x.CashOutAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
                .ReturnsAsync((MachineSessionDto?)null!);
            var storeMock = new Mock<Lucky5.Application.Interfaces.IDataStore>();
            storeMock
                .Setup(x => x.GetMachineSessionAsync(It.IsAny<Guid>(), It.IsAny<int>()))
                .ReturnsAsync((Lucky5.Domain.Entities.MachineSessionState?)null);
            services.AddScoped(_ => gameServiceMock.Object);
            services.AddScoped(_ => storeMock.Object);
            var provider = services.BuildServiceProvider();
            var allMock = new Mock<IClientProxy>();
            var hubContextMock = new Mock<IHubContext<CarrePokerGameHub>>();
            hubContextMock.Setup(x => x.Clients.All).Returns(allMock.Object);

            var armed = CarrePokerGameHub.HandleStaleConnection(
                deadConnectionId,
                provider.GetRequiredService<IServiceScopeFactory>(),
                hubContextMock.Object,
                registry);

            // No user derivable -> the immediate release path ran (fire-and-forget).
            // Give the fire-and-forget task a moment to run its synchronous prefix.
            await Task.Delay(50);

            var noPendingEntry = !pendingDisconnects.ContainsKey(machineId);
            Assert(failures,
                "Heartbeat-pruned occupant with no resolvable user should settle immediately (no grace entry armed)",
                armed && noPendingEntry);

            provider.Dispose();
        }
        finally
        {
            occupancy.TryRemove(machineId, out _);
            if (pendingDisconnects.TryRemove(machineId, out var leftover)) leftover.Timer.Dispose();
        }
    }

    private static void LobbyPayloadsCarryNoOccupantIdentity(List<string> failures)
    {
        // The redaction is enforced at the type level: no DTO or hub payload type
        // may carry an occupant-identity member. Reflect to prove it.
        var lobbyTypes = new[]
        {
            typeof(Lucky5.Application.Dtos.LobbyMachineInfo),
            typeof(Lucky5.Application.Dtos.PlayerLobbyMachineDto),
            typeof(Lucky5.Application.Dtos.MachineListingDto)
        };

        var offending = new List<string>();
        foreach (var t in lobbyTypes)
        {
            foreach (var prop in t.GetProperties())
            {
                if (prop.Name.Contains("Username", StringComparison.OrdinalIgnoreCase)
                    || prop.Name.Contains("Occupant", StringComparison.OrdinalIgnoreCase)
                    || prop.Name.Contains("OccupiedBy", StringComparison.OrdinalIgnoreCase))
                {
                    offending.Add($"{t.Name}.{prop.Name}");
                }
            }
            foreach (var ctor in t.GetConstructors())
            {
                foreach (var p in ctor.GetParameters())
                {
                    if (p.Name is not null
                        && (p.Name.Contains("username", StringComparison.OrdinalIgnoreCase)
                            || p.Name.Contains("occupant", StringComparison.OrdinalIgnoreCase)
                            || p.Name.Contains("occupiedBy", StringComparison.OrdinalIgnoreCase)))
                    {
                        offending.Add($"{t.Name} ctor param {p.Name}");
                    }
                }
            }
        }

        Assert(failures,
            "Lobby DTOs must carry no occupant identity (no OccupiedByUsername / OccupantUserId / username ctor param) — found: " + string.Join(", ", offending),
            offending.Count == 0);
    }
}
