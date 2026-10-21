# Clube de Benefícios

Clube de benefícios por assinatura: o membro assina com CPF, recebe créditos por competência e troca o saldo por recompensas. O saldo é a soma de um ledger de partidas dobradas, append-only e idempotente. O monorepo reúne a API em .NET e o app mobile.

```
.
├── back/    API .NET 10 (arquitetura hexagonal, SQLite, EF Core)
├── front/   app Expo / React Native (web e celular)
└── docs/    documentação auxiliar
```

## Como rodar

Pré-requisitos: .NET 10 SDK, Node 22+ e pnpm 10.

**Back** (em um terminal):

```bash
cd back
dotnet tool restore
dotnet test
dotnet user-jwts create --project src/Clube.Host   # uma vez por máquina: cria a chave que assina os tokens de dev
dotnet run --project src/Clube.Host --launch-profile http
```

O perfil `http` sobe em Development, escuta em `0.0.0.0:5135` (o celular alcança pelo IP do PC) e expõe as rotas `/dev/*` que o app usa. Da raiz, os testes rodam com `dotnet test back/Clube.slnx`.

**Front** (em outro terminal, com a API no ar):

```bash
cd front
pnpm install
cp .env.example .env.local   # EXPO_PUBLIC_API_URL: localhost no navegador, IP da LAN no celular
pnpm start                   # "w" abre no navegador; o QR code abre no Expo Go
```

Detalhes do celular (IP da rede, firewall, `npx expo login` no iOS) estão no [front/README.md](front/README.md).

## Detalhes de cada parte

- [back/README.md](back/README.md): arquitetura, decisões de domínio, rotas, autenticação e como gerar tokens e webhooks de teste.
- [front/README.md](front/README.md): app Expo, stack, estrutura, decisões e roteiro de demonstração.

O `.editorconfig` e o `.gitignore` da raiz valem para as duas pastas.
