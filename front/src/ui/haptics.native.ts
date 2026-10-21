import * as Haptics from 'expo-haptics';

// Reserved for confirmations (a credit landed, a reward was redeemed), never for every tap.
export function confirmWithHaptics(outcome: 'success' | 'warning' | 'error'): void {
  const type = {
    success: Haptics.NotificationFeedbackType.Success,
    warning: Haptics.NotificationFeedbackType.Warning,
    error: Haptics.NotificationFeedbackType.Error,
  }[outcome];
  Haptics.notificationAsync(type).catch((error: unknown) => console.warn('haptics: unavailable', error));
}
