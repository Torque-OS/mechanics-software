using System.Diagnostics.CodeAnalysis;
using MechanicsSoftware.Application.UseCases.Auth.Handlers;
using MechanicsSoftware.Application.UseCases.Customers.Handlers;
using MechanicsSoftware.Application.UseCases.Inventory.Handlers;
using MechanicsSoftware.Application.UseCases.ServiceOrders.Handlers;
using MechanicsSoftware.Application.UseCases.ServiceOrders.Handlers.SagaEvents;
using MechanicsSoftware.Application.UseCases.Services.Handlers;
using MechanicsSoftware.Application.UseCases.Vehicles.Handlers;
using Microsoft.Extensions.DependencyInjection;

namespace MechanicsSoftware.Application;

[ExcludeFromCodeCoverage]
public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        // Auth
        services.AddScoped<LoginHandler>();

        // Customers
        services.AddScoped<CreateCustomerHandler>();
        services.AddScoped<GetCustomerHandler>();
        services.AddScoped<ListCustomersHandler>();
        services.AddScoped<UpdateCustomerHandler>();
        services.AddScoped<DeleteCustomerHandler>();

        // Vehicles
        services.AddScoped<CreateVehicleHandler>();
        services.AddScoped<GetVehicleHandler>();
        services.AddScoped<ListVehiclesHandler>();
        services.AddScoped<UpdateVehicleHandler>();
        services.AddScoped<DeleteVehicleHandler>();

        // Inventory
        services.AddScoped<CreatePartHandler>();
        services.AddScoped<GetPartHandler>();
        services.AddScoped<ListPartsHandler>();
        services.AddScoped<UpdatePartHandler>();
        services.AddScoped<DeletePartHandler>();
        services.AddScoped<UpdateStockHandler>();

        // Services catalogue
        services.AddScoped<CreateServiceHandler>();
        services.AddScoped<GetServiceHandler>();
        services.AddScoped<ListServicesHandler>();
        services.AddScoped<UpdateServiceHandler>();
        services.AddScoped<DeleteServiceHandler>();

        // Service Orders
        services.AddScoped<CreateServiceOrderHandler>();
        services.AddScoped<StartDiagnosisHandler>();
        services.AddScoped<AddServiceItemHandler>();
        services.AddScoped<AddPartItemHandler>();
        services.AddScoped<StartExecutionHandler>();
        services.AddScoped<CompleteServiceOrderHandler>();
        services.AddScoped<DeliverServiceOrderHandler>();
        services.AddScoped<GetServiceOrderStatusHandler>();
        services.AddScoped<GetServiceOrderHandler>();
        services.AddScoped<ListServiceOrdersHandler>();
        services.AddScoped<GetAverageExecutionTimeHandler>();
        services.AddScoped<GetDailyServiceOrderVolumeHandler>();

        // Service Orders — Saga event handlers (ADR-011). Not yet wired to a message consumer
        // (broker provisioning is F4-08, MassTransit wiring is F4-13); registered here so the
        // saga-side domain logic is exercised by tests ahead of that infrastructure.
        services.AddScoped<BudgetGeneratedHandler>();
        services.AddScoped<BudgetApprovedHandler>();
        services.AddScoped<BudgetRejectedHandler>();
        services.AddScoped<PaymentConfirmedHandler>();
        services.AddScoped<PaymentFailedHandler>();
        services.AddScoped<ExecutionFailedHandler>();

        return services;
    }
}
