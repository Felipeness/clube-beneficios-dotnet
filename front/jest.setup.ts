import { timeoutManager } from '@tanstack/react-query';
import * as SecureStore from 'expo-secure-store';
import { server } from '@/test/server';

type NodeFetch = Pick<typeof globalThis, 'fetch' | 'Headers' | 'Request' | 'Response'>;

function isNodeFetch(value: unknown): value is NodeFetch {
  return typeof value === 'object' && value !== null && 'fetch' in value && typeof value.fetch === 'function';
}

// See jest-native-fetch.js: undo jest-expo's expo/fetch stub so requests reach MSW.
const saved: unknown = Reflect.get(globalThis, '__nodeFetch');
if (!isNodeFetch(saved)) throw new Error('jest-native-fetch.js did not run before the jest-expo setup');
for (const [name, value] of Object.entries(saved)) {
  Object.defineProperty(globalThis, name, { value, writable: true, configurable: true });
}

// Platform modules the app does not own: SecureStore becomes an in-memory keychain and expo-crypto uses Node's CSPRNG,
// so the real session and redemption code runs unchanged on top of them.
jest.mock('expo-secure-store', () => {
  const store = new Map<string, string>();
  return {
    getItemAsync: async (key: string) => store.get(key) ?? null,
    setItemAsync: async (key: string, value: string) => {
      store.set(key, value);
    },
    deleteItemAsync: async (key: string) => {
      store.delete(key);
    },
  };
});
jest.mock('expo-crypto', () => ({ randomUUID: () => globalThis.crypto.randomUUID() }));
// Worklets need the native UI runtime; its official mock runs them in JS, and Reanimated works on top of it.
jest.mock('react-native-worklets', () => jest.requireActual('react-native-worklets/src/mock'));
jest.mock('react-native-reanimated', () => require('@/test/reduced-motion-reanimated').reanimatedWithReducedMotion());
// expo-router/testing-library replaces Reanimated with its bundled mock, which lacks useReducedMotion: route that mock
// to the same reduced-motion module so route tests and screen tests render alike.
jest.mock('react-native-reanimated/mock', () =>
  require('@/test/reduced-motion-reanimated').reanimatedWithReducedMotion(),
);

// TanStack Query keeps cache entries alive with timers (gcTime, 5 min by default). Unref'd, they no longer hold the Jest
// process open after the last test, without changing how any query behaves.
function hasUnref(handle: unknown): handle is { unref: () => void } {
  return typeof handle === 'object' && handle !== null && 'unref' in handle && typeof handle.unref === 'function';
}
const unrefTimer = (timer: number): number => {
  const handle: unknown = timer;
  if (hasUnref(handle)) handle.unref();
  return timer;
};
timeoutManager.setTimeoutProvider({
  setTimeout: (callback, delay) => unrefTimer(setTimeout(callback, delay)),
  clearTimeout: (timer) => clearTimeout(timer),
  setInterval: (callback, delay) => unrefTimer(setInterval(callback, delay)),
  clearInterval: (timer) => clearInterval(timer),
});

beforeAll(() => server.listen({ onUnhandledFrame: 'error' }));
afterEach(async () => {
  server.resetHandlers();
  await SecureStore.deleteItemAsync('clube.session');
});
afterAll(() => server.close());
