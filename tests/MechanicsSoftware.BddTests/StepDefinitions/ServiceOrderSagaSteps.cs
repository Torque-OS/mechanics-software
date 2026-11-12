using FluentAssertions;
using MechanicsSoftware.Application.Abstractions;
using MechanicsSoftware.Application.UseCases.ServiceOrders;
using MechanicsSoftware.Application.UseCases.ServiceOrders.Handlers;
using MechanicsSoftware.Application.UseCases.ServiceOrders.Handlers.SagaEvents;
using MechanicsSoftware.Domain.Entities;
using MechanicsSoftware.Domain.ValueObjects;
using MechanicsSoftware.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Reqnroll;
using TorqueOS.Contracts.Billing;
using TorqueOS.Contracts.Execution;

namespace MechanicsSoftware.BddTests.StepDefinitions;

[Binding]
public sealed class ServiceOrderSagaSteps : IDisposable
{
    private readonly AppDbContext _db;
    private readonly IEmailNotifier _emailNotifier = Mock.Of<IEmailNotifier>();
    private readonly RecordingEventPublisher _eventPublisher = new();

    private Guid _serviceOrderId;
    private ServiceOrderResponse _lastResponse = null!;

    public ServiceOrderSagaSteps()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"bdd-saga-{Guid.NewGuid()}")
            .Options;
        _db = new AppDbContext(options);
    }

    [Given(@"que uma nova Ordem de Serviço foi aberta e iniciou o diagnóstico com um serviço no valor de (\d+) centavos")]
    public async Task DadoQueUmaNovaOrdemDeServicoFoiAbertaEIniciouODiagnostico(int servicePriceInCents)
    {
        var order = ServiceOrder.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        order.StartDiagnosis();
        order.AddServiceItem(Guid.NewGuid(), "Serviço Mecânico", new Money(servicePriceInCents), 1);

        _db.ServiceOrders.Add(order);
        await _db.SaveChangesAsync();
        _serviceOrderId = order.Id;
    }

    [When(@"o Billing Service publica o evento BudgetGenerated no valor de (\d+) centavos")]
    public async Task QuandoOBillingServicePublicaOEventoBudgetGenerated(int totalInCents)
    {
        var handler = new BudgetGeneratedHandler(
            _db, _emailNotifier, NullLogger<BudgetGeneratedHandler>.Instance);
        _lastResponse = await handler.ExecuteAsync(_serviceOrderId, Guid.NewGuid(), totalInCents);
    }

    [When(@"o cliente aprova o orçamento publicando o evento BudgetApproved")]
    public async Task QuandoOClienteAprovaOOrcamentoPublicandoOEventoBudgetApproved()
    {
        var handler = new BudgetApprovedHandler(
            _db, _emailNotifier, NullLogger<BudgetApprovedHandler>.Instance);
        _lastResponse = await handler.ExecuteAsync(_serviceOrderId);
    }

    [When(@"o pagamento via Mercado Pago é confirmado publicando o evento PaymentConfirmed")]
    public async Task QuandoOPagamentoViaMercadoPagoEConfirmadoPublicandoOEventoPaymentConfirmed()
    {
        var handler = new PaymentConfirmedHandler(
            _db, _eventPublisher, _emailNotifier, NullLogger<PaymentConfirmedHandler>.Instance);
        _lastResponse = await handler.ExecuteAsync(_serviceOrderId);
    }

    [When(@"o Execution Service finaliza o reparo publicando o evento ExecutionCompleted")]
    public async Task QuandoOExecutionServiceFinalizaOReparoPublicandoOEventoExecutionCompleted()
    {
        var completeHandler = new CompleteServiceOrderHandler(
            _db, _emailNotifier, NullLogger<CompleteServiceOrderHandler>.Instance);
        var handler = new ExecutionCompletedHandler(_db, completeHandler);
        _lastResponse = await handler.ExecuteAsync(_serviceOrderId);
    }

    [When(@"o Billing Service publica o evento PaymentFailed por expiração do QR Code")]
    public async Task QuandoOBillingServicePublicaOEventoPaymentFailed()
    {
        var handler = new PaymentFailedHandler(
            _db, _eventPublisher, _emailNotifier, NullLogger<PaymentFailedHandler>.Instance);
        _lastResponse = await handler.ExecuteAsync(_serviceOrderId);
    }

    [When(@"o Execution Service encontra uma falha impeditiva e publica o evento ExecutionFailed")]
    public async Task QuandoOExecutionServicePublicaOEventoExecutionFailed()
    {
        var handler = new ExecutionFailedHandler(
            _db, _eventPublisher, _emailNotifier, NullLogger<ExecutionFailedHandler>.Instance);
        _lastResponse = await handler.ExecuteAsync(_serviceOrderId);
    }

    [Then(@"a Ordem de Serviço deve estar com o status ""(.*)""")]
    [Then(@"a Ordem de Serviço deve ser revertida para o status ""(.*)""")]
    public void EntaoAOrdemDeServicoDeveEstarComOStatus(string expectedStatus)
    {
        _lastResponse.Status.Should().Be(expectedStatus);
    }

    [Then(@"o orquestrador deve ter enviado o comando EnqueueExecution para o Execution Service")]
    public void EntaoOOrquestradorDeveTerEnviadoOComandoEnqueueExecution()
    {
        _eventPublisher.Published.OfType<EnqueueExecution>()
            .Should().ContainSingle(cmd => cmd.ServiceOrderId == _serviceOrderId);
    }

    [Then(@"o orquestrador deve ter enviado o comando CancelBudget para o Billing Service")]
    public void EntaoOOrquestradorDeveTerEnviadoOComandoCancelBudget()
    {
        _eventPublisher.Published.OfType<CancelBudget>()
            .Should().ContainSingle(cmd => cmd.ServiceOrderId == _serviceOrderId);
    }

    public void Dispose() => _db.Dispose();

    private sealed class RecordingEventPublisher : IEventPublisher
    {
        public List<object> Published { get; } = [];

        public Task PublishAsync<TEvent>(TEvent integrationEvent, CancellationToken cancellationToken = default)
            where TEvent : class
        {
            Published.Add(integrationEvent);
            return Task.CompletedTask;
        }

        public Task PublishAsync<TEvent>(TEvent integrationEvent, Guid correlationId, CancellationToken cancellationToken = default)
            where TEvent : class
        {
            Published.Add(integrationEvent);
            return Task.CompletedTask;
        }
    }
}
