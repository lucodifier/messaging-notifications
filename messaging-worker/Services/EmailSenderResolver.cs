using messaging.worker.Interfaces;

namespace messaging.worker.Services;

public class EmailSenderResolver : IEmailSenderResolver
{
    private readonly ElasticEmailSender _elasticEmailSender;
    private readonly SmtpEmailSender _smtpEmailSender;
    private readonly HashSet<string> _smtpSystems;

    public EmailSenderResolver(
        ElasticEmailSender elasticEmailSender,
        SmtpEmailSender smtpEmailSender,
        IConfiguration configuration)
    {
        _elasticEmailSender = elasticEmailSender;
        _smtpEmailSender = smtpEmailSender;
        // Smtp:UseForSystems no appsettings ou Smtp__UseForSystems em env (string com vírgula)
        // Use "*" para rotear TODOS os sistemas via SMTP (ex.: Mailpit em dev, sem consumir créditos do Elastic)
        var useForSystems = configuration["Smtp:UseForSystems"] ?? "";
        _smtpSystems = new HashSet<string>(
            useForSystems.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(v => v.Length > 0),
            StringComparer.OrdinalIgnoreCase);
        _useSmtpForAll = _smtpSystems.Contains("*");
    }

    private readonly bool _useSmtpForAll;

    public IEmailSender GetSender(string system)
    {
        if (_useSmtpForAll)
            return _smtpEmailSender;

        var normalized = (system ?? "").Trim();
        return _smtpSystems.Contains(normalized) ? _smtpEmailSender : _elasticEmailSender;
    }
}
