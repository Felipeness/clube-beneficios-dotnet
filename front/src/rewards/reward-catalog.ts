import type { IconName } from '@/ui/button';

type RewardPresentation = { readonly name: string; readonly icon: IconName; readonly blurb: string };

// The API owns ids and prices; the app owns how a reward looks and reads in Portuguese. An id the app does not know
// still renders, with the server's name and a generic icon.
const presentations: Readonly<Record<string, RewardPresentation>> = {
  'cinema-ticket': { name: 'Ingresso de cinema', icon: 'movie-open-outline', blurb: 'Uma sessão 2D em qualquer dia.' },
  'perfume-kit': { name: 'Kit de perfume', icon: 'flower-outline', blurb: 'Três miniaturas para testar.' },
  'thermal-bottle': { name: 'Garrafa térmica', icon: 'bottle-tonic-outline', blurb: 'Inox, 500 ml, gelado por 24 h.' },
};

export function presentReward(id: string, serverName: string): RewardPresentation {
  return presentations[id] ?? { name: serverName, icon: 'gift-outline', blurb: '' };
}
