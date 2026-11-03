using MassTransit;
using MechanicsSoftware.Application.UseCases.ServiceOrders.Handlers.SagaEvents;
using TorqueOS.Contracts.Billing;

namespace MechanicsSoftware.Infrastructure.Messaging.Consumers;

/// <summary>Mercado Pago confirmed the payment: the order moves to IN_EXECUTION.</summary>
public sealed class PaymentConfirmedConsumer(PaymentConfirmedHandler handler) : IConsumer<PaymentConfirmed>
{
    public Task Consume(ConsumeContext<PaymentConfirmed> context) =>
        handler.ExecuteAsync(context.Message.ServiceOrderId, context.CancellationToken);
}
