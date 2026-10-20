# clube-beneficios-dotnet

Clube de benefícios por assinatura em .NET 10 com **arquitetura hexagonal (ports and adapters)**: o membro assina com CPF, recebe R$ 20,00 por competência e troca o saldo por recompensas. O saldo não é um campo: é a soma de um **ledger de partidas dobradas**, append-only e idempotente.

## A ideia em uma frase

A aplicação mora dentro de um hexágono e não sabe que o mundo existe. Tudo que entra ou sai passa por uma **porta**, que é um contrato escrito pelo próprio hexágono, e cada tecnologia de fora se pluga numa porta através de um **adaptador**.

> "Allow an application to equally be driven by users, programs, automated test or batch scripts, and to be developed and tested in isolation from its eventual run-time devices and databases."
> Alistair Cockburn, [Hexagonal Architecture](https://alistair.cockburn.us/hexagonal-architecture/) (2005)

```mermaid
flowchart LR
    subgraph driving["Lado que dirige (esquerda)"]
        HTTP["HTTP<br/>Clube.Adapters.Http"]
        TEST["Teste<br/>MembershipManagerTests"]
    end

    subgraph hexagon["Hexágono: Clube.Core"]
        IN(["porta de entrada<br/>IForManagingMemberships"])
        APP["MembershipManager<br/>+ domínio"]
        OUT(["porta de saída<br/>IForStoringMemberships"])
        IN --> APP --> OUT
    end

    subgraph driven["Lado dirigido (direita)"]
        SQL["SQLite<br/>Clube.Adapters.Sqlite"]
        MEM["Em memória<br/>InMemoryMembershipStore"]
    end

    HTTP --> IN
    TEST --> IN
    SQL -. implementa .-> OUT
    MEM -. implementa .-> OUT
```

## O que é cada peça, neste repo

| Conceito | O que é | Aqui |
|---|---|---|
| **Hexágono** | regra de negócio e casos de uso. Não referencia nenhum framework | `src/Clube.Core` (zero pacote no csproj) |
| **Porta de entrada** (driving / primary port) | o que o mundo pode **pedir** ao hexágono | `Ports/Driving/IForManagingMemberships.cs` |
| **Porta de saída** (driven / secondary port) | o que o hexágono **precisa** do mundo, descrito nos termos dele | `Ports/Driven/IForStoringMemberships.cs` |
| **Adaptador de entrada** (driving adapter) | traduz uma tecnologia em chamadas na porta de entrada | `Clube.Adapters.Http` (HTTP para porta), `MembershipManagerTests` (teste para porta) |
| **Adaptador de saída** (driven adapter) | implementa a porta de saída com uma tecnologia concreta | `Clube.Adapters.Sqlite` (EF Core), `InMemoryMembershipStore` (teste) |
| **Configurador** | o único lugar que conhece tudo e pluga adaptador em porta | `src/Clube.Host/Program.cs` |

A direção da dependência é a regra que segura tudo: **adaptadores dependem do hexágono, nunca o contrário**. Quem define a interface de storage é o núcleo, não o EF. O `HexagonBoundaryTests` quebra o build se alguém fizer o `Clube.Core` referenciar ASP.NET, EF ou um adaptador.

### Lendo uma requisição de ponta a ponta

`POST /memberships/{id}/redemptions`

1. **Adaptador de entrada HTTP** (`MembershipEndpoints.Redeem`) lê o JSON, monta o `MembershipId` e chama `IForManagingMemberships.RedeemRewardAsync`.
2. **Hexágono** (`MembershipManager`) busca o membro e o saldo pela porta de saída, e `Membership.Redeem` gera uma transação de ledger que debita o membro e credita a conta de resgates.
3. **Adaptador de saída SQLite** (`SqliteMembershipStore`) grava a transação, incrementa o `LedgerVersion` do membro e grava a mensagem de outbox `RewardRedeemed` no mesmo `SaveChanges`. O voucher no parceiro sai depois, pelo `OutboxWorker` (veja "Voucher no parceiro").
4. O hexágono devolve um `Result<MembershipView>`, e o adaptador HTTP o traduz em 200 ou ProblemDetails (400, 404, 409 ou 422).

Trocar HTTP por gRPC ou fila, ou SQLite por Postgres, é escrever outro adaptador. O `Clube.Core` não muda uma linha.

### Por que existem dois adaptadores pra mesma porta

Uma porta só se paga quando tem mais de um adaptador. Aqui o segundo é o teste, exatamente como o Cockburn propõe: `MembershipManagerTests` dirige o hexágono pela porta de entrada com o `InMemoryMembershipStore` plugado na porta de saída. Roda em milissegundos, sem HTTP e sem banco. O `Clube.EndToEnd.Tests` cobre os adaptadores reais.

## Hexagonal x Clean Architecture

As duas buscam o mesmo isolamento, mas contam a história de jeitos diferentes.

| | Hexagonal | Clean |
|---|---|---|
| Metáfora | **dentro** x **fora**, com portas na borda | **camadas concêntricas** (entities, use cases, adapters, frameworks) |
| Vocabulário | port, adapter, driving, driven | entity, use case, interactor, gateway, presenter |
| Estrutura típica | `Core` + um projeto por adaptador | `Domain` / `Application` / `Infrastructure` / `Api` |
| Regra | adaptadores dependem das portas | dependências apontam pra dentro |

A primeira versão deste repo tinha a estrutura em camadas da Clean (`Domain/Application/Infrastructure/Api`) e só a porta de saída. Esta versão deixa porta e adaptador explícitos no nome, na pasta e no projeto.

O domínio fica numa pasta dentro do `Core`, não num projeto separado: no hexagonal a fronteira que importa é dentro x fora, não domínio x aplicação.

## Decisões que valem a leitura

### Dinheiro e ledger

- **`Money` em centavos (`long`):** exato e igual no banco (`bigint`), no JSON e no JavaScript do front. `decimal` só aparece na conta de percentual.
- **Arredondamento comercial:** `PercentOf` usa `MidpointRounding.AwayFromZero`. O default do `Math.Round` é o bancário (`ToEven`), que muda centavos no ponto médio: a garrafa térmica (R$ 29,90 com 15% off) sai a R$ 25,41, e com o default sairia a R$ 25,42.
- **`checked` nas somas:** overflow lança exceção em vez de virar número negativo.
- **Partidas dobradas:** toda transação tem 2 lançamentos ou mais, que somam zero (`LedgerTransaction.Post`). O crédito mensal sai de `clube:allowance-funding` e o resgate vai pra `clube:redemptions`, então as contas do clube mostram passivo e gasto.
- **Append-only no banco:** triggers no SQLite abortam qualquer `UPDATE` ou `DELETE` em `ledger_entries` e `ledger_transactions`. Erro se corrige com lançamento de estorno.
- **Idempotência:** cada transação tem uma chave única. O crédito usa `allowance:{membro}:{competência}` e o resgate exige o header `Idempotency-Key`. Uma requisição repetida responde com o estado atual e não cobra de novo. Cada transação guarda também uma impressão digital da requisição (`redeem:{rewardId}` no resgate; no crédito, `payment:{endToEndId}`): reusar a chave com outro conteúdo devolve 422 em vez de fingir que o novo pedido foi atendido. A porta de saída expõe `FindPostedFingerprintAsync`, que devolve `string?` e serve tanto ao retry quanto à comparação. Como o schema nasce por `EnsureCreated`, bancos SQLite antigos precisam ser recriados para ganhar a coluna `Fingerprint`.
- **O crédito só nasce de um pagamento:** não existe rota pública de crédito. O PSP chama `POST /webhooks/pix` e o adaptador `PixWebhookReceiver` só traduz o evento numa chamada da porta de entrada (`CreditMonthlyAllowanceAsync` ou `RefundPaymentAsync`); quem decide o que acontece é o hexágono.
  - **Autenticidade no estilo Stripe:** headers `X-Pix-Timestamp` (unix seconds) e `X-Pix-Signature` (hex do HMAC-SHA256 de `"{timestamp}.{corpo cru}"` com `Psp:WebhookSecret`). O corpo é lido cru e desserializado pelo adaptador, porque a assinatura cobre os bytes exatos. Comparação em tempo constante (`CryptographicOperations.FixedTimeEquals`) e tolerância de 5 minutos pelo `TimeProvider`, pros dois lados (timestamp no futuro também é recusado, senão ficaria reaproveitável por mais tempo). Assinatura ausente, inválida ou expirada é sempre o mesmo 401, sem dizer qual das três falhou. Corpo acima de 16 KB é recusado antes de ser bufferizado.
  - **Segredo:** só em `appsettings.Development.json` (um valor de dev óbvio). Fora de Development, o host **não sobe** sem `Psp:WebhookSecret` com pelo menos 32 caracteres (`ValidateOnStart`), por exemplo via variável `Psp__WebhookSecret`. Escolhi falhar no boot em vez de recusar tudo no endpoint porque um deploy sem segredo vira incidente na hora, e não um silêncio de créditos que ninguém percebe até o membro reclamar. O segredo nunca vai pra log.
  - **Ator e referência externa:** depois de validar a assinatura, o adaptador marca o request como vindo do PSP e o `HttpCaller` (a mesma implementação de `IForIdentifyingTheCaller`) devolve `Actor.PixProvider` (`ActorKind.PaymentProvider`, id `pix`); sem essa marca, o request continua anônimo. O `endToEndId` vira `ExternalReference` da transação de crédito (coluna com índice único: um pagamento Pix é lançado uma vez só, e as transações sem referência ficam com NULL).
  - **`pix.received`** (`endToEndId`, `membershipId`, `period`, `amountCents`, `paidAt`): credita a competência se o valor for exatamente `Membership.MonthlyAllowance` (senão 422 `membership.allowance_amount_mismatch`) e se a competência estiver na janela. O mesmo `endToEndId` reenviado responde 200 `already_credited`. Um `endToEndId` **diferente** pra uma competência já paga é o membro pagando duas vezes: não credita, responde 200 `duplicate_payment` (o PSP para de reentregar) e registra um WARN estruturado (endToEndId, membro, competência, sem CPF) pro suporte devolver o Pix. Responder 4xx aí não ajudaria ninguém: o PSP reentregaria pra sempre e o dinheiro continuaria parado. O `paidAt` é aceito mas não entra no ledger, que carimba a hora em que o crédito foi lançado.
  - **`pix.refunded`** (`endToEndId`, `originalEndToEndId`, `refundedAt`), inclusive MED: acha o crédito pela referência externa original e o estorna com a operação de estorno que já existe (`ReverseTransactionAsync`), então o saldo pode ficar negativo. Reentrega responde `already_refunded`. Refund de pagamento desconhecido responde 200 `unknown_payment` com WARN: um 4xx faria o PSP reentregar pra sempre algo que nenhuma reentrega vai resolver; o caso é de suporte, não de retry. Só há estorno total (devolução parcial de Pix não é tratada).
  - Outros erros (membro inexistente, competência fora da janela, payload fora do contrato) continuam 4xx com WARN, porque são defeito de integração e não algo que o PSP resolva sozinho. Bancos SQLite antigos precisam ser recriados para ganhar a coluna `ExternalReference`.
- **Janela do crédito:** só dá pra creditar competências entre o mês da assinatura e o mês atual, inclusive; fora disso a resposta é 422 (`membership.future_period` ou `membership.period_before_subscription`). Os meses são calculados em `America/Sao_Paulo`, num lugar só (`BillingPeriod.At`), e não em UTC: às 21h30 de 31/10 em São Paulo ainda é outubro. O relógio vem do `TimeProvider`, que os testes substituem por um `FakeTimeProvider`. O retry de uma competência já creditada continua respondendo 200 sem reavaliar a janela.
- **Concorrência:** cada lançamento incrementa `Membership.LedgerVersion`, que é concurrency token. Dois resgates que leram o mesmo saldo não conseguem gastá-lo duas vezes, e o segundo recebe 409.
- **Auditoria (quem e qual requisição):** toda transação guarda um `Actor` (`Kind` + `Id` opcional) e um `CorrelationId`, em colunas de `ledger_transactions` (e no store em memória). `Actor` é um tipo fechado com construtor privado e fábricas (`Anonymous`, `Member(id)`, `Operator(sub)`, `PixProvider`), então "anônimo com id" não existe; um novo tipo de chamador (webhook do PSP, job) é um valor no enum `ActorKind` mais uma fábrica. O núcleo não recebe isso por parâmetro: pergunta à porta de saída `IForIdentifyingTheCaller.Current`, que o adaptador HTTP implementa a partir do request (PSP no webhook assinado, `Operator(sub)` ou `Member(membership_id)` a partir do JWT validado, anônimo no resto; nunca de header ou corpo livre; correlação = trace id da `Activity`, que respeita o header `traceparent`, ou `TraceIdentifier`). Nos testes do núcleo entra um fake. O extrato mostra `actorKind` e `actorId` (id estável, nunca CPF). O adaptador SQLite loga a postagem (`ILogger` com message template: transactionId, chave de idempotência, kind do ator, correlationId) só depois do commit. Bancos SQLite antigos precisam ser recriados para ganhar as colunas.
- **Estorno:** `POST /memberships/{id}/transactions/{transactionId}/reversal` posta uma transação nova com os lançamentos espelhados (débito vira crédito e vice-versa, mesmo valor), descrição `Reversal of <original>` e `ReversesTransactionId` apontando pro original, que continua no extrato (append-only). A chave de idempotência é determinística, `reversal:{originalId}`: estornar duas vezes responde com o estado atual e não estorna em dobro, e estornar um estorno é 422 (`ledger.reversal_of_reversal`). Transação inexistente ou de outro membro é 404. Passa pelo mesmo `LedgerVersion` e pela mesma auditoria (ator e correlação). **Saldo negativo é permitido:** estornar um crédito já gasto (caso da devolução de Pix) deixa o membro devendo, e `Redeem` segue recusando resgates até o saldo cobrir o preço. Estornar um resgate devolve o valor. **Limite conhecido:** só estorno total, não há estorno parcial. Só operador chega na rota (policy HTTP), e o próprio núcleo recusa com 403 (`ledger.reversal_forbidden`) estorno ou refund pedido por quem não é operador nem o PSP, então um adaptador que esqueça a policy não abre a porta. Bancos SQLite antigos precisam ser recriados para ganhar a coluna `ReversesTransactionId`.
- **Extrato paginado:** `GET /memberships/{id}/statement?limit=&cursor=` devolve os lançamentos do mais novo para o mais antigo, e `nextCursor` (null na última página). Paginação por keyset sobre o id da entrada do ledger, não por offset: lançamento novo entre duas páginas não repete nem pula linha (ele só aparece ao recomeçar sem cursor). O adaptador busca `limit + 1` linhas para saber se há próxima página, sem COUNT. `limit` vai de 1 a 100 (padrão 50); fora disso é 400, não clamp, para o cliente não achar que recebeu o que pediu. O cursor é opaco para o cliente (base64url da posição): o Core só conhece `StatementCursor` e `StatementPage`, a codificação fica no adaptador HTTP; cursor inválido é 400. `balanceCents` continua sendo o saldo total, não o da página.

### Voucher no parceiro (transactional outbox)

- **O problema é escrita dupla:** o resgate debita o ledger e o parceiro (cinema, perfumaria) precisa emitir o voucher. Chamar o parceiro dentro do caso de uso deixa duas falhas possíveis: débito gravado com a chamada falhando (membro sem voucher) ou voucher emitido com o `SaveChanges` falhando (voucher de graça). Por isso o caso de uso **não chama o parceiro**: grava uma mensagem `RewardRedeemed` (transactionId, membershipId, rewardId, priceCents, postedAt) na tabela `outbox_messages`, no mesmo `SaveChanges` da transação de resgate (`IForStoringMemberships.Enqueue`). Ou as duas existem, ou nenhuma: o teste e2e derruba o insert do outbox com um trigger e confere que o resgate também não ficou no ledger.
- **O evento nasce no caso de uso, não em `Membership.Redeem`:** ele é uma projeção da transação postada mais a recompensa, sem nenhuma regra nova, e avisar um parceiro é decisão de integração da aplicação, não do agregado. Assim `Redeem` continua devolvendo só a transação e os testes de domínio não mudam. Se um dia o domínio precisar decidir se um resgate gera voucher, o evento sobe pra lá.
- **A tabela de outbox não é o ledger:** fica separada e **não** é append-only (sem triggers), porque cada tentativa atualiza status, tentativas, próxima tentativa e último erro. A próxima tentativa é guardada em unix ms porque o SQLite não compara nem ordena `DateTimeOffset` pelo EF. O evento vai em JSON com o nome do tipo ao lado.
- **Entrega:** `OutboxDispatcher.DispatchBatchAsync` (núcleo, atrás da porta de entrada `IForRelayingTheOutbox`) busca até 20 mensagens pendentes vencidas (`IForStoringOutbox.FindDueAsync`, com LIMIT), chama a porta do parceiro `IForIssuingVouchers` com timeout explícito de 10 s (pelo `TimeProvider`) e grava o resultado de cada mensagem na hora. Falha vira retry com backoff exponencial (30 s, 60 s, 120 s...) mais até 20% de jitter, pra mensagens que falharam juntas não voltarem juntas; na 6ª falha a mensagem vira `DeadLettered` e o worker loga ERROR estruturado (messageId, tipo, transactionId, tentativas, erro, sem CPF), que é o sinal pro suporte resolver na mão. Qualquer exceção do parceiro conta como "ainda não entregue": uma mensagem com erro não derruba o lote nem as outras. Mensagem entregue não volta a ser buscada.
- **Pelo menos uma vez, então idempotente no parceiro:** se o parceiro emitir e a marcação de entregue falhar (ou o timeout estourar com o parceiro já tendo emitido), a mensagem é reentregue. Toda chamada leva o transactionId do resgate como `IdempotencyKey`, e o parceiro precisa tratar a chave repetida como o mesmo voucher.
- **Worker:** `OutboxWorker` (`BackgroundService` em `Clube.Adapters.Partners`) só faz o loop: um escopo por lote, loga o resultado, espera 5 s quando o lote veio incompleto e para pelo `stoppingToken`. Erro no lote inteiro (banco fora) é logado e tentado no próximo ciclo, sem matar o host. A regra toda mora no dispatcher, testado sem o hosted service com `FakeTimeProvider`; nos testes e2e o worker é removido no `ClubeApiFactory` e o lote é disparado à mão (`DispatchOutboxAsync`).
- **Parceiro de mentira:** `LoggingVoucherIssuer` só loga e sempre responde ok, até existir o contrato real. O adaptador HTTP de verdade entra no lugar dele, com o token de timeout do dispatcher e a chave de idempotência num header.
- **Estorno de resgate cancela o voucher:** estornar uma transação que tocou `clube:redemptions` grava `RedemptionReversed` no mesmo `SaveChanges` do estorno, e o dispatcher chama `CancelAsync` com a chave do voucher (o transactionId do resgate original). Estorno de crédito não gera mensagem.
- **Limites conhecidos:** (1) **uma instância só do worker.** Duas leriam as mesmas linhas pendentes; o parceiro aguenta (idempotência), mas é trabalho dobrado e as tentativas se atropelam. Várias instâncias pedem lock de linha (`FOR UPDATE SKIP LOCKED` no Postgres) ou um lease por mensagem. (2) **sem ordem entre mensagens:** se a emissão estiver em backoff quando o estorno acontecer, o cancelamento pode chegar antes dela; o parceiro precisa recusar ou adiar cancelamento de chave desconhecida, ou o dispatcher teria que segurar o cancelamento até a emissão sair. (3) dead letter não tem reprocessamento por rota: hoje é voltar o status pra `Pending` direto no banco. (4) lote sequencial; paralelismo limitado se um lote passar a demorar mais que o intervalo. Bancos SQLite antigos precisam ser recriados para ganhar a tabela `outbox_messages`.

### Autenticação e autorização

- **JWT Bearer, configurado só por configuração:** `AddJwtBearer` lê `Authentication:Schemes:Bearer` (o que o `dotnet user-jwts` escreve). Em produção, aponte pra um IdP real com `Authority`, `ValidIssuer` e `ValidAudiences` (ex.: `Authentication__Schemes__Bearer__Authority`). Fora de Development o host **não sobe** sem issuer, audience e uma fonte de chave (`Authority` ou `SigningKeys`), mesmo critério do segredo do webhook: sem issuer ou audience o handler simplesmente pula a checagem, então exigir só a chave não basta. O mapeamento de claims do .NET está desligado: valem os nomes crus `sub`, `role` e `membership_id`.
- **Dois papéis, como policies:** membro (claim `membership_id`) lê e resgata só no próprio `{id}`; operador (role `operator` com `sub`) lê qualquer membro e é o único que estorna. O operador não resgata pelo membro: gastar saldo é ato do titular. A regra é um requirement resource-based (`MembershipAccessHandler`) que compara o `{id}` da rota com o token, não um `if` em cada handler.
- **403, não 404, pra membro olhando outro membro:** a policy decide antes de qualquer consulta ao banco, então qualquer id que não seja o do token responde 403, exista ou não. Não vaza existência (os ids já são GUIDs aleatórios) e o cliente distingue "não é seu" de "não existe". Sem token, token expirado ou assinado com outra chave é 401.
- **`POST /memberships` continua aberto:** quem assina ainda não tem `membership_id`, então não há token de membro pra exigir. Quando houver login no IdP, a adesão pode passar a exigir um token sem `membership_id`; o 409 de CPF repetido continua permitindo enumerar CPFs e merece rate limit (fora deste escopo).
- **Sem fallback policy:** cada rota diz o que exige. O `POST /webhooks/pix` segue sem JWT (a autenticidade é a assinatura HMAC), e um header `Authorization` qualquer nele não bloqueia nem muda o ator.
- **OpenAPI:** o documento declara o esquema `Bearer` (o Scalar mostra o campo de token) e as rotas protegidas declaram `security`, 401 e 403.

### Outras

- **Unicidade de CPF na porta de saída:** quem garante é o índice único (ou o store em memória). Não há pré-checagem, que correria risco de race.
- **CPF mascarado** no `ToString` e na resposta, pra não vazar PII em log.
- **`TimeProvider` também é porta de saída:** o relógio já vem abstraído na BCL, com fake pra teste.

## Rodar

```bash
dotnet test
dotnet run --project src/Clube.Host
```

| Método | Rota | Quem | Corpo / header |
|---|---|---|---|
| POST | `/memberships` | aberto | `{ "cpf": "529.982.247-25" }` |
| GET | `/memberships/{id}` | o próprio membro ou operador | |
| GET | `/memberships/{id}/statement` | o próprio membro ou operador | query `limit` (1 a 100) e `cursor` |
| POST | `/memberships/{id}/redemptions` | o próprio membro | `{ "rewardId": "cinema-ticket" }` + header `Idempotency-Key: <chave do cliente>` |
| POST | `/memberships/{id}/transactions/{transactionId}/reversal` | operador | sem corpo (estorno total) |
| POST | `/webhooks/pix` | PSP (assinatura HMAC, sem JWT) | evento `pix.received` ou `pix.refunded` + headers `X-Pix-Timestamp` e `X-Pix-Signature` |

Tokens de desenvolvimento saem do `dotnet user-jwts` (o issuer `dotnet-user-jwts` e as audiences locais já estão no `appsettings.Development.json`; a chave fica nos user secrets da máquina):

```bash
dotnet user-jwts create --project src/Clube.Host --claim membership_id=<id da adesão>   # membro
dotnet user-jwts create --project src/Clube.Host --role operator                        # operador
```

No Scalar (`/scalar` em Development), cole o token no campo de autenticação Bearer; com curl, `-H "Authorization: Bearer <token>"`.

O Scalar não assina requisições, então pra creditar à mão use o script, que monta, assina com o segredo de Development e envia um `pix.received` pra `http://localhost:5135`:

```bash
scripts/send-pix-webhook.sh <membershipId> [competência yyyy-MM] [endToEndId] [amountCents]
```

Repetir com o mesmo `endToEndId` mostra a reentrega idempotente; com outro, pra mesma competência, mostra o `duplicate_payment`. `PIX_WEBHOOK_SECRET` e `PIX_WEBHOOK_URL` trocam o segredo e o destino.
