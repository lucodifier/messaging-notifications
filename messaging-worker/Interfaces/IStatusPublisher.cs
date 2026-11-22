using messaging.worker.Models;

namespace messaging.worker.Interfaces;

public interface IStatusPublisher
{
    Task PublishAsync(StatusReport report);
}
