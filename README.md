# Clube de Benefícios

Clube de benefícios por assinatura: o membro assina com CPF, recebe créditos por competência e troca o saldo por recompensas. O saldo é a soma de um ledger de partidas dobradas, append-only e idempotente. O monorepo reúne a API em .NET e o app mobile.

```
.
├── back/    API .NET 10 (arquitetura hexagonal, SQLite, EF Core)
├── front/   app React Native com Expo (em breve)
└── docs/    documentação auxiliar
```

## Como rodar

**Back** (.NET 10 SDK):

```bash
cd back
dotnet tool restore
dotnet test
dotnet run --project src/Clube.Host
```

Da raiz, os testes rodam com `dotnet test back/Clube.slnx`.

**Front**: o app Expo ainda não existe; as instruções entram em `front/README.md` quando ele chegar.

## Detalhes de cada parte

- [back/README.md](back/README.md): arquitetura, decisões de domínio, rotas, autenticação e como gerar tokens e webhooks de teste.
- [front/README.md](front/README.md): app mobile.

O `.editorconfig` e o `.gitignore` da raiz valem para as duas pastas.
