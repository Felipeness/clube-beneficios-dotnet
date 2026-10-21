import { useOperatorSession } from '@/session/session-provider';
import { AppTabs, type TabSpec } from '@/ui/app-tabs';

const operatorTabs: readonly TabSpec[] = [
  { name: 'index', href: '/operator', label: 'Membro', icon: 'account-search-outline' },
  { name: 'health', href: '/operator/health', label: 'Saúde', icon: 'heart-pulse' },
  { name: 'tools', href: '/operator/tools', label: 'Ferramentas', icon: 'wrench-outline' },
];

export default function OperatorLayout() {
  const session = useOperatorSession();
  return <AppTabs tabs={operatorTabs} audience={`Balcão · ${session.operatorName}`} />;
}
