using MassTransit;
using MechanicsSoftware.Application.UseCases.ServiceOrders.Handlers.SagaEvents;
using TorqueOS.Contracts.Billing;

namespace MechanicsSoftware.Infrastructure.Messaging.Consumers;

/// <summary>The payment failed or expired: the order is cancelled and reserved parts are released.</summary>
public sealed class PaymentFailedConsumer(PaymentFailedHandler handler) : IConsumer<PaymentFailed>
{
    public Task Consume(ConsumeContext<PaymentFailed> context) =>
        handler.ExecuteAsync(context.Message.ServiceOrderId, context.CancellationToken);
}
