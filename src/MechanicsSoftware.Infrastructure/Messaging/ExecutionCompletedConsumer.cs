using System.Diagnostics.CodeAnalysis;
using MassTransit;
using MechanicsSoftware.Application.Exceptions;
using MechanicsSoftware.Application.UseCases.ServiceOrders.Handlers.SagaEvents;
using MechanicsSoftware.Domain.Exceptions;
using MechanicsSoftware.Infrastructure.Persistence;
using TorqueOS.Contracts.Execution;

namespace MechanicsSoftware.Infrastructure.Messaging;

/// <summary>Saga event from the Execution Service (ADR-011, F4-22) — the last step of the happy path.</summary>
public sealed class ExecutionCompletedConsumer(ExecutionCompletedHandler handler) : IConsumer<ExecutionCompleted>
{
    public Task Consume(ConsumeContext<ExecutionCompleted> context) =>
        handler.ExecuteAsync(context.Message.ServiceOrderId, context.CancellationToken);
}

/// <summary>Queue <c>os-service.execution-completed</c>. See <see cref="BudgetGeneratedConsumerDefinition"/> for the retry policy.</summary>
[ExcludeFromCodeCoverage(Justification = "Endpoint wiring needs PostgreSQL and RabbitMQ; exercised manually against the compose stack.")]
public sealed class ExecutionCompletedConsumerDefinition : ConsumerDefinition<ExecutionCompletedConsumer>
{
    public const string QueueName = "os-service.execution-completed";

    public ExecutionCompletedConsumerDefinition() => EndpointName = QueueName;

    protected override void ConfigureConsumer(
        IReceiveEndpointConfigurator endpointConfigurator,
        IConsumerConfigurator<ExecutionCompletedConsumer> consumerConfigurator,
        IRegistrationContext context)
    {
        endpointConfigurator.UseMessageRetry(retry =>
        {
            retry.Intervals(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15));
            retry.Ignore<DomainException>();
            retry.Ignore<NotFoundException>();
        });

        endpointConfigurator.UseEntityFrameworkOutbox<AppDbContext>(context);
    }
}
