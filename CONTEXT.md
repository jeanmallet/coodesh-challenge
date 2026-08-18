# Order Exposure Control

Duas aplicações que trocam ordens via FIX 4.4: um gerador de ordens com formulário
web e um acumulador que controla a exposição financeira por símbolo contra um limite.

## Language

**OrderGenerator**:
Aplicação que apresenta o formulário web, monta a ordem e a envia pela sessão FIX
como *initiator*. Valida o formato antes de enviar (fail-fast), mas não é a
autoridade sobre as regras.
_Avoid_: cliente, front, emissor

**OrderAccumulator**:
Aplicação *acceptor* que recebe as ordens, é a **autoridade** que revalida todas as
regras (formato e negócio) e mantém a exposição por símbolo. Correto independente de
quem o chamou.
_Avoid_: servidor, backend, matching engine

**Order** (NewOrderSingle):
Uma intenção de negociar um símbolo, com lado, quantidade e preço. Trafega como a
mensagem FIX `NewOrderSingle` (35=D).
_Avoid_: ordem de compra/venda genérica, trade, requisição

**ExecutionReport**:
A resposta do OrderAccumulator a uma Order (35=8). É sempre única por Order e informa
se foi Aceita ou Rejeitada, mais o motivo em caso de rejeição.
_Avoid_: resposta, ack, confirmação

**Symbol**:
O papel negociado, restrito a {PETR4, VALE3, VIIA4}. Unidade de agregação da exposição
e do limite.
_Avoid_: ticker, ativo, instrumento

**Side**:
A direção da Order: **Compra** (aumenta exposição) ou **Venda** (diminui exposição).
Mapeia para FIX Side 1=Buy / 2=Sell.
_Avoid_: direção, operação

**Executed Quantity** (quantidade executada):
Na aceitação, é a `OrderQty` integral da Order — uma Order aceita é tratada como
executada por completo. Base do cálculo de exposição.
_Avoid_: quantidade preenchida, fill, LastQty parcial

**Exposure** (exposição financeira):
Por símbolo: `Σ(preço × quantidade executada de Compras) − Σ(preço × quantidade
executada de Vendas)`. Pode ser líquida comprada (positiva) ou vendida (negativa).
_Avoid_: posição, saldo, risco

**Exposure Limit**:
Constante interna de R$ 100.000.000 por símbolo, aplicada sobre o **valor absoluto**
da Exposure resultante. Uma Order é rejeitada se a Exposure resultante ultrapassar
(estritamente `>`) o limite; exatamente no limite é aceita.
_Avoid_: teto, cap, margem

**Accept**:
Resultado em que a Order respeita todas as regras e o limite. Responde
`ExecutionReport` com `ExecType=New` e **entra** no cálculo da Exposure.
_Avoid_: aprovar, executar, preencher

**Reject**:
Resultado em que a Order fere uma regra de validação ou ultrapassaria o limite.
Responde `ExecutionReport` com `ExecType=Rejected`, carrega o motivo em `Text` (58) e
**não** entra no cálculo da Exposure.
_Avoid_: recusar, cancelar, negar
