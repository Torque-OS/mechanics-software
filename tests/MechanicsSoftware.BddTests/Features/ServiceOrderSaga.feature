# language: pt-BR
Funcionalidade: Coordenação Transacional de Ordem de Serviço via Saga Orquestrada
  Como orquestrador da plataforma Torque-OS (OS Service)
  Eu quero coordenar o ciclo de vida da Ordem de Serviço através dos eventos e comandos da Saga
  Para garantir consistência eventual no fluxo feliz e rollback com compensação em caso de falha

  Cenário: Fluxo feliz completo de uma Ordem de Serviço passando por todos os microsserviços
    Dado que uma nova Ordem de Serviço foi aberta e iniciou o diagnóstico com um serviço no valor de 15000 centavos
    Quando o Billing Service publica o evento BudgetGenerated no valor de 15000 centavos
    E o cliente aprova o orçamento publicando o evento BudgetApproved
    E o pagamento via Mercado Pago é confirmado publicando o evento PaymentConfirmed
    E o Execution Service finaliza o reparo publicando o evento ExecutionCompleted
    Então a Ordem de Serviço deve estar com o status "COMPLETED"
    E o orquestrador deve ter enviado o comando EnqueueExecution para o Execution Service

  Cenário: Rollback e compensação da Saga quando o pagamento falha ou expira no Mercado Pago
    Dado que uma nova Ordem de Serviço foi aberta e iniciou o diagnóstico com um serviço no valor de 25000 centavos
    Quando o Billing Service publica o evento BudgetGenerated no valor de 25000 centavos
    E o cliente aprova o orçamento publicando o evento BudgetApproved
    Mas o Billing Service publica o evento PaymentFailed por expiração do QR Code
    Então a Ordem de Serviço deve ser revertida para o status "CANCELLED"
    E o orquestrador deve ter enviado o comando CancelBudget para o Billing Service

  Cenário: Rollback e compensação da Saga quando a execução falha após o pagamento confirmado
    Dado que uma nova Ordem de Serviço foi aberta e iniciou o diagnóstico com um serviço no valor de 150000 centavos
    Quando o Billing Service publica o evento BudgetGenerated no valor de 150000 centavos
    E o cliente aprova o orçamento publicando o evento BudgetApproved
    E o pagamento via Mercado Pago é confirmado publicando o evento PaymentConfirmed
    Mas o Execution Service encontra uma falha impeditiva e publica o evento ExecutionFailed
    Então a Ordem de Serviço deve ser revertida para o status "CANCELLED"
    E o orquestrador deve ter enviado o comando CancelBudget para o Billing Service
