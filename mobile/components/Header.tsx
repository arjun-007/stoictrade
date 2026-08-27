import React from 'react';
import { View, Text, StyleSheet, TouchableOpacity } from 'react-native';
import { Activity, ShieldAlert, Zap, AlertTriangle } from 'lucide-react-native';
import { COLORS } from '../lib/theme';

interface HeaderProps {
  spotPrice?: number;
  spotChange?: number;
  isEngineRunning?: boolean;
  onEmergencyPress?: () => void;
  onToggleEngine?: () => void;
}

export const Header: React.FC<HeaderProps> = ({
  spotPrice = 24250.00,
  spotChange = 0,
  isEngineRunning = false,
  onEmergencyPress,
  onToggleEngine,
}) => {
  const isPositive = spotChange >= 0;

  return (
    <View style={styles.container}>
      <View style={styles.topRow}>
        <View style={styles.logoContainer}>
          <Zap size={22} color={COLORS.primary} />
          <Text style={styles.logoText}>Stoic<Text style={{ color: COLORS.primary }}>Trade</Text></Text>
        </View>

        <View style={styles.actionsContainer}>
          <TouchableOpacity
            style={[styles.engineButton, isEngineRunning ? styles.engineRunning : styles.engineStopped]}
            onPress={onToggleEngine}
            activeOpacity={0.8}
          >
            <View style={[styles.dot, isEngineRunning ? styles.dotActive : styles.dotInactive]} />
            <Text style={styles.engineText}>{isEngineRunning ? 'Running' : 'Stopped'}</Text>
          </TouchableOpacity>

          {onEmergencyPress && (
            <TouchableOpacity
              style={styles.emergencyButton}
              onPress={onEmergencyPress}
              activeOpacity={0.8}
            >
              <AlertTriangle size={16} color="#ffffff" />
              <Text style={styles.emergencyText}>Square Off</Text>
            </TouchableOpacity>
          )}
        </View>
      </View>

      {/* Spot Price Bar */}
      <View style={styles.spotBar}>
        <View style={styles.spotLeft}>
          <Text style={styles.indexLabel}>NIFTY 50</Text>
          <Text style={styles.spotPrice}>₹{spotPrice.toFixed(2)}</Text>
        </View>
        <View style={[styles.changeBadge, isPositive ? styles.badgeProfit : styles.badgeLoss]}>
          <Text style={[styles.changeText, isPositive ? styles.textProfit : styles.textLoss]}>
            {isPositive ? '+' : ''}{spotChange.toFixed(2)} pts
          </Text>
        </View>
      </View>
    </View>
  );
};

const styles = StyleSheet.create({
  container: {
    backgroundColor: COLORS.surface,
    paddingTop: 48,
    paddingHorizontal: 16,
    paddingBottom: 12,
    borderBottomWidth: 1,
    borderBottomColor: COLORS.surfaceBorder,
  },
  topRow: {
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'space-between',
    marginBottom: 12,
  },
  logoContainer: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: 6,
  },
  logoText: {
    fontSize: 20,
    fontWeight: '800',
    color: COLORS.text,
    letterSpacing: 0.5,
  },
  actionsContainer: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: 8,
  },
  engineButton: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: 6,
    paddingHorizontal: 10,
    paddingVertical: 5,
    borderRadius: 8,
  },
  engineRunning: {
    backgroundColor: COLORS.profitLight,
  },
  engineStopped: {
    backgroundColor: 'rgba(100, 116, 139, 0.2)',
  },
  dot: {
    width: 7,
    height: 7,
    borderRadius: 4,
  },
  dotActive: {
    backgroundColor: COLORS.profit,
  },
  dotInactive: {
    backgroundColor: COLORS.textSubtle,
  },
  engineText: {
    fontSize: 12,
    fontWeight: '700',
    color: COLORS.text,
  },
  emergencyButton: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: 4,
    backgroundColor: COLORS.loss,
    paddingHorizontal: 10,
    paddingVertical: 5,
    borderRadius: 8,
  },
  emergencyText: {
    fontSize: 11,
    fontWeight: '800',
    color: '#ffffff',
  },
  spotBar: {
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'space-between',
    backgroundColor: COLORS.bg,
    paddingHorizontal: 12,
    paddingVertical: 8,
    borderRadius: 10,
    borderWidth: 1,
    borderColor: COLORS.surfaceBorder,
  },
  spotLeft: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: 8,
  },
  indexLabel: {
    fontSize: 12,
    fontWeight: '700',
    color: COLORS.textMuted,
  },
  spotPrice: {
    fontSize: 15,
    fontWeight: '800',
    color: COLORS.text,
    fontFamily: 'System',
  },
  changeBadge: {
    paddingHorizontal: 8,
    paddingVertical: 3,
    borderRadius: 6,
  },
  badgeProfit: {
    backgroundColor: COLORS.profitLight,
  },
  badgeLoss: {
    backgroundColor: COLORS.lossLight,
  },
  changeText: {
    fontSize: 11,
    fontWeight: '700',
  },
  textProfit: {
    color: COLORS.profit,
  },
  textLoss: {
    color: COLORS.loss,
  },
});
