using System.Text;
using System.Text.Json;
using messaging.worker.Interfaces;

namespace messaging.worker.Services;

public class ComteleSmsSender : ISmsSender
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;
    private readonly ILogger<ComteleSmsSender> _logger;
    private readonly bool _enabled;

    public ComteleSmsSender(HttpClient httpClient, IConfiguration configuration, ILogger<ComteleSmsSender> logger)
    {
        _httpClient = httpClient;
        _configuration = configuration;
        _logger = logger;
        _enabled = _configuration.GetValue<bool>("Comtele:Enabled");
    }

    public async Task SendSmsAsync(string phone, string message)
    {
        if (!_enabled)
        {
            _logger.LogInformation("📱 [SIMULADO] SMS enviado para {Phone}", phone);
            return;
        }

        var apiKey = _configuration["Comtele:ApiKey"];
        var senderId = _configuration["Comtele:SenderId"]; // Nome do Remetente configurado

        var url = "https://sms.comtele.com.br/api/v2/send";

        var payload = new
        {
            Sender = senderId,
            Receivers = phone,
            Content = message
        };

        var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
        };

        request.Headers.Add("auth-key", apiKey);

        try
        {
            var response = await _httpClient.SendAsync(request);

            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("📱 SMS enviado com sucesso para {Phone}", phone);
            }
            else
            {
                var responseContent = await response.Content.ReadAsStringAsync();
                _logger.LogError("❌ Falha ao enviar SMS para {Phone}. StatusCode: {StatusCode}. Erro: {ErrorContent}", phone, response.StatusCode, responseContent);
                throw new Exception($"Erro ao enviar SMS: {responseContent}");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro inesperado ao enviar SMS para {Phone}", phone);
            throw;
        }
    }
}
