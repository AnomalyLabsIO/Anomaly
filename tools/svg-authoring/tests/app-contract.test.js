'use strict';

const fs = require('node:fs');
const path = require('node:path');
const test = require('node:test');
const assert = require('node:assert/strict');

const root = path.resolve(__dirname, '..');
const html = fs.readFileSync(path.join(root, 'index.html'), 'utf8');
const app = fs.readFileSync(path.join(root, 'app.js'), 'utf8');
const styles = fs.readFileSync(path.join(root, 'styles.css'), 'utf8');

test('every DOM id used by app.js exists exactly once in index.html', () => {
  const declared = [...html.matchAll(/\bid="([^"]+)"/g)].map((match) => match[1]);
  const referenced = [...app.matchAll(/\$\('([^']+)'\)/g)].map((match) => match[1]);
  const counts = new Map();
  declared.forEach((id) => counts.set(id, (counts.get(id) || 0) + 1));

  assert.deepEqual(declared.filter((id) => counts.get(id) > 1), []);
  assert.deepEqual([...new Set(referenced)].filter((id) => !counts.has(id)), []);
});

test('the browser loads the vector core before the editor app', () => {
  const coreIndex = html.indexOf('<script src="editor-core.js"></script>');
  const appIndex = html.indexOf('<script src="app.js"></script>');
  assert.ok(coreIndex >= 0);
  assert.ok(appIndex > coreIndex);
});

test('autosave uses version two with validated one-way version-one migration', () => {
  assert.match(app, /autosave\.v2/);
  assert.match(app, /LEGACY_AUTOSAVE_KEY = 'anomaly-vector-studio\.autosave\.v1'/);
  assert.match(app, /Core\.isProjectDocument\(parsed\)/);
});

test('critical artboard overlays and preview states have styles', () => {
  [
    '.selection-box',
    '.selection-handle',
    '.line-curve-handle',
    '.arc-center-indicator',
    '.snap-target-indicator',
    '.grid-minor-line',
    '.grid-major-line',
    '.node-anchor',
    '.bezier-handle',
    '.draft-segment',
    '.artboard-shell[data-preview="checker"]',
    '.artboard-shell[data-preview="dark"]',
    '.artboard-shell[data-preview="light"]',
    '.toast-region'
  ].forEach((selector) => assert.ok(styles.includes(selector), `Missing ${selector}`));
});

test('the reference layer remains separate from exported artwork', () => {
  assert.ok(html.indexOf('id="reference-layer"') < html.indexOf('id="artwork-layer"'));
  assert.match(html, /never included in SVG/i);
});

test('SVG import normalizes non-zero viewBox origins', () => {
  assert.match(app, /const viewBoxX = viewBoxValues\.length === 4/);
  assert.match(app, /Core\.translateElement\(element, -viewBoxX, -viewBoxY\)/);
});

test('line curve completion samples pointerup and Escape restores the original line', () => {
  assert.match(app, /event\.type === 'pointerup' && interaction\.kind === 'line-curve'[\s\S]*?updateLineCurve\(interaction, point, event\)/);
  assert.match(app, /event\.key === 'Escape'[\s\S]*?cancelLineCurve\(true\)/);
  assert.match(app, /function cancelLineCurve[\s\S]*?restoreSnapshot\(interaction\.before\)/);
  assert.match(app, /interaction\.moved \|\| Math\.hypot\(event\.clientX - interaction\.clientX/);
  assert.match(app, /original\.type === 'line' && geometry\.inputSagitta < threshold/);
  assert.match(app, /interaction\.bendStart\.x \+ rawPoint\.x - interaction\.pointerStart\.x/);
  assert.match(app, /interaction\.kind === 'node' \|\| interaction\.kind === 'line-node'[\s\S]*?interaction\.moved \|\| Math\.hypot/);
  assert.match(app, /event\.type === 'pointercancel' && interaction\.before/);
});

test('line bending creates semantic circle arcs and exposes reusable ring geometry', () => {
  assert.match(app, /Core\.circularArcFromChord\(/);
  assert.match(app, /replacement\.type = 'arc'/);
  ['arc-center-x', 'arc-center-y', 'arc-radius', 'arc-start-angle', 'arc-sweep-angle'].forEach((id) => {
    assert.match(html, new RegExp(`id="${id}"`));
  });
});

test('variable minor and major grids support grid and anchor snapping', () => {
  assert.match(html, /id="grid-size"[^>]*min="0\.25"/);
  assert.match(html, /id="grid-major-every"/);
  ['off', 'grid', 'anchors', 'grid-anchors'].forEach((value) => {
    assert.match(html, new RegExp(`<option value="${value}"`));
  });
  assert.match(app, /function collectSnapAnchors/);
  assert.match(app, /Core\.nearestSnapAnchor\(/);
  assert.match(app, /kind: 'line-node'/);
  assert.match(app, /function updateLineNodeDrag/);
  assert.match(app, /function canChangeGridSettings/);
  assert.match(app, /interaction\.tool === 'line' && !event\.shiftKey/);
  assert.match(app, /while \(displayedMajorStep \* state\.zoom < 5/);
  assert.match(app, /snapUsesGrid\(\) && !event\.altKey/);
  assert.match(app, /state\.draft \|\| state\.interaction/);
  assert.match(app, /mutate\(state\.gridVisible \? 'hide grid' : 'show grid'/);
});
