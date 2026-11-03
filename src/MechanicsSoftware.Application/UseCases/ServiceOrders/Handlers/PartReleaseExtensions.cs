using MechanicsSoftware.Application.Abstractions;
using MechanicsSoftware.Application.Exceptions;
using MechanicsSoftware.Domain.Entities;
using MechanicsSoftware.Domain.Enums;

namespace MechanicsSoftware.Application.UseCases.ServiceOrders.Handlers;

internal static class PartReleaseExtensions
{
    /// <summary>Releases parts reserved for a cancelled order back to stock.</summary>
    internal static async Task ReleaseReservedPartsAsync(
        this IAppDbContext db,
        ServiceOrder order,
        CancellationToken cancellationToken)
    {
        var availablePartItems = order.PartItems
            .Where(p => p.Availability == PartAvailability.Available);

        foreach (var partItem in availablePartItems)
        {
            var part = await db.Parts.FindAsync([partItem.PartId], cancellationToken)
                ?? throw new NotFoundException(nameof(Part), partItem.PartId);

            part.Release(partItem.Quantity, order.Id);
        }
    }
}
