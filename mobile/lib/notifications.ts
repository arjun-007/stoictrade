import * as Notifications from 'expo-notifications';
import { Platform } from 'react-native';

Notifications.setNotificationHandler({
  handleNotification: async () => ({
    shouldShowAlert: true,
    shouldPlaySound: true,
    shouldSetBadge: true,
    shouldShowBanner: true,
    shouldShowList: true,
  }),
});

export async function setupNotificationChannels(): Promise<void> {
  if (Platform.OS === 'android') {
    await Notifications.setNotificationChannelAsync('squad-approvals', {
      name: 'Squad & Strategy Approvals',
      importance: Notifications.AndroidImportance.MAX,
      vibrationPattern: [0, 250, 250, 250],
      lightColor: '#6366f1',
      sound: 'default',
      enableLights: true,
      enableVibrate: true,
    });

    await Notifications.setNotificationChannelAsync('emergency-alerts', {
      name: 'Risk & Emergency Alerts',
      importance: Notifications.AndroidImportance.HIGH,
      vibrationPattern: [0, 500, 250, 500],
      lightColor: '#f43f5e',
      sound: 'default',
      enableLights: true,
      enableVibrate: true,
    });
  }
}

export async function registerForPushNotificationsAsync(): Promise<string | null> {
  let token: string | null = null;
  
  const { status: existingStatus } = await Notifications.getPermissionsAsync();
  let finalStatus = existingStatus;
  
  if (existingStatus !== 'granted') {
    const { status } = await Notifications.requestPermissionsAsync();
    finalStatus = status;
  }
  
  if (finalStatus !== 'granted') {
    console.warn('Failed to get push notification permission');
    return null;
  }
  
  try {
    const pushTokenData = await Notifications.getExpoPushTokenAsync();
    token = pushTokenData.data;
    console.log('Expo Push Token:', token);
  } catch (e) {
    console.warn('Could not fetch push token:', e);
  }
  
  await setupNotificationChannels();
  return token;
}

export async function triggerLocalSignalAlert(strategyName: string, action: string, instrument: string, price: number): Promise<void> {
  await Notifications.scheduleNotificationAsync({
    content: {
      title: `🚨 ${strategyName} Signal`,
      body: `${action} ${instrument} @ ₹${price.toFixed(2)} — Tap to review and approve!`,
      data: { screen: 'analysis' },
      sound: 'default',
    },
    trigger: null, // trigger immediately
  });
}
