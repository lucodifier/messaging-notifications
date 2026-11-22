using System.Text;
public interface ILoggerService
{
    Task Info(string message);
    Task Warn(string message);
    Task Error(string message);
}

public class LoggerService : ILoggerService
{
    public LoggerService(IConfiguration config)
    {
        var endpoint = config["Logger:Endpoint"]!;
        string key = config["Logger:Key"]!;
        string system = config["Logger:System"]!;
        string environment = config["Logger:Environment"]!;

        string token = GenerateToken(key, system, environment);
    }

    public static string GenerateToken(string key, string system, string environment)
    {
        var tokenString = $"{key}:{system}:{environment}";
        var bytes = Encoding.UTF8.GetBytes(tokenString);
        return Convert.ToBase64String(bytes);
    }

    public Task Info(string message) { return null; }
    public Task Warn(string message) { return null; }
    public Task Error(string message) { return null; }
}
