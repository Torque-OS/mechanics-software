# ADR-012: Banco de Dados por Serviço (PostgreSQL + MongoDB)

**Status:** Accepted
**Date:** 2026-09-20
**Autores:** Time Torque-OS

---

## Contexto

Cada microsserviço deve ter banco próprio, com **ao menos um relacional (SQL) e um não relacional (NoSQL)**, e nenhum serviço acessa o banco de outro.

## Decisão

| Serviço | SQL (PostgreSQL) | NoSQL (MongoDB) |
|---|---|---|
| OS Service | Clientes, veículos, catálogo, peças, OS, itens, estado da saga | Histórico de status da OS e linha do tempo da saga |
| Billing Service | Orçamentos, pagamentos | Payloads brutos de eventos/webhooks do Mercado Pago |
| Execution Service | Ordens de execução | Fila de execução e anotações de diagnóstico/reparo |

- **PostgreSQL:** dados transacionais com integridade referencial (mantém a decisão da ADR-003 e o RDS da Fase 3, agora **uma instância/schema/credencial por serviço**).
- **MongoDB:** dados append-only, semiestruturados e de alta escrita (histórico, eventos, payloads externos), onde o esquema flexível evita migrações.
- Credenciais isoladas por serviço (Secrets do Kubernetes); rede restringe o acesso de cada banco ao seu serviço.

## Justificativa do MongoDB

- Roda igual em `docker-compose`, testes (Testcontainers) e Kubernetes/Atlas, sem depender de acesso AWS para desenvolver.
- Driver .NET maduro; documentos se ajustam ao histórico e aos payloads de terceiros.
- **DynamoDB descartado:** gerenciado, porém com lock-in e teste local mais difícil.

## Consequências

- Dois modelos de persistência por serviço (mais código de infraestrutura e testes).
- Sem `JOIN` entre serviços: dados de outros contextos vêm por evento/snapshot.
- Hospedagem do MongoDB a definir na infraestrutura (Atlas free tier ou in-cluster) — issue F4-09.
