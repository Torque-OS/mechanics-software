using FluentAssertions;
using MechanicsSoftware.Application.Abstractions;
using MechanicsSoftware.Application.UseCases.ServiceOrders.Handlers;
using MechanicsSoftware.Application.UseCases.ServiceOrders.Handlers.SagaEvents;
using MechanicsSoftware.Domain.Entities;
using MechanicsSoftware.Domain.Enums;
using MechanicsSoftware.Domain.Exceptions;
using MechanicsSoftware.Domain.ValueObjects;
using MechanicsSoftware.UnitTests.Helpers;
using Moq;

namespace MechanicsSoftware.UnitTests.Application.ServiceOrders;

public class StatusChangeEmailNotificationTests
{
    private static Customer BuildCustomer(Guid id) =>
        Customer.Create(id, "João Silva", "529.982.247-25", PersonType.INDIVIDUAL, "joao@example.com", "11999999999");

    private static ServiceOrder NewOrder(Guid customerId) =>
        ServiceOrder.Create(Guid.NewGuid(), customerId, Guid.NewGuid());

    private static ServiceOrder OrderInDiagnosis(Guid customerId)
    {
        var order = NewOrder(customerId);
        order.StartDiagnosis();
        order.AddServiceItem(Guid.NewGuid(), "Oil Change", new Money(5000), 1);
        return order;
    }

    private static ServiceOrder OrderAwaitingApproval(Guid customerId)
    {
        var order = OrderInDiagnosis(customerId);
        order.AttachBudget(Guid.NewGuid(), new Money(5000));
        return order;
    }

    private static ServiceOrder OrderAwaitingPayment(Guid customerId)
    {
        var order = OrderAwaitingApproval(customerId);
        order.ConfirmBudgetApproval();
        return order;
    }

    [Fact]
    public async Task StartDiagnosis_Success_NotifiesWithCorrectParameters()
    {
        await using var db = InMemoryDbContextHelper.Create();
        var customerId = Guid.NewGuid();
        var customer = BuildCustomer(customerId);
        db.Customers.Add(customer);
        var order = NewOrder(customerId);
        db.ServiceOrders.Add(order);
        await db.SaveChangesAsync();

        var emailNotifier = new Mock<IEmailNotifier>();

        await new StartDiagnosisHandler(db, emailNotifier.Object, HandlerStubs.Logger<StartDiagnosisHandler>())
            .ExecuteAsync(order.Id);

        emailNotifier.Verify(e => e.SendStatusChangedAsync(
            customer.Email.Value,
            customer.Name,
            order.Id,
            ServiceOrderStatus.Status.InDiagnosis,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task BudgetGenerated_Success_NotifiesWithCorrectParameters()
    {
        await using var db = InMemoryDbContextHelper.Create();
        var customerId = Guid.NewGuid();
        var customer = BuildCustomer(customerId);
        db.Customers.Add(customer);
        var order = OrderInDiagnosis(customerId);
        db.ServiceOrders.Add(order);
        await db.SaveChangesAsync();

        var emailNotifier = new Mock<IEmailNotifier>();

        await new BudgetGeneratedHandler(db, emailNotifier.Object, HandlerStubs.Logger<BudgetGeneratedHandler>())
            .ExecuteAsync(order.Id, Guid.NewGuid(), 5000);

        emailNotifier.Verify(e => e.SendStatusChangedAsync(
            customer.Email.Value,
            customer.Name,
            order.Id,
            ServiceOrderStatus.Status.AwaitingApproval,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task PaymentConfirmed_Success_NotifiesWithCorrectParameters()
    {
        await using var db = InMemoryDbContextHelper.Create();
        var customerId = Guid.NewGuid();
        var customer = BuildCustomer(customerId);
        db.Customers.Add(customer);
        var order = OrderAwaitingPayment(customerId);
        db.ServiceOrders.Add(order);
        await db.SaveChangesAsync();

        var emailNotifier = new Mock<IEmailNotifier>();

        await new PaymentConfirmedHandler(db, emailNotifier.Object, HandlerStubs.Logger<PaymentConfirmedHandler>())
            .ExecuteAsync(order.Id);

        emailNotifier.Verify(e => e.SendStatusChangedAsync(
            customer.Email.Value,
            customer.Name,
            order.Id,
            ServiceOrderStatus.Status.InExecution,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task StartDiagnosis_NotifierThrows_StatusStillPersisted()
    {
        await using var db = InMemoryDbContextHelper.Create();
        var customerId = Guid.NewGuid();
        db.Customers.Add(BuildCustomer(customerId));
        var order = NewOrder(customerId);
        db.ServiceOrders.Add(order);
        await db.SaveChangesAsync();

        var emailNotifier = new Mock<IEmailNotifier>();
        emailNotifier
            .Setup(e => e.SendStatusChangedAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Guid>(),
                It.IsAny<ServiceOrderStatus.Status>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("SMTP unavailable"));

        var act = async () => await new StartDiagnosisHandler(db, emailNotifier.Object, HandlerStubs.Logger<StartDiagnosisHandler>())
            .ExecuteAsync(order.Id);

        await act.Should().NotThrowAsync();

        var persisted = await db.ServiceOrders.FindAsync(order.Id);
        persisted!.Status.Value.Should().Be(ServiceOrderStatus.Status.InDiagnosis);
    }

    [Fact]
    public async Task BudgetGenerated_NotifierThrows_StatusStillPersisted()
    {
        await using var db = InMemoryDbContextHelper.Create();
        var customerId = Guid.NewGuid();
        db.Customers.Add(BuildCustomer(customerId));
        var order = OrderInDiagnosis(customerId);
        db.ServiceOrders.Add(order);
        await db.SaveChangesAsync();

        var emailNotifier = new Mock<IEmailNotifier>();
        emailNotifier
            .Setup(e => e.SendStatusChangedAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Guid>(),
                It.IsAny<ServiceOrderStatus.Status>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("SMTP unavailable"));

        var act = async () => await new BudgetGeneratedHandler(db, emailNotifier.Object, HandlerStubs.Logger<BudgetGeneratedHandler>())
            .ExecuteAsync(order.Id, Guid.NewGuid(), 5000);

        await act.Should().NotThrowAsync();

        var persisted = await db.ServiceOrders.FindAsync(order.Id);
        persisted!.Status.Value.Should().Be(ServiceOrderStatus.Status.AwaitingApproval);
    }

    [Fact]
    public async Task PaymentConfirmed_NotifierThrows_StatusStillPersisted()
    {
        await using var db = InMemoryDbContextHelper.Create();
        var customerId = Guid.NewGuid();
        db.Customers.Add(BuildCustomer(customerId));
        var order = OrderAwaitingPayment(customerId);
        db.ServiceOrders.Add(order);
        await db.SaveChangesAsync();

        var emailNotifier = new Mock<IEmailNotifier>();
        emailNotifier
            .Setup(e => e.SendStatusChangedAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Guid>(),
                It.IsAny<ServiceOrderStatus.Status>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("SMTP unavailable"));

        var act = async () => await new PaymentConfirmedHandler(db, emailNotifier.Object, HandlerStubs.Logger<PaymentConfirmedHandler>())
            .ExecuteAsync(order.Id);

        await act.Should().NotThrowAsync();

        var persisted = await db.ServiceOrders.FindAsync(order.Id);
        persisted!.Status.Value.Should().Be(ServiceOrderStatus.Status.InExecution);
    }

    [Fact]
    public async Task StartDiagnosis_InvalidTransition_DoesNotNotify()
    {
        await using var db = InMemoryDbContextHelper.Create();
        var customerId = Guid.NewGuid();
        db.Customers.Add(BuildCustomer(customerId));
        var order = NewOrder(customerId);
        order.StartDiagnosis();
        db.ServiceOrders.Add(order);
        await db.SaveChangesAsync();

        var emailNotifier = new Mock<IEmailNotifier>();

        var act = async () => await new StartDiagnosisHandler(db, emailNotifier.Object, HandlerStubs.Logger<StartDiagnosisHandler>())
            .ExecuteAsync(order.Id);

        await act.Should().ThrowAsync<DomainException>();
        emailNotifier.Verify(e => e.SendStatusChangedAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Guid>(),
            It.IsAny<ServiceOrderStatus.Status>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task BudgetGenerated_InvalidTransition_DoesNotNotify()
    {
        await using var db = InMemoryDbContextHelper.Create();
        var customerId = Guid.NewGuid();
        db.Customers.Add(BuildCustomer(customerId));
        var order = NewOrder(customerId);
        db.ServiceOrders.Add(order);
        await db.SaveChangesAsync();

        var emailNotifier = new Mock<IEmailNotifier>();

        var act = async () => await new BudgetGeneratedHandler(db, emailNotifier.Object, HandlerStubs.Logger<BudgetGeneratedHandler>())
            .ExecuteAsync(order.Id, Guid.NewGuid(), 5000);

        await act.Should().ThrowAsync<DomainException>();
        emailNotifier.Verify(e => e.SendStatusChangedAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Guid>(),
            It.IsAny<ServiceOrderStatus.Status>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task PaymentConfirmed_InvalidTransition_DoesNotNotify()
    {
        await using var db = InMemoryDbContextHelper.Create();
        var customerId = Guid.NewGuid();
        db.Customers.Add(BuildCustomer(customerId));
        var order = NewOrder(customerId);
        db.ServiceOrders.Add(order);
        await db.SaveChangesAsync();

        var emailNotifier = new Mock<IEmailNotifier>();

        var act = async () => await new PaymentConfirmedHandler(db, emailNotifier.Object, HandlerStubs.Logger<PaymentConfirmedHandler>())
            .ExecuteAsync(order.Id);

        await act.Should().ThrowAsync<DomainException>();
        emailNotifier.Verify(e => e.SendStatusChangedAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Guid>(),
            It.IsAny<ServiceOrderStatus.Status>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
