using Messaging.Api.Interfaces;

namespace Messaging.Api.Health;

/// <summary>
/// Resolve o <see cref="IServiceBusSender"/> no startup.
///
/// O sender é singleton e abre a conexão com o RabbitMQ no construtor, mas nada o
/// resolvia na subida: o `StatusConsumerService` não o usa, o `OutboxRetryService`
/// só o resolve dentro do ciclo e quando há mensagem pendente, e os endpoints o
/// recebem por requisição. Resultado: depois do `up -d` o readiness ficava em 503
/// até alguém enviar a primeira mensagem — e o portão de deploy derrubava e
/// revertia todo deploy.
/// </summary>
public class BrokerConnectionStarter : IHostedService
{
    private readonly IServiceProvider _provider;
    private readonly ILogger<BrokerConnectionStarter> _logger;

    public BrokerConnectionStarter(IServiceProvider provider, ILogger<BrokerConnectionStarter> logger)
    {
        _provider = provider;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        // Falhar aqui é melhor que subir sem broker: o container não fica healthy e o
        // portão de deploy reverte, em vez de a API aceitar requisição e perder mensagem.
        _ = _provider.GetRequiredService<IServiceBusSender>();
        _logger.LogInformation("🐰 Conexão com o RabbitMQ estabelecida no startup");
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
