import * as SecureStore from 'expo-secure-store';
import * as LocalAuthentication from 'expo-local-authentication';

const TOKEN_KEY = 'stoictrade_jwt_token';
const PIN_KEY = 'stoictrade_saved_pin';

type AuthStateListener = (isAuthenticated: boolean) => void;
const listeners = new Set<AuthStateListener>();

export function onAuthStateChanged(listener: AuthStateListener): () => void {
  listeners.add(listener);
  return () => {
    listeners.delete(listener);
  };
}

export function notifyAuthState(isAuthenticated: boolean): void {
  listeners.forEach((listener) => {
    try {
      listener(isAuthenticated);
    } catch (e) {
      console.error('Error in auth state listener:', e);
    }
  });
}

export async function saveAuthToken(token: string): Promise<void> {
  try {
    await SecureStore.setItemAsync(TOKEN_KEY, token);
    notifyAuthState(true);
  } catch (err) {
    console.error('Failed to save auth token:', err);
  }
}

export async function getAuthToken(): Promise<string | null> {
  try {
    return await SecureStore.getItemAsync(TOKEN_KEY);
  } catch (err) {
    console.error('Failed to get auth token:', err);
    return null;
  }
}

export async function clearAuthToken(): Promise<void> {
  try {
    await SecureStore.deleteItemAsync(TOKEN_KEY);
    notifyAuthState(false);
  } catch (err) {
    console.error('Failed to clear auth token:', err);
    notifyAuthState(false);
  }
}

export async function authenticateWithBiometrics(promptMessage = 'Authenticate to unlock StoicTrade'): Promise<boolean> {
  try {
    const hasHardware = await LocalAuthentication.hasHardwareAsync();
    if (!hasHardware) return false;

    const isEnrolled = await LocalAuthentication.isEnrolledAsync();
    if (!isEnrolled) return false;

    const result = await LocalAuthentication.authenticateAsync({
      promptMessage,
      fallbackLabel: 'Enter PIN',
      disableDeviceFallback: false,
    });

    return result.success;
  } catch (err) {
    console.error('Biometric authentication error:', err);
    return false;
  }
}
