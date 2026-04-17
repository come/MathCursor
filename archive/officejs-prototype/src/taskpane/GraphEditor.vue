<script setup lang="ts">
import { ref, reactive, onMounted, onUnmounted, computed } from "vue";

interface Point {
  name: string;
  x: number;
  y: number;
  curveIndex: number;
}

interface Curve {
  name: string;
  color: string;
  points: Point[];
}

const COLORS = ["#e94560", "#00d2ff", "#ffd700", "#7bed9f", "#ff6b81", "#a29bfe"];

const canvasRef = ref<HTMLCanvasElement | null>(null);
const curves = reactive<Curve[]>([
  { name: "Courbe 1", color: COLORS[0], points: [] },
]);

const transform = reactive({
  offsetX: 0,
  offsetY: 0,
  scale: 40, // pixels per unit
});

const namingPoint = ref<{ x: number; y: number } | null>(null);
const pointName = ref("");
const activeCurveIndex = ref(0);
const statusMessage = ref("");

const BG = "#fafafa";
const GRID_COLOR = "#e8e8e8";
const GRID_COLOR_MAJOR = "#d0d0d0";
const AXIS_COLOR = "#333";

function worldToScreen(wx: number, wy: number): [number, number] {
  const canvas = canvasRef.value!;
  const cx = canvas.width / 2 + transform.offsetX + wx * transform.scale;
  const cy = canvas.height / 2 + transform.offsetY - wy * transform.scale;
  return [cx, cy];
}

function screenToWorld(sx: number, sy: number): [number, number] {
  const canvas = canvasRef.value!;
  const wx = (sx - canvas.width / 2 - transform.offsetX) / transform.scale;
  const wy = -(sy - canvas.height / 2 - transform.offsetY) / transform.scale;
  return [wx, wy];
}

function snapToHalf(val: number): number {
  return Math.round(val * 2) / 2;
}

function draw() {
  const canvas = canvasRef.value;
  if (!canvas) return;
  const ctx = canvas.getContext("2d")!;
  const W = canvas.width;
  const H = canvas.height;

  ctx.fillStyle = BG;
  ctx.fillRect(0, 0, W, H);

  // Adaptive grid step
  let step = 1;
  if (transform.scale < 20) step = 5;
  if (transform.scale < 8) step = 10;

  // Grid
  const [minWx, maxWy] = screenToWorld(0, 0);
  const [maxWx, minWy] = screenToWorld(W, H);

  const startX = Math.floor(minWx / step) * step;
  const endX = Math.ceil(maxWx / step) * step;
  const startY = Math.floor(minWy / step) * step;
  const endY = Math.ceil(maxWy / step) * step;

  for (let x = startX; x <= endX; x += step) {
    const [sx] = worldToScreen(x, 0);
    ctx.strokeStyle = x === 0 ? AXIS_COLOR : x % (step * 5) === 0 ? GRID_COLOR_MAJOR : GRID_COLOR;
    ctx.lineWidth = x === 0 ? 1.5 : 0.5;
    ctx.beginPath();
    ctx.moveTo(sx, 0);
    ctx.lineTo(sx, H);
    ctx.stroke();
  }

  for (let y = startY; y <= endY; y += step) {
    const [, sy] = worldToScreen(0, y);
    ctx.strokeStyle = y === 0 ? AXIS_COLOR : y % (step * 5) === 0 ? GRID_COLOR_MAJOR : GRID_COLOR;
    ctx.lineWidth = y === 0 ? 1.5 : 0.5;
    ctx.beginPath();
    ctx.moveTo(0, sy);
    ctx.lineTo(W, sy);
    ctx.stroke();
  }

  // Axis arrows + labels
  const [ox, oy] = worldToScreen(0, 0);
  ctx.fillStyle = AXIS_COLOR;
  ctx.font = "12px sans-serif";
  ctx.fillText("x", W - 16, oy - 6);
  ctx.fillText("y", ox + 6, 14);

  // Draw points per curve
  for (const curve of curves) {
    for (const pt of curve.points) {
      const [sx, sy] = worldToScreen(pt.x, pt.y);
      // Dot
      ctx.beginPath();
      ctx.arc(sx, sy, 5, 0, Math.PI * 2);
      ctx.fillStyle = curve.color;
      ctx.fill();
      // Label
      ctx.fillStyle = "#1d1d1f";
      ctx.font = "bold 11px sans-serif";
      ctx.fillText(`${pt.name}(${pt.x},${pt.y})`, sx + 8, sy - 8);
    }

    // Connect points with lines
    if (curve.points.length > 1) {
      ctx.strokeStyle = curve.color;
      ctx.lineWidth = 2;
      ctx.beginPath();
      const [sx0, sy0] = worldToScreen(curve.points[0].x, curve.points[0].y);
      ctx.moveTo(sx0, sy0);
      for (let i = 1; i < curve.points.length; i++) {
        const [sx, sy] = worldToScreen(curve.points[i].x, curve.points[i].y);
        ctx.lineTo(sx, sy);
      }
      ctx.stroke();
    }
  }

  requestAnimationFrame(draw);
}

// Mouse interactions
let isPanning = false;
let lastMouse = { x: 0, y: 0 };

function onMouseDown(e: MouseEvent) {
  if (e.shiftKey) {
    isPanning = true;
    lastMouse = { x: e.offsetX, y: e.offsetY };
    return;
  }

  // Place a point
  const [wx, wy] = screenToWorld(e.offsetX, e.offsetY);
  namingPoint.value = { x: snapToHalf(wx), y: snapToHalf(wy) };
  pointName.value = "";
}

function onMouseMove(e: MouseEvent) {
  if (isPanning) {
    transform.offsetX += e.offsetX - lastMouse.x;
    transform.offsetY += e.offsetY - lastMouse.y;
    lastMouse = { x: e.offsetX, y: e.offsetY };
  }
}

function onMouseUp() {
  isPanning = false;
}

function onWheel(e: WheelEvent) {
  e.preventDefault();
  const factor = e.deltaY < 0 ? 1.1 : 0.9;
  transform.scale = Math.max(4, Math.min(200, transform.scale * factor));
}

function confirmPoint() {
  if (!namingPoint.value || !pointName.value.trim()) return;
  const curve = curves[activeCurveIndex.value];
  curve.points.push({
    name: pointName.value.trim(),
    x: namingPoint.value.x,
    y: namingPoint.value.y,
    curveIndex: activeCurveIndex.value,
  });
  namingPoint.value = null;
}

function removePoint(curveIdx: number, ptIdx: number) {
  curves[curveIdx].points.splice(ptIdx, 1);
}

function addCurve() {
  const idx = curves.length;
  curves.push({
    name: `Courbe ${idx + 1}`,
    color: COLORS[idx % COLORS.length],
    points: [],
  });
  activeCurveIndex.value = idx;
}

function generateSVG(): string {
  const canvas = canvasRef.value!;
  const W = canvas.width;
  const H = canvas.height;
  let svg = `<svg xmlns="http://www.w3.org/2000/svg" width="${W}" height="${H}" viewBox="0 0 ${W} ${H}">`;
  svg += `<rect width="${W}" height="${H}" fill="${BG}"/>`;

  // Axes
  const [ox, oy] = worldToScreen(0, 0);
  svg += `<line x1="0" y1="${oy}" x2="${W}" y2="${oy}" stroke="${AXIS_COLOR}" stroke-width="1.5"/>`;
  svg += `<line x1="${ox}" y1="0" x2="${ox}" y2="${H}" stroke="${AXIS_COLOR}" stroke-width="1.5"/>`;

  // Points and lines
  for (const curve of curves) {
    if (curve.points.length > 1) {
      let d = "";
      for (let i = 0; i < curve.points.length; i++) {
        const [sx, sy] = worldToScreen(curve.points[i].x, curve.points[i].y);
        d += `${i === 0 ? "M" : "L"}${sx},${sy}`;
      }
      svg += `<path d="${d}" fill="none" stroke="${curve.color}" stroke-width="2"/>`;
    }
    for (const pt of curve.points) {
      const [sx, sy] = worldToScreen(pt.x, pt.y);
      svg += `<circle cx="${sx}" cy="${sy}" r="5" fill="${curve.color}"/>`;
      svg += `<text x="${sx + 8}" y="${sy - 8}" fill="#1d1d1f" font-size="11" font-weight="bold">${pt.name}(${pt.x},${pt.y})</text>`;
    }
  }

  svg += `</svg>`;
  return svg;
}

async function insertGraphInWord() {
  try {
    const svgString = generateSVG();
    const base64 = btoa(unescape(encodeURIComponent(svgString)));

    await Word.run(async (context) => {
      const range = context.document.getSelection();
      range.insertInlinePictureFromBase64(base64, Word.InsertLocation.replace);
      await context.sync();
    });
    statusMessage.value = "Graphe inséré dans Word";
    setTimeout(() => (statusMessage.value = ""), 2000);
  } catch (err) {
    statusMessage.value = `Erreur : ${(err as Error).message}`;
    setTimeout(() => (statusMessage.value = ""), 4000);
  }
}

onMounted(() => {
  const canvas = canvasRef.value!;
  const resize = () => {
    const parent = canvas.parentElement!;
    canvas.width = parent.clientWidth;
    canvas.height = parent.clientHeight;
  };
  resize();
  window.addEventListener("resize", resize);
  draw();
});
</script>

<template>
  <div class="graph-editor">
    <div class="canvas-container">
      <canvas
        ref="canvasRef"
        @mousedown="onMouseDown"
        @mousemove="onMouseMove"
        @mouseup="onMouseUp"
        @mouseleave="onMouseUp"
        @wheel.prevent="onWheel"
      />

      <!-- Naming popup -->
      <div v-if="namingPoint" class="naming-popup">
        <span class="naming-coords">({{ namingPoint.x }}, {{ namingPoint.y }})</span>
        <input
          v-model="pointName"
          class="naming-input"
          placeholder="Nom du point"
          autofocus
          @keydown.enter="confirmPoint"
          @keydown.escape="namingPoint = null"
        />
      </div>
    </div>

    <!-- Panel droit -->
    <div class="panel">
      <div class="panel-header">
        <h3>Courbes</h3>
        <button class="add-btn" @click="addCurve">+</button>
      </div>

      <div v-for="(curve, ci) in curves" :key="ci" class="curve-section">
        <div
          :class="['curve-header', { active: ci === activeCurveIndex }]"
          @click="activeCurveIndex = ci"
        >
          <span class="curve-dot" :style="{ background: curve.color }"></span>
          <input
            v-model="curve.name"
            class="curve-name"
            @click.stop
          />
        </div>
        <div class="points-list">
          <span
            v-for="(pt, pi) in curve.points"
            :key="pi"
            class="point-badge"
            :style="{ borderColor: curve.color }"
            @click="removePoint(ci, pi)"
            :title="'Cliquer pour supprimer'"
          >
            {{ pt.name }}({{ pt.x }},{{ pt.y }})
          </span>
        </div>
      </div>

      <button class="insert-btn" @click="insertGraphInWord">
        Insérer dans Word
      </button>

      <div v-if="statusMessage" class="status">{{ statusMessage }}</div>
    </div>
  </div>
</template>

<style scoped>
.graph-editor {
  display: flex;
  height: 100%;
}

.canvas-container {
  flex: 1;
  position: relative;
  min-width: 0;
}

.canvas-container canvas {
  display: block;
  width: 100%;
  height: 100%;
  cursor: crosshair;
}

.naming-popup {
  position: absolute;
  top: 50%;
  left: 50%;
  transform: translate(-50%, -50%);
  background: var(--bg-surface);
  border: 1px solid var(--accent);
  border-radius: var(--radius);
  padding: 12px;
  display: flex;
  flex-direction: column;
  gap: 8px;
  z-index: 10;
}

.naming-coords {
  font-family: var(--font-mono);
  font-size: 13px;
  color: var(--text-muted);
  text-align: center;
}

.naming-input {
  background: var(--bg-input);
  border: 1px solid var(--border);
  border-radius: 4px;
  color: var(--text);
  padding: 6px 10px;
  font-size: 14px;
  outline: none;
  width: 140px;
}

.naming-input:focus {
  border-color: var(--accent);
}

.panel {
  width: 180px;
  background: var(--bg-surface);
  border-left: 1px solid var(--border);
  padding: 10px;
  display: flex;
  flex-direction: column;
  gap: 8px;
  overflow-y: auto;
}

.panel-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
}

.panel-header h3 {
  font-size: 12px;
  color: var(--text-muted);
  text-transform: uppercase;
  letter-spacing: 0.5px;
}

.add-btn {
  width: 24px;
  height: 24px;
  border: 1px solid var(--border);
  border-radius: 4px;
  background: var(--bg-input);
  color: var(--accent);
  font-size: 16px;
  cursor: pointer;
  display: flex;
  align-items: center;
  justify-content: center;
}

.curve-section {
  display: flex;
  flex-direction: column;
  gap: 4px;
}

.curve-header {
  display: flex;
  align-items: center;
  gap: 6px;
  padding: 4px 6px;
  border-radius: 4px;
  cursor: pointer;
}

.curve-header.active {
  background: var(--bg-input);
}

.curve-dot {
  width: 10px;
  height: 10px;
  border-radius: 50%;
  flex-shrink: 0;
}

.curve-name {
  background: transparent;
  border: none;
  color: var(--text);
  font-size: 12px;
  width: 100%;
  outline: none;
}

.points-list {
  display: flex;
  flex-wrap: wrap;
  gap: 4px;
  padding-left: 16px;
}

.point-badge {
  font-family: var(--font-mono);
  font-size: 10px;
  padding: 2px 6px;
  border: 1px solid;
  border-radius: 3px;
  color: var(--text);
  cursor: pointer;
  transition: opacity 0.15s;
}

.point-badge:hover {
  opacity: 0.6;
}

.insert-btn {
  margin-top: auto;
  padding: 10px;
  background: var(--accent);
  color: #fff;
  border: none;
  border-radius: var(--radius);
  font-size: 13px;
  font-weight: 600;
  cursor: pointer;
  transition: background 0.15s;
}

.insert-btn:hover {
  background: var(--accent-hover);
}

.status {
  font-size: 11px;
  color: var(--accent);
  text-align: center;
}
</style>
