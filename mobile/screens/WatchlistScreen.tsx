import React, { useState, useEffect } from 'react';
import { View, Text, StyleSheet, FlatList, TouchableOpacity, RefreshControl, Alert } from 'react-native';
import { TrendingUp, TrendingDown, Layers, ShoppingCart, Zap } from 'lucide-react-native';
import * as Haptics from 'expo-haptics';
import { COLORS } from '../lib/theme';
import { apiClient } from '../lib/api';
import { OrderBottomSheet } from '../components/OrderBottomSheet';

interface StrikeData {
  strike: number;
  ceSymbol: string;
  ceLtp: number;
  peSymbol: string;
  peLtp: number;
  isAtm?: boolean;
}

export const WatchlistScreen: React.FC = () => {
  const [spotPrice, setSpotPrice] = useState(24250);
  const [strikes, setStrikes] = useState<StrikeData[]>([]);
  const [refreshing, setRefreshing] = useState(false);
  const [selectedOrder, setSelectedOrder] = useState<{
    visible: boolean;
    symbol: string;
    action: 'BUY' | 'SELL';
    ltp: number;
  }>({
    visible: false,
    symbol: '',
    action: 'BUY',
    ltp: 0,
  });

  const generateStrikesForSpot = (spot: number): StrikeData[] => {
    const atmStrike = Math.round(spot / 50) * 50;
    const strikeList: StrikeData[] = [];

    // Generate 5 ITM and 5 OTM strikes
    for (let i = -4; i <= 4; i++) {
      const strike = atmStrike + i * 50;
      const strikeDiff = strike - spot;
      
      // Calibrate realistic option pricing curve based on spot distance
      const ceIntrinsic = Math.max(0, spot - strike);
      const peIntrinsic = Math.max(0, strike - spot);
      const timeValue = Math.max(25, 120 - Math.abs(strikeDiff) * 0.18);

      const ceLtp = Math.round((ceIntrinsic + timeValue) * 100) / 100;
      const peLtp = Math.round((peIntrinsic + timeValue) * 100) / 100;

      strikeList.push({
        strike,
        ceSymbol: `NIFTY26908${strike}CE`,
        ceLtp,
        peSymbol: `NIFTY26908${strike}PE`,
        peLtp,
        isAtm: strike === atmStrike,
      });
    }

    return strikeList;
  };

  const fetchMarketData = async () => {
    try {
      const res = await apiClient.get('/api/marketdata/spot?symbol=NIFTY');
      if (res.data?.price) {
        const spot = res.data.price;
        setSpotPrice(spot);
        setStrikes(generateStrikesForSpot(spot));
      } else {
        setStrikes(generateStrikesForSpot(spotPrice));
      }
    } catch {
      setStrikes(generateStrikesForSpot(spotPrice));
    }
  };

  useEffect(() => {
    fetchMarketData();
    const interval = setInterval(fetchMarketData, 3000);
    return () => clearInterval(interval);
  }, []);

  const onRefresh = async () => {
    setRefreshing(true);
    await fetchMarketData();
    setRefreshing(false);
  };

  const handleOpenOrder = (symbol: string, action: 'BUY' | 'SELL', ltp: number) => {
    Haptics.selectionAsync();
    setSelectedOrder({
      visible: true,
      symbol,
      action,
      ltp,
    });
  };

  const handlePlaceOrder = async (order: {
    symbol: string;
    action: string;
    lots: number;
    quantity: number;
    targetPoints: number;
    stopLossPoints: number;
  }) => {
    try {
      const res = await apiClient.post('/api/orders', {
        instrument: order.symbol,
        action: order.action,
        quantity: order.quantity,
        price: selectedOrder.ltp,
        orderType: 'MARKET',
        targetPrice: selectedOrder.ltp + (order.action === 'BUY' ? order.targetPoints : -order.targetPoints),
        stopLossPrice: Math.max(5, selectedOrder.ltp - (order.action === 'BUY' ? order.stopLossPoints : -order.stopLossPoints)),
      });

      Alert.alert('Order Executed', `Successfully placed ${order.action} order for ${order.quantity} qty of ${order.symbol}.`);
    } catch (err: any) {
      Alert.alert('Order Failed', err.response?.data?.message || 'Could not place order');
    }
  };

  return (
    <View style={styles.container}>
      {/* Header Info */}
      <View style={styles.spotHeader}>
        <View>
          <Text style={styles.chainTitle}>NIFTY Option Chain Watchlist</Text>
          <Text style={styles.chainSubtitle}>Tap CE / PE to launch 1-tap mobile order ticket</Text>
        </View>
        <View style={styles.spotTag}>
          <Text style={styles.spotTagLabel}>NIFTY</Text>
          <Text style={styles.spotTagValue}>₹{spotPrice.toFixed(2)}</Text>
        </View>
      </View>

      {/* Strikes List */}
      <FlatList
        data={strikes}
        keyExtractor={(item) => item.strike.toString()}
        refreshControl={<RefreshControl refreshing={refreshing} onRefresh={onRefresh} tintColor={COLORS.primary} />}
        contentContainerStyle={{ paddingBottom: 40 }}
        renderItem={({ item }) => (
          <View style={[styles.strikeCard, item.isAtm && styles.atmCard]}>
            {item.isAtm ? (
              <View style={styles.atmBadge}>
                <Text style={styles.atmBadgeText}>ATM STRIKE</Text>
              </View>
            ) : null}

            <View style={styles.strikeRow}>
              {/* Call Option Button */}
              <TouchableOpacity
                style={[styles.optionBtn, styles.callBtn]}
                onPress={() => handleOpenOrder(item.ceSymbol, 'BUY', item.ceLtp)}
                activeOpacity={0.75}
              >
                <View>
                  <Text style={styles.optionTypeCall}>CALL (CE)</Text>
                  <Text style={styles.optionLtp}>₹{item.ceLtp.toFixed(2)}</Text>
                </View>
                <View style={styles.tradePillCall}>
                  <Text style={styles.tradePillText}>BUY</Text>
                </View>
              </TouchableOpacity>

              {/* Center Strike Display */}
              <View style={styles.centerStrike}>
                <Text style={[styles.strikeText, item.isAtm && styles.strikeAtmText]}>
                  {item.strike}
                </Text>
              </View>

              {/* Put Option Button */}
              <TouchableOpacity
                style={[styles.optionBtn, styles.putBtn]}
                onPress={() => handleOpenOrder(item.peSymbol, 'BUY', item.peLtp)}
                activeOpacity={0.75}
              >
                <View style={styles.tradePillPut}>
                  <Text style={styles.tradePillText}>BUY</Text>
                </View>
                <View style={{ alignItems: 'flex-end' }}>
                  <Text style={styles.optionTypePut}>PUT (PE)</Text>
                  <Text style={styles.optionLtp}>₹{item.peLtp.toFixed(2)}</Text>
                </View>
              </TouchableOpacity>
            </View>
          </View>
        )}
      />

      <OrderBottomSheet
        visible={selectedOrder.visible}
        symbol={selectedOrder.symbol}
        initialAction={selectedOrder.action}
        ltp={selectedOrder.ltp}
        onClose={() => setSelectedOrder({ ...selectedOrder, visible: false })}
        onSubmit={handlePlaceOrder}
      />
    </View>
  );
};

const styles = StyleSheet.create({
  container: {
    flex: 1,
    backgroundColor: COLORS.bg,
  },
  spotHeader: {
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'space-between',
    backgroundColor: COLORS.surface,
    padding: 16,
    borderBottomWidth: 1,
    borderBottomColor: COLORS.surfaceBorder,
  },
  chainTitle: {
    fontSize: 15,
    fontWeight: '800',
    color: COLORS.text,
  },
  chainSubtitle: {
    fontSize: 11,
    color: COLORS.textMuted,
    marginTop: 2,
  },
  spotTag: {
    backgroundColor: COLORS.bg,
    paddingHorizontal: 10,
    paddingVertical: 6,
    borderRadius: 8,
    alignItems: 'center',
    borderWidth: 1,
    borderColor: COLORS.surfaceBorder,
  },
  spotTagLabel: {
    fontSize: 10,
    fontWeight: '700',
    color: COLORS.textMuted,
  },
  spotTagValue: {
    fontSize: 13,
    fontWeight: '900',
    color: COLORS.text,
  },
  strikeCard: {
    backgroundColor: COLORS.surface,
    borderRadius: 14,
    marginHorizontal: 16,
    marginVertical: 5,
    padding: 12,
    borderWidth: 1,
    borderColor: COLORS.surfaceBorder,
  },
  atmCard: {
    borderColor: COLORS.primary,
    backgroundColor: '#161f36',
  },
  atmBadge: {
    alignSelf: 'center',
    backgroundColor: COLORS.primary,
    paddingHorizontal: 8,
    paddingVertical: 2,
    borderRadius: 4,
    marginBottom: 6,
  },
  atmBadgeText: {
    fontSize: 9,
    fontWeight: '900',
    color: '#ffffff',
    letterSpacing: 0.5,
  },
  strikeRow: {
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'space-between',
  },
  optionBtn: {
    flex: 1,
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'space-between',
    paddingHorizontal: 10,
    paddingVertical: 8,
    borderRadius: 10,
    borderWidth: 1,
  },
  callBtn: {
    backgroundColor: COLORS.profitLight,
    borderColor: 'rgba(16, 185, 129, 0.3)',
    marginRight: 6,
  },
  putBtn: {
    backgroundColor: COLORS.lossLight,
    borderColor: 'rgba(244, 63, 94, 0.3)',
    marginLeft: 6,
  },
  optionTypeCall: {
    fontSize: 10,
    fontWeight: '800',
    color: COLORS.profit,
  },
  optionTypePut: {
    fontSize: 10,
    fontWeight: '800',
    color: COLORS.loss,
  },
  optionLtp: {
    fontSize: 14,
    fontWeight: '900',
    color: COLORS.text,
    marginTop: 2,
  },
  tradePillCall: {
    backgroundColor: COLORS.profit,
    paddingHorizontal: 8,
    paddingVertical: 4,
    borderRadius: 6,
  },
  tradePillPut: {
    backgroundColor: COLORS.loss,
    paddingHorizontal: 8,
    paddingVertical: 4,
    borderRadius: 6,
  },
  tradePillText: {
    fontSize: 10,
    fontWeight: '900',
    color: '#ffffff',
  },
  centerStrike: {
    width: 65,
    alignItems: 'center',
    justifyContent: 'center',
  },
  strikeText: {
    fontSize: 14,
    fontWeight: '800',
    color: COLORS.textMuted,
  },
  strikeAtmText: {
    fontSize: 15,
    fontWeight: '900',
    color: COLORS.primary,
  },
});
