# ADR-013: REST Síncrono entre Serviços

**Status:** Accepted
**Date:** 2026-11-14
**Autores:** Time Torque-OS

---

## Contexto

A ADR-011 define que os serviços se comunicam por mensagens (RabbitMQ) e que REST síncrono só é usado "onde necessário (consultas), com timeout e resiliência". Até aqui nenhuma chamada HTTP entre serviços existia.

Surgiu uma necessidade real de consulta: quem atende uma OS precisa ver, em um só lugar, o **orçamento** (itens, total, status) e o **pagamento** (status, QR) — dados que pertencem ao Billing Service (ADR-010, ADR-012). Replicar esses dados no OS Service por eventos traria uma cópia a mais para manter consistente só para leitura.

## Decisão

### Quando usar REST síncrono

- **Somente para consultas** (leitura). Tudo que muda estado entre serviços continua sendo comando/evento pela mensageria, com outbox (ADR-011).
- **Somente via a API pública do outro serviço** — nunca acesso direto ao banco de outro serviço (ADR-012).
- **A indisponibilidade do outro serviço nunca derruba a operação local**: a resposta degrada (dados ausentes + motivo), em vez de falhar.

### Primeiro caso: OS Service → Billing Service

- `GET /api/service-orders/{id}/billing` (staff) no OS Service consulta o Billing:
  - `GET /api/budgets?serviceOrderId={id}` — orçamento da OS;
  - `GET /api/payments?budgetId={id}` — pagamento do orçamento.
- `404` do Billing significa "ainda não existe" (sem orçamento ou sem pagamento), não erro.
- Se o Billing estiver indisponível, a resposta é `200` com `billingAvailable: false` e `unavailableReason`.
- O token JWT de quem chamou é repassado ao Billing (mesmo `JWT_SECRET`/issuer/audience na plataforma), então as políticas de acesso do Billing continuam valendo.
- Endereço configurado em `BILLING_API_URL` (no cluster: `http://mechanics-billing-api.mechanics-billing.svc.cluster.local:8080`).

### Resiliência (`Microsoft.Extensions.Http.Resilience`)

| Política | Valor | Por quê |
|---|---|---|
| Timeout por tentativa | 2 s | consulta simples; acima disso o Billing está degradado |
| Timeout total | 5 s | limita o tempo que a requisição do usuário fica presa |
| Retry | 2 novas tentativas, backoff exponencial com jitter | só para falhas transitórias (5xx, 408, 429, falha de rede, timeout); 4xx não é repetido |
| Circuit breaker | abre com 50% de falhas em 30 s (mínimo 5 chamadas), fica aberto 15 s | evita insistir num Billing fora do ar e libera o OS Service rápido |

O tracing HTTP (OpenTelemetry) propaga o trace context, então a consulta aparece no mesmo trace da requisição original.

## Alternativas descartadas

- **Projeção local por eventos:** o OS Service manteria uma cópia de orçamento/pagamento a partir dos eventos do Billing. Descartada por ora: mais tabelas e consumidores só para leitura, e a cópia fica atrasada em relação ao Mercado Pago.
- **API Gateway agregando as duas APIs:** move regra de composição para a infraestrutura e não resolve a degradação graciosa.

## Consequências

- O OS Service passa a depender da disponibilidade do Billing **apenas** para essa consulta; o restante do fluxo continua assíncrono.
- Novas consultas síncronas entre serviços devem seguir as mesmas regras (só leitura, API pública, timeout/retry/circuit breaker, degradação graciosa) e ser registradas aqui.
