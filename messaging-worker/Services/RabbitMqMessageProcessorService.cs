using System.Globalization;
using System.Text;
using System.Text.Json;
using messaging.worker.Interfaces;
using messaging.worker.Models;
using messaging.worker.Services.Handlers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using messaging.worker.Health;

namespace messaging.worker.Services;

public class RabbitMqMessageProcessorService : BackgroundService
{
    private readonly ILogger<RabbitMqMessageProcessorService> _logger;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _configuration;
    private readonly IStatusPublisher _statusPublisher;
    private readonly BrokerHealthState _brokerState;
    private IModel _channel;
    private IConnection _connection;
    private string _queueName;

    public RabbitMqMessageProcessorService(
        ILogger<RabbitMqMessageProcessorService> logger,
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration,
        IStatusPublisher statusPublisher,
        BrokerHealthState brokerState)
    {
        _logger = logger;
        _scopeFactory = scopeFactory;
        _configuration = configuration;
        _statusPublisher = statusPublisher;
        _brokerState = brokerState;
    }

    public override Task StartAsync(CancellationToken cancellationToken)
    {
        var rawUrl = _configuration["RabbitMQ:Url"];
        var pass = _configuration["RabbitMQ:pass"];
        _queueName = _configuration["RabbitMQ:QueueName"] ?? "messages-queue";

        if (string.IsNullOrWhiteSpace(rawUrl) || string.IsNullOrWhiteSpace(pass))
            throw new ArgumentException("RabbitMQ:Url or pass not configured properly.");

        var resolvedUrl = rawUrl.Replace("{pass}", Uri.EscapeDataString(pass));
        var factory = new ConnectionFactory
        {
            Uri = new Uri(resolvedUrl),
            DispatchConsumersAsync = true
        };

        _connection = factory.CreateConnection();

        // Sem isto a queda passava despercebida: o processo seguia de pé sem consumir
        // nada e o container continuava reportado como saudável.
        _connection.ConnectionShutdown += (_, args) =>
        {
            _brokerState.SetConnected(false);
            _logger.LogWarning("⚠️ Conexão com o RabbitMQ encerrada: {Reason}", args.ReplyText);
        };

        // O RabbitMQ.Client tem recuperação automática ligada por padrão: ele reconecta
        // sozinho. Sem assinar RecoverySucceeded, o flag ficaria em false para sempre
        // depois de uma queda transitória e o container seguiria unhealthy sem motivo.
        if (_connection is IAutorecoveringConnection recuperavel)
        {
            recuperavel.RecoverySucceeded += (_, _) =>
            {
                _brokerState.SetConnected(_channel?.IsOpen == true);
                _logger.LogInformation("✅ Conexão com o RabbitMQ recuperada");
            };

            recuperavel.ConnectionRecoveryError += (_, args) =>
            {
                _brokerState.SetConnected(false);
                _logger.LogError(args.Exception, "❌ Falha ao recuperar a conexão com o RabbitMQ");
            };
        }

        _channel = _connection.CreateModel();

        // O canal pode fechar sem a conexão cair — nesse caso o consumo para e o
        // readiness precisa acompanhar.
        _channel.ModelShutdown += (_, args) =>
        {
            _brokerState.SetConnected(false);
            _logger.LogWarning("⚠️ Canal do RabbitMQ encerrado: {Reason}", args.ReplyText);
        };

        _brokerState.SetConnected(true);
        _channel.QueueDeclare(_queueName, durable: true, exclusive: false, autoDelete: false);
        _channel.BasicQos(prefetchSize: 0, prefetchCount: 10, global: false);

        _logger.LogInformation("🚀 messaging.worker iniciado! Escutando fila: {QueueName}", _queueName);
        return base.StartAsync(cancellationToken);
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("🚀 messaging.worker iniciado! Escutando fila...");
        var consumer = new AsyncEventingBasicConsumer(_channel);
        consumer.Received += async (model, ea) =>
        {
            using var scope = _scopeFactory.CreateScope();
            NotificationMessage? notification = null;
            var delivered = new List<string>();

            try
            {
                var messageBody = Encoding.UTF8.GetString(ea.Body.ToArray());
                _logger.LogInformation("📥 Mensagem recebida: {Message}", messageBody);

                notification = JsonSerializer.Deserialize<NotificationMessage>(messageBody, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (notification is null)
                    throw new Exception("Mensagem nula ou inválida.");

                if (ea.Redelivered)
                    _logger.LogWarning("🔁 Mensagem reentregue pelo broker: {CorrelationId}", notification.CorrelationId);

                await ProcessNotificationAsync(scope, notification, delivered);

                await ReportStatusAsync(notification, "SENT", null);

                _channel.BasicAck(ea.DeliveryTag, multiple: false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Erro ao processar mensagem.");

                await ReportStatusAsync(notification, "FAILED", ex.Message, delivered);

                try
                {
                    if (notification?.CorrelationId is not null)
                    {
                        // Fluxo Outbox: FAILED reportado — ack para não perder/travar; retry é dirigido pelo scheduler da API
                        _channel.BasicAck(ea.DeliveryTag, multiple: false);
                    }
                    else
                    {
                        // Formato antigo (sem correlationId): mantém comportamento legado
                        _channel.BasicNack(ea.DeliveryTag, multiple: false, requeue: false);
                    }
                }
                catch (Exception ackEx)
                {
                    _logger.LogError(ackEx, "⚠️ Falha ao enviar Ack/Nack para a mensagem.");
                }
            }
        };

        _channel.BasicConsume(queue: _queueName, autoAck: false, consumer: consumer);
        return Task.CompletedTask;
    }

    private async Task ProcessNotificationAsync(IServiceScope scope, NotificationMessage notification, List<string> delivered)
    {
        var metadata = NormalizeMetadata(notification.Metadata);
        var subject = metadata.GetValueOrDefault("subject") ?? "Sem assunto";

        var emailHandler = scope.ServiceProvider.GetRequiredService<EmailHandler>();
        var smsHandler = scope.ServiceProvider.GetRequiredService<SmsHandler>();
        var whatsAppHandler = scope.ServiceProvider.GetRequiredService<WhatsAppHandler>();

        switch (notification.NotificationType.ToLower())
        {
            case "email":
                var template = metadata.GetValueOrDefault("template") ?? "";
                var mergeFields = metadata
                    .Where(kv => kv.Key != "template" && kv.Key != "subject")
                    .ToDictionary(kv => kv.Key, kv => kv.Value);

                foreach (var recipient in notification.Recipients)
                {
                    await emailHandler.SendAsync(recipient, subject, template, mergeFields, notification.System);
                    delivered.Add(recipient);
                }
                break;

            case "sms":
                var sms = metadata.GetValueOrDefault("message") ?? "";
                foreach (var recipient in notification.Recipients)
                {
                    await smsHandler.SendAsync(recipient, sms);
                    delivered.Add(recipient);
                }
                break;

            case "whatsapp":
                var msg = metadata.GetValueOrDefault("message") ?? "";
                foreach (var recipient in notification.Recipients)
                {
                    await whatsAppHandler.SendAsync(recipient, msg);
                    delivered.Add(recipient);
                }
                break;

            default:
                throw new Exception($"Tipo de notificação desconhecido: {notification.NotificationType}");
        }

        _logger.LogInformation("✅ Mensagem processada: {Tipo}", notification.NotificationType);
    }

    private async Task ReportStatusAsync(NotificationMessage? notification, string status, string? error,
        List<string>? deliveredRecipients = null)
    {
        // Mensagens legadas (sem correlationId) não geram report
        if (notification?.CorrelationId is null)
            return;

        const int maxErrorLength = 2000;
        if (error is { Length: > maxErrorLength })
            error = error[..maxErrorLength];

        await _statusPublisher.PublishAsync(new StatusReport
        {
            CorrelationId = notification.CorrelationId,
            Status = status,
            Error = error,
            DeliveredRecipients = deliveredRecipients is { Count: > 0 } ? deliveredRecipients : null,
            Provider = notification.NotificationType?.ToLower(),
            FinishedAt = DateTime.UtcNow
        });
    }

    public override void Dispose()
    {
        _channel?.Close();
        _brokerState.SetConnected(false);
        _connection?.Close();
        base.Dispose();
    }
    private static Dictionary<string, string> NormalizeMetadata(Dictionary<string, JsonElement>? metadata)
    {
        if (metadata is null || metadata.Count == 0)
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var result = new Dictionary<string, string>(metadata.Count, StringComparer.OrdinalIgnoreCase);

        foreach (var (key, value) in metadata)
        {
            result[key] = ConvertJsonElementToString(value);
        }

        return result;
    }

    private static string ConvertJsonElementToString(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString() ?? string.Empty,
            JsonValueKind.Number => element.TryGetInt64(out var longValue)
                ? longValue.ToString(CultureInfo.InvariantCulture)
                : element.TryGetDouble(out var doubleValue)
                    ? doubleValue.ToString(CultureInfo.InvariantCulture)
                    : element.GetRawText(),
            JsonValueKind.True or JsonValueKind.False => element.GetBoolean().ToString(),
            JsonValueKind.Null or JsonValueKind.Undefined => string.Empty,
            JsonValueKind.Array or JsonValueKind.Object => element.GetRawText(),
            _ => element.GetRawText()
        };
    }
}
