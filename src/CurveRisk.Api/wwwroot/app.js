import { api, waitForRiskRun } from "./api.js";
import { drawBuckets, drawCurve } from "./charts.js";

const DEMO_QUOTES = [["1Y", 4.05], ["2Y", 3.8], ["3Y", 3.72], ["5Y", 3.78], ["7Y", 3.88], ["10Y", 4.02], ["20Y", 4.22], ["30Y", 4.15]];
const DEMO_TRADES = [
  { tradeId: "T-1001", notionalAmount: 100000000, fixedRatePercent: 3.5, direction: "PayFixed", tenor: "5Y", description: "Client hedge, pay-fixed 5Y" },
  { tradeId: "T-1002", notionalAmount: 50000000, fixedRatePercent: 4.25, direction: "ReceiveFixed", tenor: "10Y", description: "Receive-fixed 10Y asset swap overlay" },
  { tradeId: "T-1003", notionalAmount: 250000000, fixedRatePercent: 3.95, direction: "PayFixed", tenor: "2Y", description: "Short-dated payer" },
];

const state = { snapshotId: null, selected: null };
const $ = id => document.getElementById(id);
const money = value => value.toLocaleString("en-US", { maximumFractionDigits: 0 });
const signed = value => (value > 0 ? "+" : "") + money(value);

function setStatus(text) {
  $("status").textContent = text;
}

// Text from the API (trade descriptions are free text) is only ever set as textContent.
function row(cells, textColumns = []) {
  const tr = document.createElement("tr");
  cells.forEach((cell, index) => {
    const td = document.createElement("td");
    td.textContent = cell;
    if (textColumns.includes(index)) td.className = "text";
    tr.append(td);
  });
  return tr;
}

async function showCurve() {
  const curve = await api.curve(state.snapshotId);
  drawCurve($("curve-chart"), curve.pillars);
  $("curve-table").tBodies[0].replaceChildren(
    ...curve.pillars.map(p => row([p.tenorYears, p.zeroRatePercent.toFixed(4), p.discountFactor.toFixed(6)])));
}

async function showBook() {
  const { items } = await api.trades();
  const valuations = await Promise.all(items.map(trade => api.value(state.snapshotId, trade.tradeId)));
  const rows = items.map((trade, index) => {
    const tr = row(
      [trade.tradeId, trade.direction, money(trade.notionalAmount), trade.fixedRatePercent.toFixed(2), trade.tenor,
        valuations[index].parRatePercent.toFixed(4), signed(valuations[index].presentValue), trade.description],
      [7]);
    tr.tabIndex = 0;
    tr.addEventListener("click", () => select(trade.tradeId));
    tr.addEventListener("keydown", event => event.key === "Enter" && select(trade.tradeId));
    tr.dataset.tradeId = trade.tradeId;
    return tr;
  });
  $("trades-table").tBodies[0].replaceChildren(...rows);
  if (items.length > 0) await select(state.selected ?? items[0].tradeId);
}

async function select(tradeId) {
  state.selected = tradeId;
  for (const tr of $("trades-table").tBodies[0].rows) tr.setAttribute("aria-selected", String(tr.dataset.tradeId === tradeId));
  $("risk-trade").textContent = tradeId;
  $("scenario-result").textContent = "";

  const started = await api.startRiskRun(state.snapshotId, [tradeId]);
  const run = await waitForRiskRun(started.id);
  if (run.status !== "Completed") throw new Error(run.error ?? "The risk run failed.");

  const risk = run.results[0];
  $("risk-summary").textContent = `Parallel DV01 ${signed(risk.parallelDv01)} ${risk.currency} per +1bp of zero rates`;
  drawBuckets($("risk-chart"), risk.buckets, money);
}

async function loadDemo() {
  const snapshot = await api.createSnapshot({
    curveId: "USD-SOFR",
    asOf: "2026-09-30",
    interpolation: $("interpolation").value,
    quotes: DEMO_QUOTES.map(([tenor, ratePercent]) => ({ tenor, ratePercent })),
  });
  state.snapshotId = snapshot.id;

  // Idempotent: a second click re-sends the same keys and gets the existing trades back.
  await Promise.all(DEMO_TRADES.map(trade => api.createTrade(trade)));
  await showCurve();
  await showBook();
  setStatus(`Snapshot ${snapshot.asOf}, ${snapshot.interpolation}`);
}

async function runScenario() {
  const result = await api.scenario({
    snapshotId: state.snapshotId,
    tradeId: state.selected,
    parallelBp: Number($("parallel").value),
    steepenerBp: Number($("steepener").value),
  });
  $("scenario-result").textContent =
    `${result.tradeId}: P&L ${signed(result.profitAndLoss)} ${result.currency} (PV ${money(result.basePresentValue)} to ${money(result.shockedPresentValue)})`;
}

async function ask() {
  const answer = await api.ask(state.snapshotId, $("question").value);
  $("copilot-answer").hidden = false;
  $("copilot-status").textContent = `${answer.status} · ${answer.modelCalls} model call(s) · $${answer.costUsd.toFixed(4)}`;
  $("copilot-text").textContent = answer.answer;
  const tools = answer.toolCalls.map(call => `${call.name} (${call.outcome})`).join(", ");
  $("copilot-tools").textContent = answer.toolCalls.length === 0 ? "No tools used." : `Tools: ${tools}`;
}

// Runs an action with the triggering control disabled, and shows any failure where the user is looking.
function guarded(action, control, needsSnapshot = true) {
  return async event => {
    event?.preventDefault();
    if (needsSnapshot && !state.snapshotId) return setStatus("Load the demo market first.");
    control.disabled = true;
    try {
      await action();
    } catch (error) {
      setStatus(error.message);
    } finally {
      control.disabled = false;
    }
  };
}

$("load-demo").addEventListener("click", guarded(loadDemo, $("load-demo"), false));
$("interpolation").addEventListener("change", guarded(loadDemo, $("load-demo"), false));
$("scenario-form").addEventListener("submit", guarded(runScenario, $("scenario-form").querySelector("button")));
$("copilot-form").addEventListener("submit", guarded(ask, $("copilot-form").querySelector("button")));
