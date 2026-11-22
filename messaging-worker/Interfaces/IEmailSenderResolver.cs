namespace messaging.worker.Interfaces;

public interface IEmailSenderResolver
{
    IEmailSender GetSender(string system);
}
