// Algorithmic Portfolio Tracker - Reactive Frontend Engine
const API_BASE = window.location.origin;

// Application State
const state = {
  quotes: {},
  portfolio: {
    id: "00000000-0000-0000-0000-000000000001",
    name: "Quant Alpha Momentum",
    baseCurrency: "USD",
    cashBalance: 45200.00,
    positions: [
      { symbol: "AAPL", quantity: 150, avgPrice: 215.30 },
      { symbol: "NVDA", quantity: 220, avgPrice: 120.40 },
      { symbol: "MSFT", quantity: 80, avgPrice: 420.10 },
      { symbol: "BTC-USD", quantity: 0.85, avgPrice: 62000.00 }
    ]
  },
  riskMetrics: null,
  monteCarloResult: null,
  logs: [],
  selectedSymbol: "AAPL",
  simulationDays: 30,
  simulationCount: 500,
  confidenceLevel: 0.95
};

// UI Elements
const el = {
  tickerStrip: document.getElementById("tickerStrip"),
  totalValuation: document.getElementById("totalValuation"),
  dailyPnL: document.getElementById("dailyPnL"),
  cashBalance: document.getElementById("cashBalance"),
  var95: document.getElementById("var95"),
  cvar95: document.getElementById("cvar95"),
  sharpeRatio: document.getElementById("sharpeRatio"),
  sortinoRatio: document.getElementById("sortinoRatio"),
  positionsTable: document.getElementById("positionsTableBody"),
  activityLog: document.getElementById("activityLog"),
  chartCanvas: document.getElementById("chartCanvas"),
  symbolSelect: document.getElementById("symbolSelect"),
  tradeModal: document.getElementById("tradeModal"),
  simulationModal: document.getElementById("simulationModal"),
  btnRunSim: document.getElementById("btnRunSim")
};

// Canvas Chart State
let chartContext = null;
if (el.chartCanvas) {
  chartContext = el.chartCanvas.getContext("2d");
}

// Log utility
function addLog(source, message) {
  const now = new Date().toLocaleTimeString();
  state.logs.unshift({ time: now, source, message });
  if (state.logs.length > 50) state.logs.pop();

  if (el.activityLog) {
    el.activityLog.innerHTML = state.logs
      .map(
        l => `
      <div class="log-entry">
        <span class="log-time">[${l.time}]</span>
        <span class="log-source">[${l.source}]</span>
        <span class="log-msg">${escapeHtml(l.message)}</span>
      </div>`
      )
      .join("");
  }
}

function escapeHtml(str) {
  return String(str).replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;');
}

// Fetch Market Data via ApiGateway
async function fetchMarketQuotes() {
  try {
    const res = await fetch(`${API_BASE}/api/market-data/tickers`);
    if (res.ok) {
      const tickers = await res.json();
      for (const sym of tickers) {
        try {
          const qRes = await fetch(`${API_BASE}/api/market-data/${encodeURIComponent(sym)}/quote`);
          if (qRes.ok) {
            const quote = await qRes.json();
            const prev = state.quotes[sym];
            state.quotes[sym] = {
              ...quote,
              change: prev ? quote.price - prev.price : 0
            };
          }
        } catch (e) {
          // ignore individual tick errors
        }
      }
      renderTickerStrip();
      updatePortfolioValuation();
    }
  } catch (err) {
    console.warn("Market data polling notice:", err);
  }
}

// Render Top Real-time Ticker Strip
function renderTickerStrip() {
  if (!el.tickerStrip) return;
  const items = Object.values(state.quotes);
  if (items.length === 0) return;

  el.tickerStrip.innerHTML = items
    .map(q => {
      const isUp = q.change >= 0;
      const changeSign = isUp ? "+" : "";
      const colorClass = isUp ? "price-up" : "price-down";
      const activeClass = state.selectedSymbol === q.symbol ? "style='background: #1c2940; border-radius: 4px;'" : "";
      return `
      <div class="ticker-item" ${activeClass} onclick="selectSymbol('${q.symbol}')">
        <strong>${q.symbol}</strong>
        <span>$${q.price.toLocaleString(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 2 })}</span>
        <span class="${colorClass}">(${changeSign}${q.change.toFixed(2)})</span>
      </div>
    `;
    })
    .join("");
}

window.selectSymbol = function(sym) {
  state.selectedSymbol = sym;
  renderTickerStrip();
  addLog("Terminal", `Selected ticker ${sym}`);
};

// Calculate and Render Portfolio Positions & Total Net Asset Value
function updatePortfolioValuation() {
  let equityValuation = 0;
  let totalCost = 0;

  const renderedRows = state.portfolio.positions.map(pos => {
    const quote = state.quotes[pos.symbol];
    const currentPrice = quote ? quote.price : pos.avgPrice;
    const marketValue = pos.quantity * currentPrice;
    const costBasis = pos.quantity * pos.avgPrice;
    const unPnL = marketValue - costBasis;
    const pnlPct = costBasis > 0 ? (unPnL / costBasis) * 100 : 0;
    const isUp = unPnL >= 0;

    equityValuation += marketValue;
    totalCost += costBasis;

    return `
      <tr>
        <td style="font-weight:700; color: var(--accent-cyan);">${pos.symbol}</td>
        <td>${pos.quantity.toLocaleString()}</td>
        <td>$${pos.avgPrice.toFixed(2)}</td>
        <td>$${currentPrice.toFixed(2)}</td>
        <td>$${marketValue.toLocaleString(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 2 })}</td>
        <td class="${isUp ? 'price-up' : 'price-down'}">
          ${isUp ? '+' : ''}$${unPnL.toLocaleString(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 2 })} (${pnlPct.toFixed(2)}%)
        </td>
      </tr>
    `;
  });

  const totalNAV = equityValuation + state.portfolio.cashBalance;
  const netPnL = (equityValuation - totalCost);
  const pnlPct = totalCost > 0 ? (netPnL / totalCost) * 100 : 0;

  if (el.totalValuation) el.totalValuation.textContent = `$${totalNAV.toLocaleString(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 2 })}`;
  if (el.cashBalance) el.cashBalance.textContent = `$${state.portfolio.cashBalance.toLocaleString(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 2 })}`;
  if (el.dailyPnL) {
    const isUp = netPnL >= 0;
    el.dailyPnL.className = `metric-value ${isUp ? 'price-up' : 'price-down'}`;
    el.dailyPnL.textContent = `${isUp ? '+' : ''}$${netPnL.toLocaleString(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 2 })} (${pnlPct.toFixed(2)}%)`;
  }

  if (el.positionsTable) {
    el.positionsTable.innerHTML = renderedRows.join("");
  }
}

// Quantitative Risk Metric Evaluation via ApiGateway / RiskEngine
async function evaluateRisk() {
  try {
    let totalValuation = state.portfolio.cashBalance;
    const assetWeights = {};

    state.portfolio.positions.forEach(p => {
      const q = state.quotes[p.symbol];
      const val = p.quantity * (q ? q.price : p.avgPrice);
      totalValuation += val;
      assetWeights[p.symbol] = val;
    });

    if (totalValuation > 0) {
      for (const k in assetWeights) {
        assetWeights[k] = assetWeights[k] / totalValuation;
      }
    }

    const res = await fetch(`${API_BASE}/api/risk/${state.portfolio.id}/evaluate?valuation=${encodeURIComponent(totalValuation)}`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(assetWeights)
    });

    if (res.ok) {
      const metrics = await res.json();
      state.riskMetrics = metrics;

      if (el.var95) el.var95.textContent = `$${Math.round(metrics.valueAtRisk95).toLocaleString()}`;
      if (el.cvar95) el.cvar95.textContent = `$${Math.round(metrics.conditionalVaR95).toLocaleString()}`;
      if (el.sharpeRatio) el.sharpeRatio.textContent = metrics.sharpeRatio.toFixed(2);
      if (el.sortinoRatio) el.sortinoRatio.textContent = metrics.sortinoRatio.toFixed(2);

      addLog("RiskEngine", `Calculated VaR 95%: $${Math.round(metrics.valueAtRisk95).toLocaleString()} | Sharpe: ${metrics.sharpeRatio.toFixed(2)}`);
    }
  } catch (err) {
    console.warn("Risk evaluation notice:", err);
  }
}

// Monte Carlo Simulation via ApiGateway / RiskEngine
async function runMonteCarlo() {
  if (el.btnRunSim) {
    el.btnRunSim.disabled = true;
    el.btnRunSim.textContent = "Simulating Trajectories...";
  }

  try {
    let totalValuation = state.portfolio.cashBalance;
    state.portfolio.positions.forEach(p => {
      const q = state.quotes[p.symbol];
      totalValuation += p.quantity * (q ? q.price : p.avgPrice);
    });

    const payload = {
      portfolioId: state.portfolio.id,
      simulationsCount: parseInt(state.simulationCount, 10),
      forecastDays: parseInt(state.simulationDays, 10),
      confidenceLevel: parseFloat(state.confidenceLevel)
    };

    const res = await fetch(`${API_BASE}/api/risk/monte-carlo?currentValuation=${encodeURIComponent(totalValuation)}`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(payload)
    });

    if (res.ok) {
      const data = await res.json();
      state.monteCarloResult = data;
      renderMonteCarloChart(data);
      addLog("MonteCarlo", `Generated ${data.simulationsCount} paths. Expected: $${Math.round(data.expectedFinalValue).toLocaleString()} | Worst: $${Math.round(data.worstCaseValue).toLocaleString()}`);
    }
  } catch (err) {
    addLog("Error", `Monte Carlo simulation failed: ${err.message}`);
  } finally {
    if (el.btnRunSim) {
      el.btnRunSim.disabled = false;
      el.btnRunSim.textContent = "Run Monte Carlo Simulation";
    }
  }
}

// High-Performance HTML5 Canvas Monte Carlo Chart Renderer
function renderMonteCarloChart(sim) {
  if (!chartContext || !el.chartCanvas) return;
  const ctx = chartContext;
  const w = el.chartCanvas.width;
  const h = el.chartCanvas.height;

  ctx.clearRect(0, 0, w, h);

  // Background grid
  ctx.strokeStyle = "#1b273b";
  ctx.lineWidth = 1;
  const gridSteps = 5;
  for (let i = 0; i <= gridSteps; i++) {
    const y = (h / gridSteps) * i;
    ctx.beginPath();
    ctx.moveTo(0, y);
    ctx.lineTo(w, y);
    ctx.stroke();
  }

  const days = sim.forecastDays || 30;
  const initial = sim.initialPortfolioValue || 100000;
  const minVal = Math.min(sim.worstCaseValue * 0.95, initial * 0.85);
  const maxVal = Math.max(sim.bestCaseValue * 1.05, initial * 1.25);
  const range = maxVal - minVal;

  function toX(day) {
    return (day / days) * (w - 40) + 10;
  }
  function toY(val) {
    return h - ((val - minVal) / range) * (h - 40) - 20;
  }

  // Draw 40 synthetic trajectory bands based on percentile distribution
  const count = 40;
  for (let i = 0; i < count; i++) {
    ctx.beginPath();
    ctx.moveTo(toX(0), toY(initial));

    let cur = initial;
    const trend = (sim.expectedFinalValue - initial) / days;
    const spread = (sim.bestCaseValue - sim.worstCaseValue) / (2 * Math.sqrt(days));

    for (let d = 1; d <= days; d++) {
      const z = (Math.random() - 0.5) * 2;
      cur += trend + z * spread;
      ctx.lineTo(toX(d), toY(cur));
    }

    ctx.strokeStyle = "rgba(14, 165, 233, 0.08)";
    ctx.lineWidth = 1;
    ctx.stroke();
  }

  // Expected Value line
  ctx.beginPath();
  ctx.moveTo(toX(0), toY(initial));
  ctx.lineTo(toX(days), toY(sim.expectedFinalValue));
  ctx.strokeStyle = "#06b6d4";
  ctx.lineWidth = 2.5;
  ctx.stroke();

  // Worst Case boundary line
  ctx.beginPath();
  ctx.moveTo(toX(0), toY(initial));
  ctx.lineTo(toX(days), toY(sim.worstCaseValue));
  ctx.strokeStyle = "#f43f5e";
  ctx.lineWidth = 2;
  ctx.setLineDash([4, 4]);
  ctx.stroke();
  ctx.setLineDash([]);

  // Best Case boundary line
  ctx.beginPath();
  ctx.moveTo(toX(0), toY(initial));
  ctx.lineTo(toX(days), toY(sim.bestCaseValue));
  ctx.strokeStyle = "#10b981";
  ctx.lineWidth = 2;
  ctx.setLineDash([4, 4]);
  ctx.stroke();
  ctx.setLineDash([]);

  // Labels
  ctx.fillStyle = "#94a3b8";
  ctx.font = "11px Inter, sans-serif";
  ctx.fillText(`Initial: $${Math.round(initial).toLocaleString()}`, 15, toY(initial) - 8);
  ctx.fillStyle = "#06b6d4";
  ctx.fillText(`Expected: $${Math.round(sim.expectedFinalValue).toLocaleString()}`, toX(days) - 130, toY(sim.expectedFinalValue) - 8);
  ctx.fillStyle = "#f43f5e";
  ctx.fillText(`VaR Worst: $${Math.round(sim.worstCaseValue).toLocaleString()}`, toX(days) - 130, toY(sim.worstCaseValue) + 16);
  ctx.fillStyle = "#10b981";
  ctx.fillText(`Best: $${Math.round(sim.bestCaseValue).toLocaleString()}`, toX(days) - 130, toY(sim.bestCaseValue) - 8);
}

// Modal Handlers & Trade Execution
window.openTradeModal = function() {
  if (el.tradeModal) el.tradeModal.classList.add("active");
};

window.closeTradeModal = function() {
  if (el.tradeModal) el.tradeModal.classList.remove("active");
};

window.submitTrade = function(event) {
  event.preventDefault();
  const symbol = document.getElementById("tradeSymbol").value.toUpperCase();
  const quantity = parseFloat(document.getElementById("tradeQuantity").value);
  const type = document.getElementById("tradeType").value;

  const quote = state.quotes[symbol];
  const executionPrice = quote ? quote.price : 100.00;
  const totalCost = quantity * executionPrice;

  if (type === "BUY") {
    if (state.portfolio.cashBalance < totalCost) {
      alert("Insufficient cash balance for this order.");
      return;
    }
    state.portfolio.cashBalance -= totalCost;
    const existing = state.portfolio.positions.find(p => p.symbol === symbol);
    if (existing) {
      const newTotalCost = (existing.quantity * existing.avgPrice) + totalCost;
      existing.quantity += quantity;
      existing.avgPrice = newTotalCost / existing.quantity;
    } else {
      state.portfolio.positions.push({ symbol, quantity, avgPrice: executionPrice });
    }
  } else {
    const existing = state.portfolio.positions.find(p => p.symbol === symbol);
    if (!existing || existing.quantity < quantity) {
      alert("Insufficient position quantity to sell.");
      return;
    }
    existing.quantity -= quantity;
    state.portfolio.cashBalance += totalCost;
    if (existing.quantity === 0) {
      state.portfolio.positions = state.portfolio.positions.filter(p => p.symbol !== symbol);
    }
  }

  addLog("TradingDesk", `Executed ${type} ${quantity} ${symbol} @ $${executionPrice.toFixed(2)} (Total: $${totalCost.toLocaleString(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 2 })})`);
  closeTradeModal();
  updatePortfolioValuation();
  evaluateRisk();
  runMonteCarlo();
};

// Canvas Resize helper
function resizeCanvas() {
  if (el.chartCanvas) {
    const container = el.chartCanvas.parentElement;
    if (container) {
      el.chartCanvas.width = container.clientWidth;
      el.chartCanvas.height = container.clientHeight || 280;
      if (state.monteCarloResult) {
        renderMonteCarloChart(state.monteCarloResult);
      }
    }
  }
}

// Initial Bootstrapping
window.addEventListener("DOMContentLoaded", async () => {
  window.addEventListener("resize", resizeCanvas);
  resizeCanvas();

  addLog("System", "Algorithmic Portfolio Tracker initialized.");
  addLog("System", "Connecting to YARP ApiGateway at " + API_BASE);

  // Poll for market quotes every 1.5s
  await fetchMarketQuotes();
  await evaluateRisk();
  await runMonteCarlo();

  setInterval(fetchMarketQuotes, 1500);
  setInterval(evaluateRisk, 5000);
});
