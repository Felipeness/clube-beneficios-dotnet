import * as SecureStore from 'expo-secure-store';

// Keychain on iOS, Keystore-backed storage on Android. The async API keeps the JS thread free.
const KEY = 'clube.session';

export const readStoredSession = (): Promise<string | null> => SecureStore.getItemAsync(KEY);

export const writeStoredSession = (value: string): Promise<void> => SecureStore.setItemAsync(KEY, value);

export const clearStoredSession = (): Promise<void> => SecureStore.deleteItemAsync(KEY);
