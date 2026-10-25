using BookingService.Data;
using Microsoft.EntityFrameworkCore;
using BookingService.Models;

namespace BookingService.Messaging;

public sealed class OutboxDispatcher(
    IServiceScopeFactory scopeFactory,
    RabbitMqPublisher publisher,
    ILogger<OutboxDispatcher> logger) : BackgroundService
{
    private const string QueueName = "booking.created.v1";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PublishPendingMessagesAsync(stoppingToken);
                await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Outbox dispatch failed.");
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
    }

    private async Task PublishPendingMessagesAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<BookingDbContext>();

        var messages = await dbContext.OutboxMessages
            .Where(message => message.PublishedAtUtc == null)
            .OrderBy(message => message.OccurredAtUtc)
            .Take(20)
            .ToListAsync(cancellationToken);

        foreach (var message in messages)
        {
            try
            {
                await publisher.PublishAsync(
                    QueueName,
                    message.Id.ToString(),
                    message.Payload,
                    cancellationToken);

                message.PublishedAtUtc = DateTimeOffset.UtcNow;
                await dbContext.SaveChangesAsync(cancellationToken);

                logger.LogInformation(
                    "Published outbox message {MessageId} of type {MessageType}.",
                    message.Id,
                    message.MessageType);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                logger.LogError(
                    exception,
                    "Could not publish outbox message {MessageId}; it will be retried.",
                    message.Id);

                break;
            }
        }
    }
}