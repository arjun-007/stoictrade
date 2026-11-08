# StoicTrade — Full Application & Session Context

> **Purpose of this document**: Provide complete, self-contained architectural and operational context for **StoicTrade** so any future session can immediately resume development, debugging, or live trading analysis without re-discovering the codebase.

---

## 1. Application Overview

**StoicTrade** is an automated and semi-automated ("ApprovalRequired" / "SignalOnly" / "Automatic") options trading system for **NSE NIFTY 50 Index Options** (`CE` / `PE`) integrated with **Fyers API v3**. It supports seamless switching between **Paper Trading (`TradeMode = "Paper"`)** and **Live Broker Execution (`TradeMode = "Live"`)** using real-time Fyers market data in both modes.

### Repository Layout
```text
StoicTrade/
├── backend/                                  # .NET 8 Web API (C#)
│   ├── Controllers/
│   │   ├── ApprovalController.cs             # Approve/Deny pending signals in ApprovalRequired mode
│   │   ├── AuthController.cs                 # JWT login for web/mobile UI
│   │   ├── EngineController.cs               # Start/Stop engine, Fyers OAuth callback, manual square-off, live signal log
│   │   ├── GlobalSettingsController.cs       # Read/Update GlobalSettings (TradeMode, SL/Target, Risk limits, Expiry pref)
│   │   ├── KillSwitchController.cs           # Master Kill Switch trigger & status
│   │   ├── MarketDataController.cs           # Spot quotes, Option Chain JSON, Morning Market Condition analysis
│   │   ├── OrdersController.cs               # Manual order execution (Paper & Live)
│   │   ├── PortfolioController.cs            # Summary, Net Positions (Paper & Live), Holdings, 30-day P&L History
│   │   ├── StrategyConfigController.cs       # Enable/Disable & configure individual strategies
│   │   ├── StrategyGroupsController.cs       # Configure multi-strategy consensus squads
│   │   └── TotpController.cs                 # Time-locked & cooling-delayed Fyers TOTP generator
│   ├── Data/
│   │   └── AppDbContext.cs                   # EF Core SQLite context & default seed data
│   ├── Models/                               # Entities: GlobalSettings, StrategyConfig, StrategyGroup, PaperPosition, ClosedTrade, DailyPnLRecord, TradeLog, Signal, MarketData, MorningMarketCondition
│   ├── Services/
│   │   ├── FyersApiService.cs                # Fyers v3 OAuth, Quotes, History (/data/history), Funds, Positions, Orders (/api/v3/orders/sync)
│   │   ├── OrderManagementService.cs         # Entry/Exit execution, Broker fill sync, Active Position Monitor (Trailing SL, Partial Booking, Target/SL)
│   │   ├── RiskEngine.cs                     # Pre-trade RMS checks, Daily Loss/Trade limits, Concurrency & HTF conflict resolution
│   │   ├── KillSwitchService.cs              # Atomic emergency square-off & 24h Redis lock
│   │   ├── RedisService.cs                   # StackExchange.Redis wrapper with in-memory fallback
│   │   ├── TimeZoneHelper.cs                 # Cross-platform IST (Asia/Kolkata / India Standard Time) resolver
│   │   ├── MarketData/
│   │   │   ├── FyersDataPollingService.cs    # Background service polling Fyers spot & multi-expiry option chains
│   │   │   ├── MarketDataAggregatorService.cs# Tick-to-candle (1m, 5m, 15m, 1D) aggregator & historical loader
│   │   │   ├── MarketDataCache.cs            # Thread-safe cache for SpotData, OptionChain JSON, and OptionQuoteEntry
│   │   │   ├── OptionChainAnalysisService.cs # Live PCR, Max Pain, Put Floor, and Call Ceiling calculator
│   │   │   └── MorningMarketConditionService.cs # 7-trap Option Buyer Trap detector & morning regime classifier
│   │   └── Strategies/
│   │       ├── StrategyEngineService.cs      # Main 1s background loop evaluating strategies & squads during market hours
│   │       ├── TradingStrategies.cs          # 8 core strategies + StrategyFilterHelper
│   │       ├── SmartMoneyStructureHelper.cs  # Wyckoff Spring, Fair Value Gap (FVG), and Volume Profile POC calculations
│   │       ├── OptionSelectionEngine.cs      # Optimal 1-strike ITM CE/PE selector across weekly/monthly expiries
│   │       └── SignalAggregatorService.cs    # Priority & RVOL deduplication across simultaneous signals
│   └── stoictrade.db                         # SQLite database file
├── frontend/                                 # Next.js 15+ (React, TypeScript, Tailwind CSS) Web Dashboard
├── mobile/                                   # Expo / React Native mobile app
├── docker-compose.yml / docker-compose.prod.yml
└── README.md / .readme.md / docs/session_context.md
```

---

## 2. Daily Market Session Timeline (IST)

Understanding the exact chronological flow in `StrategyEngineService` and `FyersDataPollingService` is critical when diagnosing intraday behavior:

| Time (IST) | Event / Guard | Service | Details |
| :--- | :--- | :--- | :--- |
| **09:00:00 AM** | Daily Redis State Reset | `StrategyEngineService` | Deletes all `strategy_state_*` keys in Redis so no strategy carries over a stuck state from the prior day. |
| **09:00 – 09:14:59 AM** | Pre-Market Mode | `FyersDataPollingService` | If the engine is started early (e.g., at **09:10 AM**):<br>• Spot quotes are updated for UI display, but **ticks are NOT fed to `MarketDataAggregatorService`** (no pre-9:15 AM candles are created).<br>• Option quotes polled before 9:15 AM are stored in `_preMarketOptionSnapshots` with `TradedToday = false` so yesterday's closing option prices can never be used for trade execution.<br>• Between `09:14:00` and `09:15:00 AM`, polling interval tightens from `15s` to `1s` for instant transition at `09:15:00 AM`. |
| **09:15:00 AM** | Market Open Flush & Reset | `StrategyEngineService` & `FyersDataPollingService` | • Clears `RecentSignals` queue.<br>• Calls `_cache.ClearOptionPrices()` to flush all pre-market option quotes and chain snapshots.<br>• Calls `_aggregator.PurgeOffMarketCandles()` to strip any non-market-hour intraday candles.<br>• Deletes any `strategy_state_*` keys. |
| **09:15:00 – 09:15:10 AM** | Quote Stabilization Window | `StrategyEngineService` | Waits 10 seconds before running `MonitorActivePositionsAsync` so carry-forward positions are only evaluated against fresh post-open quotes. |
| **09:15:00 – 09:16:00 AM** | Opening Candle Stabilization | `StrategyEngineService` & `TradingStrategies` | Blocks new strategy ENTRY evaluations during the first 60 seconds of market open until the first 1-minute candle (`09:15–09:16`) completes and live option strikes print valid today's trades (`TradedToday == true`). |
| **09:16:00 AM – 03:10:00 PM** | Active Trading Window | `StrategyEngineService`, `RiskEngine`, `OrderManagementService` | • Polls Fyers every `2s`.<br>• Evaluates active strategies & squads every `1s`.<br>• Monitors open positions every `1s` for Breakeven activation, Trailing SL, Partial Profit Booking (Target 1), Target 2, and Stop-Loss. |
| **11:30 AM – 01:15 PM** | Midday Chop Filter | `StrategyFilterHelper` | Requires elevated relative volume (`RVOL >= 2.0x–2.5x`) during low-volume midday theta bleed hours. |
| **03:10:00 PM** | Daily Auto-Square-Off | `StrategyEngineService` -> `OrderManagementService` | Closes all open Paper and Live positions (`Daily_310PM_AutoSquareOff`). |
| **03:30:00 PM** | NSE Market Close | `StrategyEngineService` & `MarketDataAggregatorService` | Stops strategy evaluation and stops aggregating intraday candles. |
| **03:40:00 PM** | Live Engine Auto-Stop | `StrategyEngineService` | Automatically stops the engine (`_fyersApi.StopEngine()`) if in Live mode. |

---

## 3. Option Strike Selection & Live Quote Validation

### Expiry & Strike Selection (`OptionSelectionEngine.cs`)
- **Bias Mapping**:
  - `BUY` (Bullish index signal) -> Selects **1-strike ITM Call (`CE`)** (`atmStrike - 50`).
  - `BUY_PE` / `SELL` (Bearish index signal) -> Selects **1-strike ITM Put (`PE`)** (`atmStrike + 50`).
  - Because StoicTrade is an option-buying system, the order action sent to `OrderManagementService` and Fyers for both `CE` and `PE` entries is always `"BUY"` (and `"SELL"` on exit).
- **Expiry Preference (`GlobalSettings.TargetExpiryPreference`)**:
  - `CurrentWeek` (`0`), `NextWeek` (`1`, default), `TwoWeeksOut` (`2`), `Monthly` (`3`).
  - NIFTY weekly contracts expire on **Tuesdays** (adjusted backward for NSE holidays in `FyersDataPollingService.GetNseAdjustedExpiry`).

### Live Today's Option Quote Verification (`MarketDataCache.cs` & `FyersDataPollingService.cs`)
To prevent executing trades at **yesterday's closing price** when the engine is started at 9:10 AM:
1. Every cached option price (`OptionQuoteEntry`) stores:
   - `Price` (`decimal`)
   - `TimestampUtc` (`DateTime`)
   - `TradedToday` (`bool`)
2. In `FyersDataPollingService.CacheIndividualOptionPrices`:
   - If polled before `09:15:00 AM IST`, the quote is recorded in `_preMarketOptionSnapshots[canonicalKey] = (lp, vol, openPrice)` and marked `TradedToday = false`.
   - During market hours (`>= 09:15:00 AM IST`), `TradedToday` is only `true` if:
     - `volume > 0` and `open_price > 0`,
     - Fyers' `tt` (last trade timestamp, if present) converts to today's IST date (`ttDateIst == todayIst`), and
     - If a pre-market snapshot was captured for that strike, at least one of `(lp, volume, open_price)` has changed from its pre-market value.
3. In `OptionSelectionEngine.ResolveOptionLtp(symbol, requireLiveTodayQuote: true)`:
   - Rejects any quote where `TradedToday == false` or `TimestampUtc < today 09:15:00 AM IST`.
   - Never falls back to `150m` or spot price for option entry orders. If a valid live quote is unavailable, the entry is aborted and the strategy's Redis state key (`strategy_state_{id}`) is deleted so it can re-evaluate cleanly on a subsequent tick.

---

## 4. Paper Mode vs. Live Mode Execution Pipeline

Both modes share the same `PaperPositions` table in SQLite so that `OrderManagementService.MonitorActivePositionsAsync` can uniformly manage **Trailing Stop-Loss**, **Breakeven activation**, and **Target 1 Partial Profit Booking**.

| Stage | Paper Mode (`TradeMode == "Paper"`) | Live Mode (`TradeMode == "Live"`) |
| :--- | :--- | :--- |
| **Entry (`BUY`)** | Uses verified live Fyers option LTP (`ResolveOptionLtp`), creates/updates `PaperPosition`, and logs `EXECUTED` in `TradeLogs`. | 1. Calls `_fyersApiService.PlaceOrderAsync(symbol, "BUY", qty, ltp)` **first**.<br>2. If Fyers rejects the order, logs `Status = "REJECTED"` in `TradeLogs` and returns `false` (no local position created).<br>3. If Fyers succeeds, waits `400ms` and queries `_fyersApiService.GetPositionsAsync()` to fetch the actual broker `buyAvg` for that symbol.<br>4. Anchors `BuyAvg`, `TargetPrice`, and `StopLossPrice` to the real broker fill price and saves `PaperPosition` + `TradeLog` with Fyers' `OrderId`. |
| **Partial Exit (Target 1 on Multi-Lot)** | Deducts 50% lots (`partialQty`), locks SL above breakeven, extends Target 2. | Sends `PlaceOrderAsync(symbol, "SELL", partialQty, currentLtp)` to Fyers **first**; only updates local `NetQty` if Fyers confirms execution. |
| **Full Exit (Strategy Exit / Target / SL)** | Closes `PaperPosition` (`NetQty = 0`), records `ClosedTrade` (`TradeMode = "Paper"`) and `TradeLog`. | Sends `PlaceOrderAsync(symbol, "SELL", exitQty, currentLtp)` to Fyers **first**. If Fyers fails, checks `GetPositionsAsync()` to see if the user already closed the position manually on Fyers; if still open on Fyers, keeps `NetQty > 0` to retry on the next tick. |
| **Portfolio UI (`/api/portfolio/*`)** | Reads `PaperPositions` enriched with live option LTP from `MarketDataCache`. | Queries Fyers `/api/v3/funds` and `/api/v3/positions` directly, and enriches `netPositions` with `targetPrice`, `stopLossPrice`, `trailingStopLossPoint`, and `strategyName` from local DB. |

---

## 5. Chronological Log of Recent Fixes (2026-11-08)

### Issue Reported
When activating (starting) the engine at **09:10 AM** before market open, as soon as the clock hit **09:15 AM**, a trade was immediately executed using the **previous trading day's closing price** of the option strike instead of today's opening/trading price, and then exited within a few seconds with a large artificial profit/loss margin once today's real price arrived.

### Root Causes Identified & Fixed
1. **Pre-Market Option Quotes Cached as Live LTP (`FyersDataPollingService.cs`, `MarketDataCache.cs`, `OptionSelectionEngine.cs`)**
   - *Cause*: Polling between 09:10 and 09:14:59 AM cached yesterday's closing option prices (`lp`) in `MarketDataCache`. At 09:15:00 AM, `StrategyEngineService` (1s loop) executed before `FyersDataPollingService` (15s off-market sleep) fetched post-open prices, or while Fyers still returned yesterday's cached quote object for a few seconds after 09:15:00 AM.
   - *Fix*: Added `OptionQuoteEntry` with `TimestampUtc` and `TradedToday`, `_preMarketOptionSnapshots` comparison, `tt` (last trade time) date verification, automatic cache flush at `09:15:00 AM IST`, and `1s` polling during `09:14:00 – 09:15:00 AM IST`.
2. **Pre-Market Ticks Polluting Intraday Candles & Immediate Strategy Firing at 09:15:00 AM (`MarketDataAggregatorService.cs`, `TradingStrategies.cs`, `StrategyEngineService.cs`)**
   - *Cause*: `UpdateTick` was called during 09:10–09:14 AM, creating fake pre-market candles on today's date, and strategies evaluated at 09:15:00 AM on yesterday's closing candles (`SupertrendStrategy` checked `lastCandle.Close > lastSt.SuperTrend` which was immediately true on tick 1).
   - *Fix*: Blocked `UpdateTick` outside `09:15:00 – 15:30:00 IST`, added `PurgeOffMarketCandles()`, added a `09:15:00 – 09:16:00 AM IST` opening stabilization buffer, enforced `StrategyFilterHelper.HasValidTodaySessionCandles` across all 8 strategies, and restricted `OrbStrategy` to candles `>= 09:15:00 AM IST`.
3. **Hardcoded `150m` Fallback & Unverified Live Order Sequence (`OrderManagementService.cs`, `StrategyEngineService.cs`, `RiskEngine.cs`, `OrdersController.cs`, `ApprovalController.cs`)**
   - *Cause*: When `ResolveOptionLtp` returned `null`, entry logic fell back to `150m`, and in Live mode local DB positions were saved before verifying Fyers order acceptance.
   - *Fix*: Removed `150m` entry fallbacks (entries abort cleanly if live today's option LTP is unavailable), enforced broker-first order execution in Live mode, and synced actual broker `buyAvg` from Fyers `GetPositionsAsync()` after live fills.
4. **Fyers History Endpoint & Fallback Seed Drift (`FyersApiService.cs`, `MarketDataAggregatorService.cs`)**
   - *Cause*: `GetHistoricalCandlesAsync` used `/api/v3/history/` instead of `/data/history?cont_flag=1`, and `GenerateSeedCandles` drifted away from `basePrice` up to `DateTime.UtcNow`.
   - *Fix*: Updated history endpoint to `https://api-t1.fyers.in/data/history?...&cont_flag=1` and anchored fallback seed candles to end at `basePrice` at yesterday's `15:25 IST` close.

