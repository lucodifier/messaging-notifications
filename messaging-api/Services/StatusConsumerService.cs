using System.Text;
using System.Text.Json;
using Messaging.Api.Contracts;
using Messaging.Api.Interfaces;
using Messaging.Api.Models;
using Microsoft.Data.Sqlite;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Messaging.Api.Services;

public class StatusConsumerService : BackgroundService
{
    // BUSY, LOCKED, IOERR, FULL: o report volta para a fila
    private static readonly int[] TransientSqliteErrors = { 5, 6, 10, 13 };
    private static readonly TimeSpan RequeueDelay = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan ReconnectInterval = TimeSpan.FromSeconds(30);

    private readonly ILogger<StatusConsumerService> _logger;
    private readonly IConfiguration _configuration;
    private readonly IOutboxRepository _outbox;
    private IConnection? _connection;
    private IModel? _channel;

    public StatusConsumerService(
        ILogger<StatusConsumerService> logger,
        IConfiguration configuration,
        IOutboxRepository outbox)
    {
        _logger = logger;
        _configuration = configuration;
        _outbox = outbox;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var queueName = _configuration["RabbitMQ:StatusQueueName"] ?? "messages-status-queue";

        // Reconecta em loop para não derrubar a API se o RabbitMQ estiver fora
        while (!stoppingToken.IsCancellationRequested)
        {
            if (NeedsReconnect())
            {
                try
                {
                    Close();
                    Connect(queueName);
                    _logger.LogInformation("📡 StatusConsumer escutando fila: {QueueName}", queueName);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "❌ Falha ao conectar no RabbitMQ (status queue). Nova tentativa em 30s.");
                }
            }

            await Task.Delay(ReconnectInterval, stoppingToken);
        }
    }

    private bool NeedsReconnect()
    {
        if (_connection is null || _channel is null)
            return true;

        // Conexão caída: a recuperação automática refaz
        if (!_connection.IsOpen)
            return _connection is not IAutorecoveringConnection;

        return !_channel.IsOpen;
    }

    private void Connect(string queueName)
    {
        var rawUrl = _configuration["RabbitMQ:Url"];
        var rawPass = _configuration["RabbitMQ:pass"];

        if (string.IsNullOrEmpty(rawUrl) || string.IsNullOrEmpty(rawPass))
            throw new ArgumentException("RabbitMQ Url or pass not configured properly.");

        var resolvedUrl = rawUrl.Replace("{pass}", Uri.EscapeDataString(rawPass));
        var factory = new ConnectionFactory
        {
            Uri = new Uri(resolvedUrl),
            DispatchConsumersAsync = true
        };

        _connection = factory.CreateConnection();
        // Local: o handler confirma no canal que entregou a mensagem
        var channel = _connection.CreateModel();
        _channel = channel;
        channel.QueueDeclare(queueName, durable: true, exclusive: false, autoDelete: false);

        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.Received += async (model, ea) =>
        {
            try
            {
                var body = Encoding.UTF8.GetString(ea.Body.ToArray());
                var report = JsonSerializer.Deserialize<StatusReport>(body, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (report is null || string.IsNullOrWhiteSpace(report.CorrelationId))
                    throw new Exception("Status report nulo ou sem correlationId.");

                var status = report.Status.ToUpperInvariant();
                if (status != OutboxStatus.Sent && status != OutboxStatus.Failed)
                    throw new Exception($"Status desconhecido no report: {report.Status}");

                if (report.DeliveredRecipients is { Count: > 0 })
                    await _outbox.AddDeliveredRecipientsAsync(report.CorrelationId, report.DeliveredRecipients);

                // Report para mensagem UNKNOWN = worker estava só atrasado, não perdeu o report.
                // Se isso for frequente, Outbox:UnknownTimeoutSeconds está curto demais.
                var previous = await _outbox.GetByCorrelationIdAsync(report.CorrelationId);

                var updated = await _outbox.UpdateStatusAsync(report.CorrelationId, status, report.Error);
                if (updated && previous?.Status == OutboxStatus.Unknown)
                    _logger.LogWarning(
                        "⏰ OutboxLateReport: {CorrelationId} estava UNKNOWN e recebeu report tardio → {Status} ({Age:F0}s após a última atualização). Considere aumentar Outbox:UnknownTimeoutSeconds.",
                        report.CorrelationId, status, (DateTime.UtcNow - previous.UpdatedAt).TotalSeconds);
                else if (updated)
                    _logger.LogInformation("📊 Outbox atualizado: {CorrelationId} → {Status}", report.CorrelationId, status);
                else
                    _logger.LogWarning("⚠️ Status report ignorado (correlationId desconhecido ou estado terminal SENT/DEAD): {CorrelationId}", report.CorrelationId);

                channel.BasicAck(ea.DeliveryTag, multiple: false);
            }
            catch (SqliteException ex) when (TransientSqliteErrors.Contains(ex.SqliteErrorCode))
            {
                _logger.LogWarning(ex, "⚠️ Erro transitório do SQLite ao gravar status report. Devolvendo à fila em 5s.");
                await Task.Delay(RequeueDelay);
                try
                {
                    channel.BasicNack(ea.DeliveryTag, multiple: false, requeue: true);
                }
                catch (Exception nackEx)
                {
                    _logger.LogError(nackEx, "⚠️ Falha ao devolver status report à fila.");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Erro ao processar status report. Descartando.");
                try
                {
                    channel.BasicNack(ea.DeliveryTag, multiple: false, requeue: false);
                }
                catch (Exception nackEx)
                {
                    _logger.LogError(nackEx, "⚠️ Falha ao enviar BasicNack do status report.");
                }
            }
        };

        channel.BasicConsume(queue: queueName, autoAck: false, consumer: consumer);
    }

    private void Close()
    {
        try { _channel?.Close(); } catch { /* canal já inválido */ }
        try { _connection?.Close(); } catch { /* conexão já inválida */ }
        _channel = null;
        _connection = null;
    }

    public override void Dispose()
    {
        Close();
        base.Dispose();
    }
}
