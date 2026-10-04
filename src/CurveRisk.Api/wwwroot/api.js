// Thin client for /api/v1. Every failure becomes an Error carrying the problem detail the API sent.

const BASE = "/api/v1";

async function request(method, path, body, headers = {}) {
  const response = await fetch(BASE + path, {
    method,
    headers: { ...(body ? { "Content-Type": "application/json" } : {}), ...headers },
    body: body ? JSON.stringify(body) : undefined,
  });
  if (response.status === 204) return null;

  const payload = await response.json().catch(() => ({}));
  if (!response.ok) {
    const error = new Error(payload.detail ?? payload.title ?? `Request failed (${response.status})`);
    error.status = response.status;
    throw error;
  }
  return payload;
}

export const api = {
  createSnapshot: snapshot => request("POST", "/market-snapshots", snapshot),
  curve: snapshotId => request("GET", `/market-snapshots/${snapshotId}/curve`),
  trades: () => request("GET", "/trades?pageSize=100"),
  createTrade: trade => request("POST", "/trades", trade, { "Idempotency-Key": `demo-${trade.tradeId}` }),
  value: (snapshotId, tradeId) => request("POST", "/valuations", { snapshotId, tradeId }),
  scenario: run => request("POST", "/scenario-runs", run),
  startRiskRun: (snapshotId, tradeIds) => request("POST", "/risk-runs", { snapshotId, tradeIds }),
  riskRun: id => request("GET", `/risk-runs/${id}`),
  ask: (snapshotId, question) => request("POST", "/copilot/answers", { snapshotId, question }),
};

/** Polls a risk run until it leaves Pending, or gives up after about ten seconds. */
export async function waitForRiskRun(id) {
  for (let attempt = 0; attempt < 50; attempt++) {
    const run = await api.riskRun(id);
    if (run.status !== "Pending") return run;
    await new Promise(resolve => setTimeout(resolve, 200));
  }
  throw new Error("The risk run is taking longer than expected.");
}
