\# System Context & Architecture Directive  
Act as an expert Full-Stack Trading Systems Architect and .NET Developer. Your task is to build a high-performance, private algorithmic and manual trading application integrated with the \*\*Fyers API (v3)\*\*.

The system MUST enforce a strictly decoupled architecture: a Headless .NET Backend Engine and a separate Next.js Frontend UI. The primary goal of this system is strict behavioral risk control (preventing emotional trading) using a centralized Redis-based lock, asset restrictions, and enforced time-delay mechanisms.

\#\# Tech Stack Requirements  
\* \*\*Frontend:\*\* Next.js (React), Tailwind CSS. (Target deployment: Railway.app).  
\* \*\*Backend:\*\* .NET 8 Web API. (Target deployment: DigitalOcean Droplet Ubuntu VPS).  
\* \*\*In-Memory State/Locks:\*\* Redis with AOF persistence enabled (Mandatory for sub-millisecond RMS checks, Kill Switch state, and Time Locks).  
\* \*\*Persistent Database:\*\* SQLite (For saving Strategy configurations, order book history, and trade logs).  
\* \*\*Deployment:\*\* Docker Compose (Backend API \+ Redis container \+ SQLite volume mapped to host).

\#\# Phase 1: Backend Modules (.NET 8 Engine)  
Implement the following core micro-services/middlewares:

1\. \*\*Risk Management System (RMS) \[CRITICAL\]:\*\* Create a middleware that intercepts EVERY order (both systematic and manual). It evaluates state against Redis on every tick.  
    \* \*\*Time Window Rule:\*\* Trading is ONLY allowed between 09:30 AM and 03:10 PM (IST). If an order is placed outside this window, reject it immediately and return the validation message: "Trading only allowed between 9:30 AM and 3:10 PM."  
    \* \*\*Instrument Rule:\*\* Only allow trades for \`NIFTY\` Index Options and Equity Stocks (\`EQ\`). Reject all Stock Options or other indices/instruments.  
    \* \*\*Kill Switch Rules:\*\* The system must track and trigger a lockdown if any of the following occur: Maximum loss per trade breached, Maximum daily loss breached, Maximum trades per day reached, Maximum failed trades in a day reached.  
    \* \*\*Pre-Trade Rule:\*\* Check India VIX. Reject signals if VIX is outside the configured Minimum/Maximum thresholds.  
    \* If the Redis key \`kill\_switch:{accountId}\` status is "LOCKED", reject all orders immediately.

2\. \*\*Kill Switch Sequence:\*\* A specific service that executes atomically when RMS limits are breached:  
    \* Fetch & cancel all pending orders via the \*\*Fyers API\*\*.  
    \* Fetch all active positions.  
    \* Fire opposite market orders to square off.  
    \* Set the Redis \`kill\_switch:{accountId}\` flag to LOCKED with a 24-hour expiration.

3\. \*\*Manual Access Gate (Secure Time-Delayed TOTP):\*\* Implement strict time and state locks for generating the \*\*Fyers\*\* TOTP login code to prevent emotional manual overrides.  
    \* \*\*Security Setup:\*\* The TOTP secret must be stored as an AES-256 encrypted string in the \`.env\` file (\`TOTP\_SECRET\_ENC\`). It is decrypted at runtime using a separate \`MASTER\_KEY\`.  
    \* \*\*Rule 1 (Hard Time Lock):\*\* On weekdays, completely RESTRICT/BLOCK TOTP generation between 06:00 AM \- 09:30 AM and 15:10 PM \- 16:00 PM (IST).  
    \* \*\*Rule 2 (Kill Switch Lock):\*\* If the \`kill\_switch:{accountId}\` is ACTIVE, the TOTP generation must be restricted and forced into the 20-minute behavioral cooling-off delay.  
    \* \*\*The Flow:\*\*  
        1\. \`POST /totp/request\`: Sets \`totp\_req:{accountId}\` in Redis with the current timestamp. No PIN is asked yet.  
        2\. \`POST /totp/generate\`:   
            \* Check Time Lock: If inside restricted hours, throw exception.  
            \* Check Delay: If Kill Switch is active, verify 20 minutes have passed since the request. If not, throw exception ("Cooling period active").  
            \* Validate PIN.  
            \* Decrypt TOTP secret and return the 6-digit code.

4\. \*\*Strategy Engine (Algorithmic Brain):\*\* Implement logic for trading high-Delta, In-The-Money (ITM) Nifty Index options 2 to 3 weeks out from expiry. Code the following mathematical strategies:  
    \* \*\*Opening Range Breakout (ORB) with VWAP\*\*.  
    \* \*\*EMA Pullback (9-EMA / 21-EMA)\*\*.  
    \* \*\*Volatility Squeeze (Bollinger Band Breakout)\*\*.  
    \* \*\*Supertrend Rider (Automated trailing stop)\*\*.  
    \* \*\*NR7 Breakout (Volatility Contraction)\*\*.  
    \* \*\*MACD Zero-Line Crossover\*\*.  
    \* All trades must apply the configured "Per trade stop loss point" and "Per trade gain point".

5\. \*\*Market Data Ticker:\*\* Implement a WebSocket client that listens to live \*\*Fyers Order and Data WebSockets\*\* and broadcasts standard JSON payloads to the Next.js frontend via SignalR or WebSockets. \*(Note to Agent: Fyers provides this data feed completely free).\*

6\. \*\*SQLite Context:\*\* Entity Framework Core setup to store \`StrategyConfigs\` (parameters like Target %, Trailing SL, Timeframe) and \`TradeLogs\`.

\#\# Phase 2: Frontend UI Structure (Next.js)  
Implement a responsive layout using a professional base theme color of \`\#0033a0\`. Data must be fetched via standard JWT-secured REST and WebSockets.

1\. \*\*Navigation Strategy:\*\* Persistent left sidebar on Desktop. Breakpoint at \`768px\` to convert this into a fixed Bottom Navigation Bar for Mobile.  
2\. \*\*Master Dashboard & Risk Control:\*\* Displays consolidated daily P\&L, available margin, and active positions. MUST include:  
    \* A prominent red "Master Kill Switch" button (triggers backend sequence).  
    \* "Request Manual Access" button (triggers the backend delay timer and shows a live countdown UI before prompting for the PIN).  
3\. \*\*Strategy Configuration Page:\*\* Build a dashboard tab that reads/writes to the backend SQLite DB. Include UI elements to toggle predefined algorithms ON/OFF and adjust parameters.  
4\. \*\*Watchlist & Order Execution:\*\* A searchable list pulling from cached instrument data.   
    \* On desktop, render execution forms as a central floating modal.   
    \* On mobile, render as a Bottom Sheet sliding up from the screen bottom.   
    \* Must include an async call to calculate required margin before submission.  
    \* Must display the validation message ("Trading only allowed between 9:30 AM and 3:10 PM") if the user attempts to submit outside hours.

\#\# Phase 3: Infrastructure & DevOps  
Generate a production-ready \`docker-compose.yml\` file designed for a DigitalOcean Droplet. It must include:  
\* The \`.NET 8\` Backend API container.  
\* A \`Redis\` container (configured with \`--appendonly yes\` so locks survive a server restart).  
\* Volume mappings so the SQLite \`.db\` file and Redis AOF file persist on the host Linux machine.

\#\# Execution Rules for the Agent  
1\. Begin by scaffolding the directory structure: \`/backend\` (.NET 8\) and \`/frontend\` (Next.js).  
2\. Write the \`docker-compose.yml\` and the \*\*RMS Kill Switch Middleware\*\* first, as they form the non-bypassable core of the application.  
3\. Implement the \*\*Manual Access Gate (Time-Delayed TOTP)\*\* logic exactly as specified, ensuring the Time Window blocks and Kill Switch delays are fully enforced.  
4\. Ensure strict separation of concerns: The Next.js frontend should NEVER hold API keys, secrets, or the TOTP seed.  
5\. Provide the code iteratively, explaining how to run the Docker environment locally for testing.  
