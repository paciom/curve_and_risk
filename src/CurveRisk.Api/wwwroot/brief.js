// The risk brief panel: runs the brief, shows its figures and commentary, and carries an approval back.
// Every value from the API is set as textContent. Nothing here builds markup from a string.

import { api } from "./api.js";
import { drawBuckets } from "./charts.js";
import { drawGraph } from "./graph.js";

const $ = id => document.getElementById(id);
const money = value => value.toLocaleString("en-US", { maximumFractionDigits: 0 });
const signed = value => (value > 0 ? "+" : "") + money(value);

const COMMENTARY = {
  Withheld: "The commentary was withheld: it stated figures the engine did not produce",
  Grounded: "",
  Unavailable: "No commentary: the model did not return a usable answer.",
  BudgetExhausted: "No commentary: the daily model budget is spent.",
  NotConfigured: "No commentary: no model provider is configured on this deployment. The figures below do not need one.",
  None: "",
};

let graph = null;
let approvalId = null;

function row(cells, className = "") {
  const tr = document.createElement("tr");
  tr.className = className;
  for (const cell of cells) {
    const td = document.createElement("td");
    td.textContent = cell;
    tr.append(td);
  }
  return tr;
}

function commentaryText(commentary) {
  if (commentary.status === "Grounded") return commentary.text;
  const named = commentary.ungroundedFigures.length > 0 ? ` (${commentary.ungroundedFigures.join("; ")}).` : "";
  return COMMENTARY[commentary.status] + named;
}

function showFigures(figures) {
  $("brief-figures").hidden = figures === null;
  if (figures === null) return;

  $("brief-totals").textContent =
    `Book PV ${signed(figures.totalPresentValue)} ${figures.currency} · DV01 ${signed(figures.totalParallelDv01)} ${figures.currency} per +1bp of zero rates`;
  $("brief-trades").tBodies[0].replaceChildren(
    ...figures.trades.map(trade => row([trade.tradeId, signed(trade.presentValue), trade.parRatePercent.toFixed(4), signed(trade.parallelDv01)])),
    row(["Total", signed(figures.totalPresentValue), "", signed(figures.totalParallelDv01)], "total"));
  $("brief-scenarios").tBodies[0].replaceChildren(
    ...figures.scenarios.map(line => row([line.name, line.parallelBp, line.steepenerBp, signed(line.profitAndLoss)])));
  drawBuckets($("brief-buckets"), figures.buckets, money);
}

function showApproval(pending) {
  approvalId = pending?.approvalId ?? null;
  $("brief-approval").hidden = pending === null;
  if (pending === null) return;
  $("brief-proposal").textContent =
    `Save the scenario "${pending.scenarioName}" (parallel ${pending.parallelBp}bp, steepener ${pending.steepenerBp}bp)? Nothing is saved until you approve.`;
}

function show(brief) {
  // The API does not store scenarios yet, so the page does not claim more than the engine did.
  const saved = brief.savedScenarioId ? ` (${brief.savedScenarioId}, kept for this request only: scenarios are not stored yet)` : "";
  $("brief-result").hidden = false;
  $("brief-status").textContent =
    `${brief.status}${saved} · ${brief.trace.length} step(s) · ${brief.usage.modelCalls} model call(s) · $${brief.usage.costUsd.toFixed(4)}`;
  $("brief-commentary").textContent = commentaryText(brief.commentary);
  showFigures(brief.figures);
  showApproval(brief.pendingApproval);
  drawGraph($("brief-graph"), graph, brief);
}

/** Runs a brief on the snapshot and shows it. */
export async function runBrief(snapshotId) {
  show(await api.brief(snapshotId, $("brief-offer-save").checked));
}

/** Sends the decision on the proposal the panel is showing. */
export async function decideBrief(approved) {
  const id = approvalId;

  // An approval id works once. Whatever comes back, this one is spent, so the buttons go.
  showApproval(null);
  show(await api.decideBrief(id, approved));
}

/** Draws the graph before anything has run. */
export async function initBrief() {
  graph = await api.briefGraph();
  drawGraph($("brief-graph"), graph);
}
