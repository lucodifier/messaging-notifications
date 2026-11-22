namespace messaging.worker.Services.Handlers;

using messaging.worker.Interfaces;

public class SmsHandler
{
    private readonly ISmsSender _smsSender;

    public SmsHandler(ISmsSender smsSender)
    {
        _smsSender = smsSender;
    }

    public async Task SendAsync(string to, string message)
    {
        await _smsSender.SendSmsAsync(to, message);
    }
}
