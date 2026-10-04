// Inline SVG charts, built with DOM calls only (no innerHTML), so no value from the API is ever parsed as markup.

const SVG = "http://www.w3.org/2000/svg";
const BOX = { width: 640, height: 240, left: 56, right: 16, top: 16, bottom: 32 };

export function element(name, attributes = {}, text = null) {
  const node = document.createElementNS(SVG, name);
  for (const [key, value] of Object.entries(attributes)) node.setAttribute(key, value);
  if (text !== null) node.textContent = text;
  return node;
}

function linear(domainMin, domainMax, rangeMin, rangeMax) {
  const span = domainMax - domainMin || 1;
  return value => rangeMin + ((value - domainMin) / span) * (rangeMax - rangeMin);
}

// Five round-ish ticks covering [min, max].
function ticks(min, max) {
  const step = (max - min) / 4;
  return [0, 1, 2, 3, 4].map(i => min + i * step);
}

function drawYAxis(svg, y, values, format) {
  for (const value of values) {
    svg.append(
      element("line", { class: "grid", x1: BOX.left, x2: BOX.width - BOX.right, y1: y(value), y2: y(value) }),
      element("text", { x: BOX.left - 8, y: y(value) + 4, "text-anchor": "end" }, format(value)));
  }
}

/** Zero rate against tenor. One series, so the caption names it and there is no legend. */
export function drawCurve(svg, pillars) {
  svg.replaceChildren();
  if (pillars.length === 0) return;

  const rates = pillars.map(p => p.zeroRatePercent);
  const pad = (Math.max(...rates) - Math.min(...rates)) * 0.15 || 0.1;
  const x = linear(0, Math.max(...pillars.map(p => p.tenorYears)), BOX.left, BOX.width - BOX.right);
  const y = linear(Math.min(...rates) - pad, Math.max(...rates) + pad, BOX.height - BOX.bottom, BOX.top);

  drawYAxis(svg, y, ticks(Math.min(...rates) - pad, Math.max(...rates) + pad), value => value.toFixed(2));
  svg.append(element("line", { class: "axis", x1: BOX.left, x2: BOX.width - BOX.right, y1: BOX.height - BOX.bottom, y2: BOX.height - BOX.bottom }));
  svg.append(element("polyline", { class: "line", points: pillars.map(p => `${x(p.tenorYears)},${y(p.zeroRatePercent)}`).join(" ") }));

  for (const pillar of pillars) {
    const point = element("circle", { class: "point", cx: x(pillar.tenorYears), cy: y(pillar.zeroRatePercent), r: 5 });
    point.append(element("title", {}, `${pillar.tenorYears}Y: ${pillar.zeroRatePercent.toFixed(4)}%`));
    svg.append(point, element("text", { x: x(pillar.tenorYears), y: BOX.height - BOX.bottom + 16, "text-anchor": "middle" }, `${pillar.tenorYears}Y`));
  }
}

/** Bucketed delta per pillar. Sign is carried by direction from the baseline and by the label, not by colour alone. */
export function drawBuckets(svg, buckets, format) {
  svg.replaceChildren();
  if (buckets.length === 0) return;

  const deltas = buckets.map(b => b.delta);
  const low = Math.min(0, ...deltas);
  const high = Math.max(0, ...deltas);
  const pad = (high - low) * 0.12 || 1;
  const y = linear(low - (low < 0 ? pad : 0), high + (high > 0 ? pad : 0), BOX.height - BOX.bottom, BOX.top);
  const band = (BOX.width - BOX.left - BOX.right) / buckets.length;

  drawYAxis(svg, y, ticks(low, high), format);
  buckets.forEach((bucket, index) => svg.append(...bar(bucket, { left: BOX.left + index * band, band, y, format })));
  svg.append(element("line", { class: "axis", x1: BOX.left, x2: BOX.width - BOX.right, y1: y(0), y2: y(0) }));
}

function bar(bucket, { left, band, y, format }) {
  const width = Math.min(band - 2, 40);
  const top = Math.min(y(bucket.delta), y(0));
  const height = Math.max(Math.abs(y(bucket.delta) - y(0)), bucket.delta === 0 ? 0 : 1);
  const centre = left + band / 2;

  const rect = element("rect", { class: bucket.delta < 0 ? "bar negative" : "bar", x: centre - width / 2, y: top, width, height, rx: 3 });
  rect.append(element("title", {}, `${bucket.tenorYears}Y: ${format(bucket.delta)}`));

  const labelY = bucket.delta < 0 ? top + height + 12 : top - 4;
  const nodes = [rect, element("text", { x: centre, y: BOX.height - 8, "text-anchor": "middle" }, `${bucket.tenorYears}Y`)];
  if (bucket.delta !== 0) nodes.push(element("text", { class: "value", x: centre, y: labelY, "text-anchor": "middle" }, format(bucket.delta)));
  return nodes;
}
