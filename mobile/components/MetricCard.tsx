import React from 'react';
import { View, Text, StyleSheet } from 'react-native';
import { LucideIcon } from 'lucide-react-native';
import { COLORS } from '../lib/theme';

interface MetricCardProps {
  title: string;
  value: string;
  subValue?: string;
  icon: LucideIcon;
  variant?: 'neutral' | 'profit' | 'loss' | 'primary';
}

export const MetricCard: React.FC<MetricCardProps> = ({
  title,
  value,
  subValue,
  icon: Icon,
  variant = 'neutral',
}) => {
  const getColors = () => {
    switch (variant) {
      case 'profit':
        return { iconColor: COLORS.profit, iconBg: COLORS.profitLight, valColor: COLORS.profit };
      case 'loss':
        return { iconColor: COLORS.loss, iconBg: COLORS.lossLight, valColor: COLORS.loss };
      case 'primary':
        return { iconColor: COLORS.primary, iconBg: COLORS.primaryLight, valColor: COLORS.primary };
      default:
        return { iconColor: COLORS.textMuted, iconBg: 'rgba(100, 116, 139, 0.15)', valColor: COLORS.text };
    }
  };

  const colors = getColors();

  return (
    <View style={styles.card}>
      <View style={styles.topRow}>
        <Text style={styles.title}>{title}</Text>
        <View style={[styles.iconBox, { backgroundColor: colors.iconBg }]}>
          <Icon size={18} color={colors.iconColor} />
        </View>
      </View>
      <Text style={[styles.value, { color: colors.valColor }]}>{value}</Text>
      {subValue ? <Text style={styles.subValue}>{subValue}</Text> : null}
    </View>
  );
};

const styles = StyleSheet.create({
  card: {
    flex: 1,
    backgroundColor: COLORS.surface,
    borderRadius: 14,
    padding: 14,
    borderWidth: 1,
    borderColor: COLORS.surfaceBorder,
    minWidth: 140,
  },
  topRow: {
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'space-between',
    marginBottom: 8,
  },
  title: {
    fontSize: 12,
    fontWeight: '600',
    color: COLORS.textMuted,
  },
  iconBox: {
    padding: 6,
    borderRadius: 8,
  },
  value: {
    fontSize: 18,
    fontWeight: '800',
    letterSpacing: -0.5,
  },
  subValue: {
    fontSize: 11,
    color: COLORS.textSubtle,
    marginTop: 2,
  },
});
