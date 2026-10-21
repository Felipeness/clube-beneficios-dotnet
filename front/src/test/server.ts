import { setupServer } from 'msw/node';

// One MSW server for the whole suite; each test adds the handlers it needs with server.use.
export const server = setupServer();
