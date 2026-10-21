// Tests run as a "reduce motion" user: endless loops (skeleton pulse) never start and counters jump to their value,
// which is the same path the app takes for that accessibility setting. Used as a jest.mock factory.
export function reanimatedWithReducedMotion() {
  const actual = jest.requireActual<typeof import('react-native-reanimated')>('react-native-reanimated');
  return { __esModule: true, ...actual, default: actual.default, useReducedMotion: () => true };
}
