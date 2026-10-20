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
3. **Adaptador de saída SQLite** (`SqliteMembershipStore`) grava a transação e incrementa o `LedgerVersion` do membro no mesmo `SaveChanges`.
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
- **Idempotência:** cada transação tem uma chave única. O crédito usa `allowance:{membro}:{competência}` e o resgate exige o header `Idempotency-Key`. Uma requisição repetida responde com o estado atual e não cobra de novo. Cada transação guarda também uma impressão digital da requisição (`redeem:{rewardId}` no resgate; no crédito, a própria chave): reusar a chave com outro conteúdo devolve 422 em vez de fingir que o novo pedido foi atendido. A porta de saída expõe `FindPostedFingerprintAsync`, que devolve `string?` e serve tanto ao retry quanto à comparação. Como o schema nasce por `EnsureCreated`, bancos SQLite antigos precisam ser recriados para ganhar a coluna `Fingerprint`.
- **Janela do crédito:** só dá pra creditar competências entre o mês da assinatura e o mês atual, inclusive; fora disso a resposta é 422 (`membership.future_period` ou `membership.period_before_subscription`). Os meses são calculados em `America/Sao_Paulo`, num lugar só (`BillingPeriod.At`), e não em UTC: às 21h30 de 31/10 em São Paulo ainda é outubro. O relógio vem do `TimeProvider`, que os testes substituem por um `FakeTimeProvider`. O retry de uma competência já creditada continua respondendo 200 sem reavaliar a janela.
- **Concorrência:** cada lançamento incrementa `Membership.LedgerVersion`, que é concurrency token. Dois resgates que leram o mesmo saldo não conseguem gastá-lo duas vezes, e o segundo recebe 409.
- **Extrato:** `GET /memberships/{id}/statement` lista os lançamentos que explicam o saldo, com limite de 50.

### Outras

- **Unicidade de CPF na porta de saída:** quem garante é o índice único (ou o store em memória). Não há pré-checagem, que correria risco de race.
- **CPF mascarado** no `ToString` e na resposta, pra não vazar PII em log.
- **`TimeProvider` também é porta de saída:** o relógio já vem abstraído na BCL, com fake pra teste.

## Rodar

```bash
dotnet test
dotnet run --project src/Clube.Host
```

| Método | Rota | Corpo / header |
|---|---|---|
| POST | `/memberships` | `{ "cpf": "529.982.247-25" }` |
| GET | `/memberships/{id}` | |
| GET | `/memberships/{id}/statement` | |
| POST | `/memberships/{id}/allowance-credits` | `{ "period": "2026-10" }` |
| POST | `/memberships/{id}/redemptions` | `{ "rewardId": "cinema-ticket" }` + header `Idempotency-Key: <chave do cliente>` |
