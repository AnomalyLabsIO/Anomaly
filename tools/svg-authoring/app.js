(function () {
  'use strict';

  const Core = window.VectorStudioCore;
  if (!Core) {
    throw new Error('VectorStudioCore did not load.');
  }

  const SVG_NS = 'http://www.w3.org/2000/svg';
  const AUTOSAVE_KEY = 'anomaly-vector-studio.autosave.v2';
  const LEGACY_AUTOSAVE_KEY = 'anomaly-vector-studio.autosave.v1';
  const MAX_HISTORY = 80;
  const MIN_ZOOM = 0.05;
  const MAX_ZOOM = 12;

  const $ = (id) => document.getElementById(id);
  const dom = {
    artboard: $('artboard'),
    artboardShell: $('artboard-shell'),
    stage: $('stage-scroll'),
    stageSpace: $('stage-space'),
    referenceLayer: $('reference-layer'),
    gridLayer: $('grid-layer'),
    artworkLayer: $('artwork-layer'),
    draftLayer: $('draft-layer'),
    selectionLayer: $('selection-layer'),
    toastRegion: $('toast-region'),
    layersList: $('layers-list'),
    layerCount: $('layer-count'),
    selectionEmpty: $('selection-empty'),
    selectionControls: $('selection-controls'),
    selectionKind: $('selection-kind'),
    arcControls: $('arc-controls'),
    nodeActions: $('node-actions'),
    activeNodeLabel: $('active-node-label'),
    referenceControls: $('reference-controls'),
    referenceStatus: $('reference-status'),
    referenceDrop: $('reference-drop'),
    referenceInput: $('reference-input'),
    projectInput: $('project-input'),
    svgInput: $('svg-input'),
    undo: $('undo'),
    redo: $('redo'),
    zoomReadout: $('zoom-readout'),
    statusTool: $('status-tool'),
    statusHint: $('status-hint'),
    statusCursor: $('status-cursor'),
    statusSize: $('status-size')
  };

  const TOOL_HINTS = {
    select: 'Drag a shape to move it. Use the handles to resize.',
    node: 'Select a line, path, or polygon, then drag anchors and Bézier handles.',
    rect: 'Drag to draw. Hold Shift for a square or Alt to draw from center.',
    ellipse: 'Drag to draw. Hold Shift for a circle or Alt to draw from center.',
    line: 'Draw a chord (Shift snaps its angle), then pull the purple midpoint to make an exact constant-radius circle segment.',
    polygon: 'Click each corner. Click the first point or press Enter to finish.',
    pen: 'Click for corners; click-drag for curves. Click the first point or press Enter to finish.',
    pan: 'Drag the workspace to pan. The Space key temporarily pans from any tool.'
  };

  const TOOL_LABELS = {
    select: 'Select',
    node: 'Node edit',
    rect: 'Rectangle',
    ellipse: 'Ellipse',
    line: 'Line',
    polygon: 'Polygon',
    pen: 'Bézier pen',
    pan: 'Pan'
  };

  const state = {
    doc: makeDocument(512, 512),
    tool: 'select',
    selectionId: null,
    activeNode: null,
    zoom: 1,
    gridVisible: true,
    guidesVisible: true,
    outline: false,
    stageTheme: 'checker',
    gridSize: 16,
    gridMajorEvery: 4,
    snapMode: 'off',
    snapTarget: null,
    undoStack: [],
    redoStack: [],
    interaction: null,
    draft: null,
    cursor: { x: 0, y: 0 },
    spacePressed: false,
    autosaveTimer: null,
    autosaveBlocked: false,
    exportAspectLocked: true,
    defaultStyle: {
      fill: '#ffffff',
      fillOpacity: 1,
      stroke: '#11151d',
      strokeOpacity: 1,
      strokeWidth: 2,
      lineCap: 'round',
      lineJoin: 'round',
      dash: ''
    }
  };

  function makeDocument(width, height) {
    const doc = Core.createDocument(width, height);
    doc.title = 'Anomaly logo';
    doc.reference = null;
    doc.settings = {
      gridSize: 16,
      gridMajorEvery: 4,
      gridVisible: true,
      snapMode: 'off'
    };
    return doc;
  }

  function svgNode(name, attributes) {
    const node = document.createElementNS(SVG_NS, name);
    Object.entries(attributes || {}).forEach(([key, value]) => {
      if (value !== null && value !== undefined) {
        node.setAttribute(key, String(value));
      }
    });
    return node;
  }

  function replaceChildren(node, children) {
    node.replaceChildren(...children);
  }

  function selectedElement() {
    return state.doc.elements.find((element) => element.id === state.selectionId) || null;
  }

  function elementIndex(id) {
    return state.doc.elements.findIndex((element) => element.id === id);
  }

  function selectionBounds() {
    const element = selectedElement();
    return element ? Core.elementBounds(element) : null;
  }

  function number(value, fallback) {
    const parsed = Number(value);
    return Number.isFinite(parsed) ? parsed : fallback;
  }

  function pretty(value) {
    return String(Core.round(number(value, 0), 2));
  }

  function snapUsesGrid() {
    return state.snapMode === 'grid' || state.snapMode === 'grid-anchors';
  }

  function snapUsesAnchors() {
    return state.snapMode === 'anchors' || state.snapMode === 'grid-anchors';
  }

  function collectSnapAnchors(excludeId) {
    const anchors = [];
    state.doc.elements.forEach((element) => {
      if (element.visible === false || element.id === excludeId) return;
      Core.elementSnapAnchors(element).forEach((point, index) => {
        anchors.push({ ...point, sourceId: element.id, sourceIndex: index });
      });
    });
    return anchors;
  }

  function snapPoint(point, event, options) {
    const bypass = Boolean(event && event.altKey);
    const settings = options || {};
    let snapped = { x: point.x, y: point.y };
    state.snapTarget = null;

    if (!bypass && snapUsesGrid()) {
      snapped = {
        x: Core.snap(point.x, state.gridSize, true),
        y: Core.snap(point.y, state.gridSize, true)
      };
    }

    if (!bypass && settings.anchors && snapUsesAnchors()) {
      const tolerance = 9 / state.zoom;
      const target = Core.nearestSnapAnchor(point, collectSnapAnchors(settings.excludeId), tolerance);
      if (target) {
        snapped = { x: target.x, y: target.y };
        state.snapTarget = target;
      }
    }

    return snapped;
  }

  function pointFromEvent(event, shouldSnap, options) {
    const rect = dom.artboard.getBoundingClientRect();
    const raw = {
      x: ((event.clientX - rect.left) / Math.max(rect.width, 1)) * state.doc.width,
      y: ((event.clientY - rect.top) / Math.max(rect.height, 1)) * state.doc.height
    };
    return shouldSnap === false ? raw : snapPoint(raw, event, options);
  }

  function applyDocumentSettings(documentValue) {
    const settings = documentValue.settings && typeof documentValue.settings === 'object'
      ? documentValue.settings
      : {};
    const snapModes = ['off', 'grid', 'anchors', 'grid-anchors'];
    state.gridSize = Core.clamp(number(settings.gridSize, 16), 0.25, 1024);
    state.gridMajorEvery = Math.round(Core.clamp(number(settings.gridMajorEvery, 4), 2, 16));
    state.gridVisible = settings.gridVisible !== false;
    state.snapMode = snapModes.includes(settings.snapMode) ? settings.snapMode : 'off';
    documentValue.settings = {
      ...settings,
      gridSize: state.gridSize,
      gridMajorEvery: state.gridMajorEvery,
      gridVisible: state.gridVisible,
      snapMode: state.snapMode
    };
  }

  function isTypingTarget(target) {
    if (!(target instanceof Element)) return false;
    return Boolean(target.closest('input, textarea, select, [contenteditable="true"]'));
  }

  function snapshotDocument() {
    return JSON.stringify(state.doc);
  }

  function beginHistory(label) {
    state.autosaveBlocked = false;
    const before = snapshotDocument();
    state.undoStack.push({ label, document: before, redoBefore: state.redoStack.slice() });
    if (state.undoStack.length > MAX_HISTORY) state.undoStack.shift();
    state.redoStack.length = 0;
    updateHistoryButtons();
    return before;
  }

  function discardNoopHistory(before) {
    if (!before) return;
    const last = state.undoStack[state.undoStack.length - 1];
    if (!last || last.document !== before) return;
    if (snapshotDocument() === before) {
      state.undoStack.pop();
      state.redoStack = last.redoBefore || [];
    } else {
      delete last.redoBefore;
    }
    updateHistoryButtons();
  }

  function mutate(label, callback, options) {
    const before = beginHistory(label);
    callback();
    discardNoopHistory(before);
    renderAll();
    if (options && options.toast) showToast(options.toast);
  }

  function restoreSnapshot(serialized) {
    const next = Core.normalizeDocument(JSON.parse(serialized));
    state.doc = next;
    applyDocumentSettings(state.doc);
    if (!selectedElement()) {
      state.selectionId = null;
      state.activeNode = null;
    }
    state.draft = null;
    state.interaction = null;
    state.snapTarget = null;
  }

  function undo() {
    if (state.draft) {
      cancelDraft();
      return;
    }
    const entry = state.undoStack.pop();
    if (!entry) return;
    state.redoStack.push({ label: entry.label, document: snapshotDocument() });
    restoreSnapshot(entry.document);
    renderAll();
    showToast(`Undid ${entry.label}`);
  }

  function redo() {
    const entry = state.redoStack.pop();
    if (!entry) return;
    state.undoStack.push({ label: entry.label, document: snapshotDocument() });
    restoreSnapshot(entry.document);
    renderAll();
    showToast(`Redid ${entry.label}`);
  }

  function updateHistoryButtons() {
    dom.undo.disabled = state.undoStack.length === 0;
    dom.redo.disabled = state.redoStack.length === 0;
    dom.undo.title = state.undoStack.length ? `Undo ${state.undoStack[state.undoStack.length - 1].label} (Ctrl+Z)` : 'Nothing to undo';
    dom.redo.title = state.redoStack.length ? `Redo ${state.redoStack[state.redoStack.length - 1].label} (Ctrl+Y)` : 'Nothing to redo';
  }

  function scheduleAutosave() {
    window.clearTimeout(state.autosaveTimer);
    if (state.autosaveBlocked) return;
    state.autosaveTimer = window.setTimeout(() => {
      try {
        window.localStorage.setItem(AUTOSAVE_KEY, snapshotDocument());
      } catch (error) {
        console.warn('Vector Studio autosave failed:', error);
      }
    }, 300);
  }

  function restoreAutosave() {
    let current = null;
    try {
      current = window.localStorage.getItem(AUTOSAVE_KEY);
      const saved = current || window.localStorage.getItem(LEGACY_AUTOSAVE_KEY);
      if (!saved) return false;
      const parsed = JSON.parse(saved);
      if (!Core.isProjectDocument(parsed)) {
        state.autosaveBlocked = Boolean(current);
        console.warn('Vector Studio ignored an incompatible autosave.');
        return false;
      }
      restoreSnapshot(JSON.stringify(parsed));
      if (!current) window.localStorage.setItem(AUTOSAVE_KEY, snapshotDocument());
      return true;
    } catch (error) {
      state.autosaveBlocked = Boolean(current);
      console.warn('Vector Studio could not restore its autosave:', error);
      return false;
    }
  }

  function showToast(message, tone) {
    const toast = document.createElement('div');
    toast.className = `toast${tone ? ` ${tone}` : ''}`;
    toast.textContent = message;
    dom.toastRegion.appendChild(toast);
    window.setTimeout(() => toast.classList.add('visible'), 10);
    window.setTimeout(() => {
      toast.classList.remove('visible');
      window.setTimeout(() => toast.remove(), 180);
    }, 2600);
  }

  function styleForNewElement(type) {
    const style = Core.clone(state.defaultStyle);
    if (type === 'line' || type === 'arc') style.fill = 'none';
    return style;
  }

  function nextName(type) {
    const label = {
      rect: 'Rectangle',
      ellipse: 'Ellipse',
      line: 'Line',
      arc: 'Arc',
      polygon: 'Polygon',
      path: 'Path'
    }[type] || 'Shape';
    const count = state.doc.elements.filter((element) => element.type === type).length + 1;
    return `${label} ${count}`;
  }

  function createElement(type, geometry) {
    return {
      id: Core.uid(type),
      type,
      name: nextName(type),
      visible: true,
      locked: false,
      style: styleForNewElement(type),
      ...geometry
    };
  }

  function applyShapeStyle(node, element) {
    const style = element.style || {};
    if (state.outline) {
      node.setAttribute('fill', 'none');
      node.setAttribute('stroke', '#ffcb69');
      node.setAttribute('stroke-width', String(Math.max(1 / state.zoom, 0.75)));
      node.setAttribute('stroke-opacity', '0.95');
    } else {
      node.setAttribute('fill', element.type === 'arc' ? 'none' : (style.fill || 'none'));
      node.setAttribute('fill-opacity', String(number(style.fillOpacity, 1)));
      node.setAttribute('stroke', style.stroke || 'none');
      node.setAttribute('stroke-opacity', String(number(style.strokeOpacity, 1)));
      node.setAttribute('stroke-width', String(Math.max(0, number(style.strokeWidth, 0))));
      node.setAttribute('stroke-linecap', style.lineCap || 'round');
      node.setAttribute('stroke-linejoin', style.lineJoin || 'round');
      if (style.dash) node.setAttribute('stroke-dasharray', style.dash);
    }
  }

  function artworkNode(element) {
    let node;
    if (element.type === 'rect') {
      node = svgNode('rect', {
        x: element.x,
        y: element.y,
        width: Math.max(0, element.width),
        height: Math.max(0, element.height),
        rx: Math.max(0, number(element.rx, 0))
      });
    } else if (element.type === 'ellipse') {
      node = svgNode('ellipse', { cx: element.cx, cy: element.cy, rx: Math.max(0, element.rx), ry: Math.max(0, element.ry) });
    } else if (element.type === 'line') {
      node = svgNode('line', { x1: element.x1, y1: element.y1, x2: element.x2, y2: element.y2 });
    } else if (element.type === 'arc') {
      node = svgNode('path', { d: Core.arcPathData(element) });
    } else if (element.type === 'polygon') {
      node = svgNode('polygon', { points: (element.points || []).map((point) => `${point.x},${point.y}`).join(' ') });
    } else if (element.type === 'path') {
      node = svgNode('path', { d: Core.pathData(element.points || [], Boolean(element.closed)) });
    } else {
      return null;
    }
    node.dataset.elementId = element.id;
    node.setAttribute('data-name', element.name || element.type);
    node.classList.add('artwork-element');
    applyShapeStyle(node, element);
    if (element.locked) node.classList.add('locked-element');
    return node;
  }

  function renderReference() {
    const reference = state.doc.reference;
    if (!reference || !reference.dataUrl || reference.visible === false) {
      replaceChildren(dom.referenceLayer, []);
      return;
    }
    const padding = Core.clamp(number(reference.padding, 0), 0, 45) / 100;
    const x = state.doc.width * padding;
    const y = state.doc.height * padding;
    const width = state.doc.width * (1 - padding * 2);
    const height = state.doc.height * (1 - padding * 2);
    const filter = {
      grayscale: 'grayscale(1)',
      invert: 'grayscale(1) invert(1)',
      contrast: 'grayscale(1) contrast(2.35)'
    }[reference.filter] || 'none';
    const image = svgNode('image', {
      href: reference.dataUrl,
      x,
      y,
      width,
      height,
      opacity: Core.clamp(number(reference.opacity, 0.42), 0, 1),
      preserveAspectRatio: 'xMidYMid meet'
    });
    image.style.filter = filter;
    image.style.pointerEvents = 'none';
    replaceChildren(dom.referenceLayer, [image]);
  }

  function renderGrid() {
    const children = [];
    if (state.gridVisible) {
      const step = Core.clamp(number(state.gridSize, 16), 0.25, 1024);
      const majorEvery = Math.round(Core.clamp(number(state.gridMajorEvery, 4), 2, 16));
      const majorStep = step * majorEvery;
      let displayedMajorStep = majorStep;
      while (displayedMajorStep * state.zoom < 5 && displayedMajorStep < Math.max(state.doc.width, state.doc.height) * 2) {
        displayedMajorStep *= majorEvery;
      }
      const minorPatternId = 'vector-studio-grid-minor';
      const majorPatternId = 'vector-studio-grid-major';
      const defs = svgNode('defs');
      if (step * state.zoom >= 4) {
        const minorPattern = svgNode('pattern', {
          id: minorPatternId,
          width: step,
          height: step,
          patternUnits: 'userSpaceOnUse'
        });
        minorPattern.appendChild(svgNode('path', {
          d: `M ${step} 0 L 0 0 0 ${step}`,
          fill: 'none',
          class: 'grid-minor-line',
          'stroke-width': Math.max(0.45 / state.zoom, 0.12)
        }));
        defs.appendChild(minorPattern);
      }
      const majorPattern = svgNode('pattern', {
        id: majorPatternId,
        width: displayedMajorStep,
        height: displayedMajorStep,
        patternUnits: 'userSpaceOnUse'
      });
      majorPattern.appendChild(svgNode('path', {
        d: `M ${displayedMajorStep} 0 L 0 0 0 ${displayedMajorStep}`,
        fill: 'none',
        class: 'grid-major-line',
        'stroke-width': Math.max(0.8 / state.zoom, 0.2)
      }));
      defs.appendChild(majorPattern);
      children.push(defs);
      if (step * state.zoom >= 4) {
        children.push(svgNode('rect', {
          x: 0,
          y: 0,
          width: state.doc.width,
          height: state.doc.height,
          fill: `url(#${minorPatternId})`,
          class: 'canvas-grid grid-minor'
        }));
      }
      children.push(svgNode('rect', {
        x: 0,
        y: 0,
        width: state.doc.width,
        height: state.doc.height,
        fill: `url(#${majorPatternId})`,
        class: 'canvas-grid grid-major'
      }));
    }
    if (state.guidesVisible) {
      children.push(svgNode('line', {
        x1: state.doc.width / 2,
        y1: 0,
        x2: state.doc.width / 2,
        y2: state.doc.height,
        class: 'center-guide',
        'stroke-width': Math.max(1 / state.zoom, 0.4)
      }));
      children.push(svgNode('line', {
        x1: 0,
        y1: state.doc.height / 2,
        x2: state.doc.width,
        y2: state.doc.height / 2,
        class: 'center-guide horizontal',
        'stroke-width': Math.max(1 / state.zoom, 0.4)
      }));
    }
    replaceChildren(dom.gridLayer, children);
  }

  function renderArtwork() {
    const nodes = state.doc.elements
      .filter((element) => element.visible !== false)
      .map(artworkNode)
      .filter(Boolean);
    replaceChildren(dom.artworkLayer, nodes);
  }

  function makeHandle(x, y, attributes) {
    const size = Core.clamp(8 / state.zoom, 1.8, 18);
    return svgNode('rect', {
      x: x - size / 2,
      y: y - size / 2,
      width: size,
      height: size,
      rx: size * 0.2,
      class: 'selection-handle',
      ...attributes
    });
  }

  function renderLineCurveHandle(element) {
    if (!element || !['line', 'arc'].includes(element.type) || element.locked) return [];
    const start = element.type === 'arc'
      ? Core.arcPoint(element, 0)
      : { x: number(element.x1, 0), y: number(element.y1, 0) };
    const end = element.type === 'arc'
      ? Core.arcPoint(element, 1)
      : { x: number(element.x2, 0), y: number(element.y2, 0) };
    if (!start || !end) return [];
    const middleX = (start.x + end.x) / 2;
    const middleY = (start.y + end.y) / 2;
    const deltaX = end.x - start.x;
    const deltaY = end.y - start.y;
    const length = Math.hypot(deltaX, deltaY);
    if ((element.type === 'line' && length < 0.5) || length < 0.000000001) return [];
    const normalX = -deltaY / length;
    const normalY = deltaX / length;
    const guideLength = Core.clamp(18 / state.zoom, 4, 34);
    const radius = Core.clamp(6 / state.zoom, 2.2, 13);
    const bend = element.type === 'arc' ? Core.arcPoint(element, 0.5) : { x: middleX, y: middleY };
    if (!bend) return [];
    const guide = svgNode('line', {
      x1: element.type === 'arc' ? middleX : middleX - normalX * guideLength,
      y1: element.type === 'arc' ? middleY : middleY - normalY * guideLength,
      x2: element.type === 'arc' ? bend.x : middleX + normalX * guideLength,
      y2: element.type === 'arc' ? bend.y : middleY + normalY * guideLength,
      class: 'line-curve-guide',
      'stroke-width': Math.max(1.1 / state.zoom, 0.45),
      'pointer-events': 'none'
    });
    const handle = svgNode('circle', {
      cx: bend.x,
      cy: bend.y,
      r: radius,
      class: 'line-curve-handle',
      'data-line-curve-handle': 'true'
    });
    const title = svgNode('title');
    title.textContent = 'Drag perpendicular to set the radius of this exact circular segment.';
    handle.appendChild(title);
    const children = [guide];
    if (element.type === 'arc') {
      children.push(svgNode('circle', {
        cx: element.cx,
        cy: element.cy,
        r: Core.clamp(3.5 / state.zoom, 1.2, 8),
        class: 'arc-center-indicator',
        'stroke-width': Math.max(1 / state.zoom, 0.4),
        'pointer-events': 'none'
      }));
    }
    children.push(handle);
    return children;
  }

  function renderBoundingSelection(element, bounds) {
    if (!bounds) return [];
    const children = [svgNode('rect', {
      x: bounds.x,
      y: bounds.y,
      width: Math.max(0, bounds.width),
      height: Math.max(0, bounds.height),
      class: 'selection-box',
      'stroke-width': Math.max(1.1 / state.zoom, 0.45),
      'pointer-events': 'none'
    })];
    if (!element.locked && state.tool === 'select') {
      const positions = {
        nw: [bounds.x, bounds.y],
        n: [bounds.x + bounds.width / 2, bounds.y],
        ne: [bounds.x + bounds.width, bounds.y],
        e: [bounds.x + bounds.width, bounds.y + bounds.height / 2],
        se: [bounds.x + bounds.width, bounds.y + bounds.height],
        s: [bounds.x + bounds.width / 2, bounds.y + bounds.height],
        sw: [bounds.x, bounds.y + bounds.height],
        w: [bounds.x, bounds.y + bounds.height / 2]
      };
      Object.entries(positions).forEach(([handle, coords]) => {
        if (element.type === 'arc' && handle.length !== 2) return;
        children.push(makeHandle(coords[0], coords[1], { 'data-resize-handle': handle }));
      });
    }
    return children;
  }

  function renderNodeSelection(element) {
    const children = [];
    const points = element.type === 'line'
      ? [{ x: element.x1, y: element.y1 }, { x: element.x2, y: element.y2 }]
      : (element.points || []);
    const handleRadius = Core.clamp(4.2 / state.zoom, 1.4, 10);
    const anchorSize = Core.clamp(8 / state.zoom, 2.2, 18);
    points.forEach((point, index) => {
      if (element.type === 'path') {
        ['in', 'out'].forEach((kind) => {
          const hx = point[`${kind}X`];
          const hy = point[`${kind}Y`];
          if (!Number.isFinite(hx) || !Number.isFinite(hy)) return;
          children.push(svgNode('line', {
            x1: point.x,
            y1: point.y,
            x2: hx,
            y2: hy,
            class: 'bezier-arm',
            'stroke-width': Math.max(1 / state.zoom, 0.4),
            'pointer-events': 'none'
          }));
          children.push(svgNode('circle', {
            cx: hx,
            cy: hy,
            r: handleRadius,
            class: 'bezier-handle',
            'data-node-index': index,
            'data-node-handle': kind
          }));
        });
      }
      children.push(svgNode('rect', {
        x: point.x - anchorSize / 2,
        y: point.y - anchorSize / 2,
        width: anchorSize,
        height: anchorSize,
        rx: anchorSize * 0.18,
        class: `node-anchor${state.activeNode === index ? ' active' : ''}`,
        'data-node-index': index,
        'data-node-handle': 'anchor'
      }));
    });
    return children;
  }

  function renderSnapIndicator() {
    if (!state.snapTarget) return [];
    return [svgNode('circle', {
      cx: state.snapTarget.x,
      cy: state.snapTarget.y,
      r: 7 / state.zoom,
      class: 'snap-target-indicator',
      'stroke-width': 1.5,
      'pointer-events': 'none'
    })];
  }

  function renderSelection() {
    const element = selectedElement();
    if (!element || element.visible === false) {
      replaceChildren(dom.selectionLayer, renderSnapIndicator());
      return;
    }
    const bounds = Core.elementBounds(element);
    if (!bounds) {
      replaceChildren(dom.selectionLayer, renderSnapIndicator());
      return;
    }
    let children = renderBoundingSelection(element, bounds);
    if ((state.tool === 'line' || state.tool === 'select') && (element.type === 'line' || element.type === 'arc')) {
      children = children.concat(renderLineCurveHandle(element));
    }
    if (state.tool === 'node' && (element.type === 'line' || element.type === 'path' || element.type === 'polygon')) {
      children = children.concat(renderNodeSelection(element));
    }
    children = children.concat(renderSnapIndicator());
    replaceChildren(dom.selectionLayer, children);
  }

  function renderDraft() {
    const draft = state.draft;
    if (!draft) {
      replaceChildren(dom.draftLayer, []);
      return;
    }
    const element = state.doc.elements.find((item) => item.id === draft.elementId);
    if (!element || !element.points || element.points.length === 0) {
      replaceChildren(dom.draftLayer, []);
      return;
    }
    const last = element.points[element.points.length - 1];
    const first = element.points[0];
    const children = [svgNode('line', {
      x1: last.x,
      y1: last.y,
      x2: state.cursor.x,
      y2: state.cursor.y,
      class: 'draft-segment',
      'stroke-width': Math.max(1.2 / state.zoom, 0.5)
    })];
    const radius = Core.clamp(6 / state.zoom, 2, 14);
    children.push(svgNode('circle', { cx: first.x, cy: first.y, r: radius, class: 'draft-close-target' }));
    children.push(svgNode('circle', { cx: state.cursor.x, cy: state.cursor.y, r: Math.max(radius * 0.55, 1), class: 'draft-cursor' }));
    replaceChildren(dom.draftLayer, children);
  }

  function renderLayers() {
    dom.layerCount.textContent = String(state.doc.elements.length);
    if (state.doc.elements.length === 0) {
      const empty = document.createElement('div');
      empty.className = 'layers-empty';
      empty.textContent = 'Draw a shape to create the first layer.';
      replaceChildren(dom.layersList, [empty]);
      return;
    }
    const rows = state.doc.elements.slice().reverse().map((element) => {
      const row = document.createElement('div');
      row.className = `layer-row${element.id === state.selectionId ? ' selected' : ''}`;
      row.dataset.layerId = element.id;

      const visible = document.createElement('button');
      visible.type = 'button';
      visible.className = 'layer-icon';
      visible.dataset.layerAction = 'visible';
      visible.title = element.visible === false ? 'Show layer' : 'Hide layer';
      visible.textContent = element.visible === false ? '○' : '●';

      const lock = document.createElement('button');
      lock.type = 'button';
      lock.className = 'layer-icon';
      lock.dataset.layerAction = 'lock';
      lock.title = element.locked ? 'Unlock layer' : 'Lock layer';
      lock.textContent = element.locked ? '◆' : '◇';

      const name = document.createElement('button');
      name.type = 'button';
      name.className = 'layer-name';
      name.dataset.layerAction = 'select';
      name.innerHTML = `<span>${escapeHtml(element.name || element.type)}</span><small>${escapeHtml(element.type)}</small>`;

      row.append(visible, lock, name);
      return row;
    });
    replaceChildren(dom.layersList, rows);
  }

  function escapeHtml(value) {
    return String(value)
      .replaceAll('&', '&amp;')
      .replaceAll('<', '&lt;')
      .replaceAll('>', '&gt;')
      .replaceAll('"', '&quot;')
      .replaceAll("'", '&#039;');
  }

  function setInputValue(id, value) {
    const input = $(id);
    if (input && document.activeElement !== input) input.value = value;
  }

  function renderInspector() {
    const element = selectedElement();
    dom.selectionEmpty.hidden = Boolean(element);
    dom.selectionControls.hidden = !element;
    dom.selectionKind.textContent = element ? (element.type === 'arc' ? 'circle arc' : element.type) : 'None';
    dom.arcControls.hidden = !element || element.type !== 'arc';
    if (!element) return;

    const bounds = Core.elementBounds(element);
    if (!bounds) {
      dom.selectionEmpty.hidden = false;
      dom.selectionControls.hidden = true;
      dom.selectionKind.textContent = 'Invalid';
      return;
    }
    setInputValue('element-name', element.name || '');
    setInputValue('element-x', pretty(bounds.x));
    setInputValue('element-y', pretty(bounds.y));
    setInputValue('element-width', pretty(bounds.width));
    setInputValue('element-height', pretty(bounds.height));

    if (element.type === 'arc') {
      setInputValue('arc-center-x', pretty(element.cx));
      setInputValue('arc-center-y', pretty(element.cy));
      setInputValue('arc-radius', pretty(element.radius));
      setInputValue('arc-start-angle', pretty(element.startAngle * 180 / Math.PI));
      setInputValue('arc-sweep-angle', pretty(element.sweepAngle * 180 / Math.PI));
    }

    const style = element.style || {};
    const supportsFill = element.type !== 'line' && element.type !== 'arc';
    $('fill-enabled').checked = supportsFill && style.fill !== 'none';
    $('fill-enabled').disabled = !supportsFill;
    setInputValue('fill-color', style.fill && style.fill !== 'none' ? style.fill : '#ffffff');
    $('fill-color').disabled = !supportsFill;
    setInputValue('fill-opacity', Math.round(number(style.fillOpacity, 1) * 100));
    $('fill-opacity').disabled = !supportsFill;
    $('stroke-enabled').checked = style.stroke !== 'none' && number(style.strokeWidth, 0) > 0;
    setInputValue('stroke-color', style.stroke && style.stroke !== 'none' ? style.stroke : '#11151d');
    setInputValue('stroke-width', pretty(number(style.strokeWidth, 0)));
    setInputValue('stroke-opacity', Math.round(number(style.strokeOpacity, 1) * 100));
    setInputValue('stroke-join', style.lineJoin || 'round');
    setInputValue('stroke-cap', style.lineCap || 'round');
    setInputValue('stroke-dash', style.dash || '');

    const canShowNodes = state.tool === 'node' && (element.type === 'line' || element.type === 'path' || element.type === 'polygon');
    const canEditNodes = canShowNodes && (element.type === 'path' || element.type === 'polygon');
    dom.nodeActions.hidden = !canEditNodes;
    dom.activeNodeLabel.textContent = canShowNodes && state.activeNode !== null ? `#${state.activeNode + 1}` : 'None';
    const nodeDisabled = !canEditNodes || element.locked || state.activeNode === null;
    $('node-smooth').disabled = nodeDisabled || element.type !== 'path';
    $('node-corner').disabled = nodeDisabled || element.type !== 'path';
    $('node-add').disabled = nodeDisabled;
    $('node-delete').disabled = nodeDisabled;
  }

  function renderReferenceInspector() {
    const reference = state.doc.reference;
    dom.referenceControls.hidden = !reference;
    dom.referenceDrop.hidden = Boolean(reference);
    dom.referenceStatus.textContent = reference ? reference.name || 'Loaded' : 'None';
    if (!reference) return;
    const opacity = Math.round(number(reference.opacity, 0.42) * 100);
    setInputValue('reference-opacity', opacity);
    $('reference-opacity-value').textContent = `${opacity}%`;
    setInputValue('reference-padding', number(reference.padding, 0));
    setInputValue('reference-filter', reference.filter || 'none');
    $('reference-visible').checked = reference.visible !== false;
  }

  function renderDocumentInspector() {
    setInputValue('artboard-width', state.doc.width);
    setInputValue('artboard-height', state.doc.height);
    setInputValue('grid-size', state.gridSize);
    setInputValue('grid-major-every', state.gridMajorEvery);
    setInputValue('snap-mode', state.snapMode);
    dom.statusSize.textContent = `${state.doc.width} × ${state.doc.height}`;
  }

  function updateArtboardGeometry() {
    dom.artboard.setAttribute('viewBox', `0 0 ${state.doc.width} ${state.doc.height}`);
    dom.artboardShell.style.width = `${state.doc.width * state.zoom}px`;
    dom.artboardShell.style.height = `${state.doc.height * state.zoom}px`;
    dom.zoomReadout.textContent = `${Math.round(state.zoom * 100)}%`;
    dom.artboardShell.dataset.preview = state.stageTheme;
    dom.stage.classList.remove('theme-checker', 'theme-dark', 'theme-light');
    dom.stage.classList.add(`theme-${state.stageTheme}`);
  }

  function renderToolState() {
    document.querySelectorAll('[data-tool]').forEach((button) => {
      const active = button.dataset.tool === state.tool;
      button.classList.toggle('active', active);
      button.setAttribute('aria-pressed', String(active));
    });
    dom.statusTool.textContent = TOOL_LABELS[state.tool];
    dom.statusHint.textContent = TOOL_HINTS[state.tool];
    dom.artboard.dataset.tool = state.spacePressed ? 'pan' : state.tool;
  }

  function renderToggles() {
    const toggles = [
      ['toggle-grid', state.gridVisible],
      ['toggle-guides', state.guidesVisible],
      ['toggle-outline', state.outline]
    ];
    toggles.forEach(([id, active]) => {
      $(id).classList.toggle('active', active);
      $(id).setAttribute('aria-pressed', String(active));
    });
    document.querySelectorAll('[data-stage-theme]').forEach((button) => {
      button.classList.toggle('active', button.dataset.stageTheme === state.stageTheme);
    });
  }

  function renderCanvas() {
    updateArtboardGeometry();
    renderReference();
    renderGrid();
    renderArtwork();
    renderDraft();
    renderSelection();
  }

  function renderAll() {
    renderCanvas();
    renderLayers();
    renderInspector();
    renderReferenceInspector();
    renderDocumentInspector();
    renderToolState();
    renderToggles();
    updateHistoryButtons();
    scheduleAutosave();
  }

  function selectElement(id, switchToSelect) {
    state.selectionId = state.doc.elements.some((element) => element.id === id) ? id : null;
    state.activeNode = null;
    if (switchToSelect) setTool('select', false);
    renderSelection();
    renderLayers();
    renderInspector();
  }

  function setTool(tool, render) {
    if (!TOOL_HINTS[tool]) return;
    if (state.draft && tool !== state.tool) finishDraft(false);
    state.tool = tool;
    state.activeNode = null;
    renderToolState();
    renderSelection();
    renderInspector();
    if (render !== false) dom.stage.focus({ preventScroll: true });
  }

  function restoreElementFrom(original) {
    const index = elementIndex(original.id);
    if (index >= 0) state.doc.elements[index] = Core.clone(original);
    return index >= 0 ? state.doc.elements[index] : null;
  }

  function isTinyElement(element) {
    const bounds = Core.elementBounds(element);
    if (!bounds) return true;
    if (element.type === 'line') {
      return Math.hypot(element.x2 - element.x1, element.y2 - element.y1) < 0.5;
    }
    return bounds.width < 0.5 && bounds.height < 0.5;
  }

  function startPrimitive(tool, point, event) {
    const before = beginHistory(`draw ${TOOL_LABELS[tool].toLowerCase()}`);
    let element;
    if (tool === 'rect') {
      element = createElement('rect', { x: point.x, y: point.y, width: 0, height: 0, rx: 0 });
    } else if (tool === 'ellipse') {
      element = createElement('ellipse', { cx: point.x, cy: point.y, rx: 0, ry: 0 });
    } else {
      element = createElement('line', { x1: point.x, y1: point.y, x2: point.x, y2: point.y });
    }
    state.doc.elements.push(element);
    state.selectionId = element.id;
    state.activeNode = null;
    state.interaction = {
      kind: 'draw',
      tool,
      elementId: element.id,
      start: point,
      before,
      pointerId: event.pointerId
    };
    dom.artboard.setPointerCapture(event.pointerId);
    renderCanvas();
    renderInspector();
    renderLayers();
  }

  function updatePrimitive(interaction, point, event) {
    const element = state.doc.elements.find((item) => item.id === interaction.elementId);
    if (!element) return;
    const start = interaction.start;
    let current = point;

    if (interaction.tool === 'line') {
      if (event.shiftKey) {
        const dx = current.x - start.x;
        const dy = current.y - start.y;
        const angleIndex = Math.round(Math.atan2(dy, dx) / (Math.PI / 4));
        const angle = angleIndex * (Math.PI / 4);
        if (snapUsesGrid() && !event.altKey) {
          const direction = ((angleIndex % 8) + 8) % 8;
          if (direction === 0 || direction === 4) {
            current = { x: current.x, y: start.y };
          } else if (direction === 2 || direction === 6) {
            current = { x: start.x, y: current.y };
          } else {
            const component = Core.snap(Math.max(Math.abs(dx), Math.abs(dy)), state.gridSize, true);
            current = {
              x: start.x + Math.sign(Math.cos(angle)) * component,
              y: start.y + Math.sign(Math.sin(angle)) * component
            };
          }
        } else {
          const distance = Math.hypot(dx, dy);
          current = { x: start.x + Math.cos(angle) * distance, y: start.y + Math.sin(angle) * distance };
        }
      }
      if (state.snapTarget && Math.hypot(current.x - state.snapTarget.x, current.y - state.snapTarget.y) > 0.000001) {
        state.snapTarget = null;
      }
      element.x2 = current.x;
      element.y2 = current.y;
      return;
    }

    let dx = current.x - start.x;
    let dy = current.y - start.y;
    if (event.shiftKey) {
      const magnitude = Math.max(Math.abs(dx), Math.abs(dy));
      dx = Math.sign(dx || 1) * magnitude;
      dy = Math.sign(dy || 1) * magnitude;
    }

    const left = event.altKey ? start.x - Math.abs(dx) : Math.min(start.x, start.x + dx);
    const right = event.altKey ? start.x + Math.abs(dx) : Math.max(start.x, start.x + dx);
    const top = event.altKey ? start.y - Math.abs(dy) : Math.min(start.y, start.y + dy);
    const bottom = event.altKey ? start.y + Math.abs(dy) : Math.max(start.y, start.y + dy);

    if (interaction.tool === 'rect') {
      element.x = left;
      element.y = top;
      element.width = right - left;
      element.height = bottom - top;
    } else {
      element.cx = (left + right) / 2;
      element.cy = (top + bottom) / 2;
      element.rx = (right - left) / 2;
      element.ry = (bottom - top) / 2;
    }
  }

  function startMove(element, point, event) {
    if (element.locked) {
      showToast('Unlock this layer before moving it.');
      return;
    }
    const before = beginHistory('move shape');
    state.interaction = {
      kind: 'move',
      elementId: element.id,
      start: point,
      original: Core.clone(element),
      before,
      pointerId: event.pointerId
    };
    dom.artboard.setPointerCapture(event.pointerId);
  }

  function updateMove(interaction, point) {
    const element = restoreElementFrom(interaction.original);
    if (!element) return;
    Core.translateElement(element, point.x - interaction.start.x, point.y - interaction.start.y);
  }

  function startLineCurve(event) {
    const element = selectedElement();
    if (!element || !['line', 'arc'].includes(element.type) || element.locked) return;
    const start = element.type === 'arc'
      ? Core.arcPoint(element, 0)
      : { x: element.x1, y: element.y1 };
    const end = element.type === 'arc'
      ? Core.arcPoint(element, 1)
      : { x: element.x2, y: element.y2 };
    if (!start || !end) return;
    const bendStart = element.type === 'arc'
      ? Core.arcPoint(element, 0.5)
      : { x: (start.x + end.x) / 2, y: (start.y + end.y) / 2 };
    const pointerStart = pointFromEvent(event, false);
    if (!bendStart) return;
    const before = beginHistory('curve line');
    state.interaction = {
      kind: 'line-curve',
      elementId: element.id,
      original: Core.clone(element),
      chord: { x1: start.x, y1: start.y, x2: end.x, y2: end.y },
      bendStart,
      pointerStart,
      before,
      pointerId: event.pointerId,
      clientX: event.clientX,
      clientY: event.clientY,
      moved: false
    };
    dom.artboard.setPointerCapture(event.pointerId);
  }

  function updateLineCurve(interaction, point, event) {
    const original = interaction.original;
    const index = elementIndex(original.id);
    if (index < 0) return;
    const chord = interaction.chord;
    const geometry = Core.circularArcFromChord(
      chord.x1,
      chord.y1,
      chord.x2,
      chord.y2,
      point.x,
      point.y
    );
    const threshold = 2.5 / state.zoom;
    if (!geometry || (original.type === 'line' && geometry.inputSagitta < threshold)) {
      const replacement = Core.clone(original);
      replacement.type = 'line';
      replacement.x1 = chord.x1;
      replacement.y1 = chord.y1;
      replacement.x2 = chord.x2;
      replacement.y2 = chord.y2;
      delete replacement.cx;
      delete replacement.cy;
      delete replacement.radius;
      delete replacement.startAngle;
      delete replacement.sweepAngle;
      state.doc.elements[index] = replacement;
      dom.statusHint.textContent = 'Pull farther from the chord to create a circular segment.';
      return;
    }

    const replacement = Core.clone(original);
    replacement.type = 'arc';
    replacement.cx = geometry.cx;
    replacement.cy = geometry.cy;
    replacement.radius = geometry.radius;
    replacement.startAngle = geometry.startAngle;
    replacement.sweepAngle = geometry.sweepAngle;
    delete replacement.x1;
    delete replacement.y1;
    delete replacement.x2;
    delete replacement.y2;
    delete replacement.points;
    delete replacement.closed;
    state.doc.elements[index] = replacement;
    dom.statusHint.textContent = `Circle segment · radius ${pretty(geometry.radius)} · sweep ${pretty(Math.abs(geometry.sweepAngle) * 180 / Math.PI)}°`;
  }

  function boundsFromResize(handle, original, point, preserveRatio) {
    const minimum = 0.01;
    let x1 = original.x;
    let y1 = original.y;
    let x2 = original.x + original.width;
    let y2 = original.y + original.height;

    if (handle.includes('w')) x1 = Math.min(point.x, x2 - minimum);
    if (handle.includes('e')) x2 = Math.max(point.x, x1 + minimum);
    if (handle.includes('n')) y1 = Math.min(point.y, y2 - minimum);
    if (handle.includes('s')) y2 = Math.max(point.y, y1 + minimum);

    if (preserveRatio && handle.length === 2) {
      const ratio = original.height > 0.000000001 ? Math.max(original.width, 0.000000001) / original.height : 1;
      const anchorX = handle.includes('w') ? original.x + original.width : original.x;
      const anchorY = handle.includes('n') ? original.y + original.height : original.y;
      let width = Math.abs((handle.includes('w') ? x1 : x2) - anchorX);
      let height = Math.abs((handle.includes('n') ? y1 : y2) - anchorY);
      if (width / Math.max(height, minimum) > ratio) height = width / ratio;
      else width = height * ratio;
      x1 = handle.includes('w') ? anchorX - width : anchorX;
      x2 = handle.includes('w') ? anchorX : anchorX + width;
      y1 = handle.includes('n') ? anchorY - height : anchorY;
      y2 = handle.includes('n') ? anchorY : anchorY + height;
    }

    return { x: x1, y: y1, width: x2 - x1, height: y2 - y1 };
  }

  function startResize(handle, point, event) {
    const element = selectedElement();
    if (!element || element.locked) return;
    const originalBounds = Core.elementBounds(element);
    if (!originalBounds) return;
    const before = beginHistory('resize shape');
    state.interaction = {
      kind: 'resize',
      handle,
      elementId: element.id,
      start: point,
      original: Core.clone(element),
      originalBounds,
      before,
      pointerId: event.pointerId
    };
    dom.artboard.setPointerCapture(event.pointerId);
  }

  function updateResize(interaction, point, event) {
    const element = restoreElementFrom(interaction.original);
    if (!element) return;
    const nextBounds = boundsFromResize(
      interaction.handle,
      interaction.originalBounds,
      point,
      event.shiftKey || element.type === 'arc'
    );
    Core.scaleElementToBounds(element, interaction.originalBounds, nextBounds);
  }

  function startNodeDrag(index, handle, point, event) {
    const element = selectedElement();
    if (!element || element.locked) return;
    const pointerStart = pointFromEvent(event, false);
    if (element.type === 'line') {
      if (handle !== 'anchor' || (index !== 0 && index !== 1)) return;
      state.activeNode = index;
      const before = beginHistory('move line endpoint');
      state.interaction = {
        kind: 'line-node',
        elementId: element.id,
        nodeIndex: index,
        handle: 'anchor',
        original: Core.clone(element),
        targetStart: index === 0 ? { x: element.x1, y: element.y1 } : { x: element.x2, y: element.y2 },
        pointerStart,
        clientX: event.clientX,
        clientY: event.clientY,
        moved: false,
        before,
        pointerId: event.pointerId
      };
      dom.artboard.setPointerCapture(event.pointerId);
      renderSelection();
      renderInspector();
      return;
    }
    if (!element.points || !element.points[index]) return;
    state.activeNode = index;
    const sourceNode = element.points[index];
    const targetStart = handle === 'anchor'
      ? { x: sourceNode.x, y: sourceNode.y }
      : { x: sourceNode[`${handle}X`], y: sourceNode[`${handle}Y`] };
    const before = beginHistory(handle === 'anchor' ? 'move node' : 'adjust curve');
    state.interaction = {
      kind: 'node',
      elementId: element.id,
      nodeIndex: index,
      handle,
      start: point,
      original: Core.clone(element),
      targetStart,
      pointerStart,
      clientX: event.clientX,
      clientY: event.clientY,
      moved: false,
      before,
      pointerId: event.pointerId
    };
    dom.artboard.setPointerCapture(event.pointerId);
    renderSelection();
    renderInspector();
  }

  function updateNodeDrag(interaction, point, event) {
    const element = restoreElementFrom(interaction.original);
    if (!element || !element.points || !element.points[interaction.nodeIndex]) return;
    const node = element.points[interaction.nodeIndex];
    const originalNode = interaction.original.points[interaction.nodeIndex];
    if (interaction.handle === 'anchor') {
      const dx = point.x - originalNode.x;
      const dy = point.y - originalNode.y;
      node.x = point.x;
      node.y = point.y;
      ['in', 'out'].forEach((kind) => {
        if (Number.isFinite(originalNode[`${kind}X`]) && Number.isFinite(originalNode[`${kind}Y`])) {
          node[`${kind}X`] = originalNode[`${kind}X`] + dx;
          node[`${kind}Y`] = originalNode[`${kind}Y`] + dy;
        }
      });
      return;
    }

    const kind = interaction.handle;
    const opposite = kind === 'in' ? 'out' : 'in';
    node[`${kind}X`] = point.x;
    node[`${kind}Y`] = point.y;
    if (!event.altKey) {
      const oppositeX = originalNode[`${opposite}X`];
      const oppositeY = originalNode[`${opposite}Y`];
      const originalLength = Number.isFinite(oppositeX) && Number.isFinite(oppositeY)
        ? Math.hypot(oppositeX - originalNode.x, oppositeY - originalNode.y)
        : Math.hypot(point.x - node.x, point.y - node.y);
      const angle = Math.atan2(point.y - node.y, point.x - node.x) + Math.PI;
      node[`${opposite}X`] = node.x + Math.cos(angle) * originalLength;
      node[`${opposite}Y`] = node.y + Math.sin(angle) * originalLength;
    }
  }

  function updateLineNodeDrag(interaction, point) {
    const element = restoreElementFrom(interaction.original);
    if (!element || element.type !== 'line') return;
    const prefix = interaction.nodeIndex === 0 ? '1' : '2';
    element[`x${prefix}`] = point.x;
    element[`y${prefix}`] = point.y;
  }

  function nodeDragPoint(interaction, rawPoint, event) {
    return snapPoint({
      x: interaction.targetStart.x + rawPoint.x - interaction.pointerStart.x,
      y: interaction.targetStart.y + rawPoint.y - interaction.pointerStart.y
    }, event, {
      anchors: interaction.handle === 'anchor',
      excludeId: interaction.elementId
    });
  }

  function distance(a, b) {
    return Math.hypot(a.x - b.x, a.y - b.y);
  }

  function beginDraft(kind, point) {
    const before = beginHistory(`draw ${kind === 'pen' ? 'path' : 'polygon'}`);
    const type = kind === 'pen' ? 'path' : 'polygon';
    const element = createElement(type, { points: [{ x: point.x, y: point.y }] });
    if (type === 'path') element.closed = false;
    state.doc.elements.push(element);
    state.selectionId = element.id;
    state.activeNode = 0;
    state.draft = { kind, elementId: element.id, before };
    return element;
  }

  function addDraftPoint(kind, point, event) {
    let element;
    if (!state.draft) {
      element = beginDraft(kind, point);
    } else {
      element = state.doc.elements.find((item) => item.id === state.draft.elementId);
      if (!element) return;
      const closeCount = kind === 'polygon' ? 3 : 2;
      const closeDistance = Core.clamp(11 / state.zoom, 2, 24);
      if (element.points.length >= closeCount && distance(point, element.points[0]) <= closeDistance) {
        if (element.type === 'path') element.closed = true;
        finishDraft(true);
        return;
      }
      element.points.push({ x: point.x, y: point.y });
      state.activeNode = element.points.length - 1;
    }

    if (kind === 'pen' && state.draft) {
      state.interaction = {
        kind: 'pen-handle',
        elementId: element.id,
        nodeIndex: element.points.length - 1,
        anchor: { x: point.x, y: point.y },
        pointerId: event.pointerId
      };
      dom.artboard.setPointerCapture(event.pointerId);
    }
    renderCanvas();
    renderLayers();
    renderInspector();
  }

  function updatePenHandle(interaction, point) {
    const element = state.doc.elements.find((item) => item.id === interaction.elementId);
    if (!element || !element.points[interaction.nodeIndex]) return;
    const node = element.points[interaction.nodeIndex];
    const dx = point.x - interaction.anchor.x;
    const dy = point.y - interaction.anchor.y;
    if (Math.hypot(dx, dy) < 0.35) {
      delete node.inX;
      delete node.inY;
      delete node.outX;
      delete node.outY;
      return;
    }
    node.outX = interaction.anchor.x + dx;
    node.outY = interaction.anchor.y + dy;
    node.inX = interaction.anchor.x - dx;
    node.inY = interaction.anchor.y - dy;
  }

  function finishDraft(announce) {
    if (!state.draft) return;
    const draft = state.draft;
    const element = state.doc.elements.find((item) => item.id === draft.elementId);
    const minimum = draft.kind === 'polygon' ? 3 : 2;
    if (!element || !element.points || element.points.length < minimum) {
      restoreSnapshot(draft.before);
      discardNoopHistory(draft.before);
      if (announce) showToast(`${draft.kind === 'polygon' ? 'Polygon' : 'Path'} needs at least ${minimum} points.`);
    } else {
      discardNoopHistory(draft.before);
      state.draft = null;
      state.interaction = null;
      state.activeNode = null;
      if (announce) showToast(`${draft.kind === 'polygon' ? 'Polygon' : 'Path'} finished.`);
    }
    state.snapTarget = null;
    renderAll();
  }

  function cancelDraft() {
    if (!state.draft) return false;
    const before = state.draft.before;
    restoreSnapshot(before);
    discardNoopHistory(before);
    state.draft = null;
    state.interaction = null;
    renderAll();
    showToast('Drawing cancelled.');
    return true;
  }

  function cancelLineCurve(announce) {
    const interaction = state.interaction;
    if (!interaction || interaction.kind !== 'line-curve') return false;
    restoreSnapshot(interaction.before);
    discardNoopHistory(interaction.before);
    state.interaction = null;
    state.snapTarget = null;
    if (interaction.pointerId !== undefined && dom.artboard.hasPointerCapture(interaction.pointerId)) {
      dom.artboard.releasePointerCapture(interaction.pointerId);
    }
    renderAll();
    if (announce) showToast('Line curve cancelled.');
    return true;
  }

  function finishPointerInteraction(event) {
    const interaction = state.interaction;
    if (!interaction || interaction.kind === 'pan') return;
    if (interaction.pointerId !== undefined && event.pointerId !== interaction.pointerId) return;

    if (event.type === 'pointercancel' && interaction.before) {
      restoreSnapshot(interaction.before);
      discardNoopHistory(interaction.before);
      state.interaction = null;
      state.snapTarget = null;
      renderAll();
      return;
    }

    if (event.type === 'pointerup' && interaction.kind === 'line-curve'
      && (interaction.moved || Math.hypot(event.clientX - interaction.clientX, event.clientY - interaction.clientY) >= 1.5)) {
      const rawPoint = pointFromEvent(event, false);
      const point = snapPoint({
        x: interaction.bendStart.x + rawPoint.x - interaction.pointerStart.x,
        y: interaction.bendStart.y + rawPoint.y - interaction.pointerStart.y
      }, event);
      state.cursor = point;
      updateLineCurve(interaction, point, event);
    } else if (event.type === 'pointerup' && interaction.kind === 'draw') {
      const rawPoint = pointFromEvent(event, false);
      const point = snapPoint(rawPoint, event, {
        anchors: interaction.tool === 'line' && !event.shiftKey,
        excludeId: interaction.elementId
      });
      state.cursor = point;
      updatePrimitive(interaction, point, event);
    } else if (event.type === 'pointerup' && (interaction.kind === 'node' || interaction.kind === 'line-node')
      && (interaction.moved || Math.hypot(event.clientX - interaction.clientX, event.clientY - interaction.clientY) >= 1.5)) {
      const rawPoint = pointFromEvent(event, false);
      const point = nodeDragPoint(interaction, rawPoint, event);
      state.cursor = point;
      if (interaction.kind === 'line-node') updateLineNodeDrag(interaction, point);
      else updateNodeDrag(interaction, point, event);
    }

    if (interaction.kind === 'draw') {
      const element = state.doc.elements.find((item) => item.id === interaction.elementId);
      if (!element || isTinyElement(element)) {
        restoreSnapshot(interaction.before);
        discardNoopHistory(interaction.before);
      } else {
        discardNoopHistory(interaction.before);
      }
    } else if (interaction.kind !== 'pen-handle') {
      discardNoopHistory(interaction.before);
    }

    state.interaction = null;
    state.snapTarget = null;
    renderAll();
  }

  function onArtboardPointerDown(event) {
    if (event.defaultPrevented || event.button !== 0 || state.spacePressed || state.tool === 'pan') return;
    const rawPoint = pointFromEvent(event, false);
    let point = snapPoint(rawPoint, event);
    state.cursor = point;

    const lineCurveHandle = event.target.closest && event.target.closest('[data-line-curve-handle]');
    if (lineCurveHandle) {
      event.preventDefault();
      startLineCurve(event);
      return;
    }

    const resizeHandle = event.target.closest && event.target.closest('[data-resize-handle]');
    if (resizeHandle) {
      event.preventDefault();
      startResize(resizeHandle.dataset.resizeHandle, point, event);
      return;
    }

    const nodeHandle = event.target.closest && event.target.closest('[data-node-index]');
    if (nodeHandle && state.tool === 'node') {
      event.preventDefault();
      startNodeDrag(Number(nodeHandle.dataset.nodeIndex), nodeHandle.dataset.nodeHandle, point, event);
      return;
    }

    const artwork = event.target.closest && event.target.closest('[data-element-id]');
    const hit = artwork ? state.doc.elements.find((element) => element.id === artwork.dataset.elementId) : null;

    if (state.tool === 'select') {
      if (!hit) {
        selectElement(null);
        return;
      }
      if (state.selectionId !== hit.id) selectElement(hit.id);
      startMove(hit, point, event);
      event.preventDefault();
      return;
    }

    if (state.tool === 'node') {
      if (hit) selectElement(hit.id);
      else selectElement(null);
      event.preventDefault();
      return;
    }

    const endpointTarget = state.tool === 'line' && hit && ['line', 'arc'].includes(hit.type) && snapUsesAnchors()
      ? Core.nearestSnapAnchor(rawPoint, Core.elementSnapAnchors(hit), 9 / state.zoom)
      : null;
    if (state.tool === 'line' && hit && ['line', 'arc'].includes(hit.type) && !endpointTarget) {
      selectElement(hit.id);
      event.preventDefault();
      return;
    }

    if (state.tool === 'rect' || state.tool === 'ellipse' || state.tool === 'line') {
      event.preventDefault();
      point = snapPoint(rawPoint, event, { anchors: state.tool === 'line' });
      state.cursor = point;
      startPrimitive(state.tool, point, event);
      return;
    }

    if (state.tool === 'polygon' || state.tool === 'pen') {
      event.preventDefault();
      point = snapPoint(rawPoint, event, { anchors: true });
      state.cursor = point;
      addDraftPoint(state.tool, point, event);
    }
  }

  function onArtboardPointerMove(event) {
    const rawPoint = pointFromEvent(event, false);
    const interaction = state.interaction;
    const anchorSnap = Boolean(
      (interaction && interaction.kind === 'draw' && interaction.tool === 'line' && !event.shiftKey)
      || (interaction && interaction.kind === 'node' && interaction.handle === 'anchor')
      || (interaction && interaction.kind === 'line-node')
      || (!interaction && state.draft)
    );
    const excludeId = interaction && (interaction.kind === 'draw' || interaction.kind === 'node' || interaction.kind === 'line-node')
      ? interaction.elementId
      : (state.draft ? state.draft.elementId : null);
    const point = snapPoint(rawPoint, event, { anchors: anchorSnap, excludeId });
    state.cursor = point;
    dom.statusCursor.textContent = `x ${pretty(rawPoint.x)} · y ${pretty(rawPoint.y)}`;

    if (interaction) {
      if (interaction.kind === 'draw') updatePrimitive(interaction, point, event);
      else if (interaction.kind === 'move') updateMove(interaction, point);
      else if (interaction.kind === 'line-curve') {
        if (interaction.moved || Math.hypot(event.clientX - interaction.clientX, event.clientY - interaction.clientY) >= 1.5) {
          interaction.moved = true;
          const curvePoint = snapPoint({
            x: interaction.bendStart.x + rawPoint.x - interaction.pointerStart.x,
            y: interaction.bendStart.y + rawPoint.y - interaction.pointerStart.y
          }, event);
          state.cursor = curvePoint;
          updateLineCurve(interaction, curvePoint, event);
        }
      }
      else if (interaction.kind === 'resize') updateResize(interaction, point, event);
      else if (interaction.kind === 'node' || interaction.kind === 'line-node') {
        if (interaction.moved || Math.hypot(event.clientX - interaction.clientX, event.clientY - interaction.clientY) >= 1.5) {
          interaction.moved = true;
          const nodePoint = nodeDragPoint(interaction, rawPoint, event);
          state.cursor = nodePoint;
          if (interaction.kind === 'line-node') updateLineNodeDrag(interaction, nodePoint);
          else updateNodeDrag(interaction, nodePoint, event);
        }
      }
      else if (interaction.kind === 'pen-handle') updatePenHandle(interaction, point);
      if (interaction.kind !== 'pan') {
        renderCanvas();
        renderInspector();
      }
    } else if (state.draft) {
      renderDraft();
      renderSelection();
    }
  }

  function onStagePointerDown(event) {
    const wantsPan = state.spacePressed || state.tool === 'pan' || event.button === 1;
    if (!wantsPan) return;
    event.preventDefault();
    event.stopPropagation();
    state.interaction = {
      kind: 'pan',
      pointerId: event.pointerId,
      clientX: event.clientX,
      clientY: event.clientY,
      scrollLeft: dom.stage.scrollLeft,
      scrollTop: dom.stage.scrollTop
    };
    dom.stage.classList.add('is-panning');
    dom.stage.setPointerCapture(event.pointerId);
  }

  function onStagePointerMove(event) {
    const interaction = state.interaction;
    if (!interaction || interaction.kind !== 'pan' || interaction.pointerId !== event.pointerId) return;
    dom.stage.scrollLeft = interaction.scrollLeft - (event.clientX - interaction.clientX);
    dom.stage.scrollTop = interaction.scrollTop - (event.clientY - interaction.clientY);
  }

  function onStagePointerUp(event) {
    const interaction = state.interaction;
    if (!interaction || interaction.kind !== 'pan' || interaction.pointerId !== event.pointerId) return;
    state.interaction = null;
    dom.stage.classList.remove('is-panning');
    if (dom.stage.hasPointerCapture(event.pointerId)) dom.stage.releasePointerCapture(event.pointerId);
  }

  function setZoom(nextZoom, focus) {
    const previous = state.zoom;
    const zoom = Core.clamp(number(nextZoom, previous), MIN_ZOOM, MAX_ZOOM);
    if (Math.abs(zoom - previous) < 0.0001) return;

    const stageRect = dom.stage.getBoundingClientRect();
    const focusX = focus ? focus.x - stageRect.left : stageRect.width / 2;
    const focusY = focus ? focus.y - stageRect.top : stageRect.height / 2;
    const contentX = dom.stage.scrollLeft + focusX;
    const contentY = dom.stage.scrollTop + focusY;
    const ratio = zoom / previous;
    state.zoom = zoom;
    updateArtboardGeometry();
    renderGrid();
    renderSelection();
    renderDraft();
    window.requestAnimationFrame(() => {
      dom.stage.scrollLeft = contentX * ratio - focusX;
      dom.stage.scrollTop = contentY * ratio - focusY;
    });
  }

  function zoomFit() {
    const padding = window.innerWidth < 900 ? 56 : 140;
    const width = Math.max(100, dom.stage.clientWidth - padding);
    const height = Math.max(100, dom.stage.clientHeight - padding);
    setZoom(Math.min(width / state.doc.width, height / state.doc.height));
    window.requestAnimationFrame(() => {
      dom.stage.scrollLeft = Math.max(0, (dom.stage.scrollWidth - dom.stage.clientWidth) / 2);
      dom.stage.scrollTop = Math.max(0, (dom.stage.scrollHeight - dom.stage.clientHeight) / 2);
    });
  }

  function updateReferenceProperty(property, value, label) {
    if (!state.doc.reference) return;
    mutate(label || 'change reference', () => {
      state.doc.reference[property] = value;
    });
  }

  function loadReferenceFile(file) {
    if (!file || !/^image\/(png|jpeg|webp|gif)$/i.test(file.type)) {
      showToast('Choose a PNG, JPG, WebP, or GIF image.', 'error');
      return;
    }
    if (file.size > 25 * 1024 * 1024) {
      showToast('That image is larger than the 25 MB local limit.', 'error');
      return;
    }

    const reader = new FileReader();
    reader.onerror = () => showToast('The reference image could not be read.', 'error');
    reader.onload = () => {
      const image = new Image();
      image.onerror = () => showToast('The reference image could not be decoded.', 'error');
      image.onload = () => {
        mutate('load reference', () => {
          state.doc.reference = {
            name: file.name,
            dataUrl: String(reader.result),
            naturalWidth: image.naturalWidth,
            naturalHeight: image.naturalHeight,
            opacity: 0.42,
            visible: true,
            padding: 0,
            filter: 'none'
          };
        }, { toast: `Reference loaded: ${image.naturalWidth} × ${image.naturalHeight}` });
      };
      image.src = String(reader.result);
    };
    reader.readAsDataURL(file);
  }

  function applyArtboardSize(width, height, label) {
    const nextWidth = Core.clamp(Math.round(number(width, state.doc.width)), 1, 16384);
    const nextHeight = Core.clamp(Math.round(number(height, state.doc.height)), 1, 16384);
    mutate(label || 'resize artboard', () => {
      state.doc.width = nextWidth;
      state.doc.height = nextHeight;
    });
    syncExportSize(nextWidth, 'width');
    window.requestAnimationFrame(zoomFit);
  }

  function deleteSelection() {
    const element = selectedElement();
    if (!element) return;
    if (element.locked) {
      showToast('Unlock this layer before deleting it.');
      return;
    }
    mutate('delete shape', () => {
      state.doc.elements = state.doc.elements.filter((item) => item.id !== element.id);
      state.selectionId = null;
      state.activeNode = null;
    });
  }

  function duplicateSelection(mirror) {
    const element = selectedElement();
    if (!element) return;
    mutate(mirror ? 'mirror shape' : 'duplicate shape', () => {
      const copy = Core.clone(element);
      copy.id = Core.uid(element.type);
      copy.name = `${element.name || element.type} ${mirror ? 'mirror' : 'copy'}`;
      copy.locked = false;
      if (mirror) Core.mirrorElementX(copy, state.doc.width / 2);
      else Core.translateElement(copy, 12, 12);
      const index = elementIndex(element.id);
      state.doc.elements.splice(index + 1, 0, copy);
      state.selectionId = copy.id;
      state.activeNode = null;
    }, { toast: mirror ? 'Mirrored copy created across the vertical center.' : 'Shape duplicated.' });
  }

  function reorderSelection(direction) {
    const index = elementIndex(state.selectionId);
    const nextIndex = index + direction;
    if (index < 0 || nextIndex < 0 || nextIndex >= state.doc.elements.length) return;
    mutate(direction > 0 ? 'bring shape forward' : 'send shape backward', () => {
      const [element] = state.doc.elements.splice(index, 1);
      state.doc.elements.splice(nextIndex, 0, element);
    });
  }

  function applyGeometryField(field, rawValue) {
    const element = selectedElement();
    if (!element || element.locked) return;
    const bounds = Core.elementBounds(element);
    if (!bounds) return;
    const value = number(rawValue, field === 'width' ? bounds.width : field === 'height' ? bounds.height : field === 'x' ? bounds.x : bounds.y);
    mutate(`change ${field}`, () => {
      if (field === 'x') Core.translateElement(element, value - bounds.x, 0);
      else if (field === 'y') Core.translateElement(element, 0, value - bounds.y);
      else {
        if (element.type === 'arc') {
          const currentSize = field === 'width' ? bounds.width : bounds.height;
          const scale = Math.max(0.000000001, value) / Math.max(currentSize, 0.000000001);
          Core.scaleElementToBounds(element, bounds, {
            ...bounds,
            width: bounds.width * scale,
            height: bounds.height * scale
          });
          return;
        }
        const next = { ...bounds };
        next[field] = Math.max(0.01, value);
        Core.scaleElementToBounds(element, bounds, next);
      }
    });
  }

  function applyArcField(field, rawValue) {
    const element = selectedElement();
    if (!element || element.type !== 'arc' || element.locked) return;
    mutate(`change arc ${field}`, () => {
      if (field === 'center-x') element.cx = number(rawValue, element.cx);
      else if (field === 'center-y') element.cy = number(rawValue, element.cy);
      else if (field === 'radius') element.radius = Core.clamp(Math.abs(number(rawValue, element.radius)), 0.001, 1000000);
      else if (field === 'start-angle') element.startAngle = number(rawValue, element.startAngle * 180 / Math.PI) * Math.PI / 180;
      else if (field === 'sweep-angle') {
        const degrees = number(rawValue, element.sweepAngle * 180 / Math.PI);
        const direction = degrees < 0 ? -1 : 1;
        element.sweepAngle = direction * Core.clamp(Math.abs(degrees), 0.01, 359.99) * Math.PI / 180;
      }
    });
  }

  function updateSelectedStyle(label, callback) {
    const element = selectedElement();
    if (!element || element.locked) return;
    mutate(label, () => {
      if (!element.style) element.style = styleForNewElement(element.type);
      callback(element.style, element);
      state.defaultStyle = { ...state.defaultStyle, ...element.style };
    });
  }

  function selectedNode() {
    const element = selectedElement();
    if (!element || !element.points || state.activeNode === null) return null;
    return { element, point: element.points[state.activeNode], index: state.activeNode };
  }

  function makeNodeSmooth() {
    const selected = selectedNode();
    if (!selected || selected.element.type !== 'path') {
      showToast('Choose a path node first.');
      return;
    }
    if (selected.element.locked) {
      showToast('Unlock this layer before editing its nodes.');
      return;
    }
    mutate('smooth node', () => {
      const points = selected.element.points;
      const current = points[selected.index];
      const previous = points[selected.index - 1] || (selected.element.closed ? points[points.length - 1] : current);
      const next = points[selected.index + 1] || (selected.element.closed ? points[0] : current);
      const angle = Math.atan2(next.y - previous.y, next.x - previous.x);
      const inLength = Math.max(8, distance(current, previous) / 3);
      const outLength = Math.max(8, distance(current, next) / 3);
      current.inX = current.x - Math.cos(angle) * inLength;
      current.inY = current.y - Math.sin(angle) * inLength;
      current.outX = current.x + Math.cos(angle) * outLength;
      current.outY = current.y + Math.sin(angle) * outLength;
    });
  }

  function makeNodeCorner() {
    const selected = selectedNode();
    if (!selected || selected.element.type !== 'path') {
      showToast('Choose a path node first.');
      return;
    }
    if (selected.element.locked) {
      showToast('Unlock this layer before editing its nodes.');
      return;
    }
    mutate('make corner node', () => {
      delete selected.point.inX;
      delete selected.point.inY;
      delete selected.point.outX;
      delete selected.point.outY;
    });
  }

  function addNodeAfter() {
    const selected = selectedNode();
    if (!selected) {
      showToast('Choose a path or polygon node first.');
      return;
    }
    if (selected.element.locked) {
      showToast('Unlock this layer before editing its nodes.');
      return;
    }
    const points = selected.element.points;
    const nextIndex = selected.index + 1;
    const next = points[nextIndex] || (selected.element.closed || selected.element.type === 'polygon' ? points[0] : null);
    if (!next) {
      showToast('The final node of an open path has no following segment.');
      return;
    }
    mutate('add node', () => {
      if (selected.element.type === 'path') {
        state.activeNode = Core.subdividePathSegment(points, selected.index, selected.element.closed);
      } else {
        const point = { x: (selected.point.x + next.x) / 2, y: (selected.point.y + next.y) / 2 };
        points.splice(nextIndex, 0, point);
        state.activeNode = nextIndex;
      }
    });
  }

  function deleteNode() {
    const selected = selectedNode();
    if (!selected) {
      showToast('Choose a path or polygon node first.');
      return;
    }
    if (selected.element.locked) {
      showToast('Unlock this layer before editing its nodes.');
      return;
    }
    const minimum = selected.element.type === 'polygon' ? 3 : 2;
    if (selected.element.points.length <= minimum) {
      showToast(`${selected.element.type === 'polygon' ? 'Polygons' : 'Paths'} need at least ${minimum} nodes.`);
      return;
    }
    mutate('delete node', () => {
      selected.element.points.splice(selected.index, 1);
      state.activeNode = Math.min(selected.index, selected.element.points.length - 1);
    });
  }

  function moveSelectionBy(dx, dy) {
    const element = selectedElement();
    if (!element || element.locked) return;
    mutate('nudge shape', () => Core.translateElement(element, dx, dy));
  }

  function updateExportHeightFromWidth() {
    const width = Core.clamp(Math.round(number($('export-width').value, state.doc.width)), 1, 16384);
    $('export-height').value = Math.max(1, Math.round(width * state.doc.height / state.doc.width));
    markExportPreset(width);
  }

  function updateExportWidthFromHeight() {
    const height = Core.clamp(Math.round(number($('export-height').value, state.doc.height)), 1, 16384);
    $('export-width').value = Math.max(1, Math.round(height * state.doc.width / state.doc.height));
    markExportPreset(Number($('export-width').value));
  }

  function syncExportSize(value, source) {
    if (source === 'height') {
      $('export-height').value = Math.round(value);
      updateExportWidthFromHeight();
    } else {
      $('export-width').value = Math.round(value);
      updateExportHeightFromWidth();
    }
  }

  function markExportPreset(width) {
    document.querySelectorAll('[data-export-size]').forEach((button) => {
      button.classList.toggle('active', Number(button.dataset.exportSize) === Number(width));
    });
  }

  function exportBackground() {
    const value = $('export-background').value;
    if (value === 'transparent') return null;
    return value === 'custom' ? $('export-background-color').value : value;
  }

  function safeFilename(extension) {
    const title = String(state.doc.title || 'anomaly-logo')
      .trim()
      .toLowerCase()
      .replace(/[^a-z0-9]+/g, '-')
      .replace(/^-|-$/g, '') || 'anomaly-logo';
    return `${title}.${extension}`;
  }

  function downloadBlob(blob, filename) {
    const url = URL.createObjectURL(blob);
    const anchor = document.createElement('a');
    anchor.href = url;
    anchor.download = filename;
    document.body.appendChild(anchor);
    anchor.click();
    anchor.remove();
    window.setTimeout(() => URL.revokeObjectURL(url), 1000);
  }

  function svgText(background) {
    return Core.serializeSvg(state.doc, {
      title: state.doc.title || 'Anomaly logo',
      background: background || null
    });
  }

  function downloadSvg(background) {
    if (state.draft) finishDraft(false);
    const source = svgText(background);
    downloadBlob(new Blob([source], { type: 'image/svg+xml;charset=utf-8' }), safeFilename('svg'));
    showToast('Clean SVG downloaded. Reference and guides were excluded.');
  }

  async function copySvg() {
    if (state.draft) finishDraft(false);
    const source = svgText(exportBackground());
    try {
      if (navigator.clipboard && window.isSecureContext) {
        await navigator.clipboard.writeText(source);
      } else {
        const textarea = document.createElement('textarea');
        textarea.value = source;
        textarea.style.position = 'fixed';
        textarea.style.opacity = '0';
        document.body.appendChild(textarea);
        textarea.select();
        document.execCommand('copy');
        textarea.remove();
      }
      showToast('SVG copied to the clipboard.');
    } catch (error) {
      console.warn(error);
      showToast('Clipboard access was blocked; use Download SVG instead.', 'error');
    }
  }

  function downloadPng() {
    if (state.draft) finishDraft(false);
    const width = Core.clamp(Math.round(number($('export-width').value, state.doc.width)), 1, 16384);
    const height = Core.clamp(Math.round(number($('export-height').value, state.doc.height)), 1, 16384);
    if (width * height > 100000000) {
      showToast('That PNG would exceed the 100-megapixel safety limit.', 'error');
      return;
    }

    const source = svgText(exportBackground());
    const blob = new Blob([source], { type: 'image/svg+xml;charset=utf-8' });
    const url = URL.createObjectURL(blob);
    const image = new Image();
    image.onerror = () => {
      URL.revokeObjectURL(url);
      showToast('The browser could not render this SVG to PNG.', 'error');
    };
    image.onload = () => {
      URL.revokeObjectURL(url);
      const canvas = document.createElement('canvas');
      canvas.width = width;
      canvas.height = height;
      const context = canvas.getContext('2d', { alpha: true });
      context.imageSmoothingEnabled = true;
      context.imageSmoothingQuality = 'high';
      context.drawImage(image, 0, 0, width, height);
      canvas.toBlob((png) => {
        if (!png) {
          showToast('PNG encoding failed.', 'error');
          return;
        }
        downloadBlob(png, safeFilename(`${width}x${height}.png`));
        showToast(`PNG exported at ${width} × ${height}.`);
      }, 'image/png');
    };
    image.src = url;
  }

  function saveProject() {
    if (state.draft) finishDraft(false);
    const json = `${JSON.stringify(state.doc, null, 2)}\n`;
    downloadBlob(new Blob([json], { type: 'application/json;charset=utf-8' }), safeFilename('avstudio.json'));
    showToast('Editable project saved.');
  }

  function openProjectFile(file) {
    if (!file) return;
    const reader = new FileReader();
    reader.onerror = () => showToast('The project file could not be read.', 'error');
    reader.onload = () => {
      try {
        const parsed = JSON.parse(String(reader.result));
        if (!Core.isProjectDocument(parsed)) {
          throw new Error('Missing or unsupported Vector Studio project data.');
        }
        const next = Core.normalizeDocument(parsed);
        if ((state.doc.elements.length || state.doc.reference) && !window.confirm('Replace the current artwork with this project?')) return;
        state.doc = next;
        state.autosaveBlocked = false;
        applyDocumentSettings(state.doc);
        state.selectionId = null;
        state.activeNode = null;
        state.undoStack.length = 0;
        state.redoStack.length = 0;
        renderAll();
        syncExportSize(state.doc.width, 'width');
        window.requestAnimationFrame(zoomFit);
        showToast('Project opened.');
      } catch (error) {
        console.warn(error);
        showToast('That file is not a valid Vector Studio project.', 'error');
      }
    };
    reader.readAsText(file);
  }

  function svgProperty(node, name, fallback) {
    const inline = node.style && node.style.getPropertyValue(name);
    const attribute = node.getAttribute && node.getAttribute(name);
    return inline || attribute || fallback;
  }

  function parseSvgStyle(node, inherited, type) {
    const base = inherited || {
      fill: '#000000',
      fillOpacity: 1,
      stroke: 'none',
      strokeOpacity: 1,
      strokeWidth: 1,
      lineCap: 'butt',
      lineJoin: 'miter',
      dash: ''
    };
    const opacity = Core.clamp(number(svgProperty(node, 'opacity', 1), 1), 0, 1);
    const style = {
      fill: svgProperty(node, 'fill', base.fill),
      fillOpacity: Core.clamp(number(svgProperty(node, 'fill-opacity', base.fillOpacity), base.fillOpacity), 0, 1) * opacity,
      stroke: svgProperty(node, 'stroke', base.stroke),
      strokeOpacity: Core.clamp(number(svgProperty(node, 'stroke-opacity', base.strokeOpacity), base.strokeOpacity), 0, 1) * opacity,
      strokeWidth: Math.max(0, number(svgProperty(node, 'stroke-width', base.strokeWidth), base.strokeWidth)),
      lineCap: svgProperty(node, 'stroke-linecap', base.lineCap),
      lineJoin: svgProperty(node, 'stroke-linejoin', base.lineJoin),
      dash: svgProperty(node, 'stroke-dasharray', base.dash)
    };
    if ((type === 'line' || type === 'arc') && !node.hasAttribute('fill') && !(node.style && node.style.fill)) style.fill = 'none';
    return style;
  }

  function parsePointList(value) {
    const values = String(value || '').match(/[-+]?(?:\d*\.?\d+)(?:e[-+]?\d+)?/gi) || [];
    const points = [];
    for (let index = 0; index + 1 < values.length; index += 2) {
      points.push({ x: Number(values[index]), y: Number(values[index + 1]) });
    }
    return points;
  }

  function parseEditablePath(value) {
    return Core.parseEditablePathData(value);
  }

  function importSvgText(source) {
    const parser = new DOMParser();
    const parsed = parser.parseFromString(source, 'image/svg+xml');
    if (parsed.querySelector('parsererror')) throw new Error('Invalid SVG markup.');
    const root = parsed.documentElement;
    if (!root || root.localName.toLowerCase() !== 'svg') throw new Error('The file does not have an SVG root.');

    const viewBoxValues = (root.getAttribute('viewBox') || '').match(/[-+]?(?:\d*\.?\d+)(?:e[-+]?\d+)?/gi) || [];
    const viewBoxX = viewBoxValues.length === 4 ? Number(viewBoxValues[0]) : 0;
    const viewBoxY = viewBoxValues.length === 4 ? Number(viewBoxValues[1]) : 0;
    const width = viewBoxValues.length === 4 ? Number(viewBoxValues[2]) : number(parseFloat(root.getAttribute('width')), 512);
    const height = viewBoxValues.length === 4 ? Number(viewBoxValues[3]) : number(parseFloat(root.getAttribute('height')), 512);
    const next = makeDocument(Core.clamp(width, 1, 16384), Core.clamp(height, 1, 16384));
    const imported = [];
    let skipped = 0;

    function visit(node, inheritedStyle) {
      if (!(node instanceof Element)) return;
      const tag = node.localName.toLowerCase();
      if (tag === 'defs' || tag === 'metadata' || tag === 'title' || tag === 'desc') return;
      if (node !== root && node.hasAttribute('transform')) {
        skipped += 1;
        return;
      }
      if (tag === 'svg' || tag === 'g') {
        const groupStyle = parseSvgStyle(node, inheritedStyle, tag);
        Array.from(node.children).forEach((child) => visit(child, groupStyle));
        return;
      }

      let element = null;
      if (tag === 'rect') {
        element = createElement('rect', {
          x: number(node.getAttribute('x'), 0),
          y: number(node.getAttribute('y'), 0),
          width: Math.max(0, number(node.getAttribute('width'), 0)),
          height: Math.max(0, number(node.getAttribute('height'), 0)),
          rx: Math.max(0, number(node.getAttribute('rx'), 0))
        });
      } else if (tag === 'ellipse' || tag === 'circle') {
        const radius = Math.max(0, number(node.getAttribute('r'), 0));
        element = createElement('ellipse', {
          cx: number(node.getAttribute('cx'), 0),
          cy: number(node.getAttribute('cy'), 0),
          rx: tag === 'circle' ? radius : Math.max(0, number(node.getAttribute('rx'), 0)),
          ry: tag === 'circle' ? radius : Math.max(0, number(node.getAttribute('ry'), 0))
        });
      } else if (tag === 'line') {
        element = createElement('line', {
          x1: number(node.getAttribute('x1'), 0),
          y1: number(node.getAttribute('y1'), 0),
          x2: number(node.getAttribute('x2'), 0),
          y2: number(node.getAttribute('y2'), 0)
        });
      } else if (tag === 'polygon') {
        const points = parsePointList(node.getAttribute('points'));
        if (points.length >= 3) element = createElement('polygon', { points });
      } else if (tag === 'path') {
        const data = node.getAttribute('d');
        const arc = Core.parseEditableArcData(data);
        const path = arc ? null : parseEditablePath(data);
        if (arc) element = createElement('arc', arc);
        else if (path) element = createElement('path', path);
      }

      if (!element) {
        skipped += 1;
        return;
      }
      element.name = node.getAttribute('data-name') || node.getAttribute('id') || element.name;
      element.visible = svgProperty(node, 'display', '') !== 'none' && svgProperty(node, 'visibility', '') !== 'hidden';
      element.style = parseSvgStyle(node, inheritedStyle, element.type);
      imported.push(element);
    }

    visit(root, null);
    if (viewBoxX || viewBoxY) {
      imported.forEach((element) => Core.translateElement(element, -viewBoxX, -viewBoxY));
    }
    next.elements = imported;
    return { document: next, imported: imported.length, skipped };
  }

  function importSvgFile(file) {
    if (!file) return;
    const reader = new FileReader();
    reader.onerror = () => showToast('The SVG file could not be read.', 'error');
    reader.onload = () => {
      try {
        const result = importSvgText(String(reader.result));
        if (!result.imported) throw new Error('No supported editable shapes found.');
        if ((state.doc.elements.length || state.doc.reference) && !window.confirm('Replace the current artwork with the imported SVG?')) return;
        state.doc = result.document;
        state.autosaveBlocked = false;
        applyDocumentSettings(state.doc);
        state.selectionId = null;
        state.activeNode = null;
        state.undoStack.length = 0;
        state.redoStack.length = 0;
        renderAll();
        syncExportSize(state.doc.width, 'width');
        window.requestAnimationFrame(zoomFit);
        const suffix = result.skipped ? ` ${result.skipped} unsupported or transformed item(s) were skipped.` : '';
        showToast(`Imported ${result.imported} editable shape(s).${suffix}`);
      } catch (error) {
        console.warn(error);
        showToast('No editable rect, ellipse, line, circle arc, polygon, or supported path was found.', 'error');
      }
    };
    reader.readAsText(file);
  }

  function newProject() {
    if ((state.doc.elements.length || state.doc.reference) && !window.confirm('Start a new project? The local autosave will be replaced.')) return;
    state.doc = makeDocument(512, 512);
    state.autosaveBlocked = false;
    state.selectionId = null;
    state.activeNode = null;
    state.draft = null;
    state.interaction = null;
    state.undoStack.length = 0;
    state.redoStack.length = 0;
    applyDocumentSettings(state.doc);
    renderAll();
    syncExportSize(512, 'width');
    window.requestAnimationFrame(zoomFit);
    showToast('New 512 × 512 project created.');
  }

  function bindInspector() {
    ['element-x', 'element-y', 'element-width', 'element-height'].forEach((id) => {
      $(id).addEventListener('change', (event) => applyGeometryField(id.replace('element-', ''), event.target.value));
    });
    ['arc-center-x', 'arc-center-y', 'arc-radius', 'arc-start-angle', 'arc-sweep-angle'].forEach((id) => {
      $(id).addEventListener('change', (event) => applyArcField(id.replace('arc-', ''), event.target.value));
    });
    $('element-name').addEventListener('change', (event) => {
      const element = selectedElement();
      if (!element) return;
      if (element.locked) {
        showToast('Unlock this layer before renaming it.');
        renderInspector();
        return;
      }
      mutate('rename layer', () => { element.name = event.target.value.trim() || nextName(element.type); });
    });
    $('fill-enabled').addEventListener('change', (event) => updateSelectedStyle('toggle fill', (style) => {
      style.fill = event.target.checked ? ($('fill-color').value || '#ffffff') : 'none';
    }));
    $('fill-color').addEventListener('change', (event) => updateSelectedStyle('change fill', (style) => {
      style.fill = event.target.value;
      $('fill-enabled').checked = true;
    }));
    $('fill-opacity').addEventListener('change', (event) => updateSelectedStyle('change fill opacity', (style) => {
      style.fillOpacity = Core.clamp(number(event.target.value, 100) / 100, 0, 1);
    }));
    $('stroke-enabled').addEventListener('change', (event) => updateSelectedStyle('toggle stroke', (style) => {
      style.stroke = event.target.checked ? ($('stroke-color').value || '#11151d') : 'none';
      if (event.target.checked && number(style.strokeWidth, 0) === 0) style.strokeWidth = 2;
    }));
    $('stroke-color').addEventListener('change', (event) => updateSelectedStyle('change stroke', (style) => {
      style.stroke = event.target.value;
      $('stroke-enabled').checked = true;
    }));
    $('stroke-width').addEventListener('change', (event) => updateSelectedStyle('change stroke width', (style) => {
      style.strokeWidth = Math.max(0, number(event.target.value, 0));
      if (style.strokeWidth > 0 && style.stroke === 'none') style.stroke = $('stroke-color').value;
    }));
    $('stroke-opacity').addEventListener('change', (event) => updateSelectedStyle('change stroke opacity', (style) => {
      style.strokeOpacity = Core.clamp(number(event.target.value, 100) / 100, 0, 1);
    }));
    $('stroke-join').addEventListener('change', (event) => updateSelectedStyle('change stroke join', (style) => { style.lineJoin = event.target.value; }));
    $('stroke-cap').addEventListener('change', (event) => updateSelectedStyle('change stroke cap', (style) => { style.lineCap = event.target.value; }));
    $('stroke-dash').addEventListener('change', (event) => {
      const value = event.target.value.trim().replaceAll(',', ' ');
      if (value && !/^(?:\d*\.?\d+\s*)+$/.test(value)) {
        showToast('Dash patterns should contain only positive numbers, such as “12 6”.', 'error');
        renderInspector();
        return;
      }
      updateSelectedStyle('change dash pattern', (style) => { style.dash = value; });
    });
  }

  function bindReferenceControls() {
    const choose = () => dom.referenceInput.click();
    $('choose-reference').addEventListener('click', choose);
    dom.referenceDrop.addEventListener('click', choose);
    dom.referenceInput.addEventListener('change', () => {
      loadReferenceFile(dom.referenceInput.files && dom.referenceInput.files[0]);
      dom.referenceInput.value = '';
    });

    let opacityBefore = null;
    $('reference-opacity').addEventListener('focus', () => {
      if (state.doc.reference && opacityBefore === null) opacityBefore = beginHistory('change reference opacity');
    });
    $('reference-opacity').addEventListener('input', (event) => {
      if (!state.doc.reference) return;
      state.doc.reference.opacity = Core.clamp(number(event.target.value, 42) / 100, 0, 1);
      $('reference-opacity-value').textContent = `${Math.round(state.doc.reference.opacity * 100)}%`;
      renderReference();
      scheduleAutosave();
    });
    const finishOpacity = () => {
      if (opacityBefore !== null) discardNoopHistory(opacityBefore);
      opacityBefore = null;
      updateHistoryButtons();
    };
    $('reference-opacity').addEventListener('change', finishOpacity);
    $('reference-opacity').addEventListener('blur', finishOpacity);

    $('reference-padding').addEventListener('change', (event) => updateReferenceProperty('padding', Core.clamp(number(event.target.value, 0), 0, 45), 'change reference padding'));
    $('reference-filter').addEventListener('change', (event) => updateReferenceProperty('filter', event.target.value, 'change reference filter'));
    $('reference-visible').addEventListener('change', (event) => updateReferenceProperty('visible', event.target.checked, event.target.checked ? 'show reference' : 'hide reference'));
    $('fit-reference').addEventListener('click', () => updateReferenceProperty('padding', 0, 'fit reference'));
    $('size-from-reference').addEventListener('click', () => {
      const reference = state.doc.reference;
      if (reference) applyArtboardSize(reference.naturalWidth, reference.naturalHeight, 'size artboard from reference');
    });
    $('remove-reference').addEventListener('click', () => {
      if (!state.doc.reference) return;
      mutate('remove reference', () => { state.doc.reference = null; }, { toast: 'Reference removed from the project.' });
    });
  }

  function canChangeGridSettings() {
    if (!state.draft && !state.interaction) return true;
    showToast('Finish the current drawing or drag before changing grid settings.');
    return false;
  }

  function bindCanvasControls() {
    document.querySelectorAll('[data-tool]').forEach((button) => {
      button.addEventListener('click', () => setTool(button.dataset.tool));
    });
    $('zoom-out').addEventListener('click', () => setZoom(state.zoom / 1.2));
    $('zoom-in').addEventListener('click', () => setZoom(state.zoom * 1.2));
    dom.zoomReadout.addEventListener('click', () => setZoom(1));
    $('zoom-fit').addEventListener('click', zoomFit);
    $('toggle-grid').addEventListener('click', () => {
      if (!canChangeGridSettings()) return;
      mutate(state.gridVisible ? 'hide grid' : 'show grid', () => {
        state.gridVisible = !state.gridVisible;
        state.doc.settings = { ...(state.doc.settings || {}), gridVisible: state.gridVisible };
      });
    });
    $('toggle-guides').addEventListener('click', () => {
      state.guidesVisible = !state.guidesVisible;
      renderGrid();
      renderToggles();
    });
    $('toggle-outline').addEventListener('click', () => {
      state.outline = !state.outline;
      renderArtwork();
      renderToggles();
    });
    document.querySelectorAll('[data-stage-theme]').forEach((button) => {
      button.addEventListener('click', () => {
        state.stageTheme = button.dataset.stageTheme;
        updateArtboardGeometry();
        renderToggles();
      });
    });

    $('apply-artboard-size').addEventListener('click', () => applyArtboardSize($('artboard-width').value, $('artboard-height').value));
    $('clear-artwork').addEventListener('click', () => {
      if (!state.doc.elements.length) return;
      if (!window.confirm('Delete every vector layer? The reference image will remain.')) return;
      mutate('clear artwork', () => {
        state.doc.elements = [];
        state.selectionId = null;
        state.activeNode = null;
      });
    });
    $('grid-size').addEventListener('change', (event) => {
      if (!canChangeGridSettings()) {
        event.target.value = state.gridSize;
        return;
      }
      const next = Core.clamp(number(event.target.value, 16), 0.25, 1024);
      mutate('change grid size', () => {
        state.gridSize = next;
        state.doc.settings = { ...(state.doc.settings || {}), gridSize: next };
      });
    });
    $('grid-major-every').addEventListener('change', (event) => {
      if (!canChangeGridSettings()) {
        event.target.value = state.gridMajorEvery;
        return;
      }
      const next = Math.round(Core.clamp(number(event.target.value, 4), 2, 16));
      mutate('change major grid interval', () => {
        state.gridMajorEvery = next;
        state.doc.settings = { ...(state.doc.settings || {}), gridMajorEvery: next };
      });
    });
    $('snap-mode').addEventListener('change', (event) => {
      if (!canChangeGridSettings()) {
        event.target.value = state.snapMode;
        return;
      }
      const next = ['off', 'grid', 'anchors', 'grid-anchors'].includes(event.target.value) ? event.target.value : 'off';
      mutate('change snapping', () => {
        state.snapMode = next;
        state.doc.settings = { ...(state.doc.settings || {}), snapMode: next };
      });
    });
  }

  function bindSelectionActions() {
    $('delete-element').addEventListener('click', deleteSelection);
    $('duplicate-element').addEventListener('click', () => duplicateSelection(false));
    $('mirror-element').addEventListener('click', () => duplicateSelection(true));
    $('bring-forward').addEventListener('click', () => reorderSelection(1));
    $('send-backward').addEventListener('click', () => reorderSelection(-1));
    $('node-smooth').addEventListener('click', makeNodeSmooth);
    $('node-corner').addEventListener('click', makeNodeCorner);
    $('node-add').addEventListener('click', addNodeAfter);
    $('node-delete').addEventListener('click', deleteNode);
  }

  function bindLayers() {
    dom.layersList.addEventListener('click', (event) => {
      const action = event.target.closest('[data-layer-action]');
      const row = event.target.closest('[data-layer-id]');
      if (!action || !row) return;
      const element = state.doc.elements.find((item) => item.id === row.dataset.layerId);
      if (!element) return;
      if (action.dataset.layerAction === 'select') {
        selectElement(element.id);
      } else if (action.dataset.layerAction === 'visible') {
        mutate(element.visible === false ? 'show layer' : 'hide layer', () => {
          element.visible = element.visible === false;
        });
      } else if (action.dataset.layerAction === 'lock') {
        mutate(element.locked ? 'unlock layer' : 'lock layer', () => {
          element.locked = !element.locked;
        });
      }
    });
  }

  function bindFileActions() {
    $('new-project').addEventListener('click', newProject);
    $('open-project').addEventListener('click', () => dom.projectInput.click());
    $('import-svg').addEventListener('click', () => dom.svgInput.click());
    $('save-project').addEventListener('click', saveProject);
    $('quick-export-svg').addEventListener('click', () => downloadSvg(null));
    dom.projectInput.addEventListener('change', () => {
      openProjectFile(dom.projectInput.files && dom.projectInput.files[0]);
      dom.projectInput.value = '';
    });
    dom.svgInput.addEventListener('change', () => {
      importSvgFile(dom.svgInput.files && dom.svgInput.files[0]);
      dom.svgInput.value = '';
    });
  }

  function bindExportControls() {
    document.querySelectorAll('[data-export-size]').forEach((button) => {
      button.addEventListener('click', () => syncExportSize(Number(button.dataset.exportSize), 'width'));
    });
    $('export-width').addEventListener('input', updateExportHeightFromWidth);
    $('export-height').addEventListener('input', updateExportWidthFromHeight);
    $('export-background').addEventListener('change', () => {
      $('export-background-color').disabled = $('export-background').value !== 'custom';
    });
    $('copy-svg').addEventListener('click', copySvg);
    $('export-svg').addEventListener('click', () => downloadSvg(exportBackground()));
    $('export-png').addEventListener('click', downloadPng);
  }

  function bindPointerEvents() {
    dom.stage.addEventListener('pointerdown', onStagePointerDown, true);
    dom.stage.addEventListener('pointermove', onStagePointerMove);
    dom.stage.addEventListener('pointerup', onStagePointerUp);
    dom.stage.addEventListener('pointercancel', onStagePointerUp);
    dom.artboard.addEventListener('pointerdown', onArtboardPointerDown);
    dom.artboard.addEventListener('pointermove', onArtboardPointerMove);
    dom.artboard.addEventListener('pointerup', finishPointerInteraction);
    dom.artboard.addEventListener('pointercancel', finishPointerInteraction);
    dom.stage.addEventListener('wheel', (event) => {
      if (!(event.ctrlKey || event.metaKey)) return;
      event.preventDefault();
      setZoom(state.zoom * (event.deltaY < 0 ? 1.12 : 1 / 1.12), { x: event.clientX, y: event.clientY });
    }, { passive: false });

    const dragTargets = [dom.stage, dom.referenceDrop];
    dragTargets.forEach((target) => {
      target.addEventListener('dragover', (event) => {
        event.preventDefault();
        target.classList.add('drag-over');
      });
      target.addEventListener('dragleave', () => target.classList.remove('drag-over'));
      target.addEventListener('drop', (event) => {
        event.preventDefault();
        target.classList.remove('drag-over');
        const file = event.dataTransfer && event.dataTransfer.files && event.dataTransfer.files[0];
        if (file && file.type === 'image/svg+xml') {
          showToast('Use Import SVG for vector files; reference images must be raster.', 'error');
        } else {
          loadReferenceFile(file);
        }
      });
    });

    window.addEventListener('paste', (event) => {
      if (isTypingTarget(event.target)) return;
      const item = Array.from(event.clipboardData && event.clipboardData.items || []).find((candidate) => candidate.type.startsWith('image/'));
      if (item) {
        event.preventDefault();
        loadReferenceFile(item.getAsFile());
      }
    });
  }

  function bindKeyboard() {
    const toolKeys = { v: 'select', a: 'node', r: 'rect', e: 'ellipse', l: 'line', g: 'polygon', p: 'pen', h: 'pan' };
    window.addEventListener('keydown', (event) => {
      const typing = isTypingTarget(event.target);
      if (typing) {
        if (event.key === 'Escape') event.target.blur();
        return;
      }

      const modifier = event.ctrlKey || event.metaKey;
      if (modifier && event.key.toLowerCase() === 'z') {
        event.preventDefault();
        if (event.shiftKey) redo(); else undo();
        return;
      }
      if (modifier && event.key.toLowerCase() === 'y') {
        event.preventDefault();
        redo();
        return;
      }
      if (modifier && event.key.toLowerCase() === 'd') {
        event.preventDefault();
        duplicateSelection(false);
        return;
      }
      if (modifier && event.key.toLowerCase() === 's') {
        event.preventDefault();
        saveProject();
        return;
      }
      if (event.code === 'Space') {
        event.preventDefault();
        if (!state.spacePressed) {
          state.spacePressed = true;
          renderToolState();
        }
        return;
      }
      if (event.key === 'Enter' && state.draft) {
        event.preventDefault();
        finishDraft(true);
        return;
      }
      if (event.key === 'Escape') {
        event.preventDefault();
        if (!cancelLineCurve(true) && !cancelDraft()) selectElement(null);
        return;
      }
      if (event.key === 'Delete' || event.key === 'Backspace') {
        event.preventDefault();
        const element = selectedElement();
        if (state.tool === 'node' && state.activeNode !== null && element && (element.type === 'path' || element.type === 'polygon')) deleteNode();
        else deleteSelection();
        return;
      }
      if ((event.key === '[' || event.key === ']') && !modifier && !event.altKey) {
        event.preventDefault();
        if (state.draft || state.interaction) {
          showToast('Finish the current drawing before changing grid spacing.');
          return;
        }
        const factor = event.key === '[' ? 0.5 : 2;
        const next = Core.clamp(state.gridSize * factor, 0.25, 1024);
        mutate('change grid size', () => {
          state.gridSize = next;
          state.doc.settings = { ...(state.doc.settings || {}), gridSize: next };
        }, { toast: `Grid spacing ${pretty(next)}` });
        return;
      }
      if (['ArrowLeft', 'ArrowRight', 'ArrowUp', 'ArrowDown'].includes(event.key)) {
        if (!selectedElement()) return;
        event.preventDefault();
        const step = snapUsesGrid()
          ? state.gridSize * (event.shiftKey ? state.gridMajorEvery : 1)
          : (event.shiftKey ? 10 : 1);
        const dx = event.key === 'ArrowLeft' ? -step : event.key === 'ArrowRight' ? step : 0;
        const dy = event.key === 'ArrowUp' ? -step : event.key === 'ArrowDown' ? step : 0;
        moveSelectionBy(dx, dy);
        return;
      }
      const tool = toolKeys[event.key.toLowerCase()];
      if (tool) {
        event.preventDefault();
        setTool(tool);
      }
    });
    window.addEventListener('keyup', (event) => {
      if (event.code === 'Space') {
        state.spacePressed = false;
        renderToolState();
      }
    });
    window.addEventListener('blur', () => {
      state.spacePressed = false;
      if (state.interaction && state.interaction.kind === 'pan') {
        state.interaction = null;
        dom.stage.classList.remove('is-panning');
      }
      renderToolState();
    });
  }

  function initialize() {
    bindCanvasControls();
    bindInspector();
    bindReferenceControls();
    bindSelectionActions();
    bindLayers();
    bindFileActions();
    bindExportControls();
    bindPointerEvents();
    bindKeyboard();

    const restored = restoreAutosave();
    applyDocumentSettings(state.doc);
    $('export-background-color').disabled = true;
    renderAll();
    syncExportSize(state.doc.width, 'width');
    window.requestAnimationFrame(() => {
      zoomFit();
      if (restored && (state.doc.elements.length || state.doc.reference)) showToast('Your local draft was restored.');
    });
  }

  initialize();
}());
