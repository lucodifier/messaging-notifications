using messaging.worker.Interfaces;

namespace messaging.worker.Services.Handlers;

public class WhatsAppHandler
{
    private readonly IWhatsAppSender _whatsAppSender;

    public WhatsAppHandler(IWhatsAppSender whatsAppSender)
    {
        _whatsAppSender = whatsAppSender;
    }

    public async Task SendAsync(string phone, string message)
    {
        await _whatsAppSender.SendWhatsAppAsync(phone, message);
    }
}
