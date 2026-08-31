using System.Diagnostics.CodeAnalysis;
using MechanicsSoftware.Application.UseCases.Auth.Handlers;
using MechanicsSoftware.Application.UseCases.Customers.Handlers;
using MechanicsSoftware.Application.UseCases.Inventory.Handlers;
using MechanicsSoftware.Application.UseCases.ServiceOrders.Handlers;
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
        services.AddScoped<GenerateBudgetHandler>();
        services.AddScoped<SendBudgetHandler>();
        services.AddScoped<ApproveServiceOrderHandler>();
        services.AddScoped<RejectServiceOrderHandler>();
        services.AddScoped<BudgetDecisionHandler>();
        services.AddScoped<StartExecutionHandler>();
        services.AddScoped<CompleteServiceOrderHandler>();
        services.AddScoped<DeliverServiceOrderHandler>();
        services.AddScoped<GetServiceOrderStatusHandler>();
        services.AddScoped<GetServiceOrderHandler>();
        services.AddScoped<ListServiceOrdersHandler>();
        services.AddScoped<GetAverageExecutionTimeHandler>();
        services.AddScoped<GetDailyServiceOrderVolumeHandler>();

        return services;
    }
}
