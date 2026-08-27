import React, { useState, useEffect, useCallback } from 'react';
import {
  View,
  Text,
  StyleSheet,
  ScrollView,
  RefreshControl,
  TouchableOpacity,
  Alert,
} from 'react-native';
import {
  TrendingUp,
  DollarSign,
  Briefcase,
  ShieldAlert,
  Key,
  AlertTriangle,
  Play,
  Square,
} from 'lucide-react-native';
import * as Haptics from 'expo-haptics';
import { COLORS } from '../lib/theme';
import { apiClient } from '../lib/api';
import { MetricCard } from '../components/MetricCard';
import { ApprovalQueueCard, PendingSignal } from '../components/ApprovalQueueCard';
import { TotpModal } from '../components/TotpModal';

interface DashboardScreenProps {
  onNavigateToTab?: (tab: string) => void;
}

export const DashboardScreen: React.FC<DashboardScreenProps> = ({ onNavigateToTab }) => {
  const [summary, setSummary] = useState({
    dailyPnL: 0,
    availableMargin: 100000,
    activePositionsCount: 0,
  });
  const [isEngineRunning, setIsEngineRunning] = useState(false);
  const [isLocked, setIsLocked] = useState(false);
  const [pendingApprovals, setPendingApprovals] = useState<PendingSignal[]>([]);
  const [refreshing, setRefreshing] = useState(false);
  const [showTotpModal, setShowTotpModal] = useState(false);

  const fetchDashboardData = useCallback(async () => {
    try {
      const [summaryRes, statusRes, killRes, approvalsRes] = await Promise.allSettled([
        apiClient.get('/api/portfolio/summary'),
        apiClient.get('/api/engine/status'),
        apiClient.get('/api/killswitch/status'),
        apiClient.get('/api/approval/pending'),
      ]);

      if (summaryRes.status === 'fulfilled') {
        setSummary(summaryRes.value.data);
      }
      if (statusRes.status === 'fulfilled') {
        setIsEngineRunning(statusRes.value.data.isRunning || statusRes.value.data.IsRunning);
      }
      if (killRes.status === 'fulfilled') {
        setIsLocked(killRes.value.data.isLocked || killRes.value.data.IsLocked);
      }
      if (approvalsRes.status === 'fulfilled' && Array.isArray(approvalsRes.value.data)) {
        setPendingApprovals(approvalsRes.value.data);
      }
    } catch (err) {
      console.error('Error fetching dashboard data:', err);
    }
  }, []);

  useEffect(() => {
    fetchDashboardData();
    const interval = setInterval(fetchDashboardData, 3000);
    return () => clearInterval(interval);
  }, [fetchDashboardData]);

  const onRefresh = async () => {
    setRefreshing(true);
    await fetchDashboardData();
    setRefreshing(false);
  };

  const handleApproveSignal = async (id: string) => {
    try {
      await apiClient.post(`/api/approval/approve/${id}`);
      Alert.alert('Trade Approved', 'Signal successfully approved and executed.');
      fetchDashboardData();
    } catch (err: any) {
      Alert.alert('Error', err.response?.data?.message || 'Failed to approve signal');
    }
  };

  const handleDenySignal = async (id: string) => {
    try {
      await apiClient.post(`/api/approval/deny/${id}`);
      Alert.alert('Trade Denied', 'Signal removed from approval queue.');
      fetchDashboardData();
    } catch (err: any) {
      Alert.alert('Error', err.response?.data?.message || 'Failed to deny signal');
    }
  };

  const handleToggleEngine = async () => {
    Haptics.impactAsync(Haptics.ImpactFeedbackStyle.Heavy);
    const endpoint = isEngineRunning ? 'stop' : 'start';
    try {
      const res = await apiClient.post(`/api/engine/${endpoint}`);
      if (res.data.authUrl || res.data.AuthUrl) {
        Alert.alert('Fyers Auth Required', 'Please authenticate with Fyers to connect live market data.');
      } else {
        setIsEngineRunning(!isEngineRunning);
        Alert.alert('Engine Status', `Strategy Engine ${isEngineRunning ? 'Stopped' : 'Started'}`);
      }
    } catch (err: any) {
      Alert.alert('Engine Error', err.response?.data?.error || 'Failed to toggle engine');
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
            Haptics.notificationAsync(Haptics.NotificationFeedbackType.Warning);
            try {
              const res = await apiClient.post('/api/engine/squareoff');
              Alert.alert('Square-Off Completed', res.data.message || res.data.Message || 'All positions scheduled for exit.');
              fetchDashboardData();
            } catch (err: any) {
              Alert.alert('Error', err.response?.data?.error || 'Failed to square off');
            }
          },
        },
      ]
    );
  };

  const handleKillSwitch = () => {
    Alert.alert(
      '🚨 MASTER KILL SWITCH',
      'This will LOCK the trading account, disable all strategies, and auto-square off positions with an unalterable cooling lock.',
      [
        { text: 'Cancel', style: 'cancel' },
        {
          text: 'LOCK ACCOUNT',
          style: 'destructive',
          onPress: async () => {
            Haptics.notificationAsync(Haptics.NotificationFeedbackType.Error);
            try {
              await apiClient.post('/api/killswitch/trigger');
              setIsLocked(true);
              Alert.alert('Account Locked', 'Master Kill Switch has been activated.');
              fetchDashboardData();
            } catch (err: any) {
              Alert.alert('Error', err.response?.data?.error || 'Failed to trigger kill switch');
            }
          },
        },
      ]
    );
  };

  const isProfit = summary.dailyPnL >= 0;

  return (
    <ScrollView
      style={styles.container}
      refreshControl={<RefreshControl refreshing={refreshing} onRefresh={onRefresh} tintColor={COLORS.primary} />}
    >
      {/* Pending Approvals Carousel / Sticky Banner */}
      {pendingApprovals.map((item) => (
        <ApprovalQueueCard
          key={item.id}
          item={item}
          onApprove={handleApproveSignal}
          onDeny={handleDenySignal}
        />
      ))}

      {/* Metrics Row */}
      <View style={styles.metricsRow}>
        <MetricCard
          title="Daily P&L"
          value={`${isProfit ? '+' : ''}₹${Math.abs(summary.dailyPnL || 0).toFixed(2)}`}
          subValue={isProfit ? 'Net Profit' : 'Net Loss'}
          icon={TrendingUp}
          variant={isProfit ? 'profit' : 'loss'}
        />
        <MetricCard
          title="Available Margin"
          value={`₹${(summary.availableMargin || 0).toFixed(0)}`}
          icon={DollarSign}
          variant="primary"
        />
        <MetricCard
          title="Active Positions"
          value={`${summary.activePositionsCount || 0}`}
          icon={Briefcase}
          variant="neutral"
        />
      </View>

      {/* Engine & Quick Controls */}
      <View style={styles.controlsCard}>
        <Text style={styles.sectionHeader}>Strategy Engine Control</Text>
        <View style={styles.engineControlRow}>
          <TouchableOpacity
            style={[styles.engineToggleBtn, isEngineRunning ? styles.engineStop : styles.engineStart]}
            onPress={handleToggleEngine}
            activeOpacity={0.85}
          >
            {isEngineRunning ? (
              <>
                <Square size={18} color="#ffffff" fill="#ffffff" />
                <Text style={styles.btnTextWhite}>Stop Strategy Engine</Text>
              </>
            ) : (
              <>
                <Play size={18} color="#ffffff" fill="#ffffff" />
                <Text style={styles.btnTextWhite}>Start Strategy Engine</Text>
              </>
            )}
          </TouchableOpacity>
        </View>
      </View>

      {/* Emergency Actions Card */}
      <View style={styles.emergencyCard}>
        <Text style={styles.sectionHeader}>Risk & Emergency Guards</Text>

        <TouchableOpacity
          style={styles.squareOffBtn}
          onPress={handleEmergencySquareOff}
          activeOpacity={0.85}
        >
          <AlertTriangle size={18} color="#ffffff" />
          <Text style={styles.squareOffText}>EMERGENCY SQUARE OFF ALL</Text>
        </TouchableOpacity>

        <TouchableOpacity
          style={[styles.killSwitchBtn, isLocked && styles.btnDisabled]}
          onPress={handleKillSwitch}
          disabled={isLocked}
          activeOpacity={0.85}
        >
          <ShieldAlert size={18} color="#ffffff" />
          <Text style={styles.killSwitchText}>
            {isLocked ? 'ACCOUNT CURRENTLY LOCKED' : 'ACTIVATE MASTER KILL SWITCH'}
          </Text>
        </TouchableOpacity>
      </View>

      {/* Manual Access Gate Quick Button */}
      <View style={styles.gateCard}>
        <View style={styles.gateLeft}>
          <Key size={20} color={COLORS.primary} />
          <View>
            <Text style={styles.gateTitle}>Manual Access Gate (Fyers TOTP)</Text>
            <Text style={styles.gateSubtitle}>Strict time & cooling-off protected TOTP generation</Text>
          </View>
        </View>
        <TouchableOpacity style={styles.gateBtn} onPress={() => setShowTotpModal(true)} activeOpacity={0.8}>
          <Text style={styles.gateBtnText}>Open Gate</Text>
        </TouchableOpacity>
      </View>

      <View style={{ height: 40 }} />

      <TotpModal visible={showTotpModal} onClose={() => setShowTotpModal(false)} />
    </ScrollView>
  );
};

const styles = StyleSheet.create({
  container: {
    flex: 1,
    backgroundColor: COLORS.bg,
  },
  metricsRow: {
    flexDirection: 'row',
    gap: 8,
    paddingHorizontal: 16,
    paddingVertical: 12,
  },
  controlsCard: {
    backgroundColor: COLORS.surface,
    borderRadius: 16,
    padding: 16,
    marginHorizontal: 16,
    marginVertical: 6,
    borderWidth: 1,
    borderColor: COLORS.surfaceBorder,
  },
  sectionHeader: {
    fontSize: 13,
    fontWeight: '800',
    color: COLORS.textMuted,
    textTransform: 'uppercase',
    letterSpacing: 0.5,
    marginBottom: 12,
  },
  engineControlRow: {
    flexDirection: 'row',
    gap: 10,
  },
  engineToggleBtn: {
    flex: 1,
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'center',
    gap: 8,
    paddingVertical: 14,
    borderRadius: 12,
  },
  engineStart: {
    backgroundColor: COLORS.profit,
  },
  engineStop: {
    backgroundColor: COLORS.warning,
  },
  btnTextWhite: {
    fontSize: 15,
    fontWeight: '800',
    color: '#ffffff',
  },
  emergencyCard: {
    backgroundColor: COLORS.surface,
    borderRadius: 16,
    padding: 16,
    marginHorizontal: 16,
    marginVertical: 6,
    borderWidth: 1,
    borderColor: COLORS.surfaceBorder,
  },
  squareOffBtn: {
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'center',
    gap: 8,
    backgroundColor: COLORS.loss,
    paddingVertical: 14,
    borderRadius: 12,
    marginBottom: 10,
  },
  squareOffText: {
    fontSize: 14,
    fontWeight: '900',
    color: '#ffffff',
    letterSpacing: 0.5,
  },
  killSwitchBtn: {
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'center',
    gap: 8,
    backgroundColor: '#7f1d1d',
    paddingVertical: 14,
    borderRadius: 12,
  },
  btnDisabled: {
    backgroundColor: '#334155',
  },
  killSwitchText: {
    fontSize: 13,
    fontWeight: '900',
    color: '#ffffff',
    letterSpacing: 0.5,
  },
  gateCard: {
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'space-between',
    backgroundColor: COLORS.surface,
    borderRadius: 16,
    padding: 16,
    marginHorizontal: 16,
    marginVertical: 6,
    borderWidth: 1,
    borderColor: COLORS.surfaceBorder,
  },
  gateLeft: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: 12,
    flex: 1,
    marginRight: 10,
  },
  gateTitle: {
    fontSize: 14,
    fontWeight: '800',
    color: COLORS.text,
  },
  gateSubtitle: {
    fontSize: 11,
    color: COLORS.textMuted,
    marginTop: 2,
  },
  gateBtn: {
    backgroundColor: COLORS.primaryLight,
    borderWidth: 1,
    borderColor: 'rgba(99, 102, 241, 0.4)',
    paddingHorizontal: 14,
    paddingVertical: 8,
    borderRadius: 10,
  },
  gateBtnText: {
    fontSize: 12,
    fontWeight: '800',
    color: COLORS.primary,
  },
});
