# RFC-001: Escolha da Nuvem, Banco Gerenciado e Estratégia de Autenticação — Fase 3

**Status:** Accepted  
**Date:** 2026-07-07  
**Autores:** Joelma Renata Oliveira  
**Issues:** #258 (F3-30)

---

## Resumo

Este documento apresenta as justificativas e trade-offs por trás das três decisões de plataforma mais impactantes da Fase 3 do Tech Challenge:

1. **Escolha da nuvem:** AWS como provedor exclusivo de infraestrutura.
2. **Banco gerenciado:** migração do PostgreSQL hospedado em Kubernetes (Fase 2) para AWS RDS.
3. **Estratégia de autenticação:** substituição da autenticação local (ASP.NET Core) por uma cadeia Lambda CPF Auth → API Gateway → Lambda Authorizer.

Cada decisão é analisada com as alternativas consideradas e os trade-offs relevantes.

---

## Motivação

A Fase 2 entregou um sistema funcional em Kubernetes com PostgreSQL interno ao cluster e autenticação JWT gerenciada pela própria API. Para a Fase 3, os requisitos evoluíram para:

- Autenticação de clientes por **CPF** (identificador brasileiro, com algoritmo de dígitos verificadores).
- **Ponto de entrada único** para a plataforma, controlando acesso antes de chegar à aplicação.
- Banco de dados em serviço gerenciado com backups automáticos e isolamento da carga do cluster.
- Infraestrutura como código cobrindo todos os recursos de nuvem.

---

## 1. Escolha da Nuvem — AWS

### Contexto

O programa FIAP POS Tech disponibiliza créditos via **AWS Academy Learner Lab**, que oferece acesso a serviços gerenciados da AWS (EKS, RDS, Lambda, API Gateway, CloudWatch) por meio de credenciais temporárias rotacionadas a cada sessão.

### Decisão

Usar **AWS** como único provedor de nuvem, aproveitando os créditos do Learner Lab.

### Alternativas Consideradas

| Alternativa | Avaliação |
|---|---|
| **Google Cloud (GCP)** | GKE tem melhor UX para Kubernetes; sem créditos disponíveis para o grupo. |
| **Azure** | AKS + Azure Database for PostgreSQL são equivalentes; sem créditos disponíveis. |
| **Localstack (simulação AWS local)** | Útil para desenvolvimento, mas não atende ao requisito de infraestrutura real em nuvem. |
| **Conta pessoal AWS free tier** | Limites severos impedem EKS (não incluso no free tier); custo proibitivo para EKS + RDS. |

### Trade-offs

| Aspecto | Positivo | Negativo |
|---|---|---|
| Custo | Créditos do Learner Lab cobrem os serviços usados | Credenciais temporárias exigem `AWS_SESSION_TOKEN` rotacionado manualmente |
| Serviços disponíveis | EKS, RDS, Lambda, API Gateway, CloudWatch todos nativos | Algumas features avançadas têm restrições no Learner Lab |
| Curva de aprendizado | Time já tinha contato prévio com AWS | API Gateway v2 (HTTP API) tem documentação menos intuitiva que a v1 (REST API) |

---

## 2. Banco de Dados Gerenciado — AWS RDS PostgreSQL 16

### Contexto

Na **Fase 2**, o PostgreSQL rodava dentro do próprio cluster EKS:

```
k8s/
  deployment-db.yaml    ← Pod PostgreSQL
  pvc.yaml              ← PersistentVolumeClaim para os dados
  service-db.yaml       ← ClusterIP interno
```

Esse modelo funciona para desenvolvimento e laboratório, mas apresenta riscos reais:

- A perda do node onde o PVC está bound destrói os dados.
- O Pod compete por memória com a aplicação no mesmo node (`t3.small`, 2 GB RAM).
- Backups e patches de versão são responsabilidade do time.

### Decisão

Migrar para **AWS RDS PostgreSQL 16** provisionado via Terraform no repositório `mechanics-infra-db`.

```hcl
resource "aws_db_instance" "this" {
  engine         = "postgres"
  engine_version = "16"
  instance_class = var.db_instance_class
  db_name        = var.db_name
  username       = var.db_username
  password       = var.db_password

  db_subnet_group_name   = aws_db_subnet_group.this.name
  vpc_security_group_ids = [aws_security_group.rds.id]
  publicly_accessible    = false
}
```

O Security Group libera a porta 5432 **apenas** para o CIDR do VPC (`10.0.0.0/16`), mantendo o banco inacessível externamente.

### Alternativas Consideradas

| Alternativa | Avaliação |
|---|---|
| **PostgreSQL em K8s com StatefulSet + EBS** | Mais resiliente que Deployment + PVC, mas ainda exige gerenciamento de backups e patches. |
| **Aurora Serverless v2 (PostgreSQL-compatible)** | Escala automática e zero-idle; custo maior e latência de cold start incompatível com o profile de uso do laboratório. |
| **Amazon DynamoDB** | Sem relações nativas; o modelo de dados do sistema é fortemente relacional (FK entre OS, Budget, ServiceItems). Incompatível. |
| **Manter PostgreSQL em K8s (status quo)** | Sem custo adicional; porém riscos de perda de dados e concorrência de recursos no node. |

### Trade-offs

| Aspecto | RDS | K8s-hosted |
|---|---|---|
| Backups | Automáticos (7 dias retenção por padrão) | Manual (CronJob ou snapshot de EBS) |
| Patches | Automáticos (janela configurável) | Manual |
| Disponibilidade | Multi-AZ opcional | Single Pod — sem HA nativa |
| Custo | Instância `db.t3.micro` sempre ligada | Compartilha node com a app |
| Acesso à connection string | Via Secret K8s (`DATABASE_URL`) | Idem |
| Complexidade de IaC | Módulo Terraform dedicado (`mechanics-infra-db`) | Manifesto K8s |

### Impacto nos Manifestos K8s

Com a migração para RDS, o `deployment-db.yaml` e o `pvc.yaml` foram removidos do repositório. A connection string é injetada via Secret:

```yaml
env:
  - name: DATABASE_URL
    valueFrom:
      secretKeyRef:
        name: mechanics-secrets
        key: database_url
```

---

## 3. Estratégia de Autenticação

### Contexto

Na **Fase 1 e 2**, toda a autenticação era responsabilidade da API ASP.NET Core:

- `POST /api/auth/login` recebia email + senha, retornava JWT.
- O middleware do ASP.NET Core (`AddAuthentication().AddJwtBearer()`) validava o token em cada requisição.

A Fase 3 adiciona o requisito de **autenticação por CPF** (identificador de pessoa física no Brasil), sem senha — apenas o número de CPF válido é suficiente para obter um token de acesso como cliente.

### Decisão

Implementar uma cadeia de autenticação em três camadas:

```
Cliente
  │
  ▼
POST /auth  ──→  AWS Lambda (mechanics-lambda)
                  │  1. Valida formato e dígitos verificadores do CPF
                  │  2. Busca o cliente no RDS por CPF
                  │  3. Verifica se o cliente está ativo
                  │  4. Gera JWT assinado com HS256
                  ▼
              { token: "eyJ..." }

Requisições subsequentes:
  │
  ▼
API Gateway HTTP API v2
  │  Lambda Authorizer (REQUEST type)
  │    1. Extrai Bearer token do header Authorization
  │    2. Valida assinatura e claims do JWT (HS256)
  │    3. Retorna { isAuthorized: true/false, context: { cpf, role } }
  │
  ▼  (se isAuthorized: true)
EKS → API ASP.NET Core
  │  GatewayKeyMiddleware valida X-Gateway-Key
  │  (header injetado pelo API Gateway; bloqueia acesso direto ao cluster)
```

#### Lambda CPF Auth (`src/handler.js`)

```js
export const handler = async (event) => {
  const cpf = event?.cpf ?? JSON.parse(event?.body ?? '{}')?.cpf;
  if (!cpf)               return response(400, { error: 'CPF is required' });
  if (!validateCpf(cpf))  return response(400, { error: 'Invalid CPF' });
  const customer = await findCustomer(cpf);
  if (!customer)          return response(404, { error: 'Customer not found' });
  if (!customer.active)   return response(403, { error: 'Customer is inactive' });
  const token = generateToken({ customerId: customer.id, cpf: customer.cpf });
  return response(200, { token });
};
```

#### Lambda Authorizer (`src/authorizer.js`)

```js
export const handler = async (event) => {
  const token = bearerToken(event);
  if (!token) return DENIED;
  try {
    const payload = jwt.verify(token, secret, { algorithms: ['HS256'], issuer, audience });
    return { isAuthorized: true, context: { cpf: payload.cpf, role: payload.role } };
  } catch {
    return DENIED;
  }
};
```

#### Gateway Key (`GatewayKeyMiddleware.cs`)

O API Gateway injeta o header `X-Gateway-Key` em cada requisição antes de encaminhar ao NLB do EKS. A aplicação valida o header com comparação em tempo constante (`CryptographicOperations.FixedTimeEquals`) para prevenir timing attacks. Requisições sem o header correto recebem `403 Forbidden`.

### Alternativas Consideradas

| Alternativa | Avaliação |
|---|---|
| **Manter auth local (ASP.NET Core só)** | Não atende o requisito de autenticação por CPF no API Gateway; toda validação ocorreria após chegar à aplicação. |
| **API Gateway JWT Authorizer nativo** | Valida JWT automaticamente sem Lambda, mas não permite lógica customizada (ex.: verificar se cliente está ativo no banco). |
| **Cognito User Pools** | Solução completa de identity; custo e complexidade além do escopo; sem suporte nativo a CPF como identificador. |
| **Lambda Authorizer com IAM policy** | Retorna uma IAM policy completa (`Allow/Deny`); mais flexível para multi-resource, porém verboso e mais lento para gerar. O formato **simple response** (`isAuthorized: bool`) é suficiente e mais performático. |
| **mTLS no API Gateway** | Autenticação por certificado cliente; adequado para B2B, não para usuários finais com CPF. |

### Trade-offs

| Aspecto | Lambda Authorizer | App-level JWT |
|---|---|---|
| Ponto de validação | Borda (API Gateway) — bloqueia antes de chegar ao cluster | Aplicação — tráfego chega até o pod |
| Centralização | Um authorizer para todas as rotas | Cada serviço valida individualmente |
| Cache | TTL configurável no API Gateway (reduz invocações Lambda) | Sem cache nativo |
| Latência extra | +latência da invocação Lambda (mitigada pelo cache) | Zero overhead extra |
| Manutenção | Código Lambda separado (Node.js 20) | Co-localizado com a app (C#) |
| Visibilidade | Logs no CloudWatch separados da app | Logs consolidados com a app |

---

## Consequências Transversais

### Positivas

- **Defense in depth:** três camadas de validação (Lambda Auth → API GW Authorizer → Gateway Key na app) tornam bypass significativamente mais difícil.
- **Separação de responsabilidades:** a validação de CPF (domínio brasileiro) fica isolada em uma Lambda Node.js, fora da API principal.
- **Escalabilidade independente:** Lambda e EKS escalam por demandas diferentes.

### Negativas / Riscos

- **Credenciais temporárias:** o AWS Academy rotaciona `AWS_SESSION_TOKEN` a cada sessão — o time deve atualizar os secrets no GitHub Actions manualmente antes de cada deploy.
- **Cold start Lambda:** invocações infrequentes podem ter latência de cold start (tipicamente 100–300 ms em Node.js 20). O cache do authorizer (TTL configurável) mitiga reinvocações frequentes.
- **Acoplamento da connection string:** a Lambda CPF Auth acessa diretamente o RDS; uma mudança de schema na tabela `customers` exige atualização coordenada entre dois repositórios (`mechanics-software` e `mechanics-lambda`).
