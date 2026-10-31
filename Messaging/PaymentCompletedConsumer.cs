using System.Text.Json;
using BookingService.Data;
using Microsoft.EntityFrameworkCore;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace BookingService.Messaging;

public sealed class PaymentCompletedConsumer(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    ILogger<PaymentCompletedConsumer> logger) : BackgroundService
{
    private const string QueueName = "payment.completed.v1";

    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunConsumerAsync(stoppingToken);
            }
            catch (OperationCanceledException)
                when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(
                    exception,
                    "PaymentCompleted consumer stopped; reconnecting in 5 seconds.");

                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
    }

    private async Task RunConsumerAsync(CancellationToken cancellationToken)
    {
        var factory = new ConnectionFactory
        {
            HostName = configuration["RabbitMq:Host"]
                ?? throw new InvalidOperationException("RabbitMq:Host is required."),
            Port = configuration.GetValue("RabbitMq:Port", 5672),
            UserName = configuration["RabbitMq:User"]
                ?? throw new InvalidOperationException("RabbitMq:User is required."),
            Password = configuration["RabbitMq:Password"]
                ?? throw new InvalidOperationException("RabbitMq:Password is required."),
            ClientProvidedName = "booking-service"
        };

        await using var connection =
            await factory.CreateConnectionAsync(cancellationToken);

        await using var channel =
            await connection.CreateChannelAsync(cancellationToken: cancellationToken);

        await channel.QueueDeclareAsync(
            queue: QueueName,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: null,
            cancellationToken: cancellationToken);

        await channel.BasicQosAsync(
            prefetchSize: 0,
            prefetchCount: 1,
            global: false,
            cancellationToken);

        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += HandleMessageAsync;

        await channel.BasicConsumeAsync(
            queue: QueueName,
            autoAck: false,
            consumer: consumer,
            cancellationToken);

        logger.LogInformation("Listening for messages on {Queue}.", QueueName);

        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
    }

    private async Task HandleMessageAsync(
        object sender,
        BasicDeliverEventArgs delivery)
    {
        var channel = ((AsyncEventingBasicConsumer)sender).Channel;
        var cancellationToken = delivery.CancellationToken;

        PaymentCompletedV1? message;

        try
        {
            message = JsonSerializer.Deserialize<PaymentCompletedV1>(
                delivery.Body.Span,
                JsonOptions);
        }
        catch (JsonException exception)
        {
            logger.LogError(
                exception,
                "Invalid JSON in {Queue}; rejecting message {MessageId}.",
                QueueName,
                delivery.BasicProperties.MessageId);

            await channel.BasicNackAsync(
                delivery.DeliveryTag,
                multiple: false,
                requeue: false,
                cancellationToken);

            return;
        }

        if (message is null ||
            message.EventId == Guid.Empty ||
            message.EventType != "payment.completed" ||
            message.SchemaVersion != 1 ||
            message.Data is null ||
            message.Data.BookingId == Guid.Empty ||
            message.Data.PaymentId == Guid.Empty)
        {
            logger.LogError(
                "Invalid PaymentCompleted event in {Queue}; rejecting message {MessageId}.",
                QueueName,
                delivery.BasicProperties.MessageId);

            await channel.BasicNackAsync(
                delivery.DeliveryTag,
                multiple: false,
                requeue: false,
                cancellationToken);

            return;
        }

        try
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<BookingDbContext>();

            var booking = await db.Bookings
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    item => item.Id == message.Data.BookingId,
                    cancellationToken);

            if (booking is null)
            {
                logger.LogError(
                    "PaymentCompleted references unknown booking {BookingId}.",
                    message.Data.BookingId);

                await channel.BasicNackAsync(
                    delivery.DeliveryTag,
                    multiple: false,
                    requeue: false,
                    cancellationToken);

                return;
            }

            if (booking.TotalPrice != message.Data.Amount)
            {
                logger.LogError(
                    "Payment amount {PaymentAmount} does not match booking {BookingId} total {BookingTotal}.",
                    message.Data.Amount,
                    booking.Id,
                    booking.TotalPrice);

                await channel.BasicNackAsync(
                    delivery.DeliveryTag,
                    multiple: false,
                    requeue: false,
                    cancellationToken);

                return;
            }

            var updated = await db.Bookings
                .Where(item =>
                    item.Id == message.Data.BookingId &&
                    item.Status == "Pending")
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(
                        item => item.Status,
                        "Confirmed"),
                    cancellationToken);

            if (updated == 1)
            {
                logger.LogInformation(
                    "Booking {BookingId} confirmed by payment event {EventId}.",
                    message.Data.BookingId,
                    message.EventId);
            }
            else
            {
                logger.LogInformation(
                    "Booking {BookingId} was not Pending; no status change for event {EventId}.",
                    message.Data.BookingId,
                    message.EventId);
            }

            await channel.BasicAckAsync(
                delivery.DeliveryTag,
                multiple: false,
                cancellationToken);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Could not process PaymentCompleted event {EventId}; requeueing.",
                message.EventId);

            await channel.BasicNackAsync(
                delivery.DeliveryTag,
                multiple: false,
                requeue: true,
                cancellationToken);
        }
    }
}