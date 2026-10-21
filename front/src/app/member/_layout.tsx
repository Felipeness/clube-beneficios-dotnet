import { AppTabs, type TabSpec } from '@/ui/app-tabs';

const memberTabs: readonly TabSpec[] = [
  { name: 'index', href: '/member', label: 'Início', icon: 'home-variant-outline' },
  { name: 'rewards', href: '/member/rewards', label: 'Recompensas', icon: 'gift-outline' },
  { name: 'statement', href: '/member/statement', label: 'Extrato', icon: 'receipt-text-outline' },
];

export default function MemberLayout() {
  return <AppTabs tabs={memberTabs} audience="Membro" />;
}
