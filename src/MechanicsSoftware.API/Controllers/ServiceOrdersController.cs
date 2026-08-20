using MechanicsSoftware.API.Transport.ServiceOrders;
using MechanicsSoftware.Application.UseCases.ServiceOrders.Commands;
using MechanicsSoftware.Application.UseCases.ServiceOrders.Handlers;
using MechanicsSoftware.Application.UseCases.ServiceOrders.Queries;
using MechanicsSoftware.Domain.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MechanicsSoftware.API.Controllers;

[ApiController]
[Route("api/service-orders")]
[Authorize(Policy = Policies.CustomerOrStaff)]
public class ServiceOrdersController( // NOSONAR S6960 S107: Clean Architecture without MediatR — handlers injected directly; param count is by design
    CreateServiceOrderHandler createHandler,
    GetServiceOrderHandler getHandler,
    GetServiceOrderStatusHandler getStatusHandler,
    ListServiceOrdersHandler listHandler,
    StartDiagnosisHandler startDiagnosisHandler,
    AddServiceItemHandler addServiceItemHandler,
    AddPartItemHandler addPartItemHandler,
    GenerateBudgetHandler generateBudgetHandler,
    SendBudgetHandler sendBudgetHandler,
    BudgetDecisionHandler budgetDecisionHandler,
    StartExecutionHandler startExecutionHandler,
    CompleteServiceOrderHandler completeHandler,
    DeliverServiceOrderHandler deliverHandler,
    GetAverageExecutionTimeHandler averageExecutionTimeHandler) : ControllerBase
{
    [HttpPost]
    [Authorize(Policy = Policies.Staff)]
    public async Task<IActionResult> Create(CreateServiceOrderRequest request, CancellationToken cancellationToken)
    {
        var result = await createHandler.ExecuteAsync(new CreateServiceOrderCommand(request.CustomerId, request.VehicleId), cancellationToken);
        return CreatedAtAction(nameof(Get), new { id = result.Id }, result);
    }

    [HttpGet("{id:guid}")]
    [Authorize(Policy = Policies.Staff)]
    public async Task<IActionResult> Get(Guid id, CancellationToken cancellationToken)
    {
        var result = await getHandler.ExecuteAsync(id, cancellationToken);
        return Ok(result);
    }

    [HttpGet("{id:guid}/status")]
    public async Task<IActionResult> GetStatus(Guid id, CancellationToken cancellationToken)
    {
        var result = await getStatusHandler.ExecuteAsync(id, cancellationToken);
        return Ok(result);
    }

    [HttpGet]
    [Authorize(Policy = Policies.Staff)]
    public async Task<IActionResult> List([FromQuery] ListServiceOrdersQuery query, CancellationToken cancellationToken)
    {
        var result = await listHandler.ExecuteAsync(query, cancellationToken);
        return Ok(result);
    }

    [HttpPost("{id:guid}/start-diagnosis")]
    [Authorize(Policy = Policies.Staff)]
    public async Task<IActionResult> StartDiagnosis(Guid id, CancellationToken cancellationToken)
    {
        var result = await startDiagnosisHandler.ExecuteAsync(id, cancellationToken);
        return Ok(result);
    }

    [HttpPost("{id:guid}/services")]
    [Authorize(Policy = Policies.Staff)]
    public async Task<IActionResult> AddService(Guid id, AddServiceItemRequest request, CancellationToken cancellationToken)
    {
        var result = await addServiceItemHandler.ExecuteAsync(id, new AddServiceItemCommand(request.ServiceId, request.Quantity), cancellationToken);
        return Ok(result);
    }

    [HttpPost("{id:guid}/parts")]
    [Authorize(Policy = Policies.Staff)]
    public async Task<IActionResult> AddPart(Guid id, AddPartItemRequest request, CancellationToken cancellationToken)
    {
        var result = await addPartItemHandler.ExecuteAsync(id, new AddPartItemCommand(request.PartId, request.Quantity), cancellationToken);
        return Ok(result);
    }

    [HttpPost("{id:guid}/budget")]
    [Authorize(Policy = Policies.Staff)]
    public async Task<IActionResult> GenerateBudget(Guid id, CancellationToken cancellationToken)
    {
        var result = await generateBudgetHandler.ExecuteAsync(id, cancellationToken);
        return Ok(result);
    }

    [HttpPost("{id:guid}/send-budget")]
    [Authorize(Policy = Policies.Staff)]
    public async Task<IActionResult> SendBudget(Guid id, CancellationToken cancellationToken)
    {
        var result = await sendBudgetHandler.ExecuteAsync(id, cancellationToken);
        return Ok(result);
    }

    [HttpPost("{id:guid}/budget-decision")]
    public async Task<IActionResult> HandleBudgetDecision(Guid id, BudgetDecisionRequest request, CancellationToken cancellationToken)
    {
        var approve = request.Decision == BudgetDecision.Approve;
        var result = await budgetDecisionHandler.ExecuteAsync(id, approve, cancellationToken);
        return Ok(result);
    }

    [HttpPost("{id:guid}/start-execution")]
    [Authorize(Policy = Policies.Staff)]
    public async Task<IActionResult> StartExecution(Guid id, CancellationToken cancellationToken)
    {
        var result = await startExecutionHandler.ExecuteAsync(id, cancellationToken);
        return Ok(result);
    }

    [HttpPost("{id:guid}/complete")]
    [Authorize(Policy = Policies.Staff)]
    public async Task<IActionResult> Complete(Guid id, CancellationToken cancellationToken)
    {
        var result = await completeHandler.ExecuteAsync(id, cancellationToken);
        return Ok(result);
    }

    [HttpPost("{id:guid}/deliver")]
    [Authorize(Policy = Policies.Staff)]
    public async Task<IActionResult> Deliver(Guid id, CancellationToken cancellationToken)
    {
        var result = await deliverHandler.ExecuteAsync(id, cancellationToken);
        return Ok(result);
    }

    [HttpGet("metrics/average-execution-time")]
    [Authorize(Policy = Policies.Staff)]
    public async Task<IActionResult> AverageExecutionTime(CancellationToken cancellationToken)
    {
        var result = await averageExecutionTimeHandler.ExecuteAsync(cancellationToken);
        return Ok(result);
    }
}
