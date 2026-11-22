namespace messaging.worker.Health;

/// <summary>
/// Estado da conexão com o RabbitMQ, compartilhado entre o consumidor e o endpoint
/// de health. Sem isto o container só sabe dizer que o processo existe: um worker
/// que perdeu o broker segue aparecendo como saudável e o deploy fica verde com a
/// fila parada.
/// </summary>
public class BrokerHealthState
{
    private volatile bool _connected;

    public bool IsConnected => _connected;

    public void SetConnected(bool connected) => _connected = connected;
}
