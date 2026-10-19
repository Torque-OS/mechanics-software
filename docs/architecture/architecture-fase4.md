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
    OS->>B: RefundPayment
    B-->>OS: PaymentRefunded
    OS->>B: CancelBudget
    B-->>OS: BudgetCancelled
    OS-->>OS: CANCELLED
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
| `RefundPayment`, `CancelBudget`, `CancelExecution` | comando (compensação) | OS → Billing/Execution |

Todas as mensagens carregam `ServiceOrderId` (correlação), `MessageId` (idempotência) e o *trace context* para rastreamento distribuído no Datadog.

## Decisões relacionadas

ADR-010 (divisão), ADR-011 (saga + mensageria), ADR-012 (bancos), ADR-007 (API Gateway), ADR-009 (HPA).
