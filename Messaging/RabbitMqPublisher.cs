using System.Text;
using RabbitMQ.Client;

namespace BookingService.Messaging;

public sealed class RabbitMqPublisher(IConfiguration configuration)
{
    public async Task PublishAsync(
        string queueName,
        string messageId,
        string json,
        CancellationToken cancellationToken)
    {
        var factory = new ConnectionFactory
        {
            HostName = configuration["RabbitMq:Host"]
                ?? throw new InvalidOperationException("RabbitMq:Host is required."),
            Port = int.TryParse(configuration["RabbitMq:Port"], out var port)
                ? port
                : throw new InvalidOperationException("RabbitMq:Port must be a number."),
            UserName = configuration["RabbitMq:User"]
                ?? throw new InvalidOperationException("RabbitMq:User is required."),
            Password = configuration["RabbitMq:Password"]
                ?? throw new InvalidOperationException("RabbitMq:Password is required.")
        };

        await using var connection =
            await factory.CreateConnectionAsync(cancellationToken);

        var channelOptions = new CreateChannelOptions(
            publisherConfirmationsEnabled: true,
            publisherConfirmationTrackingEnabled: true);

        await using var channel =
            await connection.CreateChannelAsync(channelOptions, cancellationToken);

        await channel.QueueDeclareAsync(
            queue: queueName,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: null,
            cancellationToken: cancellationToken);

        var properties = new BasicProperties
        {
            Persistent = true,
            ContentType = "application/json",
            MessageId = messageId
        };

        await channel.BasicPublishAsync(
            exchange: string.Empty,
            routingKey: queueName,
            mandatory: true,
            basicProperties: properties,
            body: Encoding.UTF8.GetBytes(json),
            cancellationToken: cancellationToken);
    }
}