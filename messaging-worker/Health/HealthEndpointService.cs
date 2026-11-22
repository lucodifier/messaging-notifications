using System.Net;
using System.Text;

namespace messaging.worker.Health;

/// <summary>
/// Servidor de health em porta interna. O worker usa o host genérico, sem ASP.NET,
/// então um <see cref="HttpListener"/> resolve sem trocar o modelo de hospedagem.
///
/// - <c>/health</c>      readiness: a conexão com o broker está aberta.
/// - <c>/health/live</c> liveness: o processo responde, sem tocar em dependência.
///
/// A separação existe para que uma oscilação do broker não marque todos os
/// containers como unhealthy ao mesmo tempo.
/// </summary>
public class HealthEndpointService : BackgroundService
{
    private const int DefaultPort = 8080;

    private readonly ILogger<HealthEndpointService> _logger;
    private readonly BrokerHealthState _brokerState;
    private readonly int _port;

    public HealthEndpointService(
        ILogger<HealthEndpointService> logger,
        BrokerHealthState brokerState,
        IConfiguration configuration)
    {
        _logger = logger;
        _brokerState = brokerState;
        _port = int.TryParse(configuration["HEALTH_PORT"], out var configured) ? configured : DefaultPort;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        HttpListener listener;

        try
        {
            listener = StartListener("+");
        }
        catch (HttpListenerException) when (OperatingSystem.IsWindows())
        {
            // No Windows, escutar em todas as interfaces ("+") exige admin ou URL ACL.
            // Em desenvolvimento local basta localhost; em container (Linux) o "+" funciona.
            try
            {
                listener = StartListener("localhost");
                _logger.LogWarning("🩺 Sem permissão para escutar em todas as interfaces; health endpoint restrito a localhost");
            }
            catch (HttpListenerException ex)
            {
                _logger.LogError(ex, "❌ Não foi possível abrir o health endpoint na porta {Port}", _port);
                throw;
            }
        }
        catch (HttpListenerException ex)
        {
            // Sem endpoint de health o HEALTHCHECK do container falha, e é assim que
            // deve ser: melhor o deploy quebrar do que subir sem verificação.
            _logger.LogError(ex, "❌ Não foi possível abrir o health endpoint na porta {Port}", _port);
            throw;
        }

        using var listenerScope = listener;

        _logger.LogInformation("🩺 Health endpoint escutando na porta {Port}", _port);

        stoppingToken.Register(() =>
        {
            if (listener.IsListening) listener.Stop();
        });

        while (!stoppingToken.IsCancellationRequested)
        {
            HttpListenerContext context;
            try
            {
                context = await listener.GetContextAsync();
            }
            catch (Exception) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // Qualquer excecao que escape daqui sobe do ExecuteAsync e o
                // BackgroundService derruba o host inteiro: um cliente abortando a
                // conexao mataria o worker. Loga e segue aceitando requisicoes.
                _logger.LogWarning(ex, "Falha ao aceitar conexao no health endpoint");
                if (!listener.IsListening) break;
                continue;
            }

            _ = Task.Run(() => RespondAsync(context), CancellationToken.None);
        }
    }

    private HttpListener StartListener(string host)
    {
        var listener = new HttpListener();
        listener.Prefixes.Add($"http://{host}:{_port}/");
        try
        {
            listener.Start();
            return listener;
        }
        catch
        {
            listener.Close();
            throw;
        }
    }

    private async Task RespondAsync(HttpListenerContext context)
    {
        var path = context.Request.Url?.AbsolutePath ?? "/";

        try
        {
            if (path.StartsWith("/health/live", StringComparison.Ordinal))
            {
                await WriteAsync(context, 200, "{\"status\":\"ok\"}");
                return;
            }

            if (path.StartsWith("/health", StringComparison.Ordinal))
            {
                var connected = _brokerState.IsConnected;
                await WriteAsync(
                    context,
                    connected ? 200 : 503,
                    $"{{\"status\":\"{(connected ? "ok" : "error")}\",\"rabbitmq\":\"{(connected ? "up" : "down")}\"}}");
                return;
            }

            await WriteAsync(context, 404, "{}");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Falha ao responder o health endpoint");
        }
    }

    private static async Task WriteAsync(HttpListenerContext context, int statusCode, string body)
    {
        var bytes = Encoding.UTF8.GetBytes(body);
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/json";
        context.Response.ContentLength64 = bytes.Length;
        await context.Response.OutputStream.WriteAsync(bytes);
        context.Response.Close();
    }
}
