using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Messaging.Api.Health;

/// <summary>
/// Readiness: a API só consegue cumprir o que promete se conseguir publicar na
/// fila. Com o broker fora, aceitar a requisição seria perder a mensagem.
/// </summary>
public class BrokerHealthCheck : IHealthCheck
{
    private readonly BrokerHealthState _state;

    public BrokerHealthCheck(BrokerHealthState state) => _state = state;

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(
            _state.IsConnected
                ? HealthCheckResult.Healthy("RabbitMQ conectado")
                : HealthCheckResult.Unhealthy("RabbitMQ inacessível"));
    }
}
