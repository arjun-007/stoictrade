# StoicTrade — Algorithmic & Behavioral Risk-Controlled Trading Platform

StoicTrade is a full-stack algorithmic and manual options trading platform integrated with **Fyers API v3** for the Indian National Stock Exchange (**NSE NIFTY 50 Index Options** & **Equities**). It is built around a decoupled architecture featuring a headless **.NET 8 Web API** trading engine, **Redis** real-time state & lock management, **SQLite** persistence, a **Next.js (React/TypeScript)** web dashboard, and an **Expo / React Native** mobile app.

---

## 1. System Architecture

| Layer | Technology | Responsibilities |
| :--- | :--- | :--- |
| **Backend Engine** | `.NET 8 Web API` (`backend/`) | Strategy evaluation (1s tick loop), Fyers v3 market data polling (2s live loop), option strike selection, Risk Management System (RMS), order execution (Paper & Live), Trailing Stop-Loss / Target monitoring, and 3:10 PM IST auto-square-off. |
| **State & Locks** | `Redis` (with AOF persistence) | Sub-millisecond Kill Switch locks (`kill_switch:{accountId}`), strategy position state tracking (`strategy_state_{id}`), pending approval queues (`pending_approval:{accountId}:{signalId}`), TOTP time-delay gate (`totp_req:{accountId}`), and Fyers token persistence (`fyers_access_token`). |
| **Database** | `SQLite` (`backend/stoictrade.db`) | Persistent storage for `GlobalSettings`, `StrategyConfigs`, `StrategyGroups`, `PaperPositions` (active & intraday ledger for both Paper & Live tracking), `ClosedTrades`, `DailyPnLRecords`, and `TradeLogs`. |
| **Web Frontend** | `Next.js` + `Tailwind CSS` (`frontend/`) | Live dashboard, P&L analytics, Morning Market Condition & Buyer Trap scanner, Strategy & Squad configuration, Watchlist/Option Chain, Manual Order execution, and Time-Delayed TOTP gate. |
| **Mobile App** | `Expo / React Native` (`mobile/`) | Mobile companion for monitoring positions, P&L, signals, and triggering emergency risk controls. |
| **Deployment** | `Docker Compose` + `Caddy` | Production deployment (`docker-compose.prod.yml`) on Ubuntu VPS (DigitalOcean) with Caddy reverse proxy and Next.js on Railway / VPS. |

---

## 2. Core Backend Services (`backend/Services/`)

1. **`FyersDataPollingService` (`Services/MarketData/FyersDataPollingService.cs`)**
   - Polls Fyers `/data/quotes` for `NSE:NIFTY50-INDEX`, `BANKNIFTY`, `INDIAVIX`, and NIFTY weekly/monthly option chains (`ATM ± 8 strikes`).
   - **Pre-Market & Market-Open Safeguards**:
     - Never feeds pre-market (`< 09:15:00 AM IST`) or post-market (`> 03:30:00 PM IST`) ticks into intraday candle aggregation.
     - Tracks pre-market (`< 09:15 AM`) option snapshots (`lp`, `volume`, `open_price`) so that at `09:15:00 AM` market open, any option strike still returning yesterday's closing quote from Fyers is flagged `TradedToday = false` and rejected for trade execution until live today's volume (`volume > 0`), `open_price > 0`, and `tt` (last trade time) confirm a live today's tick.
     - Transitions to a 1-second polling interval during `09:14:00 – 09:15:00 AM IST` and flushes all pre-market cached option quotes at `09:15:00 AM IST`.

2. **`MarketDataAggregatorService` (`Services/MarketData/MarketDataAggregatorService.cs`)**
   - Maintains 1m, 5m, 15m, and 1D OHLCV candles from Fyers `/data/history` (`cont_flag=1`) and live market-hours ticks.
   - Purges any off-market candles (`< 09:15 AM` or `> 03:30 PM IST`) at market open.

3. **`StrategyEngineService` (`Services/Strategies/StrategyEngineService.cs`)**
   - **09:00 AM IST**: Flushes stale `strategy_state_*` keys from Redis.
   - **09:15 AM IST**: Clears the Live Signal Log, flushes pre-market option quotes from `MarketDataCache`, purges off-market candles, and resets strategy states.
   - **09:15:00 – 09:15:10 AM IST**: Post-open quote stabilization window before position monitoring begins.
   - **09:15:00 – 09:16:00 AM IST**: Opening candle stabilization buffer — blocks new strategy entry evaluations until the first 1-minute candle of today's session completes and live option prices populate.
   - **09:16:00 AM – 03:30:00 PM IST**: Evaluates enabled standalone strategies (`StrategyConfigs`) and multi-strategy consensus squads (`StrategyGroups`), resolves optimal ITM option contracts via `OptionSelectionEngine`, and routes signals through `SignalAggregatorService` -> `RiskEngine` -> `OrderManagementService`.
   - **03:10 PM IST**: Triggers daily `AutoSquareOffAllPositionsAsync` for all open positions.
   - **03:40 PM IST**: Automatically stops the engine in Live mode.

4. **`OptionSelectionEngine` (`Services/Strategies/OptionSelectionEngine.cs`)**
   - Selects 1-strike ITM `CE` (Bullish) or `PE` (Bearish) contracts for `CurrentWeek` (0), `NextWeek` (1, default), `TwoWeeksOut` (2), or `Monthly` (3) expiries, accounting for NSE Tuesday weekly expiries and NSE trading holidays.
   - `ResolveOptionLtp(symbol, requireLiveTodayQuote: true)` strictly rejects any option quote cached prior to `09:15:00 AM IST` today or with `TradedToday == false`.

5. **`TradingStrategies` (`Services/Strategies/TradingStrategies.cs`)**
   - 8 built-in institutional & technical strategies:
     1. `Supertrend Rider`
     2. `Opening Range Breakout (ORB)`
     3. `EMA Pullback` (9/21 EMA + ADX + optional FVG)
     4. `Bollinger Volatility Squeeze` (with optional Volume Profile POC gate)
     5. `NR7 Breakout`
     6. `MACD Zero-Line`
     7. `Wyckoff Spring (Liquidity Sweep)`
     8. `Fair Value Gap (FVG) / Order Block`
   - All strategies enforce `StrategyFilterHelper.HasValidTodaySessionCandles(candles, minTodayCandles: 1)` so no strategy can fire at market open on yesterday's closing candles.

6. **`RiskEngine` (`Services/RiskEngine.cs`)**
   - Enforces Kill Switch lock, Trading Window (`TradingWindowStart` – `TradingWindowEnd`), India VIX bounds, Instrument allowlist (`NIFTY` options & `-EQ`), `MaxTradesPerDay`, `MaxFailedTrades`, `MaxDailyLoss` (realized + unrealized), `MaxCapitalPerTrade`, anti-pyramiding, cross-strategy directional conflict resolution (with 15m HTF overwrite), and `MaxActivePositions`.

7. **`OrderManagementService` (`Services/OrderManagementService.cs`)**
   - Unified execution and active position monitor (`MonitorActivePositionsAsync`) for both **Paper** and **Live** modes:
     - **Live Mode (`TradeMode == "Live"`)**: Dispatches `BUY`/`SELL` market orders to Fyers (`/api/v3/orders/sync`) *first*. If Fyers rejects an order, local position creation/closure is aborted. After a live `BUY` fill, queries Fyers `GetPositionsAsync()` to sync the actual broker `buyAvg` and anchors `BuyAvg`, `TargetPrice`, and `StopLossPrice` to the real fill price.
     - **Active Position Monitoring**: Tracks Breakeven lock (`TrailingActivationPoint`), step-up `TrailingStopLossPoint`, Target 1 Partial Profit Booking (50% lot booking on multi-lot positions + runner extension to Target 2), hard Stop-Loss, and `MaxLossPerTrade`.

---

## 3. Running Locally & Building

### Backend (.NET 8)
```powershell
# Build using the repo-local .NET 8 SDK
.\.dotnet\dotnet.exe build backend/StoicTrade.Api.csproj

# Run backend API
.\.dotnet\dotnet.exe run --project backend/StoicTrade.Api.csproj
```

### Frontend (Next.js)
```powershell
cd frontend
npm install
npm run dev
```

### Production Deployment (Docker Compose)
```bash
docker compose -f docker-compose.prod.yml up -d --build
```

For full day-to-day engineering context, recent bug fixes, and operational checklists, see [`docs/session_context.md`](docs/session_context.md).

