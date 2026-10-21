# Clube (app)

App Expo / React Native que opera o clube de benefícios do `back/`. Um app, dois públicos:

- **Membro:** entra com o código da adesão (ou adere com o CPF), vê o saldo na carteirinha, paga o Pix do mês, troca crédito por recompensa e confere o extrato.
- **Operador (balcão):** busca uma adesão, estorna lançamentos, acompanha a saúde do livro-caixa e a reconciliação com o PSP, roda os jobs e move o relógio de dev.

Roda no navegador (Expo web) e no celular (Expo Go), com o mesmo código.

## Stack

| Peça | Versão | Por quê |
|---|---|---|
| Expo SDK | 57 (`expo ~57.0.30`) | É o SDK do Expo Go da loja hoje; o 58 ainda não tem Expo Go |
| React Native / React | 0.86.3 / 19.2.3 | New Architecture obrigatória, Hermes V1 |
| React Compiler | ligado (`experiments.reactCompiler`) | Nada de `useMemo`/`useCallback` manual |
| Expo Router | ~57.0.28 | Rotas por arquivo, typed routes, `Stack.Protected` |
| TanStack Query | 5.104.5 | Estado de servidor: cache, invalidação, paginação infinita |
| TanStack Form | 1.33.5 | Formulários com schema Zod (Standard Schema) |
| Zod | 4.6.7 | Toda resposta da API é parseada na fronteira |
| Reanimated / Worklets | 4.5.1 / 0.10.1 | Micro-interações no thread de UI |
| expo-secure-store | ~57.0.4 | Sessão no Keychain/Keystore (nativo) |
| expo-crypto | ~57.0.4 | `randomUUID` do CSPRNG da plataforma para `Idempotency-Key` |
| Fontes | Bricolage Grotesque, Figtree, JetBrains Mono (`@expo-google-fonts/*` 0.4.1) | Display com personalidade, texto legível, ids em mono |
| Biome | 2.5.15 | Lint e formatação |
| ESLint + `eslint-plugin-react-hooks` | 10.13.0 + 7.1.1 | Só as regras de hooks e do React Compiler |
| Testes | jest-expo 57.0.6, Jest 29.7, RNTL 14.1.0, MSW 3.0.3 | Caminho oficial do Expo |
| TypeScript | ~6.0.3 | `strict` + `noUncheckedIndexedAccess` |
| Gerenciador | pnpm 10 | Um lockfile só |

## Como rodar

Pré-requisitos: Node 22+, pnpm 10 e a API rodando (veja o [README da raiz](../README.md)).

```bash
cd front
pnpm install
cp .env.example .env.local   # ajuste EXPO_PUBLIC_API_URL se precisar
pnpm start                   # e aperte "w" para abrir no navegador
```

No navegador do próprio PC, `EXPO_PUBLIC_API_URL=http://localhost:5135` basta (é também o padrão quando a variável não existe). O backend libera CORS para `http://localhost:8081` só em Development.

### No celular (Expo Go)

1. Celular e PC na mesma rede Wi-Fi (rede sem "isolamento de cliente").
2. Descubra o IP do PC na rede (`ipconfig`, linha "Endereço IPv4", algo como `192.168.0.10`) e coloque em `.env.local`:
   ```
   EXPO_PUBLIC_API_URL=http://192.168.0.10:5135
   ```
   O perfil `http` da API já escuta em `0.0.0.0:5135`. No Windows, libere a porta para a rede privada:
   `New-NetFirewallRule -DisplayName "Kestrel dev 5135" -Direction Inbound -Protocol TCP -LocalPort 5135 -Action Allow -Profile Private`
3. `pnpm start` e leia o QR code com o Expo Go (Android) ou com a câmera (iOS).
4. **iOS:** o Expo Go só abre projetos se o PC e o app estiverem logados na mesma conta Expo: rode `npx expo login` no PC e entre com a mesma conta no app.

Variáveis `EXPO_PUBLIC_*` vão embutidas no bundle: nunca coloque segredo nelas. `.env` e `.env*.local` estão no `.gitignore`.

### Roteiro de demonstração

1. Entrada, "Sou membro", "Aderir agora", "Gerar CPF de teste", "Aderir".
2. Início: "Pagar Pix do mês" (saldo sobe com animação). Pague de novo: `duplicate_payment`.
3. Recompensas: resgate o ingresso; depois "Repetir a mesma requisição": mesma chave, mesmo resultado, saldo intacto.
4. Sair, "Sou operador": busque a adesão (o código está na carteirinha, botão de copiar), estorne o resgate.
5. Saúde: reconcilie "Hoje" (o Pix duplicado aparece como "Pago e não creditado"). Ferramentas: "+12 meses" e "Expirar créditos".

## Qualidade

```bash
pnpm exec tsc --noEmit    # tipos
pnpm lint                 # biome check . && eslint . --max-warnings 0
pnpm format               # biome check --write .
pnpm test                 # jest-expo + RNTL + MSW
pnpm exec expo-doctor     # dependências e config do SDK
```

## Estrutura

Por domínio, não por camada. As rotas em `src/app` são finas: cada arquivo só reexporta a tela do seu domínio.

```
src/
├── app/                  rotas (Expo Router)
│   ├── _layout.tsx       providers, fontes, splash e os três Stack.Protected
│   ├── (public)/         entrada e adesão
│   ├── member/           início, recompensas, extrato (abas)
│   └── operator/         membro, saúde, ferramentas (abas)
├── api/                  cliente HTTP, schemas Zod, erros humanos, QueryClient e chaves
├── session/              sessão (união discriminada), storage por plataforma, provider
├── entry/  join/         entrada, adesão e máscara de CPF
├── member/               carteirinha, Pix, competências, consultas do membro
├── rewards/              catálogo, resgate idempotente, recibo
├── statement/            extrato paginado e tradução das descrições do ledger
├── operator/             busca com estorno, saúde e reconciliação, jobs e relógio
├── money/                centavos branded e formatação BRL
├── ui/                   tokens, tema e componentes (Ticket, Button, Notice, Skeleton...)
└── test/                 servidor MSW, fake da API e helpers de render
```

## Decisões que valem a leitura

- **Direção visual "carteirinha e cupom":** papel creme, verde-garrafa para dinheiro e confiança, tangerina para os momentos de alegria (crédito que entra, resgate feito). A forma-assinatura é o `Ticket`, um cupom com canhoto picotado cujos furos são círculos na cor da página, então funciona nos dois temas. Tokens de cor, espaço, raio e tipografia em `src/ui/tokens.ts`, tema claro e escuro por `useColorScheme`.
- **Rotas protegidas declarativas:** três ramos no layout raiz (`member`, `operator`, `(public)`), cada um atrás de um `Stack.Protected`. Quando a sessão muda, o guard que vira `false` tira as telas do histórico e o router cai no primeiro ramo aberto. Não existe `useEffect` com `router.replace` nem `Redirect` espalhado; deep link para área errada cai na certa.
- **Uma camada de API, tipada de ponta a ponta:** `apiRequest` recebe o schema Zod da resposta e devolve o tipo parseado (`parse, don't validate`). Centavos são `Cents` (branded, inteiro), ids são `MembershipId`/`TransactionId` branded, datas chegam como `Date`. Erros viram `ApiError` com uma união discriminada: `problem` (status + `code` do ProblemDetails), `timeout`, `network`, `invalid-response`. `describeError` é o único lugar que traduz código em mensagem.
- **Timeout explícito:** um `AbortController` por requisição junta o cancelamento do TanStack Query e o timeout (10 s; 30 s para jobs), porque `AbortSignal.timeout/any` não é garantido no Hermes.
- **Sem retry em 4xx:** só timeout, rede e 5xx são repetidos (até 2 vezes). Mutations nunca são repetidas pelo cliente; a repetição do resgate é explícita e leva a mesma chave. Um 401 em qualquer consulta encerra a sessão e limpa o cache.
- **Idempotência visível:** cada resgate gera uma `Idempotency-Key` com `expo-crypto` e "Repetir a mesma requisição" reenvia a mesma. O recibo mostra a chave e o saldo depois de cada envio, que é a prova de que não houve cobrança dupla.
- **Chaves de query pelo que consultam:** tudo de uma adesão fica sob `['membership', id]`, então uma invalidação depois de resgate, Pix ou estorno atualiza saldo e extrato juntos, inclusive entre a tela do operador e a do membro.
- **Sessão no web:** no nativo vai para o SecureStore. No navegador vai para o `localStorage`, que qualquer script da página lê (um XSS vazaria o token). Aceito porque são tokens de dev de curta duração e a demonstração precisa sobreviver ao refresh. Em produção o certo é cookie `HttpOnly` emitido pela API, sem token no app. A escolha mora em `session-storage.ts` / `session-storage.native.ts`, sem `if (Platform.OS)` no meio da lógica.
- **Competência em São Paulo, à mão:** o Brasil não tem horário de verão desde 2019, então UTC-3 fixo dá o mesmo resultado no Hermes, no navegador e no Node, qualquer que seja a base de fusos de cada um. "Agora" vem do relógio de dev do backend, que pode ser adiantado.
- **Dinheiro:** um módulo (`src/money/money.ts`) com `Intl.NumberFormat('pt-BR', { currency: 'BRL' })`. A única divisão por 100 é dentro do formatador. Os testes rodam no Node; o Hermes do Expo Go também tem `Intl` com `pt-BR`, mas o formato no aparelho não foi conferido nesta entrega.
- **Formulários com TanStack Form + Zod:** adesão (CPF), entrada, busca de membro e data da reconciliação. O schema é a validação; os erros só aparecem depois de sair do campo ou tentar enviar, e são anunciados ao leitor de tela. A validade do CPF é decisão do servidor e o erro dele é mostrado como veio, em português. Não usei `@tanstack/react-pacer`: a busca é por id exato e só roda no envio, então não há digitação para segurar com debounce.
- **FlatList, não FlashList:** o extrato de uma demo tem dezenas de linhas, o FlatList já virtualiza e é uma dependência nativa a menos. Carrega a próxima página pelo `nextCursor` quando o fim se aproxima, com pull-to-refresh.
- **Lint em duas ferramentas, de propósito:** o Biome formata e cobre o grosso das regras (`noExplicitAny` e `noNonNullAssertion` como erro, imports organizados). Ele não tem as regras do React Compiler, então existe um ESLint mínimo, só com o preset do `eslint-plugin-react-hooks` 7 (rules-of-hooks, exhaustive-deps e as checagens do compilador), aplicado a `src/**`. As duas regras de hooks do Biome ficam desligadas para não reportar a mesma coisa duas vezes. Usei ESLint 10 e não 9 porque o 9 já sai marcado como sem suporte no npm.
- **Acessibilidade:** alvos de toque de 48 pt, papéis e rótulos em tudo que é tocável, resultados e erros em live regions, contraste AA nas cores de texto, fonte do sistema respeitada (só os números gigantes param de crescer em 1,6x) e "reduzir movimento" desliga contagem e pulsação.
- **Testes com a API de mentira, não com mocks do app:** o MSW intercepta o `fetch` real e um fake em memória (`src/test/fake-club.ts`) mantém as regras que importam (saldo, uma cobrança por chave). Só módulos de plataforma são substituídos (SecureStore, expo-crypto, worklets). O `jest-expo` instala o `expo/fetch` com módulo nativo de mentira, então `jest-native-fetch.js` guarda o `fetch` do Node antes e o `jest.setup.ts` o devolve. Os timers do TanStack Query são `unref` nos testes para o Jest sair sozinho.

## Limites conhecidos

- O layout de celular (abas embaixo) foi verificado por código e nos testes, mas o teste manual ponta a ponta foi feito no navegador do desktop; não rodei no Expo Go de um aparelho.
- O app depende das rotas `/dev/*` do backend, que só existem em Development.
- Sem modo offline: sem rede, as telas mostram o estado de erro com "Tentar de novo".
