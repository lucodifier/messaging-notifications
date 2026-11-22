namespace messaging.worker.Services.Handlers;

using messaging.worker.Interfaces;

public class EmailHandler
{
    private readonly IEmailSenderResolver _resolver;

    public EmailHandler(IEmailSenderResolver resolver)
    {
        _resolver = resolver;
    }

    public async Task SendAsync(string to, string subject, string template, Dictionary<string, string> mergeFields, string system)
    {
        var sender = _resolver.GetSender(system);
        await sender.SendEmailAsync(to, subject, template, mergeFields);
    }
}
