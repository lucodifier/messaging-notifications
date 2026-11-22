namespace Messaging.Api.Interfaces;

public interface IServiceBusSender
{
    Task SendMessageAsync(string message);
}
