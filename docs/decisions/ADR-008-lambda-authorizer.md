# ADR-008: Lambda Authorizer — REQUEST Type com Simple Response

**Status:** Accepted  
**Date:** 2026-07-07  
**Autores:** Joelma Renata Oliveira

---

## Contexto

O API Gateway precisa de um mecanismo para validar tokens JWT antes de encaminhar requisições ao EKS. O API Gateway HTTP API v2 oferece três opções nativas:

1. **JWT Authorizer nativo** — valida JWT automaticamente usando um JWKS endpoint ou secret configurado diretamente no API Gateway.
2. **Lambda Authorizer (REQUEST type)** — delega a validação para uma função Lambda, que retorna uma resposta de autorização.
3. **Sem authorizer** — toda requisição passa; autenticação é responsabilidade da aplicação.

A decisão define qual mecanismo usar e, dentro da opção Lambda, qual formato de resposta adotar.

---

## Decisão

Usar **Lambda Authorizer do tipo REQUEST** com **simple response format** (`isAuthorized: bool` + `context` opcional).

A função Lambda está implementada em `src/authorizer.js` no repositório `mechanics-lambda`.

```js
export const handler = async (event) => {
  const token = bearerToken(event);
  if (!token) return { isAuthorized: false };

  try {
    const payload = jwt.verify(token, secret, {
      algorithms: ['HS256'],
      issuer,
      audience,
    });
    return {
      isAuthorized: true,
      context: {
        sub:  payload.sub  ?? '',
        role: payload.role ?? '',
        cpf:  payload.cpf  ?? '',
      },
    };
  } catch {
    return { isAuthorized: false };
  }
};
```

O campo `context` retornado pelo authorizer é repassado pela API Gateway como headers para o backend (EKS), permitindo que a aplicação leia `cpf` e `role` sem re-validar o token.

### Cache do Authorizer

```hcl
resource "aws_apigatewayv2_authorizer" "jwt" {
  authorizer_type                  = "REQUEST"
  identity_sources                 = ["$request.header.Authorization"]
  authorizer_result_ttl_in_seconds = var.authorizer_cache_ttl_seconds  # padrão: 300
  enable_simple_responses          = true
}
```

O cache é keyed pelo header `Authorization`. Com TTL de 300 segundos, invocações repetidas do mesmo token não acionam a Lambda — reduz latência e custo.

---

## Alternativas Consideradas

### JWT Authorizer Nativo do API Gateway

O API Gateway HTTP API suporta validação de JWT nativamente usando:
- **JWKS endpoint** (para RS256/RS384 com chave pública)
- **Secret compartilhado** (para HS256)

**Vantagens:** zero código; sem cold start; sem custo de invocação Lambda.  
**Desvantagens:**
- Não permite lógica customizada: não é possível verificar se o cliente está ativo no banco, nem enriquecer o contexto com `cpf` ou `role` a partir do payload do token.
- O authorizer nativo valida claims padrão (`iss`, `aud`, `exp`) mas não claims customizados como `cpf`.

Para o sistema atual, a validação de claims padrão seria suficiente — o `cpf` já está no payload do token e poderia ser lido pela aplicação. Porém, a Lambda Authorizer foi escolhida para centralizar a lógica de autorização e permitir extensões futuras (ex.: blacklist de tokens, verificação de roles no banco).

### Lambda Authorizer com IAM Policy (formato legado)

Em vez de `{ isAuthorized: bool }`, retornar uma policy IAM completa:

```json
{
  "principalId": "user-id",
  "policyDocument": {
    "Version": "2012-10-17",
    "Statement": [{
      "Action": "execute-api:Invoke",
      "Effect": "Allow",
      "Resource": "arn:aws:execute-api:..."
    }]
  }
}
```

**Vantagens:** permite controle granular por recurso/método (ex.: apenas `GET /api/customers` para role `customer`).  
**Desvantagens:**
- ARN do recurso muda por rota, dificultando o cache (o cache é keyed por token + ARN).
- Mais verboso sem benefício real para o perfil de autorização atual (autenticado = acesso total).
- `enable_simple_responses = true` é explicitamente recomendado pela AWS para HTTP API quando não há necessidade de controle por recurso.

### Sem Authorizer (autenticação apenas na aplicação)

Manter a validação JWT apenas no middleware ASP.NET Core (`AddJwtBearer`).

**Vantagens:** sem dependência de Lambda; sem latência extra; sem cold start.  
**Desvantagens:**
- Requisições inválidas chegam até o pod, consumindo recursos de rede e processamento do cluster.
- Sem ponto centralizado de logging de requisições não autorizadas.
- Não atende o requisito de autenticação na borda (API Gateway).

---

## Consequências

### Positivas

- Tokens inválidos ou ausentes são bloqueados no API Gateway — o cluster EKS só recebe tráfego autenticado.
- O `context` retornado (`cpf`, `role`) elimina a necessidade de re-decodificar o JWT na aplicação.
- Cache de 300 s reduz invocações Lambda em ~90% para sessões ativas com tokens de longa duração.
- `enable_simple_responses = true` simplifica o código da Lambda e elimina a necessidade de construir IAM policies dinâmicas.

### Negativas / Riscos

- **Cold start:** a primeira invocação após período idle tem latência adicional (~100–300 ms para Node.js 20). Mitigado pelo cache do authorizer.
- **Dois Lambdas no mesmo repositório:** `handler.js` (CPF auth) e `authorizer.js` (JWT validation) são deployados juntos — um bug em um pode bloquear o deploy do outro. Separação em repositórios distintos seria mais resiliente, mas adiciona overhead de gestão.
- **Rotação de `JWT_SECRET`:** se o secret mudar, todos os tokens existentes ficam inválidos imediatamente. Não há suporte a rotação gradual (múltiplos secrets simultâneos) na implementação atual.
- **Ausência de revogação de tokens:** JWTs são stateless — não há blacklist. Um token comprometido permanece válido até expirar. Mitigação: `JWT_EXPIRATION` configurável via variável de ambiente (padrão: 3600 s).
