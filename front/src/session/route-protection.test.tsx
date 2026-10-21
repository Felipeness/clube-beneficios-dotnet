import { fireEvent, renderRouter, screen } from 'expo-router/testing-library';
import { HttpResponse, http } from 'msw';
import { apiBaseUrl } from '@/api/http';
import { fakeClub, MEMBERSHIP_ID } from '@/test/fake-club';
import { memberSession, operatorSession, storeSession } from '@/test/render';
import { server } from '@/test/server';

// Renders the real route tree (src/app) with the real root layout, so the guards under test are Stack.Protected's.
async function openApp(initialUrl: string) {
  server.use(
    ...fakeClub(2000).handlers,
    http.get(`${apiBaseUrl}/dev/clock`, () => HttpResponse.json({ now: '2026-10-20T15:00:00+00:00', offsetDays: 0 })),
  );
  const router = renderRouter('./src/app', { initialUrl });
  // renderRouter switches to fake timers; the session load and MSW need the real ones.
  jest.useRealTimers();
  await router;
  return { pathname: () => router.getPathname() };
}

describe('route protection', () => {
  it('sends a visitor without a session to the entrance', async () => {
    const app = await openApp('/member/rewards');

    expect(await screen.findByRole('radio', { name: /^Sou membro/ })).toBeOnTheScreen();
    expect(app.pathname()).toBe('/');
  });

  it('keeps a member out of the operator area', async () => {
    await storeSession(memberSession(MEMBERSHIP_ID));

    const app = await openApp('/operator/tools');

    expect(await screen.findByText('Olá, membro')).toBeOnTheScreen();
    expect(screen.queryByText('Bastidores do Clube')).not.toBeOnTheScreen();
    expect(app.pathname()).toBe('/member');
  });

  it('keeps an operator out of the member area', async () => {
    await storeSession(operatorSession('ana'));

    const app = await openApp('/member/rewards');

    expect(await screen.findByText('Atender um membro')).toBeOnTheScreen();
    expect(app.pathname()).toBe('/operator');
  });

  it('returns to the entrance after signing out', async () => {
    await storeSession(memberSession(MEMBERSHIP_ID));
    const app = await openApp('/member');
    await screen.findByText('Olá, membro');

    await fireEvent.press(screen.getByRole('button', { name: 'Sair' }));

    expect(await screen.findByRole('radio', { name: /^Sou membro/ })).toBeOnTheScreen();
    expect(app.pathname()).toBe('/');
  });
});
