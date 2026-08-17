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
vir da chave nativa `Urls` do ASP.NET Core, hoje `http://0.0.0.0:5080` no
`appsettings.json`. O timeout do endpoint vem de `OrderGenerator:OrderTimeoutSeconds`. O
host/porta FIX podem ser sobrescritos via `OrderGenerator:Fix:SocketConnectHost`/`:SocketConnectPort`
(`OrderGenerator__Fix__SocketConnectHost` como env var), aplicados sobre o
`SessionSettings` carregado do `.cfg` via `SettingsDictionary.SetString`.

Correção sobre a verificação original: testei o mapeamento de precedência assumindo o
padrão documentado do ASP.NET Core (`ASPNETCORE_URLS`/env var vence `appsettings.json`) e
estava errado — validado no item 7 (docker-compose) abaixo, `Urls` do `appsettings.json`
prevalece mesmo sobre `ASPNETCORE_URLS`. Na prática isso significa que o
`applicationUrl` do `launchSettings.json` não controla mais o bind do Kestrel, só o alvo
que o `dotnet run` abre no browser — inofensivo aqui porque `0.0.0.0` também aceita
`localhost`, mas documentado para não confundir quem olhar os dois arquivos depois.

### 5. Erro sem contrato — ✅ resolvido

**Hoje**: erros saem como `{ "error": "..." }` ad-hoc, em três formatos de resposta
diferentes (`BadRequest`, `Json` com 503, `Json` com 504). Não há exception handler global.

**Risco**: qualquer exceção fora das duas capturadas vira a página de erro padrão do
ASP.NET — HTML em um endpoint JSON, e stack trace em Development. Um cliente que não seja
esta página não tem contrato de erro estável para programar contra.

**Proposta**: `AddProblemDetails()` + `Results.Problem`/`Results.ValidationProblem`
(RFC 7807), mais `UseExceptionHandler` como rede. O `index.html` passa a ler `detail`/`title`.

**Esforço**: P. Feito: `builder.Services.AddProblemDetails()` + `app.UseExceptionHandler()`
como rede para exceção não tratada; os três retornos do endpoint (`400`, `503`, `504`)
viraram `Results.Problem(detail:, statusCode:, title:)`. `Results.ValidationProblem` não
se aplicou — ele espera um dicionário de erros por campo, e `OrderValidation.Validate`
devolve um motivo único em texto livre; `Results.Problem` já é RFC 7807 sem forçar essa
forma. `index.html` passa a ler `title`/`detail` em vez de `error`. Verificado em runtime:
os três status devolvem o corpo `{type, title, status, detail, traceId}`, e no browser
real (sem Accumulator de pé) a página mostra "Sessao FIX indisponivel: Sessao FIX
indisponivel. O OrderAccumulator esta rodando?" corretamente montado a partir de
`title`+`detail`.

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

### 8. Sem health check — ✅ resolvido

**Hoje**: nada expõe se a sessão FIX está logada. Descobrir exige mandar uma ordem e ver
o 503.

**Risco**: sem sonda, orquestrador nenhum sabe distinguir "processo de pé" de "processo
inútil porque não tem sessão". É também o pré-requisito do `depends_on: service_healthy`
no compose.

**Proposta**: `AddHealthChecks()` com um check derivado de `_sessionId` do `GeneratorApp`,
exposto em `/health`.

**Esforço**: P. Feito: `GeneratorApp.IsSessionActive` expõe `_sessionId is not null`;
[`FixSessionHealthCheck`](../src/OrderGenerator/FixSessionHealthCheck.cs) implementa
`IHealthCheck` sobre ela e é mapeado em `/health`. Para isso `GeneratorApp` passou a ser
resolvido via DI (`AddSingleton`) em vez de `new`-ado à mão — um primeiro passo do item 3
acima, mas só o necessário para o health check resolver a dependência; o initiator FIX
continua com start/stop manual. Verificado em runtime: `/health` responde 503 antes do
logon e 200 assim que a sessão com o Accumulator abre.

### 9. Frontend — `innerHTML` com dados de resposta — ✅ resolvido

**Hoje**: [`index.html:93`](../src/OrderGenerator/wwwroot/index.html) monta o resultado por
concatenação de string, injetando `data.text`, `data.symbol` e os demais campos sem escape.

**Risco**: a origem hoje é confiável (o próprio Accumulator), então isto não é uma
vulnerabilidade explorável no estado atual — é um padrão que não deveria estar num
codebase que se quer profissional, porque a confiança na origem é uma premissa que muda
sem aviso. `data.text` é texto livre vindo pela tag 58 de outro processo.

**Proposta**: construir os nós com `document.createElement` + `textContent` na função `row`.
Diff pequeno, remove a classe inteira de problema.

**Esforço**: P. Feito em [`index.html`](../src/OrderGenerator/wwwroot/index.html):
`renderError`/`renderResult` montam os nós via `document.createElement` + `textContent`
(helper `el()`), sem nenhum `innerHTML` restante. Verificado no browser real: chamando as
duas funções diretamente com payload malicioso (`<img src=x onerror=alert(1)>` e
`<script>alert(2)</script>`), o HTML resultante mostra os valores escapados
(`&lt;img...&gt;`) e nenhum `alert` dispara.

### 10. Frontend — duplo submit — ✅ resolvido

**Hoje**: o botão não é desabilitado durante o envio.

**Risco**: dois cliques rápidos = duas ordens enviadas, ambas contabilizadas na exposição.
Num formulário que registra risco financeiro isso não é cosmético — é a diferença entre a
exposição do sistema e a intenção do usuário.

**Proposta**: `button.disabled = true` no início do handler, restaurado no `finally`.

**Esforço**: P. Feito: `submitButton.disabled = true` logo no início do handler,
restaurado num `finally` que envolve toda a chamada. Verificado no browser real: disparar
o clique via JS e checar `disabled` imediatamente depois, antes da resposta chegar,
confirma `true`.

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

### 3. Rejeição sem `OrdRejReason` (tag 103) — ✅ resolvido

**Hoje**: o motivo da rejeição vai só em `Text` (58), como texto livre em português.

**Risco**: texto livre não é roteável. Um cliente FIX real precisa distinguir
programaticamente "símbolo inválido" de "limite de exposição excedido" para decidir se
retenta, se alerta ou se para de operar — e não vai fazer isso por `string.Contains`.

**Proposta**: preencher `OrdRejReason` junto com o `Text`; mapear as rejeições de campo
para `UNKNOWN_SYMBOL`/`INCORRECT_QUANTITY` e a de limite para `ORDER_EXCEEDS_LIMIT` (103=3),
que é exatamente o caso desta regra de negócio. `OrderRules.Validate` passa a devolver um
resultado com motivo + código em vez de `string?`.

**Esforço**: P. Feito: `OrderRules.Validate` devolve `RuleViolation?` (código + texto) em
vez de `string?`. Os códigos são constantes `int` locais em `OrderRules` (mesmo espírito
de `SideBuy`/`SideSell` já existentes) — não uma referência a `QuickFix.Fields.OrdRejReason`,
para manter a pureza que o comentário da classe já reivindicava. Símbolo inválido →
`UNKNOWN_SYMBOL` (1); quantidade inválida (não-inteira ou fora da faixa) →
`INCORRECT_QUANTITY` (13); lado/preço inválidos → `OTHER` (99), por não haver código mais
específico no dicionário FIX44; exposição excedida (em `AccumulatorApp`, fora de
`OrderRules`) → `ORDER_EXCEEDS_LIMIT` (3); erro interno inesperado (item 2 acima) →
`OTHER` (99). Verificado no wire: subindo os dois processos e forçando uma rejeição por
limite (duas ordens grandes em sequência), o `ExecutionReport` capturado no log traz
`103=3` antes da tag `58` (`Text`), na mensagem exata que rejeitou a segunda ordem.

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

### 1. `docker-compose` — a maior melhoria operacional — ✅ resolvido

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

**Esforço**: M. Feito, com uma correção em relação à proposta acima: o `depends_on` não
usa o health check do Generator (item 8) — o Generator não tem como estar saudável antes
do Accumulator sequer existir. Em vez disso, [`src/OrderAccumulator/Dockerfile`](../src/OrderAccumulator/Dockerfile)
define um `HEALTHCHECK` próprio (sonda TCP na porta 5001 via `/dev/tcp` do bash, já
presente na imagem base, sem instalar netcat só para isso), e é esse que o
[`compose.yaml`](../compose.yaml) espera antes de subir o Generator.
[`src/OrderGenerator/Dockerfile`](../src/OrderGenerator/Dockerfile) também define
`HEALTHCHECK` sobre `/health` (instala `curl`, ausente na imagem base) — não gate de
ordem, mas visibilidade em `docker ps`.

Duas correções que a proposta original não previa, mas que são necessárias para o compose
funcionar de verdade e não puramente cosméticas:
- **`SocketAcceptHost=127.0.0.1` → `0.0.0.0`** em [`accumulator.cfg`](../src/OrderAccumulator/accumulator.cfg):
  bind em loopback é inalcançável de outro container.
- **`Urls`: `localhost` → `0.0.0.0`** em [`appsettings.json`](../src/OrderGenerator/appsettings.json):
  mesmo problema do lado do Kestrel — o mapeamento de porta do host não alcança um bind
  em loopback dentro do container. Achado no processo: `ASPNETCORE_URLS` (env var) **não**
  vence o `Urls` do `appsettings.json` neste setup — supus a precedência padrão do
  ASP.NET Core e a validação em runtime provou o contrário, então a correção foi mudar o
  valor default em vez de depender de override por env var. Isso também corrige o item 5
  acima: o `applicationUrl` do `launchSettings.json` não controla mais o bind do Kestrel
  (só o alvo do browser automático do `dotnet run`, que continua funcionando porque
  `0.0.0.0` aceita `localhost`).

Também achei e corrigi um bug real ao ligar o override de host FIX (item 5) com o compose:
`SessionSettings.Get()` devolve o dicionário `[DEFAULT]`, mas o QuickFIXn já mescla
default+sessão na leitura do `.cfg` — mudar o default depois não propaga para uma sessão
já existente. [`Program.cs`](../src/OrderGenerator/Program.cs) agora itera
`settings.GetSessions()` e muta o dicionário de cada sessão diretamente.

Verificado de ponta a ponta com Docker real (`docker compose up --build`): os dois
serviços sobem, `accumulator` fica `healthy` primeiro, `generator` só inicia depois
(`depends_on`), fica `healthy` após o logon FIX, e uma ordem via
`curl http://localhost:5080/api/orders` do host completa o round-trip FIX normalmente
(aceite com exposição calculada). `/health` responde 200; símbolo inválido responde 400;
a página do formulário responde 200. Revisado (Standards + Spec, via skill `code-review`)
antes do commit — sem violações; a única lacuna apontada foi esta mesma imprecisão do
texto de proposta acima, já corrigida aqui.

### 2. `Directory.Build.props` — ✅ resolvido

**Hoje**: os três `.csproj` repetem `TargetFramework`, `Nullable` e `ImplicitUsings`. Nenhum
analisador está ligado e warnings não quebram build.

**Proposta**: centralizar as propriedades comuns e ligar `TreatWarningsAsErrors`,
`EnforceCodeStyleInBuild` e `AnalysisMode=Recommended`. Esperar um pequeno lote de warnings
na primeira execução — inclusive alguns de nulabilidade, o que é exatamente o ponto.

**Esforço**: P (mais o tempo de limpar o que aparecer). Feito: `TargetFramework`/`Nullable`/
`ImplicitUsings` saíram dos quatro `.csproj` para o `Directory.Build.props` da raiz, que
também liga `TreatWarningsAsErrors`, `EnforceCodeStyleInBuild` e `AnalysisMode=Recommended`.
Nulabilidade não gerou nada — os quatro `.csproj` já tinham `Nullable=enable`
individualmente antes desta mudança, então centralizar não altera esse comportamento; um
build limpo confirma zero `CS8xxx`. O lote de warnings apareceu no que o item não previu:
`AnalysisMode=Recommended`, que liga analisadores de qualidade além de nulabilidade. E
apareceu como *erros* de build, não avisos, porque
`TreatWarningsAsErrors` está ligado desde o primeiro build: `CA1848`/`CA1873` (pedem
`LoggerMessage` fonte-gerado em vez de `ILogger.LogX` direto) em toda chamada de log, e
`CA1822` (`BuildFIXMessage` podia ser `static`). Corrigi o `CA1822` (mudança de uma
palavra). Suprimi `CA1848`/`CA1873` via `NoWarn` com comentário explicando o motivo:
são regras para logging em hot path de alto throughput, desproporcionais para o volume
de log deste app — reescrever todo `ILogger.LogX` para o padrão fonte-gerado seria um
refactor maior do que este item propõe. Também apareceu `CA1707` ("remova underscores do
nome") em todo método de teste — é regra de nome de API pública, não de nome de teste
(`Given_When_Then` é convenção, não descuido); suprimida só para os projetos de teste via
[`tests/Directory.Build.props`](../tests/Directory.Build.props), que importa o da raiz e
adiciona a supressão por cima. Build limpo: `0 Warning(s), 0 Error(s)`; os 45 testes e o
`docker compose build --no-cache` continuam passando.

### 3. `Directory.Packages.props` (Central Package Management) — ✅ resolvido

**Hoje**: `QuickFIXn.Core` e `QuickFIXn.FIX44` em `1.14.1` declarados em dois `.csproj`
separados.

**Risco**: divergência silenciosa de versão entre os dois lados de um protocolo — o tipo de
problema que aparece em runtime, não em build.

**Proposta**: `<ManagePackageVersionsCentrally>` com todas as versões num arquivo.

**Esforço**: P. Feito: [`Directory.Packages.props`](../Directory.Packages.props) na raiz
com as oito versões de pacote (`QuickFIXn.*`, `Microsoft.Extensions.*`, pacotes de teste);
os quatro `.csproj` perderam o atributo `Version` de cada `PackageReference`. Os
Dockerfiles precisaram copiar o arquivo para o contexto de build antes do `dotnet
restore` — sem ele o MSBuild não encontra as versões e a build quebra dentro do
container (não localmente, onde o arquivo já está no diretório pai).

### 4. `global.json` — ✅ resolvido

**Hoje**: nada fixa a versão do SDK. O projeto usa .NET 10, mas qualquer SDK instalado
compila.

**Proposta**: `global.json` com `rollForward: latestFeature`. Também é o que garante
build reproduzível dentro do container.

**Esforço**: P. Feito: [`global.json`](../global.json) fixa `10.0.100` com
`rollForward: latestFeature` — a máquina de desenvolvimento tem três SDKs .NET 10
instalados (`10.0.100-rc.1...`, `10.0.204`, `10.0.400`); sem isso, qual deles compila o
projeto dependeria de qual estivesse mais recente no PATH.

### 5. `.editorconfig` — ✅ resolvido

**Hoje**: não existe. O estilo do código é consistente, mas por disciplina, não por
ferramenta.

**Risco**: sem ele `dotnet format` não tem regra e o `EnforceCodeStyleInBuild` do item 2 não
tem o que aplicar.

**Proposta**: `.editorconfig` de .NET registrando o estilo já praticado (expression-bodied
members, `var`, `namespace` file-scoped, ordenação de usings).

**Esforço**: P. Feito: [`.editorconfig`](../.editorconfig) na raiz, registrando o que já
era praticado (não mudando estilo): `namespace` file-scoped, `var`, membros
expression-bodied de uma linha, `using` fora do namespace com `System` primeiro.

### 6. README — ✅ resolvido

Atualizar a seção "Como executar" com o caminho do compose depois que ele existir,
mantendo o `dotnet run` como alternativa. O aviso das duas portas passa a ser nota de
rodapé em vez de destaque.

**Esforço**: P. Feito junto com o item 1 (docker-compose), no mesmo commit — o
`README.md` já tinha que mudar para documentar a via nova.

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
| 6 | Health check | Generator | P | ✅ feito — destrava `depends_on: service_healthy` |
| 7 | `docker-compose` | Infra | M | ✅ feito — maior ganho de operabilidade |
| 8 | `innerHTML` + duplo submit | Generator | P | ✅ feito — baratos e visíveis na avaliação |
| 9 | `ProblemDetails` | Generator | P | ✅ feito — contrato de erro estável |
| 10 | `OrdRejReason` (103) | Accumulator | P | ✅ feito — correção de protocolo FIX |
| 11 | Higiene de build (2–5 da Infra) | Infra | P | ✅ feito — melhor em lote, depois do resto |

Os itens 1–3 são independentes e podem ir juntos. Os itens 4–7 formam uma corrente: o
compose depende dos três anteriores.
