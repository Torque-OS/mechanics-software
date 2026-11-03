# Sequence Diagram — CPF Authentication Flow

**Issue:** F3-28  
**Repo:** mechanics-lambda + mechanics-infra-k8s (API Gateway)

The CPF auth flow has two distinct sub-flows:

1. **Token issuance** — client exchanges a CPF for a JWT (`POST /auth`)
2. **Authorized request** — subsequent requests carry the JWT; the Lambda Authorizer validates it before the API receives it

---

## Part 1 — Token Issuance (`POST /auth`)

```mermaid
sequenceDiagram
    autonumber
    actor Client
    participant APIGW  as API Gateway<br/>(HTTP API v2)
    participant Lambda  as mechanics-lambda<br/>(handler.js)
    participant RDS     as RDS PostgreSQL 16<br/>(customers table)

    Client->>APIGW: POST /auth<br/>{ "cpf": "12345678909" }

    APIGW->>Lambda: invoke mechanics-lambda<br/>(no authorizer on this route)

    note over Lambda: Validate CPF format<br/>11 digits, not all equal
    note over Lambda: Validate check digits<br/>(modulo-11 algorithm)

    alt CPF format invalid
        Lambda-->>APIGW: 400 Bad Request<br/>{ "error": "Invalid CPF" }
        APIGW-->>Client: 400 Bad Request
    end

    Lambda->>RDS: SELECT id, document AS cpf, active<br/>FROM customers<br/>WHERE document = $1 LIMIT 1
    RDS-->>Lambda: row | null

    alt Customer not found
        Lambda-->>APIGW: 401 Unauthorized<br/>{ "error": "Customer not found" }
        APIGW-->>Client: 401 Unauthorized
    else Customer inactive (active = false)
        Lambda-->>APIGW: 401 Unauthorized<br/>{ "error": "Customer not found" }
        APIGW-->>Client: 401 Unauthorized
    end

    note over Lambda: Sign JWT (HS256)<br/>sub = customer.id<br/>cpf = customer.cpf<br/>role = CUSTOMER<br/>iss = torque-os<br/>aud = mechanics-software-api<br/>jti = random UUID<br/>exp = now + JWT_EXPIRATION

    Lambda-->>APIGW: 200 OK<br/>{ "token": "<JWT>" }
    APIGW-->>Client: 200 OK<br/>{ "token": "<JWT>" }
```

---

## Part 2 — Authorized Request (any protected route)

After obtaining a token, the client includes it as `Authorization: Bearer <JWT>` on every subsequent call. The Lambda Authorizer intercepts the request before it reaches the API.

```mermaid
sequenceDiagram
    autonumber
    actor Client
    participant APIGW   as API Gateway<br/>(HTTP API v2)
    participant Authz   as mechanics-lambda-authorizer<br/>(authorizer.js)
    participant VPCLink as VPC Link → NLB
    participant API     as ASP.NET Core API<br/>(EKS pod)
    participant RDS     as RDS PostgreSQL 16

    Client->>APIGW: ANY /{proxy+}<br/>Authorization: Bearer <JWT>

    note over APIGW: Route matches protected path<br/>→ invoke Lambda Authorizer<br/>(cached up to 300 s per token)

    APIGW->>Authz: Authorization header + request context

    note over Authz: Verify JWT signature (HS256)<br/>Verify iss = torque-os<br/>Verify aud = mechanics-software-api<br/>Verify exp not elapsed<br/>No database access

    alt Token invalid or expired
        Authz-->>APIGW: isAuthorized: false
        APIGW-->>Client: 403 Forbidden
    end

    Authz-->>APIGW: isAuthorized: true<br/>context: { sub, cpf, role }

    note over APIGW: Inject X-Gateway-Key header<br/>Forward to VPC Link integration

    APIGW->>VPCLink: HTTP request + X-Gateway-Key + Authorization header
    VPCLink->>API: HTTP request (internal NLB → pod)

    note over API: GatewayKeyMiddleware<br/>validates X-Gateway-Key<br/>(FixedTimeEquals — constant time)

    alt X-Gateway-Key missing or wrong
        API-->>VPCLink: 401 Unauthorized
        VPCLink-->>APIGW: 401
        APIGW-->>Client: 401 Unauthorized
    end

    note over API: JwtBearer middleware<br/>re-validates JWT signature,<br/>iss, aud, exp

    note over API: Authorization policy check<br/>(Policies.CustomerOrStaff<br/>or Policies.Staff)

    API->>RDS: query (depends on endpoint)
    RDS-->>API: result

    API-->>VPCLink: 200 / 201 / 204
    VPCLink-->>APIGW: response
    APIGW-->>Client: response
```

---

## Security layers summary

| Layer | Who enforces | What it checks |
|-------|-------------|---------------|
| Lambda Authorizer | API Gateway | JWT signature, issuer, audience, expiry |
| `GatewayKeyMiddleware` | ASP.NET Core API | `X-Gateway-Key` header — defence in depth against traffic bypassing API Gateway |
| `JwtBearer` middleware | ASP.NET Core API | JWT re-validation (signature + claims) |
| Authorization policy | ASP.NET Core API | `role` claim — distinguishes `CUSTOMER` from staff |

The API validates the JWT a second time (step 10) even though the authorizer already did it. This is intentional: the NLB endpoint is internal, but re-validation ensures no forged request can reach business logic even if the network perimeter is compromised.

---

## Related

- [RFC-001 — Cloud, database, and auth design decisions](../rfc/RFC-001-fase3-cloud-db-auth.md)
- [ADR-007 — Communication pattern (HTTP API v2 + VPC Link)](../decisions/ADR-007-communication-pattern.md)
- [ADR-008 — Lambda Authorizer design](../decisions/ADR-008-lambda-authorizer.md)
- [Sequence — Service order opening](./sequence-service-order-opening.md)
