// Runs before jest-expo's setup. Keeps Node's WHATWG fetch (the same contract expo/fetch implements on device) so the
// app's HTTP client really goes over fetch in tests and MSW intercepts it; jest.setup.ts puts it back afterwards.
globalThis.__nodeFetch = {
  fetch: globalThis.fetch,
  Headers: globalThis.Headers,
  Request: globalThis.Request,
  Response: globalThis.Response,
};
