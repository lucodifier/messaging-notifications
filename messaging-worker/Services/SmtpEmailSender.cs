using MailKit.Net.Smtp;
using MailKit.Security;
using messaging.worker.Interfaces;
using MimeKit;

namespace messaging.worker.Services;

public class SmtpEmailSender : IEmailSender
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<SmtpEmailSender> _logger;

    public SmtpEmailSender(IConfiguration configuration, ILogger<SmtpEmailSender> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    public async Task SendEmailAsync(string to, string subject, string template, Dictionary<string, string> mergeFields)
    {
        var host = _configuration["Smtp:Host"];
        var port = _configuration.GetValue<int>("Smtp:Port", 587);
        var user = _configuration["Smtp:User"];
        var pass = _configuration["Smtp:Pass"];
        var from = _configuration["Smtp:From"] ?? "noreply@localhost";
        var fromName = _configuration["Smtp:FromName"] ?? "Messaging Worker";

        if (string.IsNullOrWhiteSpace(host))
        {
            _logger.LogWarning("Smtp:Host não configurado; email para {To} não enviado.", to);
            return;
        }

        var body = ApplyMergeFields(template ?? "", mergeFields);

        // Template do Elastic referenciado por nome (sem HTML nem placeholders): monta um corpo
        // legível com os merge fields para inspeção no Mailpit/dev 📬
        if (!string.IsNullOrWhiteSpace(body) &&
            !body.TrimStart().StartsWith("<", StringComparison.Ordinal) &&
            !body.Contains("{{", StringComparison.Ordinal) &&
            mergeFields is { Count: > 0 })
        {
            body = BuildFallbackBody(body, mergeFields);
        }

        var isHtml = body.TrimStart().StartsWith("<", StringComparison.Ordinal);

        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(fromName, from));
        message.To.Add(MailboxAddress.Parse(to));
        message.Subject = subject ?? "";
        message.Body = isHtml
            ? new TextPart(MimeKit.Text.TextFormat.Html) { Text = body }
            : new TextPart(MimeKit.Text.TextFormat.Plain) { Text = body };

        try
        {
            using var client = new SmtpClient();
            var secureSocketOptions = SecureSocketOptions.Auto;// port == 465 ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTls;
            await client.ConnectAsync(host, port, secureSocketOptions);

            if (!string.IsNullOrWhiteSpace(user) && !string.IsNullOrWhiteSpace(pass))
                await client.AuthenticateAsync(user, pass);

            await client.SendAsync(message);
            await client.DisconnectAsync(true);

            _logger.LogInformation("✉️ Email enviado via SMTP para {Email}.", to);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao enviar email via SMTP para {Email}", to);
            throw;
        }
    }

    private static string BuildFallbackBody(string templateName, Dictionary<string, string> mergeFields)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"[Template: {templateName}]");
        sb.AppendLine();
        foreach (var (key, value) in mergeFields)
            sb.AppendLine($"{key}: {value}");
        return sb.ToString();
    }

    private static string ApplyMergeFields(string template, Dictionary<string, string> mergeFields)
    {
        if (mergeFields is null || mergeFields.Count == 0)
            return template;

        var result = template;
        foreach (var (key, value) in mergeFields)
        {
            var placeholder = "{{" + key + "}}";
            result = result.Replace(placeholder, value ?? "", StringComparison.OrdinalIgnoreCase);
        }
        return result;
    }
}
