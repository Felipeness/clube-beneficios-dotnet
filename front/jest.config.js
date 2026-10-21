const expoPreset = require('jest-expo/jest-preset');

const [, babelOptions] = expoPreset.transform['\\.[jt]sx?$'];
// babel-jest is a dependency of jest-expo, not of the app; pnpm keeps it out of the top-level node_modules.
const babelJest = require.resolve('babel-jest', { paths: [require.resolve('jest-expo')] });

// The preset is spread instead of referenced so jest-native-fetch.js can run BEFORE jest-expo's setup: that setup
// installs expo/fetch as the global fetch, backed by a stub native module that never answers in Jest.
/** @type {import('jest').Config} */
module.exports = {
  ...expoPreset,
  setupFiles: ['<rootDir>/jest-native-fetch.js', ...expoPreset.setupFiles],
  setupFilesAfterEnv: ['<rootDir>/jest.setup.ts'],
  // MSW 3 depends on packages published only as .mjs; run them through the same Babel transform as the app code.
  transform: { ...expoPreset.transform, '\\.mjs$': [babelJest, babelOptions] },
  // jest-expo's list plus MSW and its ESM-only dependencies. Under pnpm a package sits at .pnpm/<id>/node_modules/<name>,
  // and the inner node_modules segment is the one that decides.
  transformIgnorePatterns: [
    '/node_modules/(?!(\\.pnpm|react-native|@react-native|@react-native-community|expo|@expo|@expo-google-fonts|react-navigation|@react-navigation|msw|@msw|@mswjs|rettime|until-async|@open-draft|outvariant|strict-event-emitter|headers-polyfill|is-node-process|@tanstack|cookie|tough-cookie|tldts|type-fest|graphql|statuses|picocolors))',
    '/node_modules/react-native-reanimated/plugin/',
  ],
};
