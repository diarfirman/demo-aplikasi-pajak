using System.Text;
using System.Text.Json;
using Elastic.Apm;
using Elastic.Apm.Api;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using TaxApi.Models;

namespace TaxApi.Services;

// Message contracts (shared with ReportProcessor)
public record ReportSubmittedMessage(
    string ReportId,
    string TaxPayerId,
    string TaxPayerNpwp,
    string TaxPayerName,
    string ReportType,
    string Period,
    decimal TotalIncome,
    decimal TotalTax
);

public record ReportResultMessage(
    string ReportId,
    string TaxPayerId,
    bool IsApproved,
    string? RejectionReason
);

public class RabbitMqService : IHostedService, IDisposable
{
    private readonly IConfiguration _config;
    private readonly ILogger<RabbitMqService> _logger;
    private readonly ElasticsearchService _esService;

    private IConnection? _connection;
    private IChannel? _channel;

    private const string SubmittedQueue = "pajak.laporan.submitted";
    private const string ResultQueue = "pajak.laporan.result";

    public RabbitMqService(IConfiguration config, ILogger<RabbitMqService> logger, ElasticsearchService esService)
    {
        _config = config;
        _logger = logger;
        _esService = esService;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            var factory = new ConnectionFactory
            {
                HostName = _config["RabbitMq:Host"] ?? "localhost",
                Port = int.Parse(_config["RabbitMq:Port"] ?? "5672"),
                UserName = _config["RabbitMq:Username"] ?? "guest",
                Password = _config["RabbitMq:Password"] ?? "guest",
            };

            _connection = await factory.CreateConnectionAsync(cancellationToken);
            _channel = await _connection.CreateChannelAsync(cancellationToken: cancellationToken);

            // Declare queues
            await _channel.QueueDeclareAsync(SubmittedQueue, durable: true, exclusive: false, autoDelete: false, cancellationToken: cancellationToken);
            await _channel.QueueDeclareAsync(ResultQueue, durable: true, exclusive: false, autoDelete: false, cancellationToken: cancellationToken);

            // Start consuming results from ReportProcessor
            var consumer = new AsyncEventingBasicConsumer(_channel);
            consumer.ReceivedAsync += OnReportResultReceived;
            await _channel.BasicConsumeAsync(ResultQueue, autoAck: false, consumer: consumer, cancellationToken: cancellationToken);

            _logger.LogInformation("RabbitMQ connected. Consuming queue: {Queue}", ResultQueue);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to connect to RabbitMQ. Running without messaging.");
        }
    }

    private async Task OnReportResultReceived(object sender, BasicDeliverEventArgs args)
    {
        // Read traceparent from message headers to link trace ke ReportProcessor
        var traceparent = GetTraceparentFromHeaders(args.BasicProperties.Headers);
        var tracingData = DistributedTracingData.TryDeserializeFromString(traceparent);

        var transaction = Agent.Tracer.StartTransaction(
            "RabbitMQ CONSUME pajak.laporan.result",
            ApiConstants.TypeMessaging,
            tracingData);

        try
        {
            var body = Encoding.UTF8.GetString(args.Body.ToArray());
            var result = JsonSerializer.Deserialize<ReportResultMessage>(body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (result is null)
            {
                transaction.SetLabel("error", "deserialization_failed");
                await _channel!.BasicNackAsync(args.DeliveryTag, false, false);
                return;
            }

            transaction.SetLabel("report_id", result.ReportId);
            transaction.SetLabel("is_approved", result.IsApproved.ToString());

            _logger.LogInformation("Received report result: ReportId={ReportId}, Approved={IsApproved}", result.ReportId, result.IsApproved);

            var report = await _esService.GetReportByIdAsync(result.ReportId);
            if (report is not null)
            {
                var updatedReport = report with
                {
                    Status = result.IsApproved ? "Approved" : "Rejected",
                    RejectionReason = result.RejectionReason,
                    ProcessedAt = DateTimeOffset.UtcNow,
                };

                await transaction.CaptureSpan("ES UpdateReport", ApiConstants.TypeDb, async () =>
                    await _esService.UpdateReportAsync(updatedReport));

                var notification = new Notification
                {
                    TaxPayerId = result.TaxPayerId,
                    Title = result.IsApproved ? "Laporan Disetujui" : "Laporan Ditolak",
                    Message = result.IsApproved
                        ? $"Laporan SPT periode {report.Period} Anda telah disetujui."
                        : $"Laporan SPT periode {report.Period} ditolak. Alasan: {result.RejectionReason}",
                    Type = result.IsApproved ? "Success" : "Error",
                };

                await transaction.CaptureSpan("ES IndexNotification", ApiConstants.TypeDb, async () =>
                    await _esService.IndexNotificationAsync(notification));

                _logger.LogInformation("Report {ReportId} status updated to {Status}", result.ReportId, updatedReport.Status);
            }

            await _channel!.BasicAckAsync(args.DeliveryTag, false);
        }
        catch (Exception ex)
        {
            transaction.CaptureException(ex);
            _logger.LogError(ex, "Error processing report result message");
            await _channel!.BasicNackAsync(args.DeliveryTag, false, true);
        }
        finally
        {
            transaction.End();
        }
    }

    private static string? GetTraceparentFromHeaders(IDictionary<string, object?>? headers)
    {
        if (headers?.TryGetValue("elastic-apm-traceparent", out var val) == true)
        {
            if (val is byte[] bytes) return Encoding.UTF8.GetString(bytes);
            if (val is string str) return str;
        }
        return null;
    }

    public async Task PublishReportSubmittedAsync(ReportSubmittedMessage message)
    {
        if (_channel is null)
        {
            _logger.LogWarning("RabbitMQ channel not available. Skipping publish.");
            return;
        }

        var currentTransaction = Agent.Tracer.CurrentTransaction;
        var captureAction = async () =>
        {
            var body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(message));
            var props = new BasicProperties { Persistent = true };

            await _channel.BasicPublishAsync(
                exchange: "",
                routingKey: SubmittedQueue,
                mandatory: false,
                basicProperties: props,
                body: body);

            _logger.LogInformation("Published report submitted: ReportId={ReportId}", message.ReportId);
        };

        if (currentTransaction is not null)
        {
            await currentTransaction.CaptureSpan(
                $"RabbitMQ PUBLISH {SubmittedQueue}",
                ApiConstants.TypeMessaging,
                async (span) =>
                {
                    span.SetLabel("report_id", message.ReportId);
                    span.SetLabel("queue", SubmittedQueue);

                    // Embed traceparent ke header agar ReportProcessor bisa link trace-nya
                    var body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(message));
                    var props = new BasicProperties
                    {
                        Persistent = true,
                        Headers = new Dictionary<string, object?>
                        {
                            { "elastic-apm-traceparent", Encoding.UTF8.GetBytes(
                                span.OutgoingDistributedTracingData?.SerializeToString() ?? "") }
                        }
                    };

                    await _channel.BasicPublishAsync(
                        exchange: "",
                        routingKey: SubmittedQueue,
                        mandatory: false,
                        basicProperties: props,
                        body: body);

                    _logger.LogInformation("Published report submitted: ReportId={ReportId}", message.ReportId);
                }
            );
        }
        else
        {
            await captureAction();
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_channel is not null)
            await _channel.CloseAsync(cancellationToken);
        if (_connection is not null)
            await _connection.CloseAsync(cancellationToken);
    }

    public void Dispose()
    {
        _channel?.Dispose();
        _connection?.Dispose();
    }
}
