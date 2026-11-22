using System.Text;
using Messaging.Api.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;

using Messaging.Api.Health;
namespace Messaging.Api.Services;


public class RabbitMqSender : IServiceBusSender, IDisposable
{
    private readonly IModel _channel;
    private readonly IConnection _connection;
    private readonly string _queueName;
    private readonly ILogger<RabbitMqSender> _logger;
    private readonly BrokerHealthState _healthState;

    public RabbitMqSender(IConfiguration configuration, ILogger<RabbitMqSender> logger, BrokerHealthState healthState)
    {
        _logger = logger;
        _healthState = healthState;

        var rawUrl = configuration["RabbitMQ:Url"];
        var rawPass = configuration["RabbitMQ:pass"];
        _queueName = configuration["RabbitMQ:QueueName"] ?? "messages-queue";

        if (string.IsNullOrEmpty(rawUrl) || string.IsNullOrEmpty(rawPass))
            throw new ArgumentException("RabbitMQ Url or pass not configured properly.");

        var resolvedUrl = rawUrl.Replace("{pass}", Uri.EscapeDataString(rawPass));

        var factory = new ConnectionFactory
        {
            Uri = new Uri(resolvedUrl),
            DispatchConsumersAsync = true
        };

        _connection = factory.CreateConnection();

        // Sem isto a queda passava despercebida: a API seguia aceitando requisicao
        // e perdendo mensagem, com o health respondendo saudavel.
        _connection.ConnectionShutdown += (_, args) =>
        {
            _healthState.SetConnected(false);
            _logger.LogWarning("⚠️ Conexão com o RabbitMQ encerrada: {Reason}", args.ReplyText);
        };

        // O RabbitMQ.Client tem recuperação automática ligada por padrão: ele reconecta
        // sozinho. Sem assinar RecoverySucceeded, o flag ficaria em false para sempre
        // depois de uma queda transitória e o readiness responderia 503 sem motivo.
        if (_connection is IAutorecoveringConnection recuperavel)
        {
            recuperavel.RecoverySucceeded += (_, _) =>
            {
                _healthState.SetConnected(_channel?.IsOpen == true);
                _logger.LogInformation("✅ Conexão com o RabbitMQ recuperada");
            };

            recuperavel.ConnectionRecoveryError += (_, args) =>
            {
                _healthState.SetConnected(false);
                _logger.LogError(args.Exception, "❌ Falha ao recuperar a conexão com o RabbitMQ");
            };
        }

        _channel = _connection.CreateModel();

        // O canal pode fechar sem a conexão cair — a API perderia a capacidade de
        // publicar sem o readiness perceber.
        _channel.ModelShutdown += (_, args) =>
        {
            _healthState.SetConnected(false);
            _logger.LogWarning("⚠️ Canal do RabbitMQ encerrado: {Reason}", args.ReplyText);
        };

        _healthState.SetConnected(true);

        _channel.QueueDeclare(queue: _queueName,
                              durable: true,
                              exclusive: false,
                              autoDelete: false,
                              arguments: null);
    }

    public Task SendMessageAsync(string message)
    {
        var body = Encoding.UTF8.GetBytes(message);

        _channel.BasicPublish(exchange: "",
                              routingKey: _queueName,
                              basicProperties: null,
                              body: body);

        _logger.LogInformation("📤 Mensagem publicada na fila RabbitMQ: {message}", message);
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _channel?.Close();
        _connection?.Close();
    }
}
