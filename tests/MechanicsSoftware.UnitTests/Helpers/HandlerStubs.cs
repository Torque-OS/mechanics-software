using MechanicsSoftware.Application.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace MechanicsSoftware.UnitTests.Helpers;

internal static class HandlerStubs
{
    public static IEmailNotifier EmailNotifier() => Mock.Of<IEmailNotifier>();

    public static IEventPublisher EventPublisher() => Mock.Of<IEventPublisher>();

    public static ILogger<T> Logger<T>() => NullLogger<T>.Instance;
}
