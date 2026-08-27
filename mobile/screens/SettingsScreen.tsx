import React, { useState, useEffect } from 'react';
import {
  View,
  Text,
  StyleSheet,
  ScrollView,
  TouchableOpacity,
  TextInput,
  Switch,
  ActivityIndicator,
  Alert,
} from 'react-native';
import {
  Settings2,
  Shield,
  Key,
  Sliders,
  TrendingUp,
  AlertTriangle,
  Lock,
  Flame,
  Save,
} from 'lucide-react-native';
import * as Haptics from 'expo-haptics';
import { COLORS } from '../lib/theme';
import { apiClient } from '../lib/api';
import { TotpModal } from '../components/TotpModal';

export interface FullGlobalSettings {
  maxLossPerTrade: number;
  maxDailyLoss: number;
  maxTradesPerDay: number;
  maxFailedTrades: number;
  vixMinLimit: number;
  vixMaxLimit: number;
  perTradeStopLossPoint: number;
  perTradeGainPoint: number;
  tradeMode: string;
  killSwitchShutdownMinutes: number;
  autoTradeLots: number;
  baseLotSize: number;
}

export const SettingsScreen: React.FC = () => {
  const [settings, setSettings] = useState<FullGlobalSettings>({
    maxLossPerTrade: 1500,
    maxDailyLoss: 3000,
    maxTradesPerDay: 5,
    maxFailedTrades: 3,
    vixMinLimit: 11,
    vixMaxLimit: 22,
    perTradeStopLossPoint: 15,
    perTradeGainPoint: 30,
    tradeMode: 'Paper',
    killSwitchShutdownMinutes: 20,
    autoTradeLots: 1,
    baseLotSize: 65,
  });

  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [showTotpModal, setShowTotpModal] = useState(false);

  useEffect(() => {
    fetchSettings();
  }, []);

  const fetchSettings = async () => {
    try {
      const res = await apiClient.get('/api/globalsettings');
      if (res.data) {
        setSettings(res.data);
      }
    } catch (err) {
      console.error('Error loading global settings:', err);
    } finally {
      setLoading(false);
    }
  };

  const handleFieldChange = (field: keyof FullGlobalSettings, value: any) => {
    setSettings((prev) => ({ ...prev, [field]: value }));
  };

  const handleSaveSettings = async () => {
    setSaving(true);
    Haptics.impactAsync(Haptics.ImpactFeedbackStyle.Medium);
    try {
      await apiClient.put('/api/globalsettings', settings);
      Alert.alert('Settings Saved', 'All global parameters and master risk controls updated successfully.');
      Haptics.notificationAsync(Haptics.NotificationFeedbackType.Success);
    } catch (err: any) {
      Alert.alert('Error', err.response?.data?.error || 'Failed to save global settings');
      Haptics.notificationAsync(Haptics.NotificationFeedbackType.Error);
    } finally {
      setSaving(false);
    }
  };

  if (loading) {
    return (
      <View style={styles.loadingContainer}>
        <ActivityIndicator size="large" color={COLORS.primary} />
        <Text style={styles.loadingText}>Loading Global Parameters...</Text>
      </View>
    );
  }

  return (
    <ScrollView style={styles.container} contentContainerStyle={{ paddingBottom: 50 }}>
      {/* Header Banner */}
      <View style={styles.headerBox}>
        <Settings2 size={24} color={COLORS.primary} />
        <View style={{ flex: 1 }}>
          <Text style={styles.pageTitle}>Global Parameters</Text>
          <Text style={styles.pageSubtitle}>Master risk controls, trade limits, and execution filters</Text>
        </View>
      </View>

      {/* 1. Manual Access Gate (Fyers TOTP) */}
      <View style={styles.card}>
        <View style={styles.cardHeader}>
          <Key size={18} color={COLORS.primary} />
          <Text style={styles.cardTitle}>Manual Access Gate (Fyers TOTP)</Text>
        </View>
        <Text style={styles.cardDesc}>
          Strict time locks & cooling-off delay to prevent emotional manual overrides.
        </Text>
        <TouchableOpacity
          style={styles.gateButton}
          onPress={() => setShowTotpModal(true)}
          activeOpacity={0.85}
        >
          <Key size={16} color="#ffffff" />
          <Text style={styles.gateButtonText}>Generate Fyers TOTP Code</Text>
        </TouchableOpacity>
      </View>

      {/* 2. Execution Mode */}
      <View style={styles.card}>
        <View style={styles.cardHeader}>
          <Sliders size={18} color={COLORS.primary} />
          <Text style={styles.cardTitle}>Execution Mode</Text>
        </View>
        <View style={styles.modeRow}>
          <View>
            <Text style={styles.modeLabel}>{settings.tradeMode} Trading Mode</Text>
            <Text style={styles.modeSub}>
              {settings.tradeMode === 'Paper'
                ? 'Simulated paper orders with live market data'
                : 'Live real money execution with Fyers broker'}
            </Text>
          </View>
          <Switch
            value={settings.tradeMode === 'Live'}
            onValueChange={(val) => {
              handleFieldChange('tradeMode', val ? 'Live' : 'Paper');
              Haptics.selectionAsync();
            }}
            trackColor={{ false: '#334155', true: COLORS.profit }}
            thumbColor="#ffffff"
          />
        </View>
      </View>

      {/* 3. Risk & Loss Limits */}
      <View style={styles.card}>
        <View style={styles.cardHeader}>
          <Shield size={18} color={COLORS.loss} />
          <Text style={styles.cardTitle}>Risk & Loss Limits</Text>
        </View>

        <View style={styles.gridRow}>
          <View style={styles.inputCol}>
            <Text style={styles.inputLabel}>Max Loss Per Trade (₹)</Text>
            <TextInput
              style={styles.input}
              value={settings.maxLossPerTrade?.toString()}
              onChangeText={(val) => handleFieldChange('maxLossPerTrade', parseFloat(val) || 0)}
              keyboardType="numeric"
            />
          </View>
          <View style={styles.inputCol}>
            <Text style={styles.inputLabel}>Max Loss Per Day (₹)</Text>
            <TextInput
              style={styles.input}
              value={settings.maxDailyLoss?.toString()}
              onChangeText={(val) => handleFieldChange('maxDailyLoss', parseFloat(val) || 0)}
              keyboardType="numeric"
            />
          </View>
        </View>
      </View>

      {/* 4. Per Trade Targets (Points) */}
      <View style={styles.card}>
        <View style={styles.cardHeader}>
          <TrendingUp size={18} color={COLORS.profit} />
          <Text style={styles.cardTitle}>Per Trade Points (Target / SL)</Text>
        </View>

        <View style={styles.gridRow}>
          <View style={styles.inputCol}>
            <Text style={styles.inputLabel}>Per Trade Target (pts)</Text>
            <TextInput
              style={styles.input}
              value={settings.perTradeGainPoint?.toString()}
              onChangeText={(val) => handleFieldChange('perTradeGainPoint', parseFloat(val) || 0)}
              keyboardType="numeric"
            />
          </View>
          <View style={styles.inputCol}>
            <Text style={styles.inputLabel}>Per Trade Stop Loss (pts)</Text>
            <TextInput
              style={styles.input}
              value={settings.perTradeStopLossPoint?.toString()}
              onChangeText={(val) => handleFieldChange('perTradeStopLossPoint', parseFloat(val) || 0)}
              keyboardType="numeric"
            />
          </View>
        </View>
      </View>

      {/* 5. Trade Execution Limits */}
      <View style={styles.card}>
        <View style={styles.cardHeader}>
          <Sliders size={18} color={COLORS.primary} />
          <Text style={styles.cardTitle}>Trade Execution Limits</Text>
        </View>

        <View style={styles.gridRow}>
          <View style={styles.inputCol}>
            <Text style={styles.inputLabel}>Auto Trade Lots</Text>
            <TextInput
              style={styles.input}
              value={settings.autoTradeLots?.toString()}
              onChangeText={(val) => handleFieldChange('autoTradeLots', parseInt(val) || 1)}
              keyboardType="numeric"
            />
          </View>
          <View style={styles.inputCol}>
            <Text style={styles.inputLabel}>Base Lot Size (Qty)</Text>
            <TextInput
              style={styles.input}
              value={settings.baseLotSize?.toString()}
              onChangeText={(val) => handleFieldChange('baseLotSize', parseInt(val) || 65)}
              keyboardType="numeric"
            />
          </View>
        </View>

        <View style={[styles.gridRow, { marginTop: 12 }]}>
          <View style={styles.inputCol}>
            <Text style={styles.inputLabel}>Max Trades / Day</Text>
            <TextInput
              style={styles.input}
              value={settings.maxTradesPerDay?.toString()}
              onChangeText={(val) => handleFieldChange('maxTradesPerDay', parseInt(val) || 5)}
              keyboardType="numeric"
            />
          </View>
          <View style={styles.inputCol}>
            <Text style={styles.inputLabel}>Max Failed Trades</Text>
            <TextInput
              style={styles.input}
              value={settings.maxFailedTrades?.toString()}
              onChangeText={(val) => handleFieldChange('maxFailedTrades', parseInt(val) || 3)}
              keyboardType="numeric"
            />
          </View>
        </View>
      </View>

      {/* 6. Market VIX Filters & Kill Lock */}
      <View style={styles.card}>
        <View style={styles.cardHeader}>
          <Flame size={18} color={COLORS.warning} />
          <Text style={styles.cardTitle}>Market VIX & Lock Duration</Text>
        </View>

        <View style={styles.gridRow}>
          <View style={styles.inputCol}>
            <Text style={styles.inputLabel}>Minimum VIX</Text>
            <TextInput
              style={styles.input}
              value={settings.vixMinLimit?.toString()}
              onChangeText={(val) => handleFieldChange('vixMinLimit', parseFloat(val) || 11)}
              keyboardType="numeric"
            />
          </View>
          <View style={styles.inputCol}>
            <Text style={styles.inputLabel}>Maximum VIX</Text>
            <TextInput
              style={styles.input}
              value={settings.vixMaxLimit?.toString()}
              onChangeText={(val) => handleFieldChange('vixMaxLimit', parseFloat(val) || 22)}
              keyboardType="numeric"
            />
          </View>
        </View>

        <View style={{ marginTop: 12 }}>
          <Text style={styles.inputLabel}>Kill Switch Cooldown (Minutes)</Text>
          <TextInput
            style={styles.input}
            value={settings.killSwitchShutdownMinutes?.toString()}
            onChangeText={(val) => handleFieldChange('killSwitchShutdownMinutes', parseInt(val) || 20)}
            keyboardType="numeric"
          />
        </View>
      </View>

      {/* Save Button */}
      <TouchableOpacity
        style={styles.saveBtn}
        onPress={handleSaveSettings}
        disabled={saving}
        activeOpacity={0.85}
      >
        {saving ? (
          <ActivityIndicator color="#ffffff" />
        ) : (
          <>
            <Save size={18} color="#ffffff" />
            <Text style={styles.saveBtnText}>Save All Global Parameters</Text>
          </>
        )}
      </TouchableOpacity>

      <TotpModal visible={showTotpModal} onClose={() => setShowTotpModal(false)} />
    </ScrollView>
  );
};

const styles = StyleSheet.create({
  container: {
    flex: 1,
    backgroundColor: COLORS.bg,
  },
  loadingContainer: {
    flex: 1,
    backgroundColor: COLORS.bg,
    alignItems: 'center',
    justifyContent: 'center',
  },
  loadingText: {
    marginTop: 10,
    fontSize: 14,
    fontWeight: '700',
    color: COLORS.textMuted,
  },
  headerBox: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: 12,
    paddingHorizontal: 16,
    paddingVertical: 14,
    backgroundColor: COLORS.surface,
    borderBottomWidth: 1,
    borderBottomColor: COLORS.surfaceBorder,
    marginBottom: 8,
  },
  pageTitle: {
    fontSize: 18,
    fontWeight: '900',
    color: COLORS.text,
  },
  pageSubtitle: {
    fontSize: 11,
    color: COLORS.textMuted,
    marginTop: 1,
  },
  card: {
    backgroundColor: COLORS.surface,
    borderRadius: 16,
    padding: 16,
    marginHorizontal: 16,
    marginVertical: 6,
    borderWidth: 1,
    borderColor: COLORS.surfaceBorder,
  },
  cardHeader: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: 8,
    marginBottom: 8,
  },
  cardTitle: {
    fontSize: 14,
    fontWeight: '800',
    color: COLORS.text,
  },
  cardDesc: {
    fontSize: 11,
    color: COLORS.textMuted,
    lineHeight: 15,
    marginBottom: 12,
  },
  gateButton: {
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'center',
    gap: 8,
    backgroundColor: COLORS.primary,
    paddingVertical: 12,
    borderRadius: 10,
  },
  gateButtonText: {
    fontSize: 13,
    fontWeight: '800',
    color: '#ffffff',
  },
  modeRow: {
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'space-between',
  },
  modeLabel: {
    fontSize: 14,
    fontWeight: '700',
    color: COLORS.text,
  },
  modeSub: {
    fontSize: 11,
    color: COLORS.textMuted,
    maxWidth: 220,
    marginTop: 2,
  },
  gridRow: {
    flexDirection: 'row',
    gap: 12,
  },
  inputCol: {
    flex: 1,
  },
  inputLabel: {
    fontSize: 11,
    fontWeight: '700',
    color: COLORS.textMuted,
    marginBottom: 4,
  },
  input: {
    backgroundColor: COLORS.bg,
    borderRadius: 10,
    paddingHorizontal: 12,
    paddingVertical: 10,
    color: COLORS.text,
    fontSize: 14,
    fontWeight: '800',
    borderWidth: 1,
    borderColor: COLORS.surfaceBorder,
  },
  saveBtn: {
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'center',
    gap: 8,
    backgroundColor: COLORS.profit,
    marginHorizontal: 16,
    marginTop: 12,
    paddingVertical: 14,
    borderRadius: 12,
  },
  saveBtnText: {
    fontSize: 15,
    fontWeight: '900',
    color: '#ffffff',
  },
});
