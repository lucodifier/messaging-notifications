using System.Text.Json;
using System.Text.RegularExpressions;
using messaging.worker.Interfaces;

namespace messaging.worker.Services;

public partial class ElasticEmailSender : IEmailSender
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<ElasticEmailSender> _logger;
    private readonly HttpClient _httpClient;
    private readonly bool _enabled;

    private static readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true };

    [GeneratedRegex(@"<[a-zA-Z][^>]*>", RegexOptions.Singleline)]
    private static partial Regex HtmlTagRegex();

    public ElasticEmailSender(IConfiguration configuration, ILogger<ElasticEmailSender> logger)
    {
        _configuration = configuration;
        _logger = logger;

        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(40)
        };

        _enabled = _configuration.GetValue<bool>("ElasticEmail:Enabled");
    }

    public async Task SendEmailAsync(string to, string subject, string template, Dictionary<string, string> mergeFields)
    {
        if (!_enabled)
        {
            _logger.LogInformation("✉️ [SIMULADO] Email enviado para {Email}", to);
            return;
        }

        var apiKey = _configuration["ElasticEmail:ApiKey"];
        var fromEmail = _configuration["ElasticEmail:FromEmail"];
        var fromName = _configuration["ElasticEmail:FromName"];
        var url = _configuration["ElasticEmail:ApiUrl"];

        var isHtml = HtmlTagRegex().IsMatch(template);

        var keyValues = new List<KeyValuePair<string, string>>
        {
            new KeyValuePair<string, string>("apikey", apiKey!),
            new KeyValuePair<string, string>("from", fromEmail!),
            new KeyValuePair<string, string>("fromName", fromName!),
            new KeyValuePair<string, string>("to", to),
            new KeyValuePair<string, string>(isHtml ? "bodyHtml" : "template", template),
            new KeyValuePair<string, string>("subject", subject),
            new KeyValuePair<string, string>("isTransactional", "true")
        };

        // Adiciona os campos de Merge
        foreach (var field in mergeFields)
        {
            keyValues.Add(new KeyValuePair<string, string>($"merge_{field.Key}", field.Value));
        }

        try
        {
            var content = new FormUrlEncodedContent(keyValues);

            var response = await _httpClient.PostAsync(url, content);
            var responseContent = await response.Content.ReadAsStringAsync();

            response.EnsureSuccessStatusCode();

            var elasticResponse = JsonSerializer.Deserialize<ElasticEmailResponse>(responseContent, _jsonOptions);

            if (elasticResponse is null || !elasticResponse.Success)
            {
                _logger.LogError(
                    "Falha ao enviar email via ElasticEmail para {Email}. Template: {Template}. Resposta: {Response}",
                    to, template, responseContent);
                throw new Exception($"ElasticEmail retornou erro: {elasticResponse?.Error ?? responseContent}");
            }

            _logger.LogInformation("✉️ Email com template enviado com sucesso para {Email}. TransactionId: {TransactionId}",
                to, elasticResponse.Data?.TransactionId);
        }
        catch (TaskCanceledException ex) when (!ex.CancellationToken.IsCancellationRequested)
        {
            _logger.LogError(ex, "Erro de Timeout ao enviar email para {Email}", to);
            throw;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Erro de HttpRequest ao enviar email para {Email}", to);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro geral ao enviar email para {Email}", to);
            throw;
        }
    }
}

public class ElasticEmailResponse
{
    public bool Success { get; set; }
    public ElasticEmailData? Data { get; set; }
    public string? Error { get; set; }
}

public class ElasticEmailData
{
    public string? TransactionId { get; set; }
    public string? MessageId { get; set; }
}
