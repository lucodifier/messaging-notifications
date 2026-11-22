using System.Text;
using System.Text.Json;
using messaging.worker.Interfaces;
using messaging.worker.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;

namespace messaging.worker.Services;

public class StatusPublisher : IStatusPublisher, IDisposable
{
    private readonly ILogger<StatusPublisher> _logger;
    private readonly IConfiguration _configuration;
    private readonly string _queueName;
    private readonly object _lock = new();
    private IConnection? _connection;
    private IModel? _channel;

    public StatusPublisher(IConfiguration configuration, ILogger<StatusPublisher> logger)
    {
        _logger = logger;
        _configuration = configuration;
        _queueName = configuration["RabbitMQ:StatusQueueName"] ?? "messages-status-queue";
    }

    private const int MaxAttempts = 3;
    private static readonly TimeSpan ConfirmTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan AbortTimeout = TimeSpan.FromSeconds(1);

    public async Task PublishAsync(StatusReport report)
    {
        var json = JsonSerializer.Serialize(report, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
        var body = Encoding.UTF8.GetBytes(json);

        // Falha ao publicar status nunca pode derrubar o consumo principal
        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            try
            {
                lock (_lock)
                {
                    EnsureChannel();

                    var properties = _channel!.CreateBasicProperties();
                    properties.Persistent = true;

                    _channel.BasicPublish(exchange: "",
                                          routingKey: _queueName,
                                          basicProperties: properties,
                                          body: body);

                    // Só conclui com o ack do broker. WaitForConfirmsOrDie não: fecha o canal bloqueando até 10s
                    if (!_channel.WaitForConfirms(ConfirmTimeout, out var timedOut) || timedOut)
                        throw new IOException(timedOut
                            ? "Timeout aguardando confirmação do broker."
                            : "Broker recusou o status report (nack).");
                }

                _logger.LogInformation("📊 Status report publicado: {CorrelationId} → {Status}", report.CorrelationId, report.Status);
                return;
            }
            catch (Exception ex)
            {
                AbortConnection();

                if (attempt == MaxAttempts)
                {
                    // A mensagem é confirmada mesmo assim: reprocessar reenviaria o email; a API marca UNKNOWN
                    _logger.LogError(ex, "❌ Status report de {CorrelationId} não confirmado pelo broker após {Max} tentativas.",
                        report.CorrelationId, MaxAttempts);
                    return;
                }

                _logger.LogWarning(ex, "⚠️ Falha ao publicar status report de {CorrelationId} (tentativa {Attempt}/{Max}).",
                    report.CorrelationId, attempt, MaxAttempts);
                await Task.Delay(TimeSpan.FromMilliseconds(500 * attempt));
            }
        }
    }

    private void EnsureChannel()
    {
        if (_channel is { IsOpen: true })
            return;

        var rawUrl = _configuration["RabbitMQ:Url"];
        var rawPass = _configuration["RabbitMQ:pass"];

        if (string.IsNullOrWhiteSpace(rawUrl) || string.IsNullOrWhiteSpace(rawPass))
            throw new ArgumentException("RabbitMQ:Url or pass not configured properly.");

        var resolvedUrl = rawUrl.Replace("{pass}", Uri.EscapeDataString(rawPass));
        var factory = new ConnectionFactory { Uri = new Uri(resolvedUrl) };

        _connection = factory.CreateConnection();
        _channel = _connection.CreateModel();
        _channel.ConfirmSelect();
        _channel.QueueDeclare(_queueName, durable: true, exclusive: false, autoDelete: false);
    }

    // Caminho de falha: abort com prazo curto, fora do lock (Close bloqueia até 10s)
    private void AbortConnection()
    {
        IConnection? connection;
        lock (_lock)
        {
            connection = _connection;
            _channel = null;
            _connection = null;
        }

        try
        {
            connection?.Abort(AbortTimeout);
        }
        catch
        {
            // conexão já inválida — será recriada na próxima publicação
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            try
            {
                _channel?.Close();
                _connection?.Close();
            }
            catch
            {
                // canal já inválido
            }
            finally
            {
                _channel = null;
                _connection = null;
            }
        }
    }
}
