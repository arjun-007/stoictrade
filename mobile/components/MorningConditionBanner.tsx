import React, { useState, useEffect } from 'react';
import { View, Text, TouchableOpacity, StyleSheet, ActivityIndicator } from 'react-native';
import { AlertTriangle, TrendingUp, TrendingDown, Activity, ShieldAlert, ChevronDown, ChevronUp, AlertOctagon } from 'lucide-react-native';
import { apiClient } from '../lib/api';

interface BuyerTrapAlert {
  trapId: string;
  name: string;
  severity: string;
  description: string;
  footprintEvidence: string;
  buyerDirective: string;
}

interface MorningMarketCondition {
  spotPrice: number;
  vwap: number;
  isPriorDayCompressed: boolean;
  compressionType: string;
  priorDayRange: number;
  liquidityRejection: string;
  openPrice0915: number;
  maxRejectionWickRatio: number;
  vwapStatus: string;
  vwapSlope: number;
  pcr: number;
  institutionalFloorStrike: number;
  institutionalCeilingStrike: number;
  marketRegime: string;
  regimeLabel: string;
  actionDirective: string;
  overtradingShieldActive: boolean;
  detectedTraps?: BuyerTrapAlert[];
}

export default function MorningConditionBanner() {
  const [data, setData] = useState<MorningMarketCondition | null>(null);
  const [expanded, setExpanded] = useState<boolean>(false);
  const [loading, setLoading] = useState(true);

  const fetchCondition = async () => {
    try {
      const res = await apiClient.get('/api/MarketData/morning-condition');
      setData(res.data);
    } catch (err) {
      console.warn("Failed to fetch morning condition", err);
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    fetchCondition();
    const interval = setInterval(fetchCondition, 15000);
    return () => clearInterval(interval);
  }, []);

  if (loading && !data) return <ActivityIndicator size="small" style={styles.loader} />;
  if (!data) return null;

  const hasTraps = data.detectedTraps && data.detectedTraps.length > 0;
  const isChoppy = data.overtradingShieldActive || data.marketRegime === 'CHOPPY_RANGE_BOUND' || hasTraps;
  const isBullish = data.marketRegime === 'BULLISH_TREND_DAY';
  const isBearish = data.marketRegime === 'BEARISH_TREND_DAY';

  let theme = {
    bg: '#eff6ff',
    border: '#bfdbfe',
    badge: '#2563eb',
    textPrimary: '#1e3a8a',
    textSecondary: '#1d4ed8',
    Icon: Activity
  };

  if (isChoppy) {
    theme = { bg: '#fff1f2', border: '#fecdd3', badge: '#e11d48', textPrimary: '#881337', textSecondary: '#9f1239', Icon: ShieldAlert };
  } else if (isBullish) {
    theme = { bg: '#ecfdf5', border: '#a7f3d0', badge: '#059669', textPrimary: '#022c22', textSecondary: '#047857', Icon: TrendingUp };
  } else if (isBearish) {
    theme = { bg: '#fffbeb', border: '#fde68a', badge: '#d97706', textPrimary: '#451a03', textSecondary: '#92400e', Icon: TrendingDown };
  }

  const { Icon } = theme;

  return (
    <View style={[styles.container, { backgroundColor: theme.bg, borderColor: theme.border }]}>
      <View style={styles.header}>
        <View style={styles.titleRow}>
          <Icon color={theme.textSecondary} size={20} />
          <View style={styles.titleTextContainer}>
            <View style={[styles.badge, { backgroundColor: theme.badge }]}>
              <Text style={styles.badgeText}>
                {hasTraps ? `⚠️ ${data.detectedTraps?.length} TRAP(S) ACTIVE` : isChoppy ? 'OVERTRADING SHIELD ACTIVE' : '09:15–10:00 AM SCANNER'}
              </Text>
            </View>
            <Text style={[styles.regimeLabel, { color: theme.textPrimary }]} numberOfLines={1}>
              {data.regimeLabel}
            </Text>
          </View>
        </View>

        <TouchableOpacity onPress={() => setExpanded(!expanded)} style={styles.expandBtn}>
          <Text style={styles.expandBtnText}>{hasTraps ? 'View Traps' : 'Footprint'}</Text>
          {expanded ? <ChevronUp size={16} color="#333" /> : <ChevronDown size={16} color="#333" />}
        </TouchableOpacity>
      </View>

      <Text style={[styles.directive, { color: theme.textSecondary }]}>
        👉 {data.actionDirective}
      </Text>

      <View style={styles.quickMetricsRow}>
        <Text style={styles.quickMetricText}>Spot: ₹{data.spotPrice.toFixed(1)}</Text>
        <Text style={styles.quickMetricText}>VWAP: ₹{data.vwap.toFixed(1)}</Text>
        <Text style={styles.quickMetricText}>PCR: {data.pcr.toFixed(2)}</Text>
      </View>

      {expanded && (
        <View style={styles.expandedContainer}>
          {hasTraps && (
            <View style={styles.trapsContainer}>
              <View style={styles.trapsHeader}>
                <AlertOctagon size={16} color="#e11d48" />
                <Text style={styles.trapsTitle}>Identified Option Buyer Traps</Text>
              </View>
              {data.detectedTraps?.map((trap, idx) => (
                <View key={idx} style={styles.trapCard}>
                  <View style={styles.trapCardHeader}>
                    <Text style={styles.trapName}>{trap.name}</Text>
                    <Text style={styles.trapSeverity}>{trap.severity}</Text>
                  </View>
                  <Text style={styles.trapDescription}>{trap.description}</Text>
                  <Text style={styles.trapFootprint}>🔍 {trap.footprintEvidence}</Text>
                  <Text style={styles.trapDirective}>🛡️ {trap.buyerDirective}</Text>
                </View>
              ))}
            </View>
          )}

          <Text style={styles.sectionTitle}>4-Step Institutional Footprint Breakdown</Text>
          
          <View style={styles.grid}>
            <View style={styles.gridItem}>
              <Text style={styles.gridLabel}>1. Daily Volatility</Text>
              <Text style={styles.gridValue}>{data.isPriorDayCompressed ? 'Compressed' : 'Normal'} ({data.compressionType})</Text>
            </View>
            <View style={styles.gridItem}>
              <Text style={styles.gridLabel}>2. 15m Wick</Text>
              <Text style={styles.gridValue}>{data.liquidityRejection} ({(data.maxRejectionWickRatio * 100).toFixed(0)}%)</Text>
            </View>
            <View style={styles.gridItem}>
              <Text style={styles.gridLabel}>3. VWAP Anchor</Text>
              <Text style={styles.gridValue}>{data.vwapStatus}</Text>
            </View>
            <View style={styles.gridItem}>
              <Text style={styles.gridLabel}>4. Option Skew</Text>
              <Text style={styles.gridValue}>Floor: {data.institutionalFloorStrike} | Ceiling: {data.institutionalCeilingStrike}</Text>
            </View>
          </View>
        </View>
      )}
    </View>
  );
}

const styles = StyleSheet.create({
  loader: { marginVertical: 10 },
  container: {
    borderWidth: 1,
    borderRadius: 8,
    padding: 12,
    marginHorizontal: 16,
    marginVertical: 8,
  },
  header: {
    flexDirection: 'row',
    justifyContent: 'space-between',
    alignItems: 'flex-start',
  },
  titleRow: {
    flexDirection: 'row',
    flex: 1,
  },
  titleTextContainer: {
    marginLeft: 8,
    flex: 1,
  },
  badge: {
    alignSelf: 'flex-start',
    paddingHorizontal: 6,
    paddingVertical: 2,
    borderRadius: 4,
    marginBottom: 4,
  },
  badgeText: {
    color: '#fff',
    fontSize: 9,
    fontWeight: 'bold',
  },
  regimeLabel: {
    fontSize: 14,
    fontWeight: 'bold',
  },
  expandBtn: {
    flexDirection: 'row',
    alignItems: 'center',
    backgroundColor: 'rgba(255,255,255,0.7)',
    paddingHorizontal: 8,
    paddingVertical: 4,
    borderRadius: 6,
    borderWidth: 1,
    borderColor: '#ddd',
  },
  expandBtnText: {
    fontSize: 11,
    fontWeight: '600',
    marginRight: 4,
    color: '#333',
  },
  directive: {
    fontSize: 12,
    fontWeight: '600',
    marginTop: 8,
  },
  quickMetricsRow: {
    flexDirection: 'row',
    marginTop: 8,
    backgroundColor: 'rgba(255,255,255,0.5)',
    padding: 6,
    borderRadius: 6,
    gap: 12,
  },
  quickMetricText: {
    fontSize: 11,
    fontWeight: '500',
    color: '#444',
  },
  expandedContainer: {
    marginTop: 12,
    paddingTop: 12,
    borderTopWidth: 1,
    borderTopColor: 'rgba(0,0,0,0.1)',
  },
  trapsContainer: {
    marginBottom: 12,
  },
  trapsHeader: {
    flexDirection: 'row',
    alignItems: 'center',
    marginBottom: 8,
  },
  trapsTitle: {
    fontSize: 12,
    fontWeight: 'bold',
    color: '#e11d48',
    marginLeft: 4,
    textTransform: 'uppercase',
  },
  trapCard: {
    backgroundColor: '#ffe4e6',
    borderWidth: 1,
    borderColor: '#fda4af',
    borderRadius: 6,
    padding: 8,
    marginBottom: 8,
  },
  trapCardHeader: {
    flexDirection: 'row',
    justifyContent: 'space-between',
  },
  trapName: {
    fontSize: 12,
    fontWeight: 'bold',
    color: '#881337',
  },
  trapSeverity: {
    fontSize: 10,
    fontWeight: 'bold',
    backgroundColor: '#e11d48',
    color: '#fff',
    paddingHorizontal: 4,
    borderRadius: 2,
  },
  trapDescription: {
    fontSize: 11,
    color: '#881337',
    marginTop: 4,
  },
  trapFootprint: {
    fontSize: 10,
    backgroundColor: 'rgba(255,255,255,0.5)',
    padding: 4,
    marginTop: 4,
    borderRadius: 4,
  },
  trapDirective: {
    fontSize: 10,
    fontWeight: 'bold',
    color: '#881337',
    marginTop: 4,
  },
  sectionTitle: {
    fontSize: 11,
    fontWeight: 'bold',
    color: '#666',
    textTransform: 'uppercase',
    marginBottom: 8,
  },
  grid: {
    flexDirection: 'row',
    flexWrap: 'wrap',
    gap: 8,
  },
  gridItem: {
    width: '48%',
    backgroundColor: 'rgba(255,255,255,0.7)',
    borderWidth: 1,
    borderColor: 'rgba(0,0,0,0.1)',
    borderRadius: 6,
    padding: 8,
  },
  gridLabel: {
    fontSize: 10,
    fontWeight: 'bold',
    color: '#555',
  },
  gridValue: {
    fontSize: 11,
    color: '#222',
    marginTop: 4,
  },
});
