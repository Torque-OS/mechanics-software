# Sequence Diagram — Service Order Opening

**Issue:** F3-29  
**Endpoint:** `POST /api/service-orders`  
**Authorization:** `Policies.Staff` (role must be `ADMIN`, `ATTENDANT`, or `MECHANIC`)

Opening a service order is a staff-only operation. The customer is identified by their existing `customerId`; the vehicle must already exist and belong to that customer.

---

## Flow

```mermaid
sequenceDiagram
    autonumber
    actor Staff as Staff user<br/>(ADMIN / ATTENDANT / MECHANIC)
    participant APIGW   as API Gateway<br/>(HTTP API v2)
    participant Authz   as Lambda Authorizer<br/>(authorizer.js)
    participant VPCLink as VPC Link → NLB
    participant API     as ASP.NET Core API<br/>(EKS pod)
    participant Handler as CreateServiceOrderHandler
    participant RDS     as RDS PostgreSQL 16

    Staff->>APIGW: POST /api/service-orders<br/>Authorization: Bearer <JWT (staff)><br/>{ "customerId": "uuid", "vehicleId": "uuid" }

    APIGW->>Authz: invoke Lambda Authorizer<br/>(or use cached verdict)
    note over Authz: Verify JWT — HS256<br/>iss / aud / exp
    Authz-->>APIGW: isAuthorized: true<br/>context: { sub, role: MECHANIC }

    APIGW->>VPCLink: POST /api/service-orders<br/>+ X-Gateway-Key header
    VPCLink->>API: HTTP request (internal NLB)

    note over API: GatewayKeyMiddleware<br/>FixedTimeEquals(X-Gateway-Key)

    note over API: JwtBearer re-validates token

    note over API: Policy: Policies.Staff<br/>role ∈ { ADMIN, ATTENDANT, MECHANIC }

    alt role = CUSTOMER
        API-->>VPCLink: 403 Forbidden
        VPCLink-->>APIGW: 403
        APIGW-->>Staff: 403 Forbidden
    end

    API->>Handler: CreateServiceOrderCommand<br/>{ customerId, vehicleId }

    Handler->>RDS: SELECT * FROM customers WHERE id = $1
    RDS-->>Handler: Customer row

    alt Customer not found
        Handler-->>API: NotFoundException
        API-->>VPCLink: 404 Not Found<br/>{ "error": "Customer ... not found" }
        VPCLink-->>APIGW: 404
        APIGW-->>Staff: 404 Not Found
    end

    Handler->>RDS: SELECT * FROM vehicles WHERE id = $1
    RDS-->>Handler: Vehicle row

    alt Vehicle not found
        Handler-->>API: NotFoundException
        API-->>VPCLink: 404 Not Found<br/>{ "error": "Vehicle ... not found" }
        VPCLink-->>APIGW: 404
        APIGW-->>Staff: 404 Not Found
    end

    note over Handler: ServiceOrder.Create(<br/>  id: Guid.NewGuid(),<br/>  customerId,<br/>  vehicleId<br/>)<br/>status = RECEIVED<br/>createdAt = UtcNow

    Handler->>RDS: INSERT INTO service_orders<br/>(id, customer_id, vehicle_id, status, created_at)
    RDS-->>Handler: OK

    Handler-->>API: ServiceOrderResponse<br/>{ id, customerId, vehicleId,<br/>  status: "RECEIVED", createdAt }

    API-->>VPCLink: 201 Created<br/>Location: /api/service-orders/{id}<br/>{ id, customerId, vehicleId,<br/>  status: "RECEIVED", createdAt }
    VPCLink-->>APIGW: 201 Created
    APIGW-->>Staff: 201 Created
```

---

## Notes

**Vehicle ownership is not validated at creation time.** The handler only checks that both the customer and vehicle exist; it does not verify that the vehicle's `customer_id` matches the provided `customerId`. This is intentional for the current scope — a vehicle can have been registered independently before being associated with a service order.

**`ServiceOrder.Create` is a pure domain method.** It does not query the database; all persistence happens via `IAppDbContext`. The domain entity only enforces internal invariants (e.g., valid UUIDs, initial status must be `RECEIVED`).

**`status = RECEIVED` is the entry point of the lifecycle:**

```
RECEIVED → IN_DIAGNOSIS → AWAITING_APPROVAL → IN_EXECUTION → COMPLETED → DELIVERED
                                   ↓
                               CANCELLED
```

Each subsequent transition has its own endpoint and handler. See [ADR-002](../decisions/ADR-002-architecture.md) for the Vertical Slice + DDD design rationale.

---

## Related

- [Sequence — CPF authentication flow](./sequence-cpf-auth.md)
- [RFC-001 — Cloud, database, and auth design decisions](../rfc/RFC-001-fase3-cloud-db-auth.md)
- [ADR-002 — Architecture (Vertical Slice + DDD)](../decisions/ADR-002-architecture.md)
- [Database ER Diagram](../database/database-justification.md)
