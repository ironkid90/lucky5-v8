namespace Lucky5.Infrastructure.Services;

using FirebaseAdmin;
using FirebaseAdmin.Messaging;
using Google.Apis.Auth.OAuth2;
using Lucky5.Application.Contracts;
using Lucky5.Domain.Entities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

public sealed class FirebaseNotificationService : INotificationService, IDisposable
{
    private readonly FirebaseApp? _app;
    private readonly bool _configured;
    private readonly ILogger<FirebaseNotificationService> _logger;
    private readonly ConcurrentDictionary<Guid, List<DeviceToken>> _tokens = new();

    public FirebaseNotificationService(IConfiguration configuration, ILogger<FirebaseNotificationService> logger)
    {
        _logger = logger;
        var credentialPath = configuration["Firebase:CredentialPath"];
        var credentialJson = configuration["Firebase:CredentialJson"];

        try
        {
            if (FirebaseApp.DefaultInstance is not null)
            {
                _app = FirebaseApp.DefaultInstance;
                _configured = true;
                return;
            }

            GoogleCredential credential;
            if (!string.IsNullOrWhiteSpace(credentialJson))
            {
                credential = GoogleCredential.FromJson(credentialJson);
            }
            else if (!string.IsNullOrWhiteSpace(credentialPath) && File.Exists(credentialPath))
            {
                credential = GoogleCredential.FromFile(credentialPath);
            }
            else
            {
                _logger.LogWarning("Firebase credentials not configured — push notifications disabled. " +
                    "Set Firebase:CredentialPath or Firebase:CredentialJson in appsettings.");
                return;
            }

            _app = FirebaseApp.Create(new AppOptions { Credential = credential });
            _configured = true;
            _logger.LogInformation("Firebase Admin SDK initialized.");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Firebase Admin SDK init failed — push notifications disabled.");
        }
    }

    public Task RegisterDeviceAsync(Guid userId, string token, string platform, CancellationToken cancellationToken)
    {
        var list = _tokens.GetOrAdd(userId, _ => []);
        lock (list)
        {
            if (!list.Any(t => t.Token == token))
                list.Add(new DeviceToken { UserId = userId, Token = token, Platform = platform });
        }
        return Task.CompletedTask;
    }

    public async Task SendToUserAsync(Guid userId, string title, string body, CancellationToken cancellationToken)
    {
        if (!_configured || !_tokens.TryGetValue(userId, out var tokens) || tokens.Count == 0)
        {
            _logger.LogDebug("Push skipped for user {UserId} (no tokens or Firebase not configured)", userId);
            return;
        }

        var tokenStrings = tokens.Select(t => t.Token).ToList();
        await SendMulticastAsync(tokenStrings, title, body, cancellationToken);
    }

    public async Task SendToTopicAsync(string topic, string title, string body, CancellationToken cancellationToken)
    {
        if (!_configured) return;

        var message = new Message
        {
            Topic = topic,
            Notification = new Notification { Title = title, Body = body },
            Android = new AndroidConfig { Priority = Priority.High }
        };

        try
        {
            await FirebaseMessaging.DefaultInstance.SendAsync(message, cancellationToken);
            _logger.LogDebug("Push sent to topic {Topic}", topic);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send push to topic {Topic}", topic);
        }
    }

    private async Task SendMulticastAsync(List<string> tokens, string title, string body, CancellationToken cancellationToken)
    {
        var message = new MulticastMessage
        {
            Tokens = tokens,
            Notification = new Notification { Title = title, Body = body },
            Android = new AndroidConfig { Priority = Priority.High }
        };

        try
        {
            var result = await FirebaseMessaging.DefaultInstance.SendEachForMulticastAsync(message, cancellationToken);
            _logger.LogDebug("Push sent: {Success} success, {Failure} failure", result.SuccessCount, result.FailureCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Multicast push failed");
        }
    }

    public void Dispose() { /* FirebaseApp does not implement IDisposable */ }
}
