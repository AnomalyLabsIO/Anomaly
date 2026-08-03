"use strict";

const test = require("node:test");
const assert = require("node:assert/strict");
const core = require("../editor-core.js");

function style(overrides = {}) {
    return {
        fill: "#f8fafc",
        fillOpacity: 1,
        stroke: "#0f172a",
        strokeOpacity: 1,
        strokeWidth: 2,
        lineCap: "round",
        lineJoin: "round",
        dash: "",
        ...overrides
    };
}

function assertClose(actual, expected, epsilon = 1e-8) {
    assert.ok(Math.abs(actual - expected) <= epsilon, `${actual} was not within ${epsilon} of ${expected}`);
}

test("createDocument and clone provide isolated project state", () => {
    const document = core.createDocument(640, 360);
    const copy = core.clone({ document, nested: [{ value: 1 }] });

    assert.equal(document.version, core.PROJECT_VERSION);
    assert.equal(document.width, 640);
    assert.equal(document.height, 360);
    assert.deepEqual(document.elements, []);
    assert.equal(document.reference, null);

    copy.document.width = 10;
    copy.nested[0].value = 2;
    assert.equal(document.width, 640);
    assert.equal(copy.nested[0].value, 2);
});

test("numeric helpers clamp, snap, and round editor coordinates", () => {
    assert.equal(core.clamp(14, 0, 10), 10);
    assert.equal(core.clamp(-2, 10, 0), 0);
    assert.equal(core.snap(12.4, 5, true), 10);
    assert.equal(core.snap(12.4, 5, false), 12.4);
    assert.equal(core.snap(0.29, 0.1, true), 0.3);
    assert.equal(core.round(1.23456), 1.235);
    assert.equal(core.round(-0.00001), 0);
});

test("pathData emits straight, cubic, and curved closing segments", () => {
    const open = core.pathData([
        { x: 0, y: 0 },
        { x: 10, y: 10, inX: 4, inY: 10, outX: 16, outY: 10 },
        { x: 20, y: 0 }
    ], false);
    const closed = core.pathData([
        { x: 0, y: 0, inX: -5, inY: 0 },
        { x: 10, y: 0, outX: 15, outY: 0 }
    ], true);

    assert.equal(open, "M 0 0 C 0 0 4 10 10 10 C 16 10 20 0 20 0");
    assert.equal(closed, "M 0 0 L 10 0 C 15 0 -5 0 0 0 Z");
});

test("parseEditablePathData accepts one absolute path and rejects subpath corruption", () => {
    assert.deepEqual(
        core.parseEditablePathData("M 0 0 C 10 0 20 30 40 40 L 50 50 Z"),
        {
            points: [
                { x: 0, y: 0, outX: 10, outY: 0 },
                { inX: 20, inY: 30, x: 40, y: 40 },
                { x: 50, y: 50 }
            ],
            closed: true
        }
    );
    assert.equal(core.parseEditablePathData("M 0 0 L 10 0 M 20 0 L 30 0"), null);
    assert.equal(core.parseEditablePathData("m 0 0 l 10 10"), null);
    assert.equal(core.parseEditablePathData("M 0 0 Q 10 10 20 20"), null);
});

test("circularArcFromChord creates exact minor, semicircle, and major circle segments", () => {
    const minor = core.circularArcFromChord(0, 0, 100, 0, 84, 20);
    assertClose(minor.cx, 50);
    assertClose(minor.cy, -52.5);
    assertClose(minor.radius, 72.5);
    assertClose(minor.inputSagitta, 20);
    assertClose(minor.bendX, 50);
    assertClose(minor.bendY, 20);
    assert.ok(Math.abs(minor.sweepAngle) < Math.PI);
    const apex = core.arcPoint(minor, 0.5);
    assertClose(apex.x, 50);
    assertClose(apex.y, 20);
    [core.arcPoint(minor, 0), apex, core.arcPoint(minor, 1)].forEach((point) => {
        assertClose(Math.hypot(point.x - minor.cx, point.y - minor.cy), minor.radius);
    });

    const offAxis = core.circularArcFromChord(0, 0, 100, 0, 91, 20);
    assertClose(offAxis.cx, minor.cx);
    assertClose(offAxis.cy, minor.cy);
    const opposite = core.circularArcFromChord(0, 0, 100, 0, 50, -20);
    assertClose(opposite.cy, 52.5);
    assert.ok(opposite.sweepAngle > 0);

    const semicircle = core.circularArcFromChord(0, 0, 100, 0, 50, 50);
    assertClose(semicircle.cy, 0);
    assertClose(semicircle.radius, 50);
    assertClose(semicircle.sweepAngle, -Math.PI);
    const major = core.circularArcFromChord(0, 0, 100, 0, 50, 100);
    assertClose(major.cy, 37.5);
    assertClose(major.radius, 62.5);
    assert.ok(Math.abs(major.sweepAngle) > Math.PI);
    const reversed = core.circularArcFromChord(100, 0, 0, 0, 50, 20);
    assertClose(reversed.cx, minor.cx);
    assertClose(reversed.cy, minor.cy);
    assertClose(reversed.radius, minor.radius);
    assertClose(reversed.sweepAngle, -minor.sweepAngle);

    const rotated = core.circularArcFromChord(0, 0, 0, 100, -20, 76);
    assertClose(rotated.cx, 52.5);
    assertClose(rotated.cy, 50);
    assertClose(rotated.radius, 72.5);
    assertClose(rotated.bendX, -20);
    assertClose(rotated.bendY, 50);
    assert.equal(core.circularArcFromChord(5, 5, 5, 5, 10, 10), null);
    assert.equal(core.circularArcFromChord(0, 0, 100, 0, 80, 0), null);
});

test("circle arcs use exact SVG A commands and round-trip as editable arcs", () => {
    const minor = { type: "arc", ...core.circularArcFromChord(0, 0, 100, 0, 50, 20), style: style() };
    const major = { type: "arc", ...core.circularArcFromChord(0, 0, 100, 0, 50, 100), style: style() };
    assert.equal(core.arcPathData(minor), "M 0 0 A 72.5 72.5 0 0 0 100 0");
    assert.equal(core.arcPathData(major), "M 0 0 A 62.5 62.5 0 1 0 100 0");
    assert.match(core.elementToSvg(minor), /^<path d="M 0 0 A 72\.5 72\.5 0 0 0 100 0"/);
    const document = core.createDocument(120, 80);
    document.elements.push({ id: "ring-part", name: "Ring part", visible: true, locked: false, ...minor });
    assert.match(core.serializeSvg(document), /id="ring-part"[\s\S]*d="M 0 0 A 72\.5 72\.5 0 0 0 100 0"|d="M 0 0 A 72\.5 72\.5 0 0 0 100 0"[\s\S]*id="ring-part"/);

    const parsed = core.parseEditableArcData(core.arcPathData(minor));
    assert.ok(parsed);
    assertClose(parsed.cx, minor.cx);
    assertClose(parsed.cy, minor.cy);
    assertClose(parsed.radius, minor.radius);
    assertClose(parsed.sweepAngle, minor.sweepAngle);
    const parsedMajor = core.parseEditableArcData(core.arcPathData(major));
    assert.ok(parsedMajor);
    assertClose(parsedMajor.cx, major.cx);
    assertClose(parsedMajor.cy, major.cy);
    assertClose(parsedMajor.sweepAngle, major.sweepAngle);
    assert.equal(core.parseEditableArcData("M 0 0 A 60 40 0 0 1 100 0"), null);
    assert.equal(core.parseEditableArcData("M 0 0 L 100 0"), null);

    const almostFull = {
        type: "arc",
        cx: 100.1234,
        cy: 200.4567,
        radius: 1,
        startAngle: 0,
        sweepAngle: 359.99 * Math.PI / 180,
        style: style()
    };
    const almostFullData = core.arcPathData(almostFull);
    assert.notEqual(almostFullData.match(/^M ([^ ]+) ([^ ]+)/).slice(1).join(" "), almostFullData.match(/ ([^ ]+) ([^ ]+)$/).slice(1).join(" "));
    assert.ok(core.parseEditableArcData(almostFullData));

    const bounds = core.elementBounds(minor);
    assertClose(bounds.x, 0);
    assertClose(bounds.y, 0);
    assertClose(bounds.width, 100);
    assertClose(bounds.height, 20);
    const majorBounds = core.elementBounds(major);
    assertClose(majorBounds.x, -12.5);
    assertClose(majorBounds.y, 0);
    assertClose(majorBounds.width, 125);
    assertClose(majorBounds.height, 100);
});

test("elementBounds covers every primitive and exact cubic extrema", () => {
    assert.deepEqual(
        core.elementBounds({ type: "rect", x: 10, y: 20, width: -4, height: 8 }),
        { x: 6, y: 20, width: 4, height: 8 }
    );
    assert.deepEqual(
        core.elementBounds({ type: "ellipse", cx: 10, cy: 20, rx: 4, ry: 8 }),
        { x: 6, y: 12, width: 8, height: 16 }
    );
    assert.deepEqual(
        core.elementBounds({ type: "line", x1: 8, y1: 3, x2: 2, y2: 9 }),
        { x: 2, y: 3, width: 6, height: 6 }
    );
    assert.deepEqual(
        core.elementBounds({ type: "polygon", points: [{ x: -2, y: 3 }, { x: 8, y: 9 }] }),
        { x: -2, y: 3, width: 10, height: 6 }
    );

    const curve = core.elementBounds({
        type: "path",
        closed: false,
        points: [
            { x: 0, y: 0, outX: 0, outY: 100 },
            { x: 100, y: 0, inX: 100, inY: 100 }
        ]
    });
    assert.equal(curve.x, 0);
    assert.equal(curve.y, 0);
    assert.equal(curve.width, 100);
    assert.ok(Math.abs(curve.height - 75) < 1e-9);
});

test("translation, scaling, and mirroring update anchors and Bézier handles", () => {
    const path = {
        type: "path",
        points: [
            { x: 10, y: 20, inX: 8, inY: 20, outX: 12, outY: 20 },
            { x: 30, y: 40 }
        ],
        closed: false
    };

    assert.equal(core.translateElement(path, 5, -10), path);
    assert.deepEqual(path.points[0], { x: 15, y: 10, inX: 13, inY: 10, outX: 17, outY: 10 });

    core.scaleElementToBounds(
        path,
        { x: 5, y: 0, width: 40, height: 40 },
        { x: 0, y: 0, width: 80, height: 20 }
    );
    assert.deepEqual(path.points[0], { x: 20, y: 5, inX: 16, inY: 5, outX: 24, outY: 5 });

    core.mirrorElementX(path, 40);
    assert.deepEqual(path.points[0], { x: 60, y: 5, inX: 64, inY: 5, outX: 56, outY: 5 });

    const rect = { type: "rect", x: 10, y: 4, width: 20, height: 10, rx: 3 };
    core.mirrorElementX(rect, 50);
    assert.deepEqual(rect, { type: "rect", x: 70, y: 4, width: 20, height: 10, rx: 3 });
});

test("circle arcs preserve exact geometry through translation, uniform scaling, and mirroring", () => {
    const arc = { type: "arc", ...core.circularArcFromChord(0, 0, 100, 0, 50, 20) };
    core.translateElement(arc, 10, 5);
    let start = core.arcPoint(arc, 0);
    let end = core.arcPoint(arc, 1);
    assertClose(start.x, 10);
    assertClose(start.y, 5);
    assertClose(end.x, 110);
    assertClose(end.y, 5);

    const bounds = core.elementBounds(arc);
    core.scaleElementToBounds(arc, bounds, { x: bounds.x, y: bounds.y, width: bounds.width * 2, height: bounds.height * 2 });
    assertClose(arc.radius, 145);
    core.mirrorElementX(arc, 200);
    start = core.arcPoint(arc, 0);
    end = core.arcPoint(arc, 1);
    assertClose(start.x, 390);
    assertClose(end.x, 190);
    assert.ok(arc.sweepAngle > 0);

    const beforeNonuniform = core.clone(arc);
    core.scaleElementToBounds(arc, core.elementBounds(arc), { x: 0, y: 0, width: 400, height: 40 });
    assert.deepEqual(arc, beforeNonuniform);
});

test("snap anchors expose connectable geometry and choose the nearest deterministic target", () => {
    const lineAnchors = core.elementSnapAnchors({ type: "line", x1: 1, y1: 2, x2: 9, y2: 8 });
    assert.deepEqual(lineAnchors, [
        { x: 1, y: 2, kind: "endpoint" },
        { x: 9, y: 8, kind: "endpoint" }
    ]);
    const arc = { type: "arc", ...core.circularArcFromChord(0, 0, 100, 0, 50, 20) };
    const arcAnchors = core.elementSnapAnchors(arc);
    assertClose(arcAnchors[0].x, 0);
    assertClose(arcAnchors[1].x, 100);
    assert.equal(core.elementSnapAnchors({ type: "path", points: [{ x: 4, y: 5, outX: 20, outY: 30 }, { x: 8, y: 9 }] }).length, 2);

    const nearest = core.nearestSnapAnchor({ x: 4.9, y: 5 }, [
        { x: 4, y: 5, sourceId: "first" },
        { x: 6, y: 5, sourceId: "second" }
    ], 2);
    assert.equal(nearest.sourceId, "first");
    assertClose(nearest.distance, 0.9);
    assert.equal(core.nearestSnapAnchor({ x: 20, y: 20 }, lineAnchors, 2), null);
});

test("subdividePathSegment preserves a cubic curve with de Casteljau handles", () => {
    const points = [
        { x: 0, y: 0, outX: 0, outY: 100 },
        { x: 100, y: 0, inX: 100, inY: 100 }
    ];

    assert.equal(core.subdividePathSegment(points, 0, false), 1);
    assert.equal(points.length, 3);
    assert.deepEqual(points[0], { x: 0, y: 0, outX: 0, outY: 50 });
    assert.deepEqual(points[1], { x: 50, y: 75, inX: 25, inY: 75, outX: 75, outY: 75 });
    assert.deepEqual(points[2], { x: 100, y: 0, inX: 100, inY: 50 });
    assert.equal(
        core.pathData(points, false),
        "M 0 0 C 0 50 25 75 50 75 C 75 75 100 50 100 0"
    );
});

test("isProjectDocument rejects unrelated JSON before normalization", () => {
    assert.equal(core.isProjectDocument(null), false);
    assert.equal(core.isProjectDocument({}), false);
    assert.equal(core.isProjectDocument({ width: 512, height: 512, elements: [] }), false);
    assert.equal(core.isProjectDocument({ version: 1, width: 512, height: 512, elements: [] }), true);
    assert.equal(core.isProjectDocument({ width: 512, height: 512, shapes: [] }), true);
    assert.equal(core.isProjectDocument({ version: 2, width: 512, height: 512, elements: [] }), true);
    assert.equal(core.isProjectDocument({ version: 3, width: 512, height: 512, elements: [] }), false);
});

test("elementToSvg escapes attribute values and writes clean geometry", () => {
    const svg = core.elementToSvg({
        id: "shape-\"<&",
        type: "polygon",
        name: "Logo <left> & \"right\"",
        visible: true,
        locked: false,
        points: [{ x: 1.25, y: 2 }, { x: 3, y: 4.5 }],
        style: style({ fill: "url(\"#bad<&\")", dash: "4 2" })
    });

    assert.match(svg, /^<polygon /);
    assert.match(svg, /points="1.25,2 3,4.5"/);
    assert.match(svg, /id="shape-&quot;&lt;&amp;"/);
    assert.match(svg, /data-name="Logo &lt;left&gt; &amp; &quot;right&quot;"/);
    assert.match(svg, /fill="url\(&quot;#bad&lt;&amp;&quot;\)"/);
    assert.match(svg, /stroke-dasharray="4 2"/);
    assert.doesNotMatch(svg, /locked/);
});

test("serializeSvg omits hidden elements and editor reference data", () => {
    const document = core.createDocument(100, 80);
    document.title = "A < B & C";
    document.reference = { src: "data:image/png;base64,secret-reference", opacity: 0.4 };
    document.elements.push(
        {
            id: "visible",
            type: "rect",
            name: "Visible",
            visible: true,
            locked: false,
            x: 2,
            y: 3,
            width: 10,
            height: 12,
            rx: 0,
            style: style()
        },
        {
            id: "hidden-secret",
            type: "ellipse",
            name: "Hidden",
            visible: false,
            locked: false,
            cx: 50,
            cy: 50,
            rx: 10,
            ry: 10,
            style: style()
        }
    );

    const svg = core.serializeSvg(document, { background: "#123456" });

    assert.match(svg, /^<svg xmlns="http:\/\/www\.w3\.org\/2000\/svg" width="100" height="80" viewBox="0 0 100 80">/);
    assert.match(svg, /<title>A &lt; B &amp; C<\/title>/);
    assert.match(svg, /fill="#123456"/);
    assert.match(svg, /id="visible"/);
    assert.doesNotMatch(svg, /hidden-secret/);
    assert.doesNotMatch(svg, /secret-reference/);
    assert.doesNotMatch(svg, /<image/);
    assert.match(svg, /<\/svg>$/);
});

test("normalizeDocument sanitizes geometry and style while preserving compatible extras", () => {
    const normalized = core.normalizeDocument({
        version: 99,
        width: "1024",
        height: -1,
        title: 123,
        settings: { grid: 8 },
        reference: { src: "logo.png", visible: true },
        elements: [
            {
                id: "same",
                type: "rect",
                x: 20,
                y: 30,
                width: -10,
                height: -5,
                rx: 99,
                style: { fillOpacity: 4, strokeOpacity: -2, strokeWidth: -3, lineCap: "invalid" }
            },
            {
                id: "same",
                type: "path",
                points: [
                    { x: "1", y: "2", inX: "0", inY: "2" },
                    { x: "3", y: "4" }
                ],
                closed: true
            },
            { type: "path", points: [{ x: 1, y: 2 }] },
            { type: "reference", src: "must-not-be-an-element" },
            null
        ]
    });

    assert.equal(normalized.version, core.PROJECT_VERSION);
    assert.equal(normalized.width, 1024);
    assert.equal(normalized.height, 512);
    assert.equal(normalized.title, "Untitled Artwork");
    assert.deepEqual(normalized.settings, { grid: 8 });
    assert.deepEqual(normalized.reference, { src: "logo.png", visible: true });
    assert.equal(normalized.elements.length, 2);
    assert.notEqual(normalized.elements[0].id, normalized.elements[1].id);
    assert.deepEqual(
        {
            x: normalized.elements[0].x,
            y: normalized.elements[0].y,
            width: normalized.elements[0].width,
            height: normalized.elements[0].height,
            rx: normalized.elements[0].rx
        },
        { x: 10, y: 25, width: 10, height: 5, rx: 2.5 }
    );
    assert.equal(normalized.elements[0].style.fillOpacity, 1);
    assert.equal(normalized.elements[0].style.strokeOpacity, 0);
    assert.equal(normalized.elements[0].style.strokeWidth, 0);
    assert.equal(normalized.elements[0].style.lineCap, "round");
    assert.deepEqual(normalized.elements[1].points, [{ x: 1, y: 2, inX: 0, inY: 2 }, { x: 3, y: 4 }]);
});

test("the browser global and CommonJS export point to the same API", () => {
    assert.equal(globalThis.VectorStudioCore, core);
    assert.equal(typeof core.serializeSvg, "function");
    assert.ok(core.uid("shape").startsWith("shape-"));
    assert.notEqual(core.uid("shape"), core.uid("shape"));
});
