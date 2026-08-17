# Production Readiness — oportunidades por projeto

O codebase resolve o [PRD](PRD.md) de forma deliberadamente simples e isso foi uma
premissa, não um descuido: a lógica de negócio está isolada, testada e documentada em
[ADRs](adr). Este documento não revisita essas decisões — levanta o que separa "resolve
o desafio" de "eu deixaria isso rodando".

Cada achado traz **Hoje / Risco / Proposta / Esforço** (P = poucas horas, M = um dia,
G = mais que isso). O que foi conscientemente deixado de fora está no fim, com o motivo.

---

## OrderGenerator (`src/OrderGenerator`)

### 1. Nenhum teste — ✅ resolvido

**Hoje**: `tests/` tem um único projeto, o do Accumulator. `OrderValidation` não é
exercitado por nada.

**Risco**: o [ADR-0002](adr/0002-accumulator-authoritative-no-shared-validation.md)
duplica as regras de propósito, e essa decisão está certa. Mas ela só se sustenta se as
duas cópias forem verificadas de forma independente — senão a duplicação deixa de ser
"independência deliberada" e vira "uma das cópias ninguém conferiu". As duas já não são
simétricas: `OrderRules` valida explicitamente que `Quantity` é inteira (`decimal.Truncate`),
enquanto `OrderValidation` recebe `int` e delega isso ao binder JSON — que aceita `100.0`
e rejeita `100.5` com uma mensagem que não é nenhuma das duas. É uma assimetria hoje
benigna, e ninguém saberia se deixasse de ser.

**Proposta**: `tests/OrderGenerator.Tests` espelhando `OrderRulesTests.cs` — mesma matriz
de fronteiras (0, negativo, 99.999, 100.000, 999,99, 1.000, 2+ casas decimais), mais
`SideToFix`. Idealmente uma tabela de casos compartilhada por link de arquivo (`<Compile
Include="...">`) para que uma divergência futura entre as duas validações apareça como
teste vermelho, sem criar a lib compartilhada que o ADR-0002 rejeita.

**Esforço**: P. **É o item de maior retorno do projeto inteiro.** Feito em
[`tests/OrderGenerator.Tests`](../tests/OrderGenerator.Tests), 20 testes cobrindo a mesma
matriz de fronteiras mais `SideToFix`.

### 2. `SessionNotFound` escapa como 500 — ✅ resolvido

**Hoje**: [`Program.cs:36`](../src/OrderGenerator/Program.cs) trata `InvalidOperationException`
(sessão indisponível → 503) e `TimeoutException` (→ 504). Mas
[`GeneratorApp.cs:50`](../src/OrderGenerator/GeneratorApp.cs) chama
`Session.SendToTarget`, que lança `QuickFix.SessionNotFound` — que não deriva de nenhuma
das duas.

**Risco**: entre a leitura de `_sessionId` (linha 33) e o envio (linha 50) existe uma
janela real: se o Accumulator cair nesse intervalo, o usuário recebe 500 com stack trace
em vez do 503 que o código claramente pretendia dar. O Accumulator já trata
`SessionNotFound` explicitamente ([`AccumulatorApp.cs:64`](../src/OrderAccumulator/AccumulatorApp.cs));
o Generator não.

**Proposta**: capturar `SessionNotFound` em `SendOrderAsync` e reemitir como a mesma
`InvalidOperationException` já usada para sessão ausente — mantendo o mapeamento HTTP num
lugar só.

**Esforço**: P. Feito em [`GeneratorApp.cs`](../src/OrderGenerator/GeneratorApp.cs):
`catch (SessionNotFound ex)` antes do `catch (TimeoutException)`, relançando como
`InvalidOperationException` — o `catch` já existente em `Program.cs:36` passa a cobrir o
caso. Sem teste dedicado: reproduzir a janela exige derrubar a sessão FIX entre o check e
o envio, o que pede mockar internals do QuickFIX por um custo desproporcional ao
catch-and-rethrow de uma linha.

### 3. Initiator FIX fora do host

**Hoje**: `SocketInitiator` é instanciado solto no topo de `Program.cs`, com `Start()`
imperativo, `Stop()` registrado num callback de `ApplicationStopping`, e o objeto nunca
descartado. `GeneratorApp` também é `new`-ado à mão e não está no contêiner.

**Risco**: o ciclo de vida do componente mais crítico do app não é o ciclo de vida do app.
Falha no `Start()` (porta ocupada, `.cfg` inválido) sobe como exceção não tratada antes de
`app.Run()`, sem log estruturado. Nada é injetável, então nada é substituível em teste.

**Proposta**: `GeneratorApp` como singleton no DI, initiator dentro de um
`IHostedService`/`BackgroundService` com `StartAsync`/`StopAsync`/`Dispose`. O endpoint passa
a receber `GeneratorApp` por injeção em vez de capturar a variável local.

**Esforço**: M.

### 4. Configuração hardcoded — ✅ resolvido

**Hoje**: `builder.WebHost.UseUrls("http://localhost:5080")` no código — que ainda por cima
sobrepõe o `applicationUrl` do `launchSettings.json`, duplicando a mesma informação em dois
lugares com um vencedor não óbvio. `TimeSpan.FromSeconds(5)` literal no endpoint. Host e
porta FIX apenas dentro de `generator.cfg`.

**Risco**: nada disso é ajustável sem recompilar, e `SocketConnectHost=127.0.0.1` torna o
app impossível de containerizar como está — em compose o alvo é o nome do serviço.

**Proposta**: seção tipada em `appsettings.json` (URL da web, timeout, host/porta FIX) com
binding via `IOptions<>`; remover o `UseUrls` do código; sobrescrever o host FIX no
`SessionSettings` a partir da configuração, para que uma env var resolva o caso do
container. **Habilita o item de infra.**

**Esforço**: M. Feito, mais enxuto do que a proposta original (sem `IOptions<>`, que seria
ceremônia para um script top-level lido uma vez): removido o `UseUrls` — a porta passa a
vir da chave nativa `Urls` do ASP.NET Core (`appsettings.json` como default, com
`launchSettings.json`/`ASPNETCORE_URLS` sobrescrevendo em dev, sem vencedor oculto). O
timeout do endpoint vem de `OrderGenerator:OrderTimeoutSeconds`. O host/porta FIX podem
ser sobrescritos via `OrderGenerator:Fix:SocketConnectHost`/`:SocketConnectPort`
(`OrderGenerator__Fix__SocketConnectHost` como env var), aplicados sobre o
`SessionSettings` carregado do `.cfg` via `SettingsDictionary.SetString`. Verificado em
runtime: subindo o `.dll` publicado direto (sem `launchSettings`), a porta 5080 vem só do
`appsettings.json`; com `launchSettings`, o `ASPNETCORE_URLS` de lá continua prevalecendo
como antes.

### 5. Erro sem contrato

**Hoje**: erros saem como `{ "error": "..." }` ad-hoc, em três formatos de resposta
diferentes (`BadRequest`, `Json` com 503, `Json` com 504). Não há exception handler global.

**Risco**: qualquer exceção fora das duas capturadas vira a página de erro padrão do
ASP.NET — HTML em um endpoint JSON, e stack trace em Development. Um cliente que não seja
esta página não tem contrato de erro estável para programar contra.

**Proposta**: `AddProblemDetails()` + `Results.Problem`/`Results.ValidationProblem`
(RFC 7807), mais `UseExceptionHandler` como rede. O `index.html` passa a ler `detail`/`title`.

**Esforço**: P.

### 6. `Directory.SetCurrentDirectory` muda estado global

**Hoje**: [`Program.cs:12`](../src/OrderGenerator/Program.cs) altera o diretório de trabalho
do processo inteiro só para que o caminho relativo `DataDictionary=FIX44.xml` do `.cfg`
resolva.

**Risco**: efeito colateral global e silencioso para resolver um caminho local. Qualquer
código futuro que use caminho relativo (log, upload, arquivo temporário) herda a mudança
sem saber. É especialmente arriscado num app web, onde `ContentRoot` e `WebRoot` já foram
resolvidos antes desta linha — a ordem das duas coisas passa a importar e nada documenta isso.

**Proposta**: carregar o `.cfg` e reescrever `DataDictionary` para o caminho absoluto sob
`AppContext.BaseDirectory` no próprio `SessionSettings`, sem tocar no cwd.

**Esforço**: P.

### 7. Nulabilidade só no papel

**Hoje**: `NewOrderRequest(string Symbol, string Side, int Quantity, decimal Price)` com
NRT habilitado, mas o binder JSON entrega `null` para campo ausente sem reclamar.

**Risco**: hoje a validação sobrevive por acidente — `HashSet<string>.Contains(null)`
devolve `false` em vez de lançar, então um corpo `{}` cai no 400 "correto" pelo motivo
errado. É uma garantia que o compilador acha que deu e não deu; qualquer refatoração da
validação (um `.Trim()`, um `.ToUpperInvariant()`) transforma isso em `NullReferenceException`
com 500.

**Proposta**: `required` nos membros do record (ou validação explícita de nulo antes das
regras), tornando o contrato do binder explícito.

**Esforço**: P.

### 8. Sem health check

**Hoje**: nada expõe se a sessão FIX está logada. Descobrir exige mandar uma ordem e ver
o 503.

**Risco**: sem sonda, orquestrador nenhum sabe distinguir "processo de pé" de "processo
inútil porque não tem sessão". É também o pré-requisito do `depends_on: service_healthy`
no compose.

**Proposta**: `AddHealthChecks()` com um check derivado de `_sessionId` do `GeneratorApp`,
exposto em `/health`.

**Esforço**: P.

### 9. Frontend — `innerHTML` com dados de resposta

**Hoje**: [`index.html:93`](../src/OrderGenerator/wwwroot/index.html) monta o resultado por
concatenação de string, injetando `data.text`, `data.symbol` e os demais campos sem escape.

**Risco**: a origem hoje é confiável (o próprio Accumulator), então isto não é uma
vulnerabilidade explorável no estado atual — é um padrão que não deveria estar num
codebase que se quer profissional, porque a confiança na origem é uma premissa que muda
sem aviso. `data.text` é texto livre vindo pela tag 58 de outro processo.

**Proposta**: construir os nós com `document.createElement` + `textContent` na função `row`.
Diff pequeno, remove a classe inteira de problema.

**Esforço**: P.

### 10. Frontend — duplo submit

**Hoje**: o botão não é desabilitado durante o envio.

**Risco**: dois cliques rápidos = duas ordens enviadas, ambas contabilizadas na exposição.
Num formulário que registra risco financeiro isso não é cosmético — é a diferença entre a
exposição do sistema e a intenção do usuário.

**Proposta**: `button.disabled = true` no início do handler, restaurado no `finally`.

**Esforço**: P.

### 11. Sem correlação em log

**Hoje**: o `ClOrdID` é gerado em `SendOrderAsync` e nunca aparece em log do lado do
Generator; o endpoint não loga nada.

**Risco**: com o Accumulator logando por `ClOrdID` e o Generator não logando nada,
correlacionar uma requisição HTTP com a mensagem FIX correspondente é impossível.

**Proposta**: `logger.BeginScope` com o `ClOrdID` cobrindo o envio e a resposta, e log de
entrada/saída do endpoint.

**Esforço**: P.

---

## OrderAccumulator (`src/OrderAccumulator`)

### 1. Sem Generic Host — e sem shutdown limpo em `SIGTERM` — ✅ resolvido

**Hoje**: [`Program.cs`](../src/OrderAccumulator/Program.cs) monta tudo à mão:
`LoggerFactory.Create`, `new AccumulatorApp(...)`, `ThreadedSocketAcceptor` direto, e um
`ManualResetEventSlim` liberado por `Console.CancelKeyPress`.

**Risco**: três consequências concretas, não estéticas.
1. `CancelKeyPress` só cobre Ctrl+C. Sob `docker stop` (`SIGTERM`), o processo termina sem
   passar por `acceptor.Stop()` — **sem logout FIX limpo**, deixando a contraparte
   descobrir a queda por heartbeat. Com compose no escopo, isto deixa de ser hipotético.
2. `ThreadedSocketAcceptor` é `IDisposable` e nunca é descartado.
3. Não há configuração nem DI: nada é ajustável sem recompilar e `AccumulatorApp` não é
   substituível em teste.

**Proposta**: `Host.CreateApplicationBuilder` + um `IHostedService` que encapsula o acceptor.
O host trata `SIGTERM` e `SIGINT` de fábrica, dá `IHostApplicationLifetime`, logging e
configuração sem código extra, e o `Program.cs` encolhe.

**Esforço**: M. **Maior ganho estrutural deste projeto e pré-requisito do compose.** Feito:
`Program.cs` migrado para `Host.CreateApplicationBuilder`; o acceptor foi encapsulado em
[`AccumulatorHostedService`](../src/OrderAccumulator/AccumulatorHostedService.cs)
(`IHostedService` + `IDisposable`), registrado via DI junto com `AccumulatorApp`. O host
trata `SIGTERM`/`SIGINT` de fábrica — verificado subindo o processo e enviando `SIGTERM`:
`StopAsync` roda, o acceptor faz logout e o processo termina sem travar.

### 2. Exceção em `OnMessage` sobe para a engine — ✅ resolvido

**Hoje**: `FromApp` → `Crack` → `OnMessage` sem nenhum try/catch. Só o envio do
`ExecutionReport` é protegido.

**Risco**: qualquer falha inesperada no caminho de decisão (um campo com valor absurdo, uma
mudança futura em `ExposureBook`) escapa para o QuickFIX, que derruba a sessão. Uma ordem
malformada deixa de ser "uma ordem rejeitada" e passa a ser "a sessão caiu" — e o Generator
só descobre no timeout de 5s.

**Proposta**: envolver o corpo de `OnMessage`; em exceção não prevista, logar e responder
`Rejected` com texto genérico. Falha de uma ordem nunca deve custar a sessão.

**Esforço**: P. Feito em [`AccumulatorApp.cs`](../src/OrderAccumulator/AccumulatorApp.cs):
o corpo de `OnMessage` está em try/catch, e qualquer exceção não prevista loga e responde
`Rejected` (texto genérico) em vez de propagar. Sem teste dedicado: `OnMessage` está
acoplado ao `MessageCracker`/`Session` do QuickFIX, a mesma limitação de testabilidade do
item 6 abaixo — extrair a lógica de decisão resolveria os dois de uma vez.

### 3. Rejeição sem `OrdRejReason` (tag 103)

**Hoje**: o motivo da rejeição vai só em `Text` (58), como texto livre em português.

**Risco**: texto livre não é roteável. Um cliente FIX real precisa distinguir
programaticamente "símbolo inválido" de "limite de exposição excedido" para decidir se
retenta, se alerta ou se para de operar — e não vai fazer isso por `string.Contains`.

**Proposta**: preencher `OrdRejReason` junto com o `Text`; mapear as rejeições de campo
para `UNKNOWN_SYMBOL`/`INCORRECT_QUANTITY` e a de limite para `ORDER_EXCEEDS_LIMIT` (103=3),
que é exatamente o caso desta regra de negócio. `OrderRules.Validate` passa a devolver um
resultado com motivo + código em vez de `string?`.

**Esforço**: P.

### 4. Exposição acumulada não é observável

**Hoje**: `ExposureBook.Current` existe e é público, mas nada fora dos testes o chama. O
único traço do estado é o texto do `ExecutionReport` aceito.

**Risco**: não há como conferir a exposição corrente por símbolo sem reler o stdout e somar
à mão. Para o componente cujo *único propósito* é conhecer a exposição, isso é uma lacuna
de operabilidade.

**Proposta**: um dump periódico em log (baixo custo) ou um endpoint HTTP mínimo de leitura
no processo do acceptor (mais útil, mas adiciona superfície).

**Esforço**: P a M. **Opcional — vale discutir se o valor justifica a superfície.**

### 5. `Directory.SetCurrentDirectory` muda estado global

Mesmo caso do item 6 do Generator, mesma proposta. Menos arriscado aqui (console app, sem
`ContentRoot`), mas é a mesma solução nos dois lados.

**Esforço**: P.

### 6. `BuildFIXMessage` não é testável

**Hoje**: é um método privado dentro de `AccumulatorApp`, acoplado ao `MessageCracker`.

**Risco**: os fallbacks documentados no próprio código (`"UNKNOWN"` para símbolo vazio,
`SideBuy` para lado inválido) existem justamente para o caminho de rejeição por formato —
o mais difícil de acertar — e nenhum teste os exercita. `ExtractFields` está na mesma
situação.

**Proposta**: extrair as duas para um estático puro (ou uma classe própria) recebendo e
devolvendo tipos FIX, e testar diretamente. Não exige subir sessão.

**Esforço**: P.

### 7. Nota — lock por símbolo num domínio de 3 símbolos

`ExposureBook` mantém um `ConcurrentDictionary<string, object>` de locks, que cresce por
chave. Como `Evaluate` só é chamado após `OrderRules.Validate`, na prática o dicionário
nunca passa de três entradas — não há vazamento real. Com três símbolos, um lock único
sobre o livro daria a mesma garantia com menos peças móveis.

Está escrito, correto e coberto por teste de concorrência. Registrado como simplificação
possível, **não como ação** — mexer aqui é risco sem retorno.

---

## Solução / Infraestrutura

### 1. `docker-compose` — a maior melhoria operacional

**Hoje**: o [README](../README.md) instrui abrir dois terminais em ordem específica, e ainda
precisa de um aviso em destaque para o usuário não confundir a porta web (5080) com o
socket FIX (5001).

**Risco**: menos "risco" e mais custo de avaliação — quem for rodar o projeto tem duas
chances de errar antes de ver a tela.

**Proposta**: dois `Dockerfile` multi-stage + um `compose.yaml` com um `docker compose up`
só. `depends_on` com `condition: service_healthy` (usa o health check do Generator, item 8
acima) garante a ordem que o README hoje pede em prosa; `SocketConnectHost` do Generator
vem de variável de ambiente apontando para o serviço do Accumulator (depende do item 4 do
Generator).

**Ordem de execução**: item 4 e 8 do Generator e item 1 do Accumulator vêm antes — sem
eles o compose funciona mal (sem sonda e sem shutdown limpo).

**Esforço**: M.

### 2. `Directory.Build.props`

**Hoje**: os três `.csproj` repetem `TargetFramework`, `Nullable` e `ImplicitUsings`. Nenhum
analisador está ligado e warnings não quebram build.

**Proposta**: centralizar as propriedades comuns e ligar `TreatWarningsAsErrors`,
`EnforceCodeStyleInBuild` e `AnalysisMode=Recommended`. Esperar um pequeno lote de warnings
na primeira execução — inclusive alguns de nulabilidade, o que é exatamente o ponto.

**Esforço**: P (mais o tempo de limpar o que aparecer).

### 3. `Directory.Packages.props` (Central Package Management)

**Hoje**: `QuickFIXn.Core` e `QuickFIXn.FIX44` em `1.14.1` declarados em dois `.csproj`
separados.

**Risco**: divergência silenciosa de versão entre os dois lados de um protocolo — o tipo de
problema que aparece em runtime, não em build.

**Proposta**: `<ManagePackageVersionsCentrally>` com todas as versões num arquivo.

**Esforço**: P.

### 4. `global.json`

**Hoje**: nada fixa a versão do SDK. O projeto usa .NET 10, mas qualquer SDK instalado
compila.

**Proposta**: `global.json` com `rollForward: latestFeature`. Também é o que garante
build reproduzível dentro do container.

**Esforço**: P.

### 5. `.editorconfig`

**Hoje**: não existe. O estilo do código é consistente, mas por disciplina, não por
ferramenta.

**Risco**: sem ele `dotnet format` não tem regra e o `EnforceCodeStyleInBuild` do item 2 não
tem o que aplicar.

**Proposta**: `.editorconfig` de .NET registrando o estilo já praticado (expression-bodied
members, `var`, `namespace` file-scoped, ordenação de usings).

**Esforço**: P.

### 6. README

Atualizar a seção "Como executar" com o caminho do compose depois que ele existir,
mantendo o `dotnet run` como alternativa. O aviso das duas portas passa a ser nota de
rodapé em vez de destaque.

**Esforço**: P.

---

## Fora de escopo consciente

Itens que um sistema de controle de risco **de verdade** exigiria, mas que estão além do
que o PRD descreve. Registrados aqui para que a ausência seja lida como decisão, não como
esquecimento.

- **Persistência de exposição e de sequence numbers FIX.** Ambos os processos usam
  `MemoryStoreFactory`. Reiniciar o Accumulator zera o controle de risco, e sem seqnum
  persistido o resend/recovery do FIX não funciona entre execuções. Em produção,
  `FileStoreFactory` (ou banco) seria obrigatório. O PRD trata o estado como em memória e o
  README já documenta o comportamento.
- **Idempotência por `ClOrdID`.** Não há deduplicação: um resend FIX após reconexão seria
  contabilizado duas vezes na exposição. É consequência direta do item acima — resolver
  seqnum sem resolver idempotência resolve metade do problema.
- **Log de mensagens FIX** (`FileLogFactory`). Trilha de auditoria de mensagens cruas é
  requisito de compliance em ambiente real; hoje só existe o log de aplicação.
- **Autenticação de sessão.** `ToAdmin`/`FromAdmin` estão vazios nos dois lados: nenhuma
  validação de credenciais no Logon (tags 553/554) e nenhuma whitelist de `CompID`. Qualquer
  processo que alcance a porta 5001 pode abrir sessão.
- **Rate limiting** em `POST /api/orders` e **métricas/OpenTelemetry** nos dois processos.
- **CI.** Excluído por decisão explícita nesta rodada — um workflow de `build` + `test` é o
  passo natural seguinte ao compose.

---

## Priorização sugerida

| # | Item | Projeto | Esforço | Por quê primeiro |
|---|------|---------|---------|------------------|
| 1 | Testes do Generator | Generator | P | ✅ feito — sustenta o ADR-0002 |
| 2 | `SessionNotFound` → 503 | Generator | P | ✅ feito — defeito concreto, correção de uma linha |
| 3 | try/catch em `OnMessage` | Accumulator | P | ✅ feito — uma ordem ruim não pode custar a sessão |
| 4 | Generic Host + `IHostedService` | Accumulator | M | ✅ feito — shutdown em `SIGTERM`; base do compose |
| 5 | Config externalizada | Generator | M | ✅ feito — destrava o container |
| 6 | Health check | Generator | P | Destrava `depends_on: service_healthy` |
| 7 | `docker-compose` | Infra | M | Maior ganho de operabilidade |
| 8 | `innerHTML` + duplo submit | Generator | P | Baratos e visíveis na avaliação |
| 9 | `ProblemDetails` | Generator | P | Contrato de erro estável |
| 10 | `OrdRejReason` (103) | Accumulator | P | Correção de protocolo FIX |
| 11 | Higiene de build (2–5 da Infra) | Infra | P | Melhor em lote, depois do resto |

Os itens 1–3 são independentes e podem ir juntos. Os itens 4–7 formam uma corrente: o
compose depende dos três anteriores.
