# Arquitetura — Fase 4 (Microsserviços + Saga)

## Visão geral

```mermaid
flowchart LR
    Client([Cliente]) --> GW[API Gateway HTTP API v2]
    GW -->|POST /auth| L[Lambda CPF Auth]
    GW -->|JWT + VPC Link| OS

    subgraph EKS[Cluster EKS]
        OS[OS Service<br/>+ Saga Orchestrator]
        BIL[Billing Service]
        EXE[Execution Service]
        MQ{{RabbitMQ}}
    end

    OS <--> MQ
    BIL <--> MQ
    EXE <--> MQ

    OS --- OSPG[(PostgreSQL OS)]
    OS --- OSMG[(MongoDB OS)]
    BIL --- BPG[(PostgreSQL Billing)]
    BIL --- BMG[(MongoDB Billing)]
    EXE --- EPG[(PostgreSQL Execution)]
    EXE --- EMG[(MongoDB Execution)]

    BIL -->|pagamento / webhook| MP[Mercado Pago]
    L --- OSPG
    DD[Datadog] -.métricas, logs, traces.- EKS
```

Nenhum serviço acessa o banco de outro; a comunicação é por mensagens (RabbitMQ) e, quando necessário, REST.

## Autenticação (CPF/JWT + API Gateway) — F4-13

A autenticação da Fase 3 continua a mesma; a Fase 4 só muda o que existe atrás do gateway. O API Gateway encaminha tráfego HTTP **apenas para o OS Service** — Billing e Execution não têm rotas públicas e conversam só via RabbitMQ.

```mermaid
sequenceDiagram
    actor C as Cliente
    participant GW as API Gateway
    participant LA as Lambda CPF Auth
    participant AZ as Lambda Authorizer
    participant OS as OS Service
    participant DB as PostgreSQL OS

    C->>GW: POST /auth { cpf }
    GW->>LA: invoca
    LA->>DB: SELECT customers WHERE document = cpf
    LA-->>C: JWT (sub = id do cliente, cpf, role = CUSTOMER), ou 404/403 se não existe/inativo
    C->>GW: GET /api/service-orders/{id}/status (Bearer)
    GW->>AZ: valida assinatura, iss, aud, exp
    AZ-->>GW: isAuthorized
    GW->>OS: VPC Link + NLB, com X-Gateway-Key
    OS->>OS: valida o JWT de novo + policy + dono da OS
    OS-->>C: 200 (ou 404 se a OS não for do cliente)
```

| Item | Onde | Valor |
|---|---|---|
| Algoritmo / segredo | Lambda (`JWT_SECRET`) e OS Service (`JWT_SECRET`) | HS256, **mesmo segredo** nos dois |
| Issuer / audience | `mechanics-lambda/src/config.js`, `JwtSettings.cs` | `torque-os` / `mechanics-software-api` |
| Rotas públicas (sem authorizer) | `mechanics-infra-k8s/modules/apigateway` | `POST /auth`, `POST /api/auth/login`, `GET /health` |
| Demais rotas | `ANY /{proxy+}` | Lambda Authorizer (REQUEST, cache configurável) |
| Defesa em profundidade | `GatewayKeyMiddleware` | header `X-Gateway-Key` injetado pelo gateway; o NLB é interno |

**Papéis**

| Papel | Origem do token | Acesso no OS Service |
|---|---|---|
| `ADMIN`, `ATTENDANT`, `MECHANIC` | `POST /api/auth/login` (e-mail + senha) | Todas as rotas (policy `Staff`) |
| `CUSTOMER` | `POST /auth` (CPF, Lambda) | Só `GET /api/service-orders/{id}/status` e `/history`, **e apenas das próprias OS** |

O OS Service compara o `sub` do token do cliente com o `CustomerId` da OS (`ClaimsPrincipalExtensions.OrderOwnerScope`). OS de outro cliente retorna **404**, não 403, para não revelar que o id existe. Token de cliente sem `sub` válido não acessa nenhuma OS.

**Verificação no ambiente:** `scripts/smoke-auth-gateway.sh` roda o fluxo completo contra o gateway publicado.

## Saga — fluxo feliz

```mermaid
sequenceDiagram
    participant C as Cliente
    participant OS as OS Service (orquestrador)
    participant B as Billing
    participant E as Execution
    C->>OS: POST /service-orders
    OS-->>OS: ServiceOrderOpened (RECEIVED)
    C->>OS: start-diagnosis + itens (IN_DIAGNOSIS)
    C->>OS: POST /service-orders/{id}/request-budget
    OS->>B: RequestBudget (snapshot de itens)
    B-->>OS: BudgetGenerated
    C->>B: Aprova orçamento
    B-->>OS: BudgetApproved
    C->>B: POST /payments (QR Mercado Pago)
    B-->>OS: PaymentConfirmed (Mercado Pago)
    OS->>E: EnqueueExecution
    E-->>OS: ExecutionCompleted
    OS-->>OS: COMPLETED
```

## Saga — falha na execução (compensação)

```mermaid
sequenceDiagram
    participant OS as OS Service (orquestrador)
    participant B as Billing
    participant E as Execution
    OS->>E: EnqueueExecution
    E-->>OS: ExecutionFailed
    OS-->>OS: CANCELLED
    OS->>B: CancelBudget
    B-->>B: estorna o pagamento (Mercado Pago)
    B-->>OS: BudgetCancelled
```

## Estados da OS e da Saga

`RECEIVED → IN_DIAGNOSIS → AWAITING_APPROVAL → AWAITING_PAYMENT → IN_EXECUTION → COMPLETED → DELIVERED`, com `CANCELLED` alcançável por rejeição ou compensação. `AWAITING_PAYMENT` é novo na Fase 4.

## Catálogo de eventos e comandos

| Nome | Tipo | Produtor → Consumidor |
|---|---|---|
| `ServiceOrderOpened` | evento | OS → (saga) |
| `RequestBudget` | comando | OS → Billing |
| `BudgetGenerated` / `BudgetFailed` | evento | Billing → OS |
| `BudgetApproved` / `BudgetRejected` | evento | Billing → OS |
| `PaymentConfirmed` / `PaymentFailed` | evento | Billing → OS |
| `EnqueueExecution` | comando | OS → Execution |
| `ExecutionCompleted` / `ExecutionFailed` | evento | Execution → OS |
| `CancelBudget` | comando (compensação) | OS → Billing; cancela o QR ou estorna o pagamento, conforme o estado do pagamento |
| `BudgetCancelled` | evento | Billing → OS |
| `CancelExecution` | comando (compensação) | OS → Execution; consumido pelo Execution, ainda não enviado pelo OS (as compensações atuais acontecem antes de enfileirar ou depois de a execução falhar) |

O pagamento é iniciado pelo cliente no Billing (`POST /api/payments`), por isso não existe um comando `RequestPayment`.

Todas as mensagens carregam `ServiceOrderId` (correlação), `MessageId` (idempotência) e o *trace context* para rastreamento distribuído no Datadog.

## Confiabilidade da mensageria (F4-24)

Nos três serviços, todo consumidor tem:

- **Inbox** — uma mensagem com `MessageId` já consumido é descartada (`inbox_state`).
- **Outbox** — comandos e eventos são gravados na mesma transação da mudança de estado e enviados depois do commit.
- **Retry** — 5 novas tentativas com backoff exponencial (cerca de 16 s) para falhas transitórias; regra de negócio violada ou entidade inexistente vai direto para a fila `_error` (DLQ), sem novas tentativas.
- **Handlers idempotentes** — um evento cujo efeito já foi aplicado não altera nada nem reenvia comandos.

## Consulta síncrona (F4-25)

```mermaid
sequenceDiagram
    participant C as Cliente (staff)
    participant OS as OS Service
    participant B as Billing
    C->>OS: GET /service-orders/{id}/billing
    OS->>B: GET /budgets?serviceOrderId= (token repassado)
    B-->>OS: orçamento (ou 404)
    OS->>B: GET /payments?budgetId=
    B-->>OS: pagamento (ou 404)
    OS-->>C: orçamento + pagamento
    Note over OS,B: timeout 2 s/5 s, retry com backoff, circuit breaker;<br/>Billing fora do ar → 200 com billingAvailable=false
```

Regras na [ADR-013](../decisions/ADR-013-rest-sincrono-entre-servicos.md).

## Decisões relacionadas

ADR-010 (divisão), ADR-011 (saga + mensageria), ADR-012 (bancos), ADR-013 (REST síncrono), ADR-007 (API Gateway), ADR-009 (HPA).


### Payloads

Cada serviço publica seus contratos no namespace `TorqueOS.Contracts.<Serviço>`: os do OS Service ficam no projeto `MechanicsSoftware.Contracts` (namespace `TorqueOS.Contracts.ServiceOrders`) e os do Billing em `TorqueOS.Contracts.Billing`. Quem consome deve usar **o mesmo namespace e nome de tipo** de quem publica, pois o MassTransit identifica a mensagem por eles (`urn:message:<namespace>:<tipo>`).

| Mensagem | Campos |
|---|---|
| `ServiceOrderOpened` | `ServiceOrderId` (Guid), `CustomerId` (Guid), `VehicleId` (Guid), `OpenedAt` (DateTime, UTC) |
| `EnqueueExecution` (`TorqueOS.Contracts.Execution`) | `ServiceOrderId` (Guid), `Priority` (int, padrão 0) |
| `CancelBudget` (`TorqueOS.Contracts.Billing`) | `ServiceOrderId` (Guid) |
| `RequestBudget` (`TorqueOS.Contracts.Billing`) | `ServiceOrderId` (Guid), `CustomerId` (Guid), `Items` (lista de `RequestBudgetItem`: `Type` = `Service`/`Part`, `ReferenceId` (Guid), `Description`, `UnitPriceInCents` (int), `Quantity` (int)) |

