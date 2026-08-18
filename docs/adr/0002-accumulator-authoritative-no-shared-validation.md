# OrderAccumulator é a autoridade de validação; sem lib compartilhada

Em FIX, o *acceptor* nunca confia no *initiator* — o chamador pode estar com bug,
desatualizado ou ser malicioso. O OrderAccumulator valida por conta própria as
quatro regras de campo (símbolo, lado, quantidade, preço) **e** a regra de exposição,
sendo correto independente de quem o chamou.

O OrderGenerator também valida, mas apenas como *fail-fast* de UX (rejeita entrada
inválida com `400` antes de virar mensagem FIX). Ele não é a autoridade.

Decidimos **não** extrair uma biblioteca de validação compartilhada. As mesmas regras
vivem duplicadas nos dois lados de propósito: no mundo real são deployables de donos
diferentes, e acoplar o acceptor ao código do initiator destruiria justamente a
independência que garante que o acumulador funcione corretamente venha a ordem de onde
vier. São quatro checagens — o custo da duplicação é baixo e pago conscientemente.

## Consequences

- Uma regra que mude (ex.: faixa de preço) precisa mudar nos dois lados; isso é
  aceito como o preço da independência, não um descuido a ser "corrigido" com DRY.
