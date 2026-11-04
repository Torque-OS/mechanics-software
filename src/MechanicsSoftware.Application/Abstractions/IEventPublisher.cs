using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MechanicsSoftware.Application.Abstractions
{
    /// <summary>
    /// Publishes integration events to the other services through the message broker (ADR-011).
    /// </summary>
    /// <remarks>
    /// The implementation uses the transactional outbox: the message is stored together with the
    /// entity changes on the next <see cref="IAppDbContext.SaveChangesAsync"/> call and is only
    /// delivered to the broker after that transaction commits. Call this method <b>before</b>
    /// saving, otherwise the message is never persisted.
    /// </remarks>
    public interface IEventPublisher
    {
        /// <param name="correlationId">
        /// The Saga correlation key (ADR-011) — the <c>ServiceOrderId</c> for every message in
        /// this flow. Left null only for messages with no natural correlation.
        /// </param>
        Task PublishAsync<TEvent>(
            TEvent integrationEvent, Guid? correlationId = null, CancellationToken cancellationToken = default)
            where TEvent : class;
    }
}
