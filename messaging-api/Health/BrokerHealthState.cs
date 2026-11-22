namespace Messaging.Api.Health;

/// <summary>
/// Estado da conexão com o RabbitMQ, compartilhado entre o publisher e o health
/// check. Sem isto o `/api/health` responde saudável mesmo com o broker fora —
/// `AddHealthChecks()` sem nenhum check registrado sempre devolve Healthy.
///
/// </summary>
public class BrokerHealthState
{
    private volatile bool _connected;

    public bool IsConnected => _connected;

    public void SetConnected(bool connected) => _connected = connected;
}
