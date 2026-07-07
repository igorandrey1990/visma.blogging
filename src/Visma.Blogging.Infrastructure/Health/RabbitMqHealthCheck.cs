using Microsoft.Extensions.Diagnostics.HealthChecks;
using RabbitMQ.Client;
using Visma.Blogging.Infrastructure.Messaging;

namespace Visma.Blogging.Infrastructure.Health;

/// <summary>
/// Health check that verifies RabbitMQ accepts a connection and channel.
/// </summary>
public sealed class RabbitMqHealthCheck : IHealthCheck
{
    private readonly RabbitMqOptions _options;

    /// <summary>
    /// Creates the health check.
    /// </summary>
    public RabbitMqHealthCheck(RabbitMqOptions options)
    {
        _options = options;
    }

    /// <inheritdoc />
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled)
        {
            // RabbitMQ is optional by configuration; disabled publishing should not fail readiness.
            return HealthCheckResult.Healthy("RabbitMQ publishing is disabled.");
        }

        try
        {
            // Opening a channel proves more than TCP reachability: the broker accepted AMQP credentials and vhost.
            var factory = new ConnectionFactory
            {
                HostName = _options.HostName,
                Port = _options.Port,
                UserName = _options.UserName,
                Password = _options.Password,
                VirtualHost = _options.VirtualHost,
                ClientProvidedName = "visma-blogging-api-health"
            };

            await using var connection = await factory.CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
            await using var channel = await connection.CreateChannelAsync(cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            return HealthCheckResult.Healthy("RabbitMQ accepted a connection and channel.");
        }
        catch (Exception exception)
        {
            return HealthCheckResult.Unhealthy("RabbitMQ did not accept a connection.", exception);
        }
    }
}
