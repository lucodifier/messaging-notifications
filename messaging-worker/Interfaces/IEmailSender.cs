namespace messaging.worker.Interfaces;

public interface IEmailSender
{
    Task SendEmailAsync(string to, string subject, string template, Dictionary<string, string> mergeFields);
}
