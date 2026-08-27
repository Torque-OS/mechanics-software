# ADR-007: Padrão de Comunicação — API Gateway HTTP API v2 + VPC Link

**Status:** Accepted  
**Date:** 2026-07-07  
**Autores:** Joelma Renata Oliveira

---

## Contexto

Com a introdução do **API Gateway** na Fase 3, o sistema passou a ter um ponto de entrada único para toda a plataforma. A decisão de como o API Gateway se comunica com o cluster EKS — e qual tipo de API Gateway usar — determina custo, latência, flexibilidade e segurança da solução.

Três questões foram avaliadas:

1. **Tipo de API Gateway:** HTTP API (v2) vs REST API (v1).
2. **Mecanismo de integração:** VPC Link vs internet pública.
3. **Target da integração:** NLB (Network Load Balancer) vs ALB (Application Load Balancer).

---

## Decisão

Adotar **AWS API Gateway HTTP API (v2)** integrado ao EKS via **VPC Link** apontando para o **NLB** provisionado pelo Kubernetes Service do tipo `LoadBalancer`.

```
Internet
   │
   ▼
API Gateway HTTP API v2  (mechanics-software)
   │  Authorizer: Lambda REQUEST (authorizer.js)
   │  Route: ANY /{proxy+}  →  CUSTOM auth
   │  Route: POST /auth     →  Lambda CPF Auth (sem auth)
   │  Route: GET /health    →  sem auth
   │
   ▼  (via VPC Link — conexão privada)
NLB  (criado automaticamente pelo K8s Service type=LoadBalancer)
   │
   ▼
EKS Node (NodePort)
   │
   ▼
Pod ASP.NET Core API
```

### Configuração Terraform

```hcl
# VPC Link conecta o API Gateway à subnet privada do EKS
resource "aws_apigatewayv2_vpc_link" "this" {
  name               = var.api_name
  subnet_ids         = var.vpc_link_subnet_ids
  security_group_ids = var.vpc_link_security_group_ids
}

# Integração HTTP_PROXY para o NLB
resource "aws_apigatewayv2_integration" "eks" {
  api_id           = aws_apigatewayv2_api.this.id
  integration_type = "HTTP_PROXY"
  connection_type  = "VPC_LINK"
  connection_id    = aws_apigatewayv2_vpc_link.this.id
  integration_uri  = var.nlb_listener_arn   # ARN do listener do NLB
}

# Rota catch-all com autenticação customizada
resource "aws_apigatewayv2_route" "proxy" {
  api_id             = aws_apigatewayv2_api.this.id
  route_key          = "ANY /{proxy+}"
  authorization_type = "CUSTOM"
  authorizer_id      = aws_apigatewayv2_authorizer.jwt[0].id
  target             = "integrations/${aws_apigatewayv2_integration.eks.id}"
}
```

---

## Alternativas Consideradas

### REST API (v1) vs HTTP API (v2)

| Critério | REST API v1 | HTTP API v2 (escolhida) |
|---|---|---|
| Custo | ~$3,50/milhão de requisições | ~$1,00/milhão de requisições |
| Latência | Maior (mais camadas de processamento) | Menor (~60% mais rápido) |
| Lambda Authorizer | Retorna IAM Policy (verboso) | Suporta simple response (`isAuthorized: bool`) |
| VPC Link | Suportado | Suportado |
| WebSocket | Não nativo | Suportado (se necessário no futuro) |
| Recursos avançados | Usage plans, API keys, caching nativo | Mais limitado — sem caching de resposta nativo |

A HTTP API v2 atende todos os requisitos da Fase 3 com custo e latência menores. Os recursos avançados da v1 (caching de resposta, usage plans) não são necessários.

### VPC Link vs Integração pela Internet Pública

| Critério | VPC Link (escolhido) | Internet pública |
|---|---|---|
| Segurança | Tráfego nunca sai da VPC AWS | Tráfego passa pela internet |
| Latência | Menor (sem round-trip externo) | Maior |
| Configuração | Requer NLB + Security Group específico | Requer ELB com IP público |
| Custo | Custo do VPC Link (fixo por hora) + dados | Sem custo adicional de VPC Link |

O VPC Link é obrigatório para manter o EKS inacessível diretamente pela internet — requisito de segurança da arquitetura.

### NLB vs ALB como target do VPC Link

O API Gateway HTTP API VPC Link suporta integração com **NLB** (via ARN do listener) ou **ALB** (via ARN do Listener da AWS). O K8s Service `type: LoadBalancer` em EKS na AWS provisiona um **Classic ELB** por padrão, ou NLB com a anotação `service.beta.kubernetes.io/aws-load-balancer-type: nlb`.

O NLB foi escolhido por:
- Menor latência (Layer 4, sem HTTP parsing).
- Suporte direto como target do API Gateway VPC Link sem necessidade do AWS Load Balancer Controller adicional.
- Custo ligeiramente menor que ALB para o volume de tráfego do laboratório.

---

## Consequências

### Positivas

- Ponto de entrada único: todo tráfego externo passa pelo API Gateway, habilitando throttling, logging centralizado e autenticação na borda.
- O cluster EKS não precisa de ingress controller adicional (Nginx, Traefik) — o API Gateway desempenha esse papel externamente.
- O Security Group do VPC Link limita o tráfego de entrada nos nodes EKS às portas NodePort (30000–32767), reduzindo a superfície de ataque.
- Logging de acesso estruturado (JSON) habilitado no stage `$default` do API Gateway via CloudWatch.

### Negativas / Riscos

- O NLB é provisionado pelo Kubernetes (não pelo Terraform diretamente) — dependência de timing: o `terraform apply` do módulo `apigateway` precisa do NLB já existente para ler o ARN do listener via `data "aws_lb_listener"`.
- O VPC Link tem custo fixo por hora (~$0,035/hora), mesmo com tráfego zero.
- Mudanças nas rotas do API Gateway exigem novo `terraform apply` — não há hot-reload de configuração.
