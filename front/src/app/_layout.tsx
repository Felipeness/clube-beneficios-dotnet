import { BricolageGrotesque_700Bold, BricolageGrotesque_800ExtraBold } from '@expo-google-fonts/bricolage-grotesque';
import {
  Figtree_400Regular,
  Figtree_500Medium,
  Figtree_600SemiBold,
  Figtree_700Bold,
} from '@expo-google-fonts/figtree';
import { JetBrainsMono_500Medium } from '@expo-google-fonts/jetbrains-mono';
import { focusManager, QueryClientProvider } from '@tanstack/react-query';
import { useFonts } from 'expo-font';
import { DarkTheme, DefaultTheme, Stack, ThemeProvider } from 'expo-router';
import * as SplashScreen from 'expo-splash-screen';
import { StatusBar } from 'expo-status-bar';
import { useEffect, useState } from 'react';
import { AppState, Platform, useColorScheme } from 'react-native';
import { SafeAreaProvider } from 'react-native-safe-area-context';
import { createQueryClient } from '@/api/query-client';
import { SessionProvider, useSession } from '@/session/session-provider';
import { useColors } from '@/ui/theme';

void SplashScreen.preventAutoHideAsync();

// On native, "window focus" is the app coming back to the foreground: stale balances refetch then (web has it built in).
if (Platform.OS !== 'web') {
  AppState.addEventListener('change', (status) => focusManager.setFocused(status === 'active'));
}

export default function RootLayout() {
  const [queryClient] = useState(createQueryClient);
  const [areFontsLoaded, fontError] = useFonts({
    BricolageGrotesque_700Bold,
    BricolageGrotesque_800ExtraBold,
    Figtree_400Regular,
    Figtree_500Medium,
    Figtree_600SemiBold,
    Figtree_700Bold,
    JetBrainsMono_500Medium,
  });

  return (
    <QueryClientProvider client={queryClient}>
      <SessionProvider>
        <SafeAreaProvider>
          <RootNavigator areFontsSettled={areFontsLoaded || fontError !== null} />
        </SafeAreaProvider>
      </SessionProvider>
    </QueryClientProvider>
  );
}

// Each audience is a protected branch: a guard turning false drops its screens from history and the router falls back
// to the first branch still open, so sign-in, sign-out and deep links need no manual redirect.
function RootNavigator({ areFontsSettled }: { readonly areFontsSettled: boolean }) {
  const { state } = useSession();
  const colors = useColors();
  const isDark = useColorScheme() === 'dark';
  const isReady = areFontsSettled && state.status !== 'loading';

  useEffect(() => {
    if (isReady) SplashScreen.hide();
  }, [isReady]);

  if (!isReady) return null;

  const role = state.status === 'signedIn' ? state.session.role : null;
  const base = isDark ? DarkTheme : DefaultTheme;
  const theme = { ...base, colors: { ...base.colors, background: colors.background, card: colors.surface } };

  return (
    <ThemeProvider value={theme}>
      <StatusBar style={isDark ? 'light' : 'dark'} />
      <Stack screenOptions={{ headerShown: false, contentStyle: { backgroundColor: colors.background } }}>
        <Stack.Protected guard={role === 'member'}>
          <Stack.Screen name="member" />
        </Stack.Protected>
        <Stack.Protected guard={role === 'operator'}>
          <Stack.Screen name="operator" />
        </Stack.Protected>
        <Stack.Protected guard={role === null}>
          <Stack.Screen name="(public)" />
        </Stack.Protected>
      </Stack>
    </ThemeProvider>
  );
}
