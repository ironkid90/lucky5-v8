namespace Lucky5.Tests;

using System.Security.Claims;
using Lucky5.Application.Contracts;
using Lucky5.Application.Dtos;
using Lucky5.Application.Requests;
using Lucky5.Realtime;
using Lucky5.Realtime.Services;
using Microsoft.AspNetCore.SignalR;
using Moq;

public static class HubTests
{
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
    }

    private static async Task GetAvailableMachinesReturnsMachineListAsync(List<string> failures)
    {
        var gameServiceMock = new Mock<IGameService>();
        var registry = new ConnectionRegistry();
        var hub = new CarrePokerGameHub(gameServiceMock.Object, registry);

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
        var hub = new CarrePokerGameHub(gameServiceMock.Object, registry);

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
        var hub = new CarrePokerGameHub(gameServiceMock.Object, registry);

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
        var hub = new CarrePokerGameHub(gameServiceMock.Object, registry);

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
        var hub = new CarrePokerGameHub(gameServiceMock.Object, registry);

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
        var hub = new CarrePokerGameHub(gameServiceMock.Object, registry);

        var hubClientsMock = new Mock<IHubCallerClients>();
        var callerMock = new Mock<ISingleClientProxy>();
        var groupClientMock = new Mock<IClientProxy>();

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
        var hub = new CarrePokerGameHub(gameServiceMock.Object, registry);

        var hubClientsMock = new Mock<IHubCallerClients>();
        var callerMock = new Mock<ISingleClientProxy>();
        var groupClientMock = new Mock<IClientProxy>();

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
        var hub = new CarrePokerGameHub(gameServiceMock.Object, registry);

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

    private static void Assert(List<string> failures, string message, bool condition)
    {
        if (!condition)
        {
            failures.Add(message);
        }
    }
}
