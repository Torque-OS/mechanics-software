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

## Saga — fluxo feliz

```mermaid
sequenceDiagram
    participant C as Cliente
    participant OS as OS Service (orquestrador)
    participant B as Billing
    participant E as Execution
    C->>OS: POST /service-orders
    OS-->>OS: ServiceOrderOpened (RECEIVED)
    OS->>B: RequestBudget (snapshot de itens)
    B-->>OS: BudgetGenerated
    C->>B: Aprova orçamento
    B-->>OS: BudgetApproved
    OS->>B: RequestPayment
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
| `RequestPayment` | comando | OS → Billing |
| `PaymentConfirmed` / `PaymentFailed` | evento | Billing → OS |
| `EnqueueExecution` | comando | OS → Execution |
| `ExecutionCompleted` / `ExecutionFailed` | evento | Execution → OS |
| `CancelBudget` | comando (compensação) | OS → Billing; cancela o QR ou estorna o pagamento, conforme o estado do pagamento |
| `BudgetCancelled` | evento | Billing → OS |
| `CancelExecution` | comando (compensação) | OS → Execution |

Todas as mensagens carregam `ServiceOrderId` (correlação), `MessageId` (idempotência) e o *trace context* para rastreamento distribuído no Datadog.

## Decisões relacionadas

ADR-010 (divisão), ADR-011 (saga + mensageria), ADR-012 (bancos), ADR-007 (API Gateway), ADR-009 (HPA).


### Payloads

Cada serviço publica seus contratos no namespace `TorqueOS.Contracts.<Serviço>`: os do OS Service ficam no projeto `MechanicsSoftware.Contracts` (namespace `TorqueOS.Contracts.ServiceOrders`) e os do Billing em `TorqueOS.Contracts.Billing`. Quem consome deve usar **o mesmo namespace e nome de tipo** de quem publica, pois o MassTransit identifica a mensagem por eles (`urn:message:<namespace>:<tipo>`).

| Mensagem | Campos |
|---|---|
| `ServiceOrderOpened` | `ServiceOrderId` (Guid), `CustomerId` (Guid), `VehicleId` (Guid), `OpenedAt` (DateTime, UTC) |
| `EnqueueExecution` (`TorqueOS.Contracts.Execution`) | `ServiceOrderId` (Guid), `Priority` (int, padrão 0) |
| `CancelBudget` (`TorqueOS.Contracts.Billing`) | `ServiceOrderId` (Guid) |

