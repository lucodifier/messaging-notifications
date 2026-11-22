using messaging.worker.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Net.Http.Headers;
using System.Text;

namespace messaging.worker.Services;

public class TwilioWhatsAppSender : IWhatsAppSender
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<TwilioWhatsAppSender> _logger;
    private readonly HttpClient _httpClient;

    public TwilioWhatsAppSender(IConfiguration configuration, ILogger<TwilioWhatsAppSender> logger, IHttpClientFactory httpClientFactory)
    {
        _configuration = configuration;
        _logger = logger;
        _httpClient = httpClientFactory.CreateClient();
    }

    public async Task SendWhatsAppAsync(string to, string message)
    {
        if (!_configuration.GetValue<bool>("Twilio:Enabled"))
        {
            _logger.LogInformation("Twilio WhatsApp está desabilitado. Simulando envio para {Phone}", to);
            return;
        }

        var accountSid = _configuration["Twilio:AccountSid"];
        var authToken = _configuration["Twilio:AuthToken"];
        var from = _configuration["Twilio:From"];

        var basicAuth = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{accountSid}:{authToken}"));

        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", basicAuth);

        var payload = new Dictionary<string, string>
        {
            { "From", from! },
            { "To", $"whatsapp:{to}" },
            { "Body", message }
        };

        var content = new FormUrlEncodedContent(payload);

        var url = $"https://api.twilio.com/2010-04-01/Accounts/{accountSid}/Messages.json";

        try
        {
            var response = await _httpClient.PostAsync(url, content);

            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("💬 WhatsApp enviado para {Phone}", to);
            }
            else
            {
                var responseContent = await response.Content.ReadAsStringAsync();
                _logger.LogError("Erro ao enviar WhatsApp para {Phone}. Status: {Status}. Detalhes: {Details}", to, response.StatusCode, responseContent);
                throw new Exception($"Erro ao enviar WhatsApp: {response.StatusCode} - {responseContent}");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro geral ao tentar enviar WhatsApp para {Phone}", to);
            throw;
        }
    }
}
