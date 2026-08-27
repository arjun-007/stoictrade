import React, { useState, useEffect } from 'react';
import { StyleSheet, View, Text, TouchableOpacity, SafeAreaView, StatusBar, Alert, Linking } from 'react-native';
import {
  LayoutDashboard,
  Eye,
  Layers,
  Briefcase,
  Settings,
} from 'lucide-react-native';
import { COLORS } from './lib/theme';
import { apiClient } from './lib/api';
import { Header } from './components/Header';
import { DashboardScreen } from './screens/DashboardScreen';
import { WatchlistScreen } from './screens/WatchlistScreen';
import { AnalysisScreen } from './screens/AnalysisScreen';
import { PositionsScreen } from './screens/PositionsScreen';
import { SettingsScreen } from './screens/SettingsScreen';
import { LoginScreen } from './screens/LoginScreen';
import { setupNotificationChannels, registerForPushNotificationsAsync } from './lib/notifications';
import { getAuthToken, clearAuthToken } from './lib/auth';

type TabName = 'dashboard' | 'watchlist' | 'analysis' | 'positions' | 'settings';

export default function App() {
  const [isAuthenticated, setIsAuthenticated] = useState<boolean | null>(null);
  const [activeTab, setActiveTab] = useState<TabName>('dashboard');
  const [spotData, setSpotData] = useState({ price: 24250, change: 0 });
  const [isEngineRunning, setIsEngineRunning] = useState(false);

  useEffect(() => {
    setupNotificationChannels();
    registerForPushNotificationsAsync();

    const checkAuth = async () => {
      const token = await getAuthToken();
      setIsAuthenticated(!!token);
    };
    checkAuth();

    const fetchEngineStatus = async () => {
      try {
        const [spotRes, statusRes] = await Promise.allSettled([
          apiClient.get('/api/marketdata/spot?symbol=NIFTY'),
          apiClient.get('/api/engine/status'),
        ]);

        if (spotRes.status === 'fulfilled' && spotRes.value.data?.price) {
          setSpotData({
            price: spotRes.value.data.price,
            change: spotRes.value.data.change || 0,
          });
        }
        if (statusRes.status === 'fulfilled') {
          setIsEngineRunning(statusRes.value.data.isRunning || statusRes.value.data.IsRunning);
        }
      } catch (err) {
        console.error('Error fetching header status:', err);
      }
    };

    fetchEngineStatus();
    const interval = setInterval(fetchEngineStatus, 3000);
    return () => clearInterval(interval);
  }, []);

  const handleToggleEngine = async () => {
    const endpoint = isEngineRunning ? 'stop' : 'start';
    try {
      const res = await apiClient.post(`/api/engine/${endpoint}`);
      const redirectUrl = res.data.authUrl || res.data.AuthUrl;
      if (redirectUrl) {
        Alert.alert(
          'Fyers Login Required',
          'Opening Fyers authentication in your browser to connect live market feeds.',
          [
            { text: 'Cancel', style: 'cancel' },
            {
              text: 'Open Fyers Login',
              onPress: () => Linking.openURL(redirectUrl),
            },
          ]
        );
      } else {
        setIsEngineRunning(!isEngineRunning);
      }
    } catch (err: any) {
      Alert.alert('Error', err.response?.data?.error || 'Failed to toggle engine');
    }
  };

  const handleEmergencySquareOff = () => {
    Alert.alert(
      '⚠ EMERGENCY SQUARE OFF ALL',
      'Are you sure you want to close ALL open paper and live positions immediately at market price?',
      [
        { text: 'Cancel', style: 'cancel' },
        {
          text: 'SQUARE OFF NOW',
          style: 'destructive',
          onPress: async () => {
            try {
              const res = await apiClient.post('/api/engine/squareoff');
              Alert.alert('Square-Off Completed', res.data.message || res.data.Message || 'All positions scheduled for exit.');
            } catch (err: any) {
              Alert.alert('Error', err.response?.data?.error || 'Failed to square off');
            }
          },
        },
      ]
    );
  };

  const renderActiveScreen = () => {
    switch (activeTab) {
      case 'dashboard':
        return <DashboardScreen onNavigateToTab={(t) => setActiveTab(t as TabName)} />;
      case 'watchlist':
        return <WatchlistScreen />;
      case 'analysis':
        return <AnalysisScreen />;
      case 'positions':
        return <PositionsScreen />;
      case 'settings':
        return <SettingsScreen />;
    }
  };

  if (isAuthenticated === false) {
    return <LoginScreen onLoginSuccess={() => setIsAuthenticated(true)} />;
  }

  return (
    <SafeAreaView style={styles.safeArea}>
      <StatusBar barStyle="light-content" backgroundColor={COLORS.surface} />

      {/* Global Mobile Header */}
      <Header
        spotPrice={spotData.price}
        spotChange={spotData.change}
        isEngineRunning={isEngineRunning}
        onToggleEngine={handleToggleEngine}
        onEmergencyPress={handleEmergencySquareOff}
      />

      {/* Screen Content */}
      <View style={styles.content}>
        {renderActiveScreen()}
      </View>

      {/* Bottom Navigation Bar */}
      <View style={styles.bottomNav}>
        <TouchableOpacity
          style={styles.navTab}
          onPress={() => setActiveTab('dashboard')}
          activeOpacity={0.7}
        >
          <LayoutDashboard size={20} color={activeTab === 'dashboard' ? COLORS.primary : COLORS.textMuted} />
          <Text style={[styles.navLabel, activeTab === 'dashboard' && styles.navLabelActive]}>Dashboard</Text>
        </TouchableOpacity>

        <TouchableOpacity
          style={styles.navTab}
          onPress={() => setActiveTab('watchlist')}
          activeOpacity={0.7}
        >
          <Eye size={20} color={activeTab === 'watchlist' ? COLORS.primary : COLORS.textMuted} />
          <Text style={[styles.navLabel, activeTab === 'watchlist' && styles.navLabelActive]}>Watchlist</Text>
        </TouchableOpacity>

        <TouchableOpacity
          style={styles.navTab}
          onPress={() => setActiveTab('analysis')}
          activeOpacity={0.7}
        >
          <Layers size={20} color={activeTab === 'analysis' ? COLORS.primary : COLORS.textMuted} />
          <Text style={[styles.navLabel, activeTab === 'analysis' && styles.navLabelActive]}>Squads</Text>
        </TouchableOpacity>

        <TouchableOpacity
          style={styles.navTab}
          onPress={() => setActiveTab('positions')}
          activeOpacity={0.7}
        >
          <Briefcase size={20} color={activeTab === 'positions' ? COLORS.primary : COLORS.textMuted} />
          <Text style={[styles.navLabel, activeTab === 'positions' && styles.navLabelActive]}>Positions</Text>
        </TouchableOpacity>

        <TouchableOpacity
          style={styles.navTab}
          onPress={() => setActiveTab('settings')}
          activeOpacity={0.7}
        >
          <Settings size={20} color={activeTab === 'settings' ? COLORS.primary : COLORS.textMuted} />
          <Text style={[styles.navLabel, activeTab === 'settings' && styles.navLabelActive]}>Settings</Text>
        </TouchableOpacity>
      </View>
    </SafeAreaView>
  );
}

const styles = StyleSheet.create({
  safeArea: {
    flex: 1,
    backgroundColor: COLORS.bg,
  },
  content: {
    flex: 1,
  },
  bottomNav: {
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'space-around',
    backgroundColor: COLORS.surface,
    paddingVertical: 10,
    paddingBottom: 14,
    borderTopWidth: 1,
    borderTopColor: COLORS.surfaceBorder,
  },
  navTab: {
    alignItems: 'center',
    justifyContent: 'center',
    gap: 4,
    flex: 1,
  },
  navLabel: {
    fontSize: 10,
    fontWeight: '700',
    color: COLORS.textMuted,
  },
  navLabelActive: {
    color: COLORS.primary,
  },
});
