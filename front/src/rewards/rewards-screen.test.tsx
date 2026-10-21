import { fireEvent, screen, waitFor } from '@testing-library/react-native';
import { fakeClub, MEMBERSHIP_ID } from '@/test/fake-club';
import { memberSession, renderSignedIn } from '@/test/render';
import { server } from '@/test/server';
import { RewardsScreen } from './rewards-screen';

const plain = (text: string) => text.replace(/ /g, ' ');

async function openCatalog(balanceCents: number) {
  const club = fakeClub(balanceCents);
  server.use(...club.handlers);
  await renderSignedIn(<RewardsScreen />, memberSession(MEMBERSHIP_ID));
  await screen.findByRole('button', { name: 'Resgatar Ingresso de cinema' });
  return club;
}

const balanceShown = () => plain(screen.getByText(/^Seu saldo:/).props.children);

describe('RewardsScreen', () => {
  it('redeems a reward and refreshes the balance from the server', async () => {
    const club = await openCatalog(2000);
    await waitFor(() => expect(balanceShown()).toBe('Seu saldo: R$ 20,00'));
    const readsBefore = club.membershipReads();

    await fireEvent.press(screen.getByRole('button', { name: 'Resgatar Ingresso de cinema' }));

    expect(await screen.findByText('Resgatado!')).toBeOnTheScreen();
    await waitFor(() => expect(balanceShown()).toBe('Seu saldo: R$ 10,00'));
    expect(club.membershipReads()).toBeGreaterThan(readsBefore);
  });

  it('explains an insufficient balance in plain words', async () => {
    await openCatalog(500);

    await fireEvent.press(screen.getByRole('button', { name: 'Resgatar Ingresso de cinema' }));

    expect(await screen.findByText('Saldo insuficiente')).toBeOnTheScreen();
    expect(screen.getByText(/Pague o Pix do mês/)).toBeOnTheScreen();
  });

  it('resends the same Idempotency-Key and is charged only once', async () => {
    const club = await openCatalog(2000);

    await fireEvent.press(screen.getByRole('button', { name: 'Resgatar Ingresso de cinema' }));
    await screen.findByText('Resgatado!');
    await fireEvent.press(screen.getByRole('button', { name: 'Repetir a mesma requisição' }));

    expect(await screen.findByText('Mesma chave, mesmo resultado')).toBeOnTheScreen();
    expect(club.redemptionKeys).toHaveLength(2);
    expect(club.redemptionKeys[0]).toMatch(/^[0-9a-f-]{36}$/);
    expect(club.redemptionKeys[1]).toBe(club.redemptionKeys[0]);
    expect(club.balance()).toBe(1000);
  });

  it('starts a new key for a new redemption', async () => {
    const club = await openCatalog(5000);

    await fireEvent.press(screen.getByRole('button', { name: 'Resgatar Ingresso de cinema' }));
    await screen.findByText('Resgatado!');
    await fireEvent.press(screen.getByRole('button', { name: 'Resgatar Ingresso de cinema' }));
    await waitFor(() => expect(club.redemptionKeys).toHaveLength(2));

    expect(club.redemptionKeys[1]).not.toBe(club.redemptionKeys[0]);
    await waitFor(() => expect(club.balance()).toBe(3000));
  });
});
