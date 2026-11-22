namespace messaging.worker.Interfaces;

public interface ISmsSender
{
    Task SendSmsAsync(string phoneNumber, string messageText);
}