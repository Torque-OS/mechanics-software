using Microsoft.Extensions.Configuration;

namespace MechanicsSoftware.Infrastructure.Messaging;

public sealed record RabbitMqSettings(
    string Host,
    ushort Port,
    string VirtualHost,
    string Username,
    string Password)
{
    private const ushort DefaultPort = 5672;
    private const string DefaultVirtualHost = "/";

    public static RabbitMqSettings FromConfiguration(IConfiguration configuration)
    {
        var host = configuration["RABBITMQ_HOST"]
            ?? throw new InvalidOperationException(
                "RabbitMQ host not configured. Set the 'RABBITMQ_HOST' environment variable.");

        var username = configuration["RABBITMQ_USERNAME"]
            ?? throw new InvalidOperationException(
                "RabbitMQ username not configured. Set the 'RABBITMQ_USERNAME' environment variable.");

        var password = configuration["RABBITMQ_PASSWORD"]
            ?? throw new InvalidOperationException(
                "RabbitMQ password not configured. Set the 'RABBITMQ_PASSWORD' environment variable.");

        var port = ushort.TryParse(configuration["RABBITMQ_PORT"], out var parsedPort) && parsedPort > 0
            ? parsedPort
            : DefaultPort;

        return new RabbitMqSettings(
            host,
            port,
            Fallback(configuration["RABBITMQ_VHOST"], DefaultVirtualHost),
            username,
            password);
    }

    // Records print every property in ToString(); keep the password out of logs and exceptions.
    public override string ToString() =>
        $"RabbitMqSettings {{ Host = {Host}, Port = {Port}, VirtualHost = {VirtualHost}, Username = {Username} }}";

    private static string Fallback(string? value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value;
}
