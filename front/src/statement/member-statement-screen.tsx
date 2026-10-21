import { useMemberSession } from '@/session/session-provider';
import { ScreenHeader } from '@/ui/screen';
import { StatementList } from './statement-list';

export function MemberStatementScreen() {
  const session = useMemberSession();
  return (
    <StatementList
      membershipId={session.membershipId}
      token={session.token}
      perspective="member"
      header={
        <ScreenHeader
          eyebrow="Extrato"
          title="Cada centavo, no papel"
          subtitle="Entradas em verde, saídas em vermelho. Puxe para atualizar."
        />
      }
    />
  );
}
