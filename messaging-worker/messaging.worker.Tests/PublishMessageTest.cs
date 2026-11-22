using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using RabbitMQ.Client;
using Xunit;

namespace messaging.worker.Tests;

/// <summary>
/// Teste de integração: publica uma mensagem no formato esperado na fila RabbitMQ
/// para o worker consumir. Configure RabbitMQ no appsettings.json do projeto principal
/// ou em appsettings.json nesta pasta / variáveis de ambiente.
/// </summary>
public class PublishMessageTest
{
    private static IConfiguration? _config;

    private static IConfiguration Config => _config ??= new ConfigurationBuilder()
        .SetBasePath(Directory.GetCurrentDirectory())
        .AddJsonFile("appsettings.json", optional: true)
        .AddJsonFile(Path.Combine("..", "appsettings.json"), optional: true)
        .AddJsonFile(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "appsettings.json"), optional: true)
        .AddEnvironmentVariables()
        .Build();

    [Fact]
    public void Publish_email_message_to_queue_worker_can_consume()
    {
        var url = Config["RabbitMQ:Url"];
        var pass = Config["RabbitMQ:pass"];
        var queueName = Config["RabbitMQ:QueueName"] ?? "messages-queue";

        if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(pass))
        {
            // Skip se RabbitMQ não estiver configurado (CI ou máquina sem config)
            return;
        }

        var resolvedUrl = url.Replace("{pass}", Uri.EscapeDataString(pass));

        var message = new
        {
            System = "demo-app",
            SubjectCode = "test-001",
            NotificationType = "email",
            Recipients = new[] { "test@example.com" },
            Metadata = new Dictionary<string, string>
            {
                ["subject"] = "Teste da fila (xUnit)",
                ["template"] = "<p>Olá, teste enviado pelo PublishMessageTest no teste do worker.</p>",
            }
        };

        var json = JsonSerializer.Serialize(message, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        });
        var body = Encoding.UTF8.GetBytes(json);

        var factory = new ConnectionFactory { Uri = new Uri(resolvedUrl) };
        using var connection = factory.CreateConnection();
        using var channel = connection.CreateModel();
        channel.QueueDeclare(queueName, durable: true, exclusive: false, autoDelete: false);
        channel.BasicPublish(exchange: "", routingKey: queueName, body: body);

        // Se chegou aqui, a publicação foi aceita pelo broker (worker pode consumir em seguida)
        Assert.True(body.Length > 0);
    }
}
