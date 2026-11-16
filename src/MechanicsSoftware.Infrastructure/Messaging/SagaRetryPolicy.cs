using MassTransit;
using MechanicsSoftware.Application.Exceptions;
using MechanicsSoftware.Domain.Exceptions;

namespace MechanicsSoftware.Infrastructure.Messaging;

public static class SagaRetryPolicy
{
    public const int RetryLimit = 5;

    public static void Configure(IRetryConfigurator retry)
    {
        retry.Exponential(
            RetryLimit,
            minInterval: TimeSpan.FromMilliseconds(200),
            maxInterval: TimeSpan.FromSeconds(30),
            intervalDelta: TimeSpan.FromMilliseconds(500));
        retry.Ignore<DomainException>();
        retry.Ignore<NotFoundException>();
    }
}
