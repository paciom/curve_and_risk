// Draws a workflow graph from the structure the API serves, and marks the path one run took.
// Built with DOM calls only (no innerHTML): node names come from the API and are set as text.

import { element } from "./charts.js";

const WIDTH = 640;
const ROW = 58;
const TOP = 24;
const BOX_HEIGHT = 30;
const MAX_BOX_WIDTH = 150;
const BEND = 28;
const MARGIN = 6;

// A node's row is the fewest edges from the start. A cycle needs no special case: a node keeps the first row it gets.
function rowsOf(graph) {
  const start = graph.nodes.find(node => node.kind === "Start").name;
  const depth = new Map([[start, 0]]);
  const queue = [start];
  while (queue.length > 0) {
    const name = queue.shift();
    for (const edge of graph.edges.filter(candidate => candidate.from === name && !depth.has(candidate.to))) {
      depth.set(edge.to, depth.get(name) + 1);
      queue.push(edge.to);
    }
  }
  const rows = [];
  for (const node of graph.nodes) (rows[depth.get(node.name)] ??= []).push(node.name);
  return rows;
}

function layout(graph) {
  const boxes = new Map();
  const rows = rowsOf(graph);
  rows.forEach((row, rowIndex) => row.forEach((name, index) => boxes.set(name, {
    x: (WIDTH * (index + 0.5)) / row.length,
    y: TOP + rowIndex * ROW,
    halfWidth: Math.min(MAX_BOX_WIDTH, WIDTH / row.length - 10) / 2,
  })));
  return { boxes, height: TOP + rows.length * ROW - ROW / 2 + BOX_HEIGHT };
}

// Where the line from `from` meets the edge of the box at `to`, so the arrowhead is not hidden under it.
function entry(from, to) {
  const dx = from.x - to.x;
  const dy = from.y - to.y;
  const scale = Math.min(to.halfWidth / Math.abs(dx || 1e-9), (BOX_HEIGHT / 2) / Math.abs(dy || 1e-9));
  return { x: to.x + dx * scale, y: to.y + dy * scale };
}

// The nodes a run passed through, in order: start, each visit, any pause it went through, and where it stopped.
function pathOf(graph, run) {
  const kind = name => graph.nodes.find(node => node.name === name)?.kind;
  const linked = (from, to) => graph.edges.some(edge => edge.from === from && edge.to === to);
  const pauses = graph.nodes.filter(node => node.kind === "Pause").map(node => node.name);
  const stop = run.status === "AwaitingApproval" ? pauses[0] : run.status;

  const path = [];
  for (const name of ["start", ...run.trace.map(visit => visit.node), stop]) {
    const previous = path.at(-1);
    const through = pauses.find(pause => linked(previous, pause) && linked(pause, name));
    if (previous && !linked(previous, name) && kind(previous) !== "ParallelBranch" && through) path.push(through);
    path.push(name);
  }
  return path;
}

// An edge was taken when its target follows its source in the path with only parallel siblings between them.
function taken(graph, path, edge) {
  const isBranch = name => graph.nodes.find(node => node.name === name)?.kind === "ParallelBranch";
  return path.some((name, index) => {
    if (name !== edge.from) return false;
    let next = index + 1;
    while (next < path.length && path[next] !== edge.to && isBranch(path[next])) next++;
    return path[next] === edge.to;
  });
}

function stateOf(node, run, path) {
  if (!run) return "idle";
  if (node.kind === "Pause" && run.status === "AwaitingApproval") return "waiting";
  return path.includes(node.name) ? "ran" : "idle";
}

// True when the straight line between two boxes passes behind a third, which would make it read as two edges.
function hidden(boxes, edge) {
  const from = boxes.get(edge.from);
  const to = boxes.get(edge.to);
  return [...boxes].some(([name, box]) => {
    if (name === edge.from || name === edge.to || (box.y - from.y) * (box.y - to.y) >= 0) return false;
    const x = from.x + ((to.x - from.x) * (box.y - from.y)) / (to.y - from.y);
    return Math.abs(x - box.x) < box.halfWidth + 6;
  });
}

// A straight line where that is clear; otherwise a curve that leaves sideways, away from the target, and comes back in.
function route(boxes, edge) {
  const from = boxes.get(edge.from);
  const to = boxes.get(edge.to);
  if (!hidden(boxes, edge)) {
    const tip = entry(from, to);
    return `M ${from.x} ${from.y} L ${tip.x} ${tip.y}`;
  }
  const bend = from.x - Math.sign(to.x - from.x || 1) * (from.halfWidth + BEND);
  const side = Math.max(MARGIN, Math.min(WIDTH - MARGIN, bend));
  const tip = entry({ x: side, y: to.y }, to);
  return `M ${from.x} ${from.y} C ${side} ${from.y}, ${side} ${to.y}, ${tip.x} ${tip.y}`;
}

function drawEdge(svg, boxes, edge, isTaken) {
  const line = element("path", { class: isTaken ? "edge taken" : "edge", d: route(boxes, edge), "marker-end": "url(#arrow)" });
  if (edge.label) line.append(element("title", {}, `${edge.from} to ${edge.to}: ${edge.label}`));
  svg.append(line);
}

function drawNode(svg, box, node, { state, visits }) {
  const group = element("g", { class: `step ${state}` });
  const rounded = node.kind === "Start" || node.kind === "End" ? BOX_HEIGHT / 2 : 4;
  group.append(
    element("rect", { x: box.x - box.halfWidth, y: box.y - BOX_HEIGHT / 2, width: box.halfWidth * 2, height: BOX_HEIGHT, rx: rounded }),
    element("text", { x: box.x, y: box.y + 4, "text-anchor": "middle" }, visits > 1 ? `${node.name} ×${visits}` : node.name),
    element("title", {}, `${node.name} (${node.kind}): ${state === "idle" ? "not run" : state}`));
  svg.append(group);
}

function arrowMarker() {
  const marker = element("marker", { id: "arrow", viewBox: "0 0 10 10", refX: 9, refY: 5, markerWidth: 7, markerHeight: 7, orient: "auto-start-reverse" });
  marker.append(element("path", { class: "arrow", d: "M 0 0 L 10 5 L 0 10 z" }));
  const defs = element("defs");
  defs.append(marker);
  return defs;
}

/** Draws the graph. With a run (status and trace), the steps it executed and the edges it followed are marked. */
export function drawGraph(svg, graph, run = null) {
  const { boxes, height } = layout(graph);
  const path = run ? pathOf(graph, run) : [];
  svg.setAttribute("viewBox", `0 0 ${WIDTH} ${height}`);
  svg.replaceChildren(arrowMarker());

  // Edges first, so the boxes sit on top of the lines that pass behind them.
  for (const edge of graph.edges) drawEdge(svg, boxes, edge, run !== null && taken(graph, path, edge));
  for (const node of graph.nodes) {
    const visits = run ? run.trace.filter(visit => visit.node === node.name).length : 0;
    drawNode(svg, boxes.get(node.name), node, { state: stateOf(node, run, path), visits });
  }
}
