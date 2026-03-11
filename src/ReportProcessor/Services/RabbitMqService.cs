using System.Text;
using System.Text.Json;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using ReportProcessor.Models;

namespace ReportProcessor.Services;

public class RabbitMqConsumerService : IDisposable
{
    private readonly IConfiguration _config;
    private readonly ILogger<RabbitMqConsumerService> _logger;

    private IConnection? _connection;
    private IChannel? _channel;

    private const string SubmittedQueue = "pajak.laporan.submitted";
    private const string ResultQueue = "pajak.laporan.result";

    public RabbitMqConsumerService(IConfiguration config, ILogger<RabbitMqConsumerService> logger)
    {
        _config = config;
        _logger = logger;
    }

    public async Task ConnectAsync(CancellationToken ct)
    {
        var factory = new ConnectionFactory
        {
            HostName = _config["RabbitMq:Host"] ?? "localhost",
            Port = int.Parse(_config["RabbitMq:Port"] ?? "5672"),
            UserName = _config["RabbitMq:Username"] ?? "guest",
            Password = _config["RabbitMq:Password"] ?? "guest",
        };

        _connection = await factory.CreateConnectionAsync(ct);
        _channel = await _connection.CreateChannelAsync(cancellationToken: ct);

        await _channel.QueueDeclareAsync(SubmittedQueue, durable: true, exclusive: false, autoDelete: false, cancellationToken: ct);
        await _channel.QueueDeclareAsync(ResultQueue, durable: true, exclusive: false, autoDelete: false, cancellationToken: ct);

        _logger.LogInformation("RabbitMQ connected. Waiting for messages on queue: {Queue}", SubmittedQueue);
    }

    public async Task StartConsumingAsync(Func<ReportSubmittedMessage, Task<ReportResultMessage>> handler, CancellationToken ct)
    {
        if (_channel is null) throw new InvalidOperationException("Not connected to RabbitMQ");

        var consumer = new AsyncEventingBasicConsumer(_channel);
        consumer.ReceivedAsync += async (sender, args) =>
        {
            try
            {
                var body = Encoding.UTF8.GetString(args.Body.ToArray());
                var message = JsonSerializer.Deserialize<ReportSubmittedMessage>(body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (message is null)
                {
                    await _channel.BasicNackAsync(args.DeliveryTag, false, false);
                    return;
                }

                _logger.LogInformation("Processing report: {ReportId} for {TaxPayer}", message.ReportId, message.TaxPayerName);

                var result = await handler(message);

                // Publish result back to TaxApi
                var resultBody = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(result));
                var props = new BasicProperties { Persistent = true };
                await _channel.BasicPublishAsync("", ResultQueue, false, props, resultBody);

                _logger.LogInformation("Published result for report {ReportId}: Approved={IsApproved}", result.ReportId, result.IsApproved);

                await _channel.BasicAckAsync(args.DeliveryTag, false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing message");
                await _channel.BasicNackAsync(args.DeliveryTag, false, true);
            }
        };

        await _channel.BasicConsumeAsync(SubmittedQueue, autoAck: false, consumer: consumer, cancellationToken: ct);
    }

    public void Dispose()
    {
        _channel?.Dispose();
        _connection?.Dispose();
    }
}
