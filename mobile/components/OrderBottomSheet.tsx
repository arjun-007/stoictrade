import React, { useState } from 'react';
import { View, Text, StyleSheet, Modal, TouchableOpacity, TextInput } from 'react-native';
import { X, Plus, Minus, Zap, Shield, TrendingUp } from 'lucide-react-native';
import * as Haptics from 'expo-haptics';
import { COLORS } from '../lib/theme';

interface OrderBottomSheetProps {
  visible: boolean;
  symbol: string;
  initialAction: 'BUY' | 'SELL';
  ltp: number;
  onClose: () => void;
  onSubmit: (order: {
    symbol: string;
    action: string;
    lots: number;
    quantity: number;
    targetPoints: number;
    stopLossPoints: number;
  }) => void;
}

export const OrderBottomSheet: React.FC<OrderBottomSheetProps> = ({
  visible,
  symbol,
  initialAction,
  ltp,
  onClose,
  onSubmit,
}) => {
  const [action, setAction] = useState<'BUY' | 'SELL'>(initialAction);
  const [lots, setLots] = useState(1);
  const [targetPoints, setTargetPoints] = useState('30');
  const [slPoints, setSlPoints] = useState('15');
  const lotSize = 65; // NIFTY standard lot size

  const handleLotsChange = (delta: number) => {
    const newLots = Math.max(1, Math.min(20, lots + delta));
    setLots(newLots);
    Haptics.selectionAsync();
  };

  const handleSubmit = () => {
    Haptics.notificationAsync(Haptics.NotificationFeedbackType.Success);
    onSubmit({
      symbol,
      action,
      lots,
      quantity: lots * lotSize,
      targetPoints: parseFloat(targetPoints) || 30,
      stopLossPoints: parseFloat(slPoints) || 15,
    });
    onClose();
  };

  return (
    <Modal visible={visible} transparent animationType="slide" onRequestClose={onClose}>
      <View style={styles.overlay}>
        <View style={styles.container}>
          {/* Header */}
          <View style={styles.header}>
            <View>
              <Text style={styles.symbolText}>{symbol}</Text>
              <Text style={styles.ltpText}>LTP: ₹{ltp.toFixed(2)}</Text>
            </View>
            <TouchableOpacity onPress={onClose} style={styles.closeButton}>
              <X size={20} color={COLORS.textMuted} />
            </TouchableOpacity>
          </View>

          {/* Action Toggle */}
          <View style={styles.actionToggle}>
            <TouchableOpacity
              style={[styles.actionBtn, action === 'BUY' && styles.buyActive]}
              onPress={() => setAction('BUY')}
            >
              <Text style={[styles.actionBtnText, action === 'BUY' && styles.textWhite]}>BUY</Text>
            </TouchableOpacity>
            <TouchableOpacity
              style={[styles.actionBtn, action === 'SELL' && styles.sellActive]}
              onPress={() => setAction('SELL')}
            >
              <Text style={[styles.actionBtnText, action === 'SELL' && styles.textWhite]}>SELL</Text>
            </TouchableOpacity>
          </View>

          {/* Lot Quantity Selector */}
          <View style={styles.section}>
            <Text style={styles.sectionTitle}>Order Quantity (Lots)</Text>
            <View style={styles.lotRow}>
              <TouchableOpacity style={styles.stepperBtn} onPress={() => handleLotsChange(-1)}>
                <Minus size={18} color={COLORS.text} />
              </TouchableOpacity>

              <View style={styles.lotDisplay}>
                <Text style={styles.lotNumber}>{lots} Lot{lots > 1 ? 's' : ''}</Text>
                <Text style={styles.qtyNumber}>({lots * lotSize} Qty)</Text>
              </View>

              <TouchableOpacity style={styles.stepperBtn} onPress={() => handleLotsChange(1)}>
                <Plus size={18} color={COLORS.text} />
              </TouchableOpacity>
            </View>

            {/* Quick Lot Pills */}
            <View style={styles.quickPills}>
              {[1, 2, 3, 5, 10].map((l) => (
                <TouchableOpacity
                  key={l}
                  style={[styles.pill, lots === l && styles.pillActive]}
                  onPress={() => { setLots(l); Haptics.selectionAsync(); }}
                >
                  <Text style={[styles.pillText, lots === l && styles.pillTextActive]}>+{l}</Text>
                </TouchableOpacity>
              ))}
            </View>
          </View>

          {/* Target & SL Inputs */}
          <View style={styles.rowInputs}>
            <View style={styles.inputCol}>
              <Text style={styles.inputLabel}>Target Points</Text>
              <TextInput
                style={styles.input}
                value={targetPoints}
                onChangeText={setTargetPoints}
                keyboardType="numeric"
                placeholderTextColor={COLORS.textSubtle}
              />
            </View>
            <View style={styles.inputCol}>
              <Text style={styles.inputLabel}>Stop-Loss Points</Text>
              <TextInput
                style={styles.input}
                value={slPoints}
                onChangeText={setSlPoints}
                keyboardType="numeric"
                placeholderTextColor={COLORS.textSubtle}
              />
            </View>
          </View>

          {/* Approx Order Value */}
          <View style={styles.summaryBox}>
            <Text style={styles.summaryLabel}>Estimated Premium Required</Text>
            <Text style={styles.summaryValue}>₹{((lots * lotSize) * ltp).toFixed(2)}</Text>
          </View>

          {/* Submit Button */}
          <TouchableOpacity
            style={[styles.submitButton, action === 'BUY' ? styles.submitBuy : styles.submitSell]}
            onPress={handleSubmit}
            activeOpacity={0.85}
          >
            <Zap size={18} color="#ffffff" />
            <Text style={styles.submitText}>
              Place {action} Market Order
            </Text>
          </TouchableOpacity>
        </View>
      </View>
    </Modal>
  );
};

const styles = StyleSheet.create({
  overlay: {
    flex: 1,
    backgroundColor: 'rgba(0, 0, 0, 0.65)',
    justifyContent: 'flex-end',
  },
  container: {
    backgroundColor: COLORS.surface,
    borderTopLeftRadius: 24,
    borderTopRightRadius: 24,
    padding: 20,
    borderWidth: 1,
    borderColor: COLORS.surfaceBorder,
  },
  header: {
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'space-between',
    marginBottom: 16,
  },
  symbolText: {
    fontSize: 18,
    fontWeight: '800',
    color: COLORS.text,
  },
  ltpText: {
    fontSize: 13,
    fontWeight: '600',
    color: COLORS.profit,
    marginTop: 2,
  },
  closeButton: {
    padding: 6,
    borderRadius: 8,
    backgroundColor: COLORS.bg,
  },
  actionToggle: {
    flexDirection: 'row',
    backgroundColor: COLORS.bg,
    borderRadius: 12,
    padding: 4,
    marginBottom: 16,
  },
  actionBtn: {
    flex: 1,
    paddingVertical: 10,
    alignItems: 'center',
    borderRadius: 10,
  },
  buyActive: {
    backgroundColor: COLORS.profit,
  },
  sellActive: {
    backgroundColor: COLORS.loss,
  },
  actionBtnText: {
    fontSize: 14,
    fontWeight: '800',
    color: COLORS.textMuted,
  },
  textWhite: {
    color: '#ffffff',
  },
  section: {
    marginBottom: 16,
  },
  sectionTitle: {
    fontSize: 12,
    fontWeight: '700',
    color: COLORS.textMuted,
    marginBottom: 8,
  },
  lotRow: {
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'space-between',
    backgroundColor: COLORS.bg,
    borderRadius: 12,
    padding: 6,
  },
  stepperBtn: {
    backgroundColor: COLORS.surfaceLight,
    width: 40,
    height: 40,
    borderRadius: 10,
    alignItems: 'center',
    justifyContent: 'center',
  },
  lotDisplay: {
    alignItems: 'center',
  },
  lotNumber: {
    fontSize: 16,
    fontWeight: '800',
    color: COLORS.text,
  },
  qtyNumber: {
    fontSize: 11,
    color: COLORS.textSubtle,
    marginTop: 1,
  },
  quickPills: {
    flexDirection: 'row',
    gap: 8,
    marginTop: 10,
  },
  pill: {
    flex: 1,
    backgroundColor: COLORS.bg,
    paddingVertical: 6,
    borderRadius: 8,
    alignItems: 'center',
    borderWidth: 1,
    borderColor: COLORS.surfaceBorder,
  },
  pillActive: {
    borderColor: COLORS.primary,
    backgroundColor: COLORS.primaryLight,
  },
  pillText: {
    fontSize: 12,
    fontWeight: '700',
    color: COLORS.textMuted,
  },
  pillTextActive: {
    color: COLORS.primary,
  },
  rowInputs: {
    flexDirection: 'row',
    gap: 12,
    marginBottom: 16,
  },
  inputCol: {
    flex: 1,
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
  summaryBox: {
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'space-between',
    backgroundColor: COLORS.bg,
    padding: 12,
    borderRadius: 10,
    marginBottom: 18,
  },
  summaryLabel: {
    fontSize: 12,
    color: COLORS.textMuted,
  },
  summaryValue: {
    fontSize: 14,
    fontWeight: '800',
    color: COLORS.text,
  },
  submitButton: {
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'center',
    gap: 8,
    paddingVertical: 14,
    borderRadius: 12,
  },
  submitBuy: {
    backgroundColor: COLORS.profit,
  },
  submitSell: {
    backgroundColor: COLORS.loss,
  },
  submitText: {
    fontSize: 15,
    fontWeight: '800',
    color: '#ffffff',
  },
});
