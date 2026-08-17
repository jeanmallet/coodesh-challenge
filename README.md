# Order Exposure Control (FIX 4.4)

Duas aplicações em C# que se comunicam via protocolo FIX 4.4 (QuickFIX/n): um
**OrderGenerator** com formulário web que envia ordens (`NewOrderSingle`) e um
**OrderAccumulator** que controla a exposição financeira por símbolo contra um limite
de R$ 100.000.000, respondendo com `ExecutionReport` (aceite ou rejeição).

> This is a challenge by [Coodesh](https://coodesh.com/)

## Tecnologias

- **.NET 10** / C#
- **QuickFIXn** (`QuickFIXn.Core` + `QuickFIXn.FIX44`) — engine do protocolo FIX 4.4
- **ASP.NET Core Minimal API** + HTML/JS estático (frontend do OrderGenerator)
- **xUnit** — testes da lógica de negócio

## Arquitetura

```
Browser ──HTTP/JSON──> OrderGenerator ──FIX 4.4 (TCP :5001)──> OrderAccumulator
 (form)                (web + initiator)   NewOrderSingle          (acceptor)
                                    <── ExecutionReport (New/Rejected) ──
```

- **OrderGenerator** (`src/OrderGenerator`): app ASP.NET Core única que serve o
  formulário e hospeda o *initiator* FIX in-process. O `POST /api/orders` monta a
  ordem, envia pela sessão FIX e aguarda o `ExecutionReport` correlacionado por
  `ClOrdID` (ponte assíncrona→síncrona via `TaskCompletionSource`, timeout de 5s).
  Valida o formato antes de enviar (fail-fast): entrada inválida responde `400` e
  **nunca** vira mensagem FIX.
- **OrderAccumulator** (`src/OrderAccumulator`): console *acceptor*. É a **autoridade**
  de validação — revalida todas as regras de campo e a de exposição. Mantém a
  exposição por símbolo em memória (`ConcurrentDictionary`), com a verificação do
  limite e a atualização atômicas sob lock por símbolo.

### Regras de negócio

- **Símbolos**: PETR4, VALE3, VIIA4.
- **Lado**: Compra (aumenta exposição) ou Venda (diminui).
- **Quantidade**: inteiro positivo, `< 100.000`.
- **Preço**: decimal positivo, múltiplo de 0.01, `< 1.000`.
- **Exposição** por símbolo: `Σ(preço × qtd de Compras) − Σ(preço × qtd de Vendas)`.
- **Limite**: R$ 100.000.000 sobre o **valor absoluto** da exposição. Uma ordem cuja
  exposição resultante **ultrapasse** (estritamente `>`) o limite é rejeitada e não
  entra no cálculo. Exatamente no limite é aceita.
- **Aceite** ⇒ `ExecutionReport` com `ExecType=New` e a ordem entra no cálculo.
  **Rejeição** ⇒ `ExecType=Rejected`, com o motivo em `Text` (tag 58), sem entrar no
  cálculo.

Decisões de modelagem não óbvias estão registradas em [`docs/adr/`](docs/adr) e o
glossário do domínio em [`CONTEXT.md`](CONTEXT.md).

## Como executar

### Via Docker Compose

Pré-requisito: Docker com Compose v2.

```bash
docker compose up --build
```

Sobe os dois serviços na ordem certa (`generator` espera o *healthcheck* do
`accumulator` antes de conectar) e expõe **`http://localhost:5080`**. A porta FIX
5001 fica só na rede interna do Compose — o formulário web é a única porta publicada
no host.

### Via `dotnet run`

Pré-requisito: [.NET 10 SDK](https://dotnet.microsoft.com/download).

Abra **dois terminais** na raiz do repositório.

1. Suba o acumulador primeiro (acceptor FIX na porta 5001):

   ```bash
   dotnet run --project src/OrderAccumulator
   ```

2. Suba o gerador (web + initiator):

   ```bash
   dotnet run --project src/OrderGenerator
   ```

3. Abra **`http://localhost:5080`** no browser, preencha o formulário e envie. A
   resposta do `ExecutionReport` aparece na própria página.

> `5080` é a **página web** (OrderGenerator, HTTP). `5001` é o **socket FIX** do
> OrderAccumulator (TCP puro) — abrir `5001` no browser trava em loading, pois não é
> um servidor web. Use sempre `5080` na UI.

> O gerador reconecta sozinho se o acumulador ainda não estiver no ar. O estado é em
> memória: reiniciar o acumulador zera a exposição.

## Testes

```bash
dotnet test
```

Cobrem a matriz de exposição/limite (incluindo a fronteira exata, exposição vendida
líquida e concorrência sob o lock por símbolo) e as quatro regras de validação.
