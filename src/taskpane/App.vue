<script setup lang="ts">
import { ref } from "vue";
import {
  isActive,
  lastAction,
  replaceCount,
  toggleWatcher,
  suggestions,
  selectedIdx,
  selectSuggestion,
  hasSuggestions,
  matchedRaw,
  debugInfo,
  debugSteps,
} from "./watcher";

const openSection = ref<string | null>(null);
function toggle(name: string) {
  openSection.value = openSection.value === name ? null : name;
}
</script>

<template>
  <div class="app">
    <!-- Header -->
    <header class="header">
      <div class="title">
        <span class="logo">M</span>
        <span>ath-addon</span>
      </div>
      <button :class="['toggle-btn', { active: isActive }]" @click="toggleWatcher">
        {{ isActive ? "ON" : "OFF" }}
      </button>
    </header>

    <!-- Suggestions (zone principale) -->
    <div v-if="hasSuggestions" class="suggestions">
      <div class="suggestions-header">
        <span class="matched-raw">{{ matchedRaw }}</span>
        <span class="hint">Tab pour valider</span>
      </div>
      <div
        v-for="(s, i) in suggestions"
        :key="i"
        :class="['suggestion-item', { selected: i === selectedIdx }]"
        @click="selectSuggestion(i)"
      >
        <span class="suggestion-display">{{ s.display }}</span>
        <span class="suggestion-label">{{ s.label }}</span>
        <span v-if="i === selectedIdx" class="check">&#10003;</span>
      </div>
    </div>

    <!-- Status (quand pas de suggestion) -->
    <div v-else :class="['status', { active: isActive }]">
      <div class="status-dot"></div>
      <span v-if="isActive && lastAction">{{ lastAction }}</span>
      <span v-else-if="isActive">Tapez dans Word...</span>
      <span v-else>Inactif</span>
      <span v-if="replaceCount > 0" class="counter">{{ replaceCount }}</span>
    </div>

    <!-- Debug -->
    <div class="debug">{{ debugInfo }}</div>

    <!-- Debug steps -->
    <div v-if="debugSteps.length > 0" class="debug-steps">
      <div v-for="(step, i) in debugSteps" :key="i" class="debug-step">{{ step }}</div>
    </div>

    <!-- Reference -->
    <div class="reference">
      <div class="ref-section" @click="toggle('logique')">
        <h3>Logique <span class="tog">{{ openSection === 'logique' ? '−' : '+' }}</span></h3>
      </div>
      <div v-if="openSection === 'logique'" class="ref-grid">
        <div class="ref-item"><kbd>Vx(R</kbd><span>∀x ∈ ℝ</span></div>
        <div class="ref-item"><kbd>Ex(N</kbd><span>∃x ∈ ℕ</span></div>
        <div class="ref-item"><kbd>E!x</kbd><span>∃!x</span></div>
        <div class="ref-item"><kbd>=&gt;</kbd><span>⟹</span></div>
        <div class="ref-item"><kbd>&lt;=&gt;</kbd><span>⟺</span></div>
        <div class="ref-item"><kbd>~</kbd><span>¬</span></div>
      </div>

      <div class="ref-section" @click="toggle('ens')">
        <h3>Ensembles <span class="tog">{{ openSection === 'ens' ? '−' : '+' }}</span></h3>
      </div>
      <div v-if="openSection === 'ens'" class="ref-grid">
        <div class="ref-item"><kbd>(R</kbd><span>∈ ℝ</span></div>
        <div class="ref-item"><kbd>!(R</kbd><span>∉ ℝ</span></div>
        <div class="ref-item"><kbd>sub R</kbd><span>⊂ ℝ</span></div>
        <div class="ref-item"><kbd>AuB</kbd><span>A ∪ B</span></div>
        <div class="ref-item"><kbd>AnB</kbd><span>A ∩ B</span></div>
        <div class="ref-item"><kbd>vide</kbd><span>∅</span></div>
      </div>

      <div class="ref-section" @click="toggle('analyse')">
        <h3>Analyse <span class="tog">{{ openSection === 'analyse' ? '−' : '+' }}</span></h3>
      </div>
      <div v-if="openSection === 'analyse'" class="ref-grid">
        <div class="ref-item"><kbd>lim->inf</kbd><span>lim →+∞</span></div>
        <div class="ref-item"><kbd>lim->0+</kbd><span>lim →0⁺</span></div>
        <div class="ref-item"><kbd>f'</kbd><span>f′</span></div>
        <div class="ref-item"><kbd>inf</kbd><span>∞</span></div>
        <div class="ref-item"><kbd>sqrt</kbd><span>√</span></div>
      </div>

      <div class="ref-section" @click="toggle('geo')">
        <h3>Géométrie / Opérateurs <span class="tog">{{ openSection === 'geo' ? '−' : '+' }}</span></h3>
      </div>
      <div v-if="openSection === 'geo'" class="ref-grid">
        <div class="ref-item"><kbd>vec AB</kbd><span>AB⃗</span></div>
        <div class="ref-item"><kbd>ang ABC</kbd><span>∠ABC</span></div>
        <div class="ref-item"><kbd>10 6</kbd><span>10⁶ / ×</span></div>
        <div class="ref-item"><kbd>&gt;=</kbd><span>≥</span></div>
        <div class="ref-item"><kbd>&lt;=</kbd><span>≤</span></div>
        <div class="ref-item"><kbd>!=</kbd><span>≠</span></div>
      </div>

      <div class="ref-section" @click="toggle('grec')">
        <h3>Grec <span class="tog">{{ openSection === 'grec' ? '−' : '+' }}</span></h3>
      </div>
      <div v-if="openSection === 'grec'" class="ref-grid">
        <div class="ref-item"><kbd>alpha</kbd><span>α</span></div>
        <div class="ref-item"><kbd>beta</kbd><span>β</span></div>
        <div class="ref-item"><kbd>gamma</kbd><span>γ</span></div>
        <div class="ref-item"><kbd>delta</kbd><span>δ / Δ</span></div>
        <div class="ref-item"><kbd>epsilon</kbd><span>ε</span></div>
        <div class="ref-item"><kbd>theta</kbd><span>θ</span></div>
        <div class="ref-item"><kbd>lambda</kbd><span>λ</span></div>
        <div class="ref-item"><kbd>mu</kbd><span>μ</span></div>
        <div class="ref-item"><kbd>pi</kbd><span>π</span></div>
        <div class="ref-item"><kbd>sigma</kbd><span>σ / Σ</span></div>
        <div class="ref-item"><kbd>omega</kbd><span>ω / Ω</span></div>
        <div class="ref-item"><kbd>phi</kbd><span>φ</span></div>
      </div>
    </div>

    <!-- Footer -->
    <div class="footer">
      <code>Vx(R</code> = <code>V x c R</code> = <code>pt x dans R</code>
      <div class="version">v2.1.0</div>
    </div>
  </div>
</template>

<style>
:root {
  --bg: #ffffff;
  --bg-surface: #f5f5f7;
  --bg-input: #ffffff;
  --text: #1d1d1f;
  --text-muted: #86868b;
  --accent: #0071e3;
  --green: #34c759;
  --red: #ff3b30;
  --border: #d2d2d7;
  --radius: 8px;
  --font-mono: "Cascadia Code", "Fira Code", "Consolas", monospace;
  --font-sans: "Segoe UI", system-ui, sans-serif;
}

* { margin: 0; padding: 0; box-sizing: border-box; }

body {
  font-family: var(--font-sans);
  background: var(--bg);
  color: var(--text);
  font-size: 13px;
  overflow: hidden;
}

.app {
  display: flex;
  flex-direction: column;
  height: 100vh;
  gap: 8px;
  padding: 10px;
}

.header {
  display: flex;
  align-items: center;
  justify-content: space-between;
}

.title {
  font-weight: 700;
  font-size: 15px;
  display: flex;
  align-items: center;
  gap: 2px;
}

.logo {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  width: 24px;
  height: 24px;
  background: var(--accent);
  color: white;
  border-radius: 6px;
  font-size: 14px;
  font-weight: 800;
  margin-right: 6px;
}

.toggle-btn {
  padding: 4px 14px;
  border: 2px solid var(--red);
  border-radius: 20px;
  background: transparent;
  color: var(--red);
  font-weight: 700;
  font-size: 12px;
  cursor: pointer;
}

.toggle-btn.active {
  border-color: var(--green);
  color: var(--green);
}

/* === DEBUG === */
.debug {
  padding: 4px 8px;
  background: #fff3cd;
  border: 1px solid #ffc107;
  border-radius: 4px;
  font-family: var(--font-mono);
  font-size: 11px;
  color: #856404;
  word-break: break-all;
}

/* === DEBUG STEPS === */
.debug-steps {
  padding: 6px 8px;
  background: #f0f0f0;
  border: 1px solid #ccc;
  border-radius: 4px;
  font-family: var(--font-mono);
  font-size: 10px;
  color: #333;
  max-height: 40vh;
  overflow-y: auto;
  white-space: pre-wrap;
  word-break: break-all;
}

.debug-step {
  padding: 2px 0;
  border-bottom: 1px solid #ddd;
}

.debug-step:last-child {
  border-bottom: none;
}

/* === SUGGESTIONS === */
.suggestions {
  border: 2px solid var(--accent);
  border-radius: var(--radius);
  overflow: hidden;
  animation: slideIn 0.12s ease;
}

@keyframes slideIn {
  from { opacity: 0; transform: translateY(-6px); }
  to { opacity: 1; transform: translateY(0); }
}

.suggestions-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: 6px 10px;
  background: var(--accent);
  color: white;
  font-size: 12px;
}

.matched-raw {
  font-family: var(--font-mono);
  font-weight: 600;
}

.hint {
  font-size: 10px;
  opacity: 0.8;
}

.suggestion-item {
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 8px 10px;
  cursor: pointer;
  border-bottom: 1px solid var(--border);
  transition: background 0.1s;
}

.suggestion-item:last-child {
  border-bottom: none;
}

.suggestion-item:hover {
  background: var(--bg-surface);
}

.suggestion-item.selected {
  background: #e8f0fe;
}

.suggestion-display {
  font-family: var(--font-mono);
  font-size: 18px;
  font-weight: 600;
  color: var(--text);
}

.suggestion-label {
  font-size: 11px;
  color: var(--text-muted);
  flex: 1;
}

.check {
  color: var(--accent);
  font-weight: bold;
  font-size: 14px;
}

/* === STATUS === */
.status {
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 8px 12px;
  background: var(--bg-surface);
  border-radius: var(--radius);
  font-size: 12px;
  color: var(--text-muted);
}

.status-dot {
  width: 8px;
  height: 8px;
  border-radius: 50%;
  background: var(--red);
  flex-shrink: 0;
}

.status.active .status-dot {
  background: var(--green);
  animation: pulse 2s infinite;
}

@keyframes pulse {
  0%, 100% { opacity: 1; }
  50% { opacity: 0.4; }
}

.counter {
  margin-left: auto;
  background: var(--accent);
  color: white;
  padding: 1px 7px;
  border-radius: 10px;
  font-size: 11px;
  font-weight: 600;
}

/* === INSTRUCTIONS === */
.instructions {
  padding: 6px 10px;
  background: var(--bg-surface);
  border-radius: var(--radius);
  font-size: 12px;
  line-height: 1.5;
  color: var(--text-muted);
}

.instructions code {
  font-family: var(--font-mono);
  background: var(--bg);
  border: 1px solid var(--border);
  padding: 1px 5px;
  border-radius: 3px;
  color: var(--accent);
  font-size: 12px;
}

.instructions kbd {
  font-family: var(--font-sans);
  background: var(--text);
  color: white;
  padding: 1px 6px;
  border-radius: 3px;
  font-size: 11px;
}

/* === REFERENCE === */
.reference {
  flex: 1;
  overflow-y: auto;
  border: 1px solid var(--border);
  border-radius: var(--radius);
  background: var(--bg-surface);
}

.ref-section { cursor: pointer; user-select: none; }

.ref-section h3 {
  display: flex;
  justify-content: space-between;
  font-size: 11px;
  color: var(--text-muted);
  padding: 7px 10px;
  margin: 0;
  text-transform: uppercase;
  letter-spacing: 0.5px;
  border-bottom: 1px solid var(--border);
}

.ref-section h3:hover { color: var(--accent); }

.tog { font-size: 13px; font-weight: bold; }

.ref-grid {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 2px;
  padding: 5px 10px 8px;
  border-bottom: 1px solid var(--border);
}

.ref-item {
  display: flex;
  align-items: center;
  gap: 6px;
  font-size: 12px;
}

.ref-item kbd {
  font-family: var(--font-mono);
  font-size: 10px;
  padding: 1px 5px;
  background: var(--bg);
  border: 1px solid var(--border);
  border-radius: 3px;
  color: var(--accent);
  min-width: 42px;
  text-align: center;
  white-space: nowrap;
}

.ref-item span { color: var(--text); font-size: 14px; }

.footer {
  text-align: center;
  font-size: 11px;
  color: var(--text-muted);
  padding: 4px;
}

.version {
  margin-top: 2px;
  font-family: var(--font-mono);
  font-size: 10px;
  opacity: 0.5;
}

.footer code {
  font-family: var(--font-mono);
  color: var(--accent);
  font-size: 11px;
}
</style>
