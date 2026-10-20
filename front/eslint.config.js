// Biome lints and formats everything else. This file exists only for the React rules Biome lacks: the official
// react-hooks plugin carries the React Compiler checks (purity, refs in render, setState in effects) plus rules-of-hooks
// and exhaustive-deps, and moves in step with the compiler.
const { defineConfig, globalIgnores } = require('eslint/config');
const reactHooks = require('eslint-plugin-react-hooks');
const tseslint = require('typescript-eslint');

module.exports = defineConfig([
  globalIgnores(['.expo/**', 'dist/**']),
  {
    files: ['src/**/*.{ts,tsx}'],
    languageOptions: { parser: tseslint.parser },
    extends: [reactHooks.configs.flat.recommended],
  },
]);
