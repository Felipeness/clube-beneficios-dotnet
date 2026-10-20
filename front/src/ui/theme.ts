import { useColorScheme } from 'react-native';
import { type ColorTokens, colorSchemes } from './tokens';

export function useColors(): ColorTokens {
  return useColorScheme() === 'dark' ? colorSchemes.dark : colorSchemes.light;
}
