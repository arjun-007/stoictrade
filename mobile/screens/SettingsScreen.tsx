import React, { useState, useEffect } from 'react';
import { View, Text, StyleSheet, ScrollView, TouchableOpacity, TextInput, Switch, Alert } from 'react-native';
import { Shield, Key, Sliders, DollarSign, Lock, RefreshCcw } from 'lucide-react-native';
import * as Haptics from 'expo-haptics';
import { COLORS } from '../lib/theme';
import { apiClient } from '../lib/api';
import { TotpModal } from '../components/TotpModal';

export const SettingsScreen: React.FC = () => {
  const [settings, setSettings] = useState({
    maxLossPerTrade: 1500,
    maxDailyLoss: 3000,
    maxTradesPerDay: 5,
    autoTradeLots: 1,
    tradeMode: 'Paper',
  });
  const [showTotpModal, setShowTotpModal] = useState(false);
  const [saving, setSaving] = useState(false);

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
      console.error('Error fetching global settings:', err);
    }
  };

  const handleSaveSettings = async () => {
    setSaving(true);
    Haptics.impactAsync(Haptics.ImpactFeedbackStyle.Medium);
    try {
      await apiClient.post('/api/globalsettings', settings);
      Alert.alert('Settings Saved', 'Risk parameters and trade settings have been updated.');
    } catch (err: any) {
      Alert.alert('Error', err.response?.data?.error || 'Failed to save settings');
    } finally {
      setSaving(false);
    }
  };

  return (
    <ScrollView style={styles.container} contentContainerStyle={{ paddingBottom: 40 }}>
      {/* Manual Access Gate Card */}
      <View style={styles.card}>
        <View style={styles.cardHeader}>
          <Key size={20} color={COLORS.primary} />
          <Text style={styles.cardTitle}>Manual Access Gate (Fyers TOTP)</Text>
        </View>
        <Text style={styles.cardDesc}>
          Enforce strict time locks and 20-minute behavioral cooling-off delay to prevent emotional overrides.
        </Text>
        <TouchableOpacity style={styles.gateButton} onPress={() => setShowTotpModal(true)} activeOpacity={0.85}>
          <Key size={16} color="#ffffff" />
          <Text style={styles.gateButtonText}>Generate Fyers TOTP Code</Text>
        </TouchableOpacity>
      </View>

      {/* Trade Mode Toggle */}
      <View style={styles.card}>
        <View style={styles.cardHeader}>
          <Sliders size={20} color={COLORS.primary} />
          <Text style={styles.cardTitle}>Execution Mode</Text>
        </View>
        <View style={styles.modeRow}>
          <View>
            <Text style={styles.modeLabel}>{settings.tradeMode} Trading Mode</Text>
            <Text style={styles.modeSub}>{settings.tradeMode === 'Paper' ? 'Simulated paper orders with real market data' : 'Live real money execution with Fyers broker'}</Text>
          </View>
          <Switch
            value={settings.tradeMode === 'Live'}
            onValueChange={(val) => {
              setSettings({ ...settings, tradeMode: val ? 'Live' : 'Paper' });
              Haptics.selectionAsync();
            }}
            trackColor={{ false: '#334155', true: COLORS.profit }}
            thumbColor="#ffffff"
          />
        </View>
      </View>

      {/* Risk Limits */}
      <View style={styles.card}>
        <View style={styles.cardHeader}>
          <Shield size={20} color={COLORS.loss} />
          <Text style={styles.cardTitle}>Disciplined Risk Controls</Text>
        </View>

        <View style={styles.inputGroup}>
          <Text style={styles.inputLabel}>Max Daily Loss Limit (₹)</Text>
          <TextInput
            style={styles.input}
            value={settings.maxDailyLoss?.toString() || '3000'}
            onChangeText={(val) => setSettings({ ...settings, maxDailyLoss: parseFloat(val) || 0 })}
            keyboardType="numeric"
          />
        </View>

        <View style={styles.inputGroup}>
          <Text style={styles.inputLabel}>Max Loss Per Trade (₹)</Text>
          <TextInput
            style={styles.input}
            value={settings.maxLossPerTrade?.toString() || '1500'}
            onChangeText={(val) => setSettings({ ...settings, maxLossPerTrade: parseFloat(val) || 0 })}
            keyboardType="numeric"
          />
        </View>

        <View style={styles.inputGroup}>
          <Text style={styles.inputLabel}>Auto Trade Lots</Text>
          <TextInput
            style={styles.input}
            value={settings.autoTradeLots?.toString() || '1'}
            onChangeText={(val) => setSettings({ ...settings, autoTradeLots: parseInt(val) || 1 })}
            keyboardType="numeric"
          />
        </View>

        <TouchableOpacity style={styles.saveBtn} onPress={handleSaveSettings} disabled={saving} activeOpacity={0.85}>
          <Text style={styles.saveBtnText}>{saving ? 'Saving...' : 'Save Global Settings'}</Text>
        </TouchableOpacity>
      </View>

      <TotpModal visible={showTotpModal} onClose={() => setShowTotpModal(false)} />
    </ScrollView>
  );
};

const styles = StyleSheet.create({
  container: {
    flex: 1,
    backgroundColor: COLORS.bg,
  },
  card: {
    backgroundColor: COLORS.surface,
    borderRadius: 16,
    padding: 16,
    marginHorizontal: 16,
    marginVertical: 8,
    borderWidth: 1,
    borderColor: COLORS.surfaceBorder,
  },
  cardHeader: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: 8,
    marginBottom: 6,
  },
  cardTitle: {
    fontSize: 15,
    fontWeight: '800',
    color: COLORS.text,
  },
  cardDesc: {
    fontSize: 12,
    color: COLORS.textMuted,
    lineHeight: 16,
    marginBottom: 14,
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
    fontSize: 14,
    fontWeight: '800',
    color: '#ffffff',
  },
  modeRow: {
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'space-between',
    marginTop: 4,
  },
  modeLabel: {
    fontSize: 14,
    fontWeight: '700',
    color: COLORS.text,
  },
  modeSub: {
    fontSize: 11,
    color: COLORS.textMuted,
    maxWidth: 240,
    marginTop: 2,
  },
  inputGroup: {
    marginBottom: 12,
  },
  inputLabel: {
    fontSize: 11,
    fontWeight: '600',
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
    fontWeight: '700',
    borderWidth: 1,
    borderColor: COLORS.surfaceBorder,
  },
  saveBtn: {
    backgroundColor: COLORS.profit,
    paddingVertical: 12,
    borderRadius: 10,
    alignItems: 'center',
    marginTop: 6,
  },
  saveBtnText: {
    fontSize: 14,
    fontWeight: '800',
    color: '#ffffff',
  },
});
