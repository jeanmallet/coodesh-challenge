# Order aceita é tratada como executada integralmente pela OrderQty

A especificação define exposição sobre "quantidade executada", mas manda responder a
aceitação com `ExecType=New` — que em FIX significa *reconhecida*, não *preenchida* —
e prevê um único ExecutionReport por ordem, sem fluxo de fills (`LastQty`/`CumQty`).

Decidimos interpretar "quantidade executada" como a `OrderQty` integral da ordem: uma
ordem aceita conta na exposição por `Price × OrderQty`, e o ExecutionReport é
preenchido de forma consistente (`LastQty=CumQty=OrderQty`, `LeavesQty=0`,
`AvgPx=Price`) mesmo carregando `ExecType=New`.

É a única leitura em que um `NewOrderSingle` produz um ExecutionReport e os números de
exposição fecham sem inventar um fluxo de execução que a spec não descreve.

## Consequences

- `ExecType=New` acompanhado de quantidade executada > 0 é deliberado; um leitor que
  conheça FIX estranharia sem este registro.
- Se a spec evoluir para fills reais (parciais), esta decisão precisa ser revista — a
  exposição passaria a somar `LastQty`, não `OrderQty`.
