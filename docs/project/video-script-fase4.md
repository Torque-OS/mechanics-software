# Fase 4 — Roteiro do vídeo

**Duração máxima:** 15 minutos · **Gravação:** 26/11/2026 · **Publicação:** YouTube ou Vimeo (não listado)

O enunciado pede: o fluxo completo de uma OS pelos serviços, a saga e o tratamento de falhas, o deploy automatizado
com testes e o monitoramento/rastreamento distribuído (issue #332).

---

## Antes de gravar

- [ ] Ambiente no ar:
  - `kubectl get pods -A | grep mechanics` mostra os pods do OS (`mechanics-software`), do Billing
    (`mechanics-billing`) e do Execution (`mechanics-execution`) em `Running`;
  - `kubectl get pods -n messaging` mostra o RabbitMQ em `Running`;
  - `terraform output api_gateway_url` (em `mechanics-infra-k8s`) devolve a URL do gateway.
- [ ] Postman com as três coleções importadas e `baseUrl` = URL do gateway em todas.
- [ ] Um cliente com CPF válido e um veículo já cadastrados (anotar o CPF e os ids).
- [ ] Uma peça com estoque conhecido (anotar o id e a quantidade, para mostrar a devolução ao estoque).
- [ ] App do Mercado Pago logado com o **comprador de teste** (para pagar o QR).
- [ ] Abas abertas:
  - GitHub Actions dos três serviços, com uma execução recente bem-sucedida de CI e deploy;
  - dashboard da saga no Datadog e o APM (Traces);
  - RabbitMQ Management via `kubectl -n messaging port-forward svc/rabbitmq 15672:15672`;
  - `docs/architecture/architecture-fase4.md` renderizado no GitHub.
- [ ] Fonte grande (≥ 14 px) no terminal, no Postman e no navegador; resolução 1920×1080.

---

## Roteiro

### 1. Abertura (1 min)

**Tela:** diagrama de arquitetura (`docs/architecture/architecture-fase4.md`).

- Grupo TorqueOS, integrantes.
- O que mudou na Fase 4: o monólito virou três serviços (OS, Billing, Execution), cada um com PostgreSQL e MongoDB
  próprios, conversando por RabbitMQ numa saga orquestrada pelo OS Service.

> *"Cada serviço é dono dos seus dados. Ninguém acessa o banco de outro serviço: tudo passa por mensagens ou por
> uma chamada REST."*

### 2. Repositórios e decisões (1 min 30)

**Tela:** organização Torque-OS no GitHub.

- Os seis repositórios e o papel de cada um (tabela da seção 1 do `docs/entrega-fase4.md`).
- ADR-011: por que saga orquestrada — o OS é o dono do ciclo de vida da OS e decide a compensação.
- ADR-012: PostgreSQL para o que é transacional (com outbox e inbox), MongoDB para históricos e projeções.

### 3. Login e entrada pelo gateway (1 min)

**Tela:** Postman.

1. `POST /api/auth/login` (staff) → token do atendente.
2. `POST /auth` com o CPF do cliente → token do cliente (Lambda).
3. Uma chamada sem token → `401` no gateway, antes de chegar ao cluster.

> *"O API Gateway é a única entrada. Ele valida o JWT com o Lambda Authorizer e roteia por prefixo para o serviço
> certo; cada serviço valida o token de novo e aplica as regras de papel."*

### 4. Fluxo feliz pelos três serviços (4 min)

**Tela:** Postman, com o RabbitMQ Management numa segunda janela.

1. **OS:** `POST /api/service-orders` → `start-diagnosis` → adicionar um serviço e a peça anotada →
   `POST /api/service-orders/{id}/request-budget` (`202`). Mostrar no RabbitMQ a mensagem `RequestBudget` passando.
2. **Billing:** `GET /api/budgets?serviceOrderId={id}` mostra o orçamento gerado. O OS foi para `AWAITING_APPROVAL`.
3. **Cliente aprova** com o **token do cliente**: `POST /api/budgets/{id}/decision` `{ "decision": "approve" }`.
   OS em `AWAITING_PAYMENT`.
4. **Pagamento:** `POST /api/payments` → abrir `GET /api/payments/{id}/qr.png` e pagar com o app do comprador de
   teste. Se o webhook demorar, `POST /api/payments/{id}/verify`.
5. **Execution:** `GET /api/executions` mostra a OS na fila → `start-diagnosis` → `complete`.
6. **OS:** `GET /api/service-orders/{id}/history` mostra a linha do tempo inteira até `COMPLETED`.

> *"Cada transição veio de um evento. O OS nunca chamou o Billing ou o Execution para mudar de estado: ele reagiu
> às mensagens e mandou os próximos comandos pelo outbox."*

### 5. Falha e compensação (3 min)

**Tela:** Postman e o dashboard da saga.

1. Repetir o fluxo até o pagamento confirmado (pode ser mais rápido, sem narrar cada passo).
2. **Execution:** `start-diagnosis` → `POST /api/executions/{id}/fail` `{ "reason": "Peça incompatível" }`.
3. **OS:** `GET /api/service-orders/{id}/status` → `CANCELLED`.
4. **Estoque:** `GET /api/parts/{id}` mostra a quantidade de volta ao valor anotado.
5. **Billing:** `GET /api/payments/{id}` → `REFUNDED`. O estorno foi pedido ao Mercado Pago com uma chave de
   idempotência, então repetir a mensagem nunca estorna duas vezes.
6. Explicar rapidamente o que protege a saga: outbox, inbox, 5 retries com backoff e a fila `_error` (DLQ) para o
   que não pode ser reprocessado.

> *"A compensação é decidida em um só lugar, o orquestrador: OS cancelada, peças devolvidas e pagamento estornado."*

### 6. Deploy automatizado com testes (2 min)

**Tela:** GitHub Actions.

- Pipeline de cada serviço: build, testes, gate de 80% de cobertura, SonarCloud e deploy no EKS.
- Mostrar o comentário de cobertura num PR e os números: OS 96,6%, Billing 99,1%, Execution 95,1%.
- Mostrar os cenários BDD (`ServiceOrderSaga.feature`): fluxo feliz, falha no pagamento e falha na execução.
- `kubectl get pods -A | grep mechanics` — os três serviços rodando, cada um no seu namespace.

### 7. Monitoramento e rastreamento distribuído (2 min)

**Tela:** Datadog.

- **Dashboard da saga:** volume de OS, tempo por etapa, compensações por etapa (a OS cancelada no passo 5 aparece
  aqui), mensagens consumidas e publicadas, falhas e DLQs.
- **Trace distribuído:** abrir o trace de uma OS e mostrar o caminho HTTP → RabbitMQ → consumidor do Billing → volta
  ao OS.
- **Logs:** filtrar pelo `CorrelationId` (o id da OS) e ver as linhas dos serviços envolvidos.

### 8. Encerramento (30 s)

- Recapitular: três serviços, banco por serviço, saga orquestrada com compensação, deploy automatizado e
  observabilidade de ponta a ponta.
- Links no documento de entrega (`docs/entrega-fase4.md`).

---

## Tempo

| Parte | Tempo | Acumulado |
|---|---|---|
| 1. Abertura | 1:00 | 1:00 |
| 2. Repositórios e decisões | 1:30 | 2:30 |
| 3. Login e gateway | 1:00 | 3:30 |
| 4. Fluxo feliz | 4:00 | 7:30 |
| 5. Falha e compensação | 3:00 | 10:30 |
| 6. Deploy e testes | 2:00 | 12:30 |
| 7. Monitoramento | 2:00 | 14:30 |
| 8. Encerramento | 0:30 | 15:00 |

Se passar do tempo, encurte a parte 2 e a repetição do fluxo na parte 5.

## Dicas

- Ensaie o fluxo uma vez antes de gravar: os ids mudam a cada OS.
- O QR expira em 15 minutos; gere o pagamento só na hora de pagar.
- O webhook de pagamento pode atrasar no sandbox; o `verify` é a saída sem cortar a gravação.
- Grave em partes e junte depois, se algo falhar no meio.
