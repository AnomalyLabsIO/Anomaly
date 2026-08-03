# Anomaly Vector Studio

A dependency-free, local SVG tracing and logo-authoring tool. Raster references stay in the browser, are locked beneath the artwork, and are never included in SVG or PNG exports.

## Start

Run `start-svg-authoring.ps1`, or from this directory run:

```powershell
npm start
```

Then open `http://127.0.0.1:8765/`. The PowerShell launcher opens the page automatically and stops its loopback-only server when the launcher closes.

You can also open `index.html` directly. The local server is preferable because it enables reliable clipboard access.

## Trace the current logo

1. Choose **Reference** and select `<client-root>\UserData\Anomaly\Skins\logo.png`.
2. Switch the artboard preview among checker, dark, and light backgrounds. Adjust the reference opacity or use grayscale/invert/high-contrast filters.
3. Use **Pen** for free-form Bézier curves, **Polygon** for hard-edged sections, and the primitive tools for rectangles and ellipses. To make a ring section, draw a **Line** as its chord, then drag the purple midpoint handle. The result is an exact constant-radius SVG circle arc: a shallow pull creates a subsection of a large circle, while pulling beyond a semicircle creates the long arc around a small gap.
4. Switch to **Nodes** to move line endpoints, path anchors, and Bézier handles. The smooth/corner/add/delete controls operate on path and polygon nodes.
5. For several separated pieces of one ring, duplicate an arc and give each copy the same **Center X**, **Center Y**, and **Radius**, then change **Start °** and **Sweep °**. A complete circle can also be drawn with the Ellipse tool, with **Fill** disabled, and broken into a repeating pattern with the stroke **Dash** field.
6. Set **Minor spacing** and **Major every** under Artboard. Choose **Grid + endpoints** snapping to make separate line endpoints and path anchors meet exactly; the cyan halo identifies a magnetic snap.
7. Keep **Center** enabled while tracing the outer seal. **Mirror copy** reflects a selected shape across the artboard’s vertical center.
8. Save an editable project periodically. Exported SVGs are intentionally clean and do not contain the reference or editor metadata.

The SCP:SL client currently consumes `Skins\logo.png`, not SVG. Keep the SVG/project as the master and export a 512×512 transparent PNG for the runtime skin.

## Shortcuts

- `V` select, `A` nodes, `R` rectangle, `E` ellipse, `L` line/circle arc, `G` polygon, `P` pen, `H` pan
- `[` halves the minor grid spacing; `]` doubles it
- Hold `Alt` while placing geometry to bypass grid and endpoint snapping temporarily
- Hold `Space` to pan; hold `Ctrl` while using the mouse wheel to zoom
- `Enter` finishes a path or polygon; `Escape` cancels it
- `Ctrl+Z` / `Ctrl+Y` undo and redo; `Ctrl+D` duplicates; `Ctrl+S` saves the editable project
- With grid snapping active, arrow keys nudge by one minor cell and `Shift` uses one major cell; otherwise they use 1 and 10 units

## Import and export

- **Save project** preserves editable nodes, styles, layers, and the optional local reference image.
- **Import SVG** supports untransformed `rect`, `ellipse`, `circle`, `line`, `polygon`, exact single `M … A …` circle arcs, and single-subpath absolute `M`, `L`, `C`, `Z` paths. Unsupported elements, extra subpaths, or transformed groups are reported and skipped.
- **Download SVG** exports the artboard `viewBox` and visible vector layers.
- **Download PNG** rasterizes at any requested width/height up to a 100-megapixel safety limit, with transparent, black, white, or custom background.

## Validation

```powershell
npm test
node --check app.js
node --check editor-core.js
```
