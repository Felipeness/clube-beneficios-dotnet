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

beforeAll(() => server.listen({ onUnhandledFrame: 'error' }));
afterEach(() => server.resetHandlers());
afterAll(() => server.close());
