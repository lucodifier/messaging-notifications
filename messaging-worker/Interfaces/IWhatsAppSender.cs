namespace messaging.worker.Interfaces;

public interface IWhatsAppSender
{
    Task SendWhatsAppAsync(string phoneNumber, string message);
}