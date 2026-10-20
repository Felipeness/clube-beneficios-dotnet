import { ApiError, type ApiFailure, apiBaseUrl } from './http';

export type HumanError = { readonly title: string; readonly detail: string };

// The API speaks in stable codes; this is the one place that turns them into something a member can act on.
const byCode: Readonly<Record<string, HumanError>> = {
  'membership.invalid_cpf': {
    title: 'CPF inválido',
    detail: 'Os dígitos verificadores não conferem. Confira os 11 números e tente de novo.',
  },
  'membership.already_subscribed': {
    title: 'Esse CPF já é membro',
    detail: 'Cada CPF tem uma adesão só. Entre com o código da adesão que já existe.',
  },
  'membership.not_found': {
    title: 'Adesão não encontrada',
    detail: 'Nenhuma adesão tem esse código. Confira se copiou o código inteiro.',
  },
  'membership.insufficient_balance': {
    title: 'Saldo insuficiente',
    detail: 'Seu saldo não cobre essa recompensa. Pague o Pix do mês para receber mais créditos.',
  },
  'ledger.idempotency_key_reused': {
    title: 'Essa chave já foi usada em outro pedido',
    detail: 'Uma Idempotency-Key vale para um único resgate. Para outro resgate, comece um pedido novo.',
  },
  'ledger.invalid_idempotency_key': {
    title: 'Chave de idempotência inválida',
    detail: 'O servidor recusou a Idempotency-Key enviada. Comece um pedido novo.',
  },
  'membership.reward_not_found': {
    title: 'Recompensa indisponível',
    detail: 'Essa recompensa saiu do catálogo. Atualize a lista.',
  },
  'membership.concurrent_update': {
    title: 'A conta mudou durante o pedido',
    detail: 'Outro lançamento chegou ao mesmo tempo. Tente de novo.',
  },
  'membership.future_period': {
    title: 'Competência no futuro',
    detail: 'Só dá para pagar o mês corrente ou meses anteriores.',
  },
  'membership.period_before_subscription': {
    title: 'Antes da adesão',
    detail: 'Essa competência é anterior ao mês em que a adesão começou.',
  },
  'membership.allowance_amount_mismatch': {
    title: 'Valor diferente da mensalidade',
    detail: 'O Pix precisa ter exatamente o valor da mensalidade.',
  },
  'membership.transaction_not_found': {
    title: 'Lançamento não encontrado',
    detail: 'Esse lançamento não pertence a essa adesão.',
  },
  'ledger.reversal_of_reversal': {
    title: 'Estorno não pode ser estornado',
    detail: 'Para desfazer um estorno, lance a operação original de novo.',
  },
  'ledger.reversal_forbidden': {
    title: 'Estorno não permitido',
    detail: 'Só operadores podem estornar lançamentos.',
  },
};

const byStatus: Readonly<Record<number, HumanError>> = {
  401: { title: 'Sessão expirada', detail: 'Entre de novo para continuar.' },
  403: { title: 'Sem permissão', detail: 'Seu perfil não pode fazer isso.' },
  404: { title: 'Não encontrado', detail: 'O servidor não encontrou o que foi pedido.' },
};

const serverFailure: HumanError = {
  title: 'O servidor falhou',
  detail: 'Algo deu errado do lado da API. Tente de novo em instantes.',
};

export function describeError(error: unknown): HumanError {
  if (!(error instanceof ApiError)) return { title: 'Algo deu errado', detail: 'Tente de novo.' };
  return describeFailure(error.failure);
}

function describeFailure(failure: ApiFailure): HumanError {
  switch (failure.kind) {
    case 'problem':
      return describeProblem(failure);
    case 'timeout':
      return {
        title: 'A API demorou demais',
        detail: `Sem resposta em ${failure.timeoutMs / 1000} s. Confira se o backend está rodando.`,
      };
    case 'network':
      return {
        title: 'Sem conexão com a API',
        detail: `Não deu para falar com ${apiBaseUrl}. No celular, use o IP da rede do PC em EXPO_PUBLIC_API_URL.`,
      };
    case 'invalid-response':
      return {
        title: 'Resposta inesperada da API',
        detail: 'O servidor respondeu num formato que o app não conhece. As versões podem estar desalinhadas.',
      };
  }
}

function describeProblem(failure: Extract<ApiFailure, { kind: 'problem' }>): HumanError {
  const known = (failure.code ? byCode[failure.code] : undefined) ?? byStatus[failure.status];
  if (known) return known;
  if (failure.status >= 500) return serverFailure;
  return { title: 'Pedido recusado', detail: failure.title ?? `HTTP ${failure.status}` };
}

export function errorCodeOf(error: unknown): string | null {
  return error instanceof ApiError && error.failure.kind === 'problem' ? error.failure.code : null;
}
