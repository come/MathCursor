<script setup lang="ts">
import { ref, computed, onMounted, nextTick } from "vue";
import { findMatch, type MatchResult, type PatternChoice } from "./patterns";

const editorRef = ref<HTMLDivElement | null>(null);
const currentMatch = ref<MatchResult | null>(null);
const selectedChoiceIndex = ref(0);
const statusMessage = ref("");

// Hook for future AI suggestion
const onAISuggestion: ((text: string) => void) | undefined = undefined;

const ghostText = computed(() => {
  if (!currentMatch.value) return "";
  return currentMatch.value.choices[selectedChoiceIndex.value]?.display ?? "";
});

const choices = computed(() => currentMatch.value?.choices ?? []);
const hasMultipleChoices = computed(() => choices.value.length > 1);

function getEditorText(): string {
  return editorRef.value?.textContent ?? "";
}

function handleInput() {
  const text = getEditorText();
  const match = findMatch(text);
  if (match) {
    currentMatch.value = match;
    selectedChoiceIndex.value = 0;
  } else {
    currentMatch.value = null;
  }
}

async function insertIntoWord(choice: PatternChoice) {
  if (typeof Word === "undefined" || !Word.run) {
    statusMessage.value = `${choice.label} → ${choice.display}`;
    setTimeout(() => (statusMessage.value = ""), 2500);
    return;
  }

  try {
    await Word.run(async (context) => {
      const range = context.document.getSelection();
      if (choice.ooxml) {
        range.insertOoxml(choice.ooxml, Word.InsertLocation.replace);
      } else {
        range.insertText(choice.text ?? choice.display, Word.InsertLocation.replace);
      }
      await context.sync();
    });
    statusMessage.value = `Inséré : ${choice.label}`;
    setTimeout(() => (statusMessage.value = ""), 2000);
  } catch (err) {
    statusMessage.value = `Erreur : ${(err as Error).message}`;
    setTimeout(() => (statusMessage.value = ""), 4000);
  }
}

function acceptSuggestion() {
  if (!currentMatch.value) return;
  const choice = currentMatch.value.choices[selectedChoiceIndex.value];

  insertIntoWord(choice);

  // Clear matched portion
  const text = getEditorText();
  const newText = text.substring(0, currentMatch.value.startIndex);
  if (editorRef.value) {
    editorRef.value.textContent = newText;
    const range = document.createRange();
    const sel = window.getSelection();
    if (editorRef.value.childNodes.length > 0) {
      range.setStartAfter(editorRef.value.lastChild!);
    } else {
      range.setStart(editorRef.value, 0);
    }
    range.collapse(true);
    sel?.removeAllRanges();
    sel?.addRange(range);
  }

  currentMatch.value = null;
}

function handleKeydown(e: KeyboardEvent) {
  if (e.key === "Tab") {
    e.preventDefault();
    e.stopPropagation();
    if (currentMatch.value) {
      acceptSuggestion();
    }
    return;
  }

  if (e.key === "Escape") {
    if (currentMatch.value) {
      e.preventDefault();
      currentMatch.value = null;
    }
    return;
  }

  if (hasMultipleChoices.value) {
    if (e.key === "ArrowUp") {
      e.preventDefault();
      selectedChoiceIndex.value =
        (selectedChoiceIndex.value - 1 + choices.value.length) % choices.value.length;
    }
    if (e.key === "ArrowDown") {
      e.preventDefault();
      selectedChoiceIndex.value =
        (selectedChoiceIndex.value + 1) % choices.value.length;
    }
  }
}

// Collapsible reference sections
const openSection = ref<string | null>(null);
function toggleSection(name: string) {
  openSection.value = openSection.value === name ? null : name;
}

onMounted(() => {
  nextTick(() => editorRef.value?.focus());
});
</script>

<template>
  <div class="math-editor">
    <div class="editor-area">
      <div
        ref="editorRef"
        class="editor-input"
        contenteditable="true"
        spellcheck="false"
        @input="handleInput"
        @keydown="handleKeydown"
        data-placeholder="Tapez ici... (ex: vec AB, pi, V x ( R, 1/)"
      ></div>

      <!-- Ghost text bar -->
      <div v-if="currentMatch" class="ghost-bar">
        <span class="ghost-label">{{ ghostText }}</span>
        <span class="ghost-hint">Tab accepter · Échap ignorer</span>
      </div>

      <!-- Multi-choice picker -->
      <div v-if="hasMultipleChoices" class="picker">
        <div
          v-for="(choice, i) in choices"
          :key="i"
          :class="['picker-item', { active: i === selectedChoiceIndex }]"
        >
          <span class="picker-display">{{ choice.display }}</span>
          <span class="picker-label">{{ choice.label }}</span>
        </div>
        <div class="picker-hint">&#8593;&#8595; naviguer · Tab valider</div>
      </div>
    </div>

    <!-- Status -->
    <div v-if="statusMessage" class="status">{{ statusMessage }}</div>

    <!-- Pattern reference (collapsible) -->
    <div class="reference">
      <!-- Logique -->
      <div class="ref-section" @click="toggleSection('logique')">
        <h3>Logique <span class="toggle">{{ openSection === 'logique' ? '−' : '+' }}</span></h3>
      </div>
      <div v-if="openSection === 'logique'" class="ref-grid">
        <div class="ref-item"><kbd>V x ( R</kbd><span>∀x ∈ ℝ</span></div>
        <div class="ref-item"><kbd>E x ( R</kbd><span>∃x ∈ ℝ</span></div>
        <div class="ref-item"><kbd>E! x</kbd><span>∃!x</span></div>
        <div class="ref-item"><kbd>=&gt;</kbd><span>⟹</span></div>
        <div class="ref-item"><kbd>&lt;=&gt;</kbd><span>⟺</span></div>
        <div class="ref-item"><kbd>~</kbd><span>¬</span></div>
      </div>

      <!-- Ensembles -->
      <div class="ref-section" @click="toggleSection('ensembles')">
        <h3>Ensembles <span class="toggle">{{ openSection === 'ensembles' ? '−' : '+' }}</span></h3>
      </div>
      <div v-if="openSection === 'ensembles'" class="ref-grid">
        <div class="ref-item"><kbd>( R</kbd><span>∈ ℝ</span></div>
        <div class="ref-item"><kbd>!( R</kbd><span>∉ ℝ</span></div>
        <div class="ref-item"><kbd>sub R</kbd><span>⊂ ℝ</span></div>
        <div class="ref-item"><kbd>AuB</kbd><span>A ∪ B</span></div>
        <div class="ref-item"><kbd>AnB</kbd><span>A ∩ B</span></div>
        <div class="ref-item"><kbd>A\B</kbd><span>A ∖ B</span></div>
        <div class="ref-item"><kbd>vide</kbd><span>∅</span></div>
      </div>

      <!-- Analyse -->
      <div class="ref-section" @click="toggleSection('analyse')">
        <h3>Analyse <span class="toggle">{{ openSection === 'analyse' ? '−' : '+' }}</span></h3>
      </div>
      <div v-if="openSection === 'analyse'" class="ref-grid">
        <div class="ref-item"><kbd>lim ->inf</kbd><span>lim →+∞</span></div>
        <div class="ref-item"><kbd>lim ->0+</kbd><span>lim →0⁺</span></div>
        <div class="ref-item"><kbd>int a b</kbd><span>∫ₐᵇ</span></div>
        <div class="ref-item"><kbd>f'</kbd><span>f′</span></div>
        <div class="ref-item"><kbd>f''</kbd><span>f″</span></div>
        <div class="ref-item"><kbd>inf</kbd><span>∞</span></div>
        <div class="ref-item"><kbd>-inf</kbd><span>-∞</span></div>
      </div>

      <!-- Algèbre -->
      <div class="ref-section" @click="toggleSection('algebre')">
        <h3>Algèbre <span class="toggle">{{ openSection === 'algebre' ? '−' : '+' }}</span></h3>
      </div>
      <div v-if="openSection === 'algebre'" class="ref-grid">
        <div class="ref-item"><kbd>sum i=1 n</kbd><span>Σᵢ₌₁ⁿ</span></div>
        <div class="ref-item"><kbd>prod i=1 n</kbd><span>Πᵢ₌₁ⁿ</span></div>
        <div class="ref-item"><kbd>Cn k</kbd><span>Cₙᵏ</span></div>
        <div class="ref-item"><kbd>10 6</kbd><span>10⁶ / ×</span></div>
        <div class="ref-item"><kbd>1/</kbd><span>fraction</span></div>
        <div class="ref-item"><kbd>sqrt</kbd><span>√</span></div>
        <div class="ref-item"><kbd>nrt</kbd><span>ⁿ√</span></div>
      </div>

      <!-- Géométrie -->
      <div class="ref-section" @click="toggleSection('geo')">
        <h3>Géométrie <span class="toggle">{{ openSection === 'geo' ? '−' : '+' }}</span></h3>
      </div>
      <div v-if="openSection === 'geo'" class="ref-grid">
        <div class="ref-item"><kbd>vec AB</kbd><span>→AB</span></div>
        <div class="ref-item"><kbd>seg AB</kbd><span>—AB</span></div>
        <div class="ref-item"><kbd>ang ABC</kbd><span>∠ABC</span></div>
        <div class="ref-item"><kbd>||v||</kbd><span>‖v‖</span></div>
        <div class="ref-item"><kbd>u.v</kbd><span>u·v</span></div>
        <div class="ref-item"><kbd>u^v</kbd><span>u∧v</span></div>
      </div>

      <!-- Opérateurs -->
      <div class="ref-section" @click="toggleSection('ops')">
        <h3>Opérateurs <span class="toggle">{{ openSection === 'ops' ? '−' : '+' }}</span></h3>
      </div>
      <div v-if="openSection === 'ops'" class="ref-grid">
        <div class="ref-item"><kbd>&gt;=</kbd><span>≥</span></div>
        <div class="ref-item"><kbd>&lt;=</kbd><span>≤</span></div>
        <div class="ref-item"><kbd>!=</kbd><span>≠</span></div>
      </div>

      <!-- Grec -->
      <div class="ref-section" @click="toggleSection('grec')">
        <h3>Grec <span class="toggle">{{ openSection === 'grec' ? '−' : '+' }}</span></h3>
      </div>
      <div v-if="openSection === 'grec'" class="ref-grid">
        <div class="ref-item"><kbd>alpha</kbd><span>α</span></div>
        <div class="ref-item"><kbd>beta</kbd><span>β</span></div>
        <div class="ref-item"><kbd>gamma</kbd><span>γ</span></div>
        <div class="ref-item"><kbd>delta</kbd><span>δ</span></div>
        <div class="ref-item"><kbd>Delta</kbd><span>Δ</span></div>
        <div class="ref-item"><kbd>epsilon</kbd><span>ε</span></div>
        <div class="ref-item"><kbd>theta</kbd><span>θ</span></div>
        <div class="ref-item"><kbd>lambda</kbd><span>λ</span></div>
        <div class="ref-item"><kbd>mu</kbd><span>μ</span></div>
        <div class="ref-item"><kbd>pi</kbd><span>π</span></div>
        <div class="ref-item"><kbd>sigma</kbd><span>σ</span></div>
        <div class="ref-item"><kbd>Sigma</kbd><span>Σ</span></div>
        <div class="ref-item"><kbd>omega</kbd><span>ω</span></div>
        <div class="ref-item"><kbd>Omega</kbd><span>Ω</span></div>
        <div class="ref-item"><kbd>phi</kbd><span>φ</span></div>
      </div>
    </div>
  </div>
</template>

<style scoped>
.math-editor {
  display: flex;
  flex-direction: column;
  height: 100%;
  padding: 12px;
  gap: 10px;
}

.editor-area {
  position: relative;
}

.editor-input {
  width: 100%;
  min-height: 48px;
  padding: 12px;
  background: var(--bg-input);
  border: 2px solid var(--border);
  border-radius: var(--radius);
  color: var(--text);
  font-family: var(--font-mono);
  font-size: 16px;
  line-height: 1.5;
  outline: none;
  caret-color: var(--accent);
}

.editor-input:focus {
  border-color: var(--accent);
  box-shadow: 0 0 0 3px rgba(0, 113, 227, 0.15);
}

.editor-input:empty::before {
  content: attr(data-placeholder);
  color: var(--text-muted);
  pointer-events: none;
}

.ghost-bar {
  display: flex;
  align-items: center;
  justify-content: space-between;
  margin-top: 6px;
  padding: 8px 12px;
  background: var(--bg-surface);
  border: 2px solid var(--accent);
  border-radius: var(--radius);
  animation: fadeIn 0.1s ease;
}

@keyframes fadeIn {
  from { opacity: 0; transform: translateY(-4px); }
  to { opacity: 1; transform: translateY(0); }
}

.ghost-label {
  font-family: var(--font-mono);
  font-size: 20px;
  color: var(--accent);
  font-weight: 600;
}

.ghost-hint {
  font-size: 11px;
  color: var(--text-muted);
  white-space: nowrap;
}

.picker {
  margin-top: 4px;
  padding: 6px;
  background: var(--bg-surface);
  border: 1px solid var(--border);
  border-radius: var(--radius);
}

.picker-item {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 6px 10px;
  border-radius: 4px;
}

.picker-item.active {
  background: var(--bg-input);
  border: 1px solid var(--accent);
}

.picker-display {
  font-family: var(--font-mono);
  font-size: 15px;
  color: var(--text);
}

.picker-label {
  font-size: 12px;
  color: var(--text-muted);
}

.picker-hint {
  text-align: center;
  font-size: 11px;
  color: var(--text-muted);
  padding-top: 4px;
  border-top: 1px solid var(--border);
  margin-top: 4px;
}

.status {
  padding: 6px 10px;
  background: var(--bg-surface);
  border-radius: var(--radius);
  font-size: 12px;
  color: var(--accent);
  text-align: center;
  font-weight: 500;
}

.reference {
  margin-top: auto;
  overflow-y: auto;
  max-height: 50vh;
  border: 1px solid var(--border);
  border-radius: var(--radius);
  background: var(--bg-surface);
}

.ref-section {
  cursor: pointer;
  user-select: none;
}

.ref-section h3 {
  display: flex;
  align-items: center;
  justify-content: space-between;
  font-size: 12px;
  color: var(--text-muted);
  padding: 8px 10px;
  margin: 0;
  text-transform: uppercase;
  letter-spacing: 0.5px;
  border-bottom: 1px solid var(--border);
}

.ref-section h3:hover {
  color: var(--accent);
}

.toggle {
  font-size: 14px;
  font-weight: bold;
}

.ref-grid {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 3px;
  padding: 6px 10px 10px;
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
  padding: 2px 5px;
  background: var(--bg-input);
  border: 1px solid var(--border);
  border-radius: 3px;
  color: var(--accent);
  min-width: 48px;
  text-align: center;
  white-space: nowrap;
}

.ref-item span {
  color: var(--text);
  font-size: 14px;
}
</style>
