# PRD — Controle de Exposição Financeira por Ordem

## Contexto

Duas aplicações que, juntas, permitem enviar ordens de negociação e controlar o
risco financeiro assumido por símbolo, rejeitando automaticamente qualquer ordem
que ultrapasse um limite de exposição pré-estabelecido.

## Aplicações

### OrderGenerator

Aplicação com um formulário web onde o usuário cria uma nova ordem, informando:

- **Símbolo**: um entre PETR4, VALE3 ou VIIA4.
- **Lado**: Compra ou Venda.
- **Quantidade**: valor inteiro positivo, menor que 100.000.
- **Preço**: valor decimal positivo, múltiplo de 0,01, menor que 1.000.

Ao submeter o formulário, a ordem é enviada para processamento e a resposta da
requisição é apresentada ao usuário na própria página.

### OrderAccumulator

Aplicação que recebe as ordens enviadas pelo OrderGenerator e calcula, para cada
símbolo, a exposição financeira resultante:

> Exposição financeira = Σ(preço × quantidade executada) das ordens de Compra
> − Σ(preço × quantidade executada) das ordens de Venda

Ou seja, ordens de Compra aumentam a exposição do símbolo e ordens de Venda a
diminuem.

## Regra de negócio: limite de exposição

O OrderAccumulator mantém um limite interno constante de **R$ 100.000.000
(cem milhões)** de exposição por símbolo.

Qualquer ordem cuja aceitação faça a exposição resultante (em valor absoluto)
ultrapassar esse limite deve ser **rejeitada**.

## Comportamento esperado

- **Ordem aceita**: o OrderAccumulator responde com uma confirmação de execução
  informando que a ordem foi aceita, e a ordem passa a ser considerada no
  cálculo da exposição do símbolo.
- **Ordem rejeitada**: o OrderAccumulator responde com uma confirmação
  informando que a ordem foi rejeitada, e a ordem **não** é considerada no
  cálculo da exposição do símbolo.
