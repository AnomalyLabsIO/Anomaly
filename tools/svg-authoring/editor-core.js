(function (root, factory) {
    "use strict";

    var api = factory();

    if (typeof module === "object" && module.exports) {
        module.exports = api;
    }

    if (root) {
        root.VectorStudioCore = api;
    }
}(typeof globalThis !== "undefined" ? globalThis : this, function () {
    "use strict";

    var PROJECT_VERSION = 2;
    var uidCounter = 0;
    var SVG_NS = "http://www.w3.org/2000/svg";
    var TAU = Math.PI * 2;
    var SHAPE_TYPES = ["rect", "ellipse", "line", "arc", "polygon", "path"];
    var DEFAULT_STYLE = {
        fill: "#ffffff",
        fillOpacity: 1,
        stroke: "#111827",
        strokeOpacity: 1,
        strokeWidth: 2,
        lineCap: "round",
        lineJoin: "round",
        dash: ""
    };

    function uid(prefix) {
        var safePrefix = String(prefix || "element")
            .trim()
            .replace(/[^A-Za-z0-9_-]+/g, "-")
            .replace(/^-+|-+$/g, "") || "element";
        var stamp = Date.now().toString(36);
        var randomPart = Math.floor(Math.random() * 0x1000000).toString(36);

        uidCounter += 1;
        return safePrefix + "-" + stamp + "-" + uidCounter.toString(36) + "-" + randomPart;
    }

    function createDocument(width, height) {
        return {
            version: PROJECT_VERSION,
            width: positiveNumber(width, 512),
            height: positiveNumber(height, 512),
            title: "Untitled Artwork",
            elements: [],
            reference: null
        };
    }

    function clone(value, seen) {
        var output;
        var keys;
        var index;

        if (value === null || typeof value !== "object") {
            return value;
        }

        if (value instanceof Date) {
            return new Date(value.getTime());
        }

        if (value instanceof RegExp) {
            return new RegExp(value.source, value.flags);
        }

        seen = seen || new WeakMap();
        if (seen.has(value)) {
            return seen.get(value);
        }

        output = Array.isArray(value) ? [] : {};
        seen.set(value, output);
        keys = Object.keys(value);

        for (index = 0; index < keys.length; index += 1) {
            output[keys[index]] = clone(value[keys[index]], seen);
        }

        return output;
    }

    function clamp(value, minimum, maximum) {
        var numeric = Number(value);
        var low = Number(minimum);
        var high = Number(maximum);
        var swap;

        if (!Number.isFinite(numeric)) {
            return numeric;
        }

        if (!Number.isFinite(low) || !Number.isFinite(high)) {
            return numeric;
        }

        if (low > high) {
            swap = low;
            low = high;
            high = swap;
        }

        return Math.min(high, Math.max(low, numeric));
    }

    function snap(value, step, enabled) {
        var numeric = Number(value);
        var interval = Math.abs(Number(step));

        if (enabled === false || !Number.isFinite(numeric) || !Number.isFinite(interval) || interval === 0) {
            return numeric;
        }

        return round(Math.round(numeric / interval) * interval, 8);
    }

    function round(value, precision) {
        var numeric = Number(value);
        var digits = precision === undefined ? 3 : Math.trunc(Number(precision));
        var factor;
        var rounded;

        if (!Number.isFinite(numeric)) {
            return numeric;
        }

        if (!Number.isFinite(digits)) {
            digits = 3;
        }

        digits = Math.max(0, Math.min(12, digits));
        factor = Math.pow(10, digits);
        rounded = Math.round((numeric + Math.sign(numeric) * Number.EPSILON) * factor) / factor;
        return Object.is(rounded, -0) ? 0 : rounded;
    }

    function pathData(points, closed) {
        var normalized = normalizePoints(points, true);
        var commands = [];
        var index;

        if (normalized.length === 0) {
            return "";
        }

        commands.push("M " + coordinatePair(normalized[0].x, normalized[0].y));

        for (index = 1; index < normalized.length; index += 1) {
            appendPathSegment(commands, normalized[index - 1], normalized[index]);
        }

        if (closed) {
            if (normalized.length > 1 && segmentHasCurve(normalized[normalized.length - 1], normalized[0])) {
                appendPathSegment(commands, normalized[normalized.length - 1], normalized[0]);
            }
            commands.push("Z");
        }

        return commands.join(" ");
    }

    function parseEditablePathData(value) {
        var source = String(value || "").trim();
        var letters = source.match(/[A-DF-Za-df-z]/g) || [];
        var tokens;
        var points = [];
        var index = 0;
        var command = null;
        var closed = false;

        if (!source || letters.some(function (letter) {
            return ["M", "L", "C", "Z"].indexOf(letter) < 0;
        })) {
            return null;
        }

        tokens = source.match(/[MLCZ]|[-+]?(?:\d*\.?\d+)(?:[eE][-+]?\d+)?/g);
        if (!tokens || tokens.length === 0) {
            return null;
        }

        function readNumber() {
            var token;
            if (index >= tokens.length || /^[A-Z]$/.test(tokens[index])) {
                throw new Error("Expected path coordinate.");
            }
            token = Number(tokens[index]);
            index += 1;
            if (!Number.isFinite(token)) {
                throw new Error("Invalid path coordinate.");
            }
            return token;
        }

        try {
            while (index < tokens.length) {
                if (/^[A-Z]$/.test(tokens[index])) {
                    command = tokens[index];
                    index += 1;
                }
                if (command === "Z") {
                    closed = true;
                    command = null;
                } else if (command === "M") {
                    if (points.length > 0) {
                        return null;
                    }
                    points.push({ x: readNumber(), y: readNumber() });
                    command = "L";
                } else if (command === "L") {
                    points.push({ x: readNumber(), y: readNumber() });
                } else if (command === "C") {
                    if (points.length === 0) {
                        return null;
                    }
                    points[points.length - 1].outX = readNumber();
                    points[points.length - 1].outY = readNumber();
                    points.push({
                        inX: readNumber(),
                        inY: readNumber(),
                        x: readNumber(),
                        y: readNumber()
                    });
                } else {
                    return null;
                }
            }
        } catch (error) {
            return null;
        }

        return points.length >= 2 ? { points: points, closed: closed } : null;
    }

    function circularArcFromChord(x1, y1, x2, y2, bendX, bendY) {
        var startX = finiteNumber(x1, 0);
        var startY = finiteNumber(y1, 0);
        var endX = finiteNumber(x2, 0);
        var endY = finiteNumber(y2, 0);
        var deltaX = endX - startX;
        var deltaY = endY - startY;
        var chordLength = Math.sqrt(deltaX * deltaX + deltaY * deltaY);
        var halfChord;
        var tangentX;
        var tangentY;
        var normalX;
        var normalY;
        var middleX;
        var middleY;
        var sagitta;
        var centerOffset;
        var centerX;
        var centerY;
        var radius;
        var startAngle;
        var sweepAngle;
        var apexX;
        var apexY;

        if (chordLength < 0.000001) {
            return null;
        }

        halfChord = chordLength / 2;
        tangentX = deltaX / chordLength;
        tangentY = deltaY / chordLength;
        normalX = -tangentY;
        normalY = tangentX;
        middleX = (startX + endX) / 2;
        middleY = (startY + endY) / 2;
        sagitta = (finiteNumber(bendX, middleX) - middleX) * normalX
            + (finiteNumber(bendY, middleY) - middleY) * normalY;

        if (Math.abs(sagitta) < 0.000001) {
            return null;
        }

        centerOffset = (sagitta * sagitta - halfChord * halfChord) / (2 * sagitta);
        centerX = middleX + normalX * centerOffset;
        centerY = middleY + normalY * centerOffset;
        radius = (halfChord * halfChord + sagitta * sagitta) / (2 * Math.abs(sagitta));
        startAngle = Math.atan2(startY - centerY, startX - centerX);
        sweepAngle = -Math.sign(sagitta) * 4 * Math.atan2(Math.abs(sagitta), halfChord);
        sweepAngle = Math.sign(sweepAngle) * Math.min(Math.abs(sweepAngle), TAU - 0.000001);
        apexX = middleX + normalX * sagitta;
        apexY = middleY + normalY * sagitta;

        return {
            cx: centerX,
            cy: centerY,
            radius: radius,
            startAngle: startAngle,
            sweepAngle: sweepAngle,
            inputSagitta: Math.abs(sagitta),
            bendX: apexX,
            bendY: apexY
        };
    }

    function arcPoint(element, fraction) {
        var angle;
        var radius;

        if (!element || typeof element !== "object") {
            return null;
        }

        radius = Math.abs(finiteNumber(element.radius, 0));
        if (radius < 0.000001) {
            return null;
        }

        angle = finiteNumber(element.startAngle, 0)
            + finiteNumber(element.sweepAngle, 0) * finiteNumber(fraction, 0);
        return {
            x: finiteNumber(element.cx, 0) + radius * Math.cos(angle),
            y: finiteNumber(element.cy, 0) + radius * Math.sin(angle)
        };
    }

    function arcPathData(element) {
        var radius;
        var sweep;
        var start;
        var end;
        var largeArc;
        var sweepFlag;

        if (!element || typeof element !== "object") {
            return "";
        }

        radius = Math.abs(finiteNumber(element.radius, 0));
        sweep = finiteNumber(element.sweepAngle, 0);
        if (radius < 0.000001 || Math.abs(sweep) < 0.000001 || Math.abs(sweep) >= TAU) {
            return "";
        }

        start = arcPoint(element, 0);
        end = arcPoint(element, 1);
        if (!start || !end) {
            return "";
        }

        largeArc = Math.abs(sweep) > Math.PI ? 1 : 0;
        sweepFlag = sweep > 0 ? 1 : 0;
        return "M " + arcCoordinatePair(start.x, start.y)
            + " A " + formatArcNumber(radius) + " " + formatArcNumber(radius)
            + " 0 " + largeArc + " " + sweepFlag + " " + arcCoordinatePair(end.x, end.y);
    }

    function parseEditableArcData(value) {
        var source = String(value || "").trim();
        var letters = source.match(/[A-DF-Za-df-z]/g) || [];
        var tokens;
        var startX;
        var startY;
        var radiusX;
        var radiusY;
        var rotation;
        var largeArc;
        var sweepFlag;
        var endX;
        var endY;
        var halfX;
        var halfY;
        var radius;
        var scale;
        var numerator;
        var denominator;
        var coefficient;
        var centerOffsetX;
        var centerOffsetY;
        var centerX;
        var centerY;
        var startAngle;
        var endAngle;
        var sweepAngle;

        if (!source || letters.join("") !== "MA") {
            return null;
        }

        tokens = source.match(/[MA]|[-+]?(?:\d*\.?\d+)(?:[eE][-+]?\d+)?/g);
        if (!tokens || tokens.length !== 11 || tokens[0] !== "M" || tokens[3] !== "A") {
            return null;
        }

        startX = Number(tokens[1]);
        startY = Number(tokens[2]);
        radiusX = Math.abs(Number(tokens[4]));
        radiusY = Math.abs(Number(tokens[5]));
        rotation = Number(tokens[6]);
        largeArc = Number(tokens[7]);
        sweepFlag = Number(tokens[8]);
        endX = Number(tokens[9]);
        endY = Number(tokens[10]);

        if (![startX, startY, radiusX, radiusY, rotation, largeArc, sweepFlag, endX, endY].every(Number.isFinite)
            || radiusX < 0.000001
            || radiusY < 0.000001
            || Math.abs(radiusX - radiusY) > Math.max(radiusX, radiusY) * 0.000001
            || (largeArc !== 0 && largeArc !== 1)
            || (sweepFlag !== 0 && sweepFlag !== 1)
            || (Math.abs(rotation) > 0.000001 && Math.abs(radiusX - radiusY) > 0.000001)
            || Math.hypot(endX - startX, endY - startY) < 0.000001) {
            return null;
        }

        radius = (radiusX + radiusY) / 2;
        halfX = (startX - endX) / 2;
        halfY = (startY - endY) / 2;
        scale = Math.sqrt((halfX * halfX + halfY * halfY) / (radius * radius));
        if (scale > 1) {
            radius *= scale;
        }

        numerator = Math.max(0, radius * radius * radius * radius
            - radius * radius * halfY * halfY
            - radius * radius * halfX * halfX);
        denominator = radius * radius * halfY * halfY + radius * radius * halfX * halfX;
        coefficient = (largeArc === sweepFlag ? -1 : 1)
            * Math.sqrt(denominator > 0 ? numerator / denominator : 0);
        centerOffsetX = coefficient * halfY;
        centerOffsetY = coefficient * -halfX;
        centerX = (startX + endX) / 2 + centerOffsetX;
        centerY = (startY + endY) / 2 + centerOffsetY;
        startAngle = Math.atan2(startY - centerY, startX - centerX);
        endAngle = Math.atan2(endY - centerY, endX - centerX);
        sweepAngle = normalizeAngle(endAngle - startAngle);
        if (!sweepFlag && sweepAngle > 0) {
            sweepAngle -= TAU;
        } else if (sweepFlag && sweepAngle < 0) {
            sweepAngle += TAU;
        }

        if (Math.abs(sweepAngle) < 0.000001 || Math.abs(sweepAngle) >= TAU) {
            return null;
        }

        return {
            cx: centerX,
            cy: centerY,
            radius: radius,
            startAngle: startAngle,
            sweepAngle: sweepAngle
        };
    }

    function normalizeAngle(value) {
        var angle = finiteNumber(value, 0) % TAU;
        return angle < 0 ? angle + TAU : angle;
    }

    function angleOnArc(angle, startAngle, sweepAngle) {
        var sweep = finiteNumber(sweepAngle, 0);
        var distance = sweep >= 0
            ? normalizeAngle(angle - startAngle)
            : normalizeAngle(startAngle - angle);
        return distance <= Math.abs(sweep) + 0.0000001;
    }

    function arcBounds(element) {
        var radius = Math.abs(finiteNumber(element && element.radius, 0));
        var startAngle = finiteNumber(element && element.startAngle, 0);
        var sweepAngle = finiteNumber(element && element.sweepAngle, 0);
        var centerX = finiteNumber(element && element.cx, 0);
        var centerY = finiteNumber(element && element.cy, 0);
        var bounds;
        var endpoints;

        if (radius < 0.000001 || Math.abs(sweepAngle) < 0.000001 || Math.abs(sweepAngle) >= TAU) {
            return null;
        }

        bounds = emptyBounds();
        endpoints = [arcPoint(element, 0), arcPoint(element, 1)];
        endpoints.forEach(function (point) {
            if (point) {
                includePoint(bounds, point.x, point.y);
            }
        });
        [0, Math.PI / 2, Math.PI, Math.PI * 1.5].forEach(function (angle) {
            if (angleOnArc(angle, startAngle, sweepAngle)) {
                includePoint(
                    bounds,
                    centerX + radius * Math.cos(angle),
                    centerY + radius * Math.sin(angle)
                );
            }
        });
        return finishBounds(bounds);
    }

    function elementSnapAnchors(element) {
        var anchors = [];
        var x;
        var y;
        var width;
        var height;

        if (!element || typeof element !== "object") {
            return anchors;
        }

        if (element.type === "line") {
            return [
                { x: finiteNumber(element.x1, 0), y: finiteNumber(element.y1, 0), kind: "endpoint" },
                { x: finiteNumber(element.x2, 0), y: finiteNumber(element.y2, 0), kind: "endpoint" }
            ];
        }

        if (element.type === "arc") {
            return [arcPoint(element, 0), arcPoint(element, 1)].filter(Boolean).map(function (point) {
                return { x: point.x, y: point.y, kind: "endpoint" };
            });
        }

        if (element.type === "polygon" || element.type === "path") {
            return normalizePoints(element.points, false).map(function (point) {
                return { x: point.x, y: point.y, kind: "anchor" };
            });
        }

        if (element.type === "rect") {
            x = finiteNumber(element.x, 0);
            y = finiteNumber(element.y, 0);
            width = finiteNumber(element.width, 0);
            height = finiteNumber(element.height, 0);
            return [
                { x: x, y: y, kind: "corner" },
                { x: x + width, y: y, kind: "corner" },
                { x: x + width, y: y + height, kind: "corner" },
                { x: x, y: y + height, kind: "corner" }
            ];
        }

        if (element.type === "ellipse") {
            x = finiteNumber(element.cx, 0);
            y = finiteNumber(element.cy, 0);
            width = Math.abs(finiteNumber(element.rx, 0));
            height = Math.abs(finiteNumber(element.ry, 0));
            return [
                { x: x - width, y: y, kind: "edge" },
                { x: x, y: y - height, kind: "edge" },
                { x: x + width, y: y, kind: "edge" },
                { x: x, y: y + height, kind: "edge" }
            ];
        }

        return anchors;
    }

    function nearestSnapAnchor(point, candidates, tolerance) {
        var targetX = finiteNumber(point && point.x, NaN);
        var targetY = finiteNumber(point && point.y, NaN);
        var limit = Math.max(0, finiteNumber(tolerance, 0));
        var best = null;
        var bestDistance = Infinity;

        if (!Number.isFinite(targetX) || !Number.isFinite(targetY) || !Array.isArray(candidates)) {
            return null;
        }

        candidates.forEach(function (candidate) {
            var x = finiteNumber(candidate && candidate.x, NaN);
            var y = finiteNumber(candidate && candidate.y, NaN);
            var distance;
            if (!Number.isFinite(x) || !Number.isFinite(y)) {
                return;
            }
            distance = Math.hypot(x - targetX, y - targetY);
            if (distance <= limit && distance < bestDistance - 0.000000001) {
                best = clone(candidate);
                best.x = x;
                best.y = y;
                best.distance = distance;
                bestDistance = distance;
            }
        });

        return best;
    }

    function elementBounds(element) {
        var type;
        var x;
        var y;
        var width;
        var height;
        var points;
        var bounds;
        var index;

        if (!element || typeof element !== "object") {
            return null;
        }

        type = element.type;

        if (type === "rect") {
            x = finiteNumber(element.x, 0);
            y = finiteNumber(element.y, 0);
            width = finiteNumber(element.width, 0);
            height = finiteNumber(element.height, 0);
            return makeBounds(
                Math.min(x, x + width),
                Math.min(y, y + height),
                Math.max(x, x + width),
                Math.max(y, y + height)
            );
        }

        if (type === "ellipse") {
            x = finiteNumber(element.cx, 0);
            y = finiteNumber(element.cy, 0);
            width = Math.abs(finiteNumber(element.rx, 0));
            height = Math.abs(finiteNumber(element.ry, 0));
            return makeBounds(x - width, y - height, x + width, y + height);
        }

        if (type === "line") {
            return makeBounds(
                Math.min(finiteNumber(element.x1, 0), finiteNumber(element.x2, 0)),
                Math.min(finiteNumber(element.y1, 0), finiteNumber(element.y2, 0)),
                Math.max(finiteNumber(element.x1, 0), finiteNumber(element.x2, 0)),
                Math.max(finiteNumber(element.y1, 0), finiteNumber(element.y2, 0))
            );
        }

        if (type === "arc") {
            return arcBounds(element);
        }

        if (type === "polygon") {
            points = normalizePoints(element.points, false);
            return boundsFromPoints(points);
        }

        if (type !== "path") {
            return null;
        }

        points = normalizePoints(element.points, true);
        if (points.length === 0) {
            return null;
        }

        bounds = emptyBounds();
        includePoint(bounds, points[0].x, points[0].y);
        for (index = 1; index < points.length; index += 1) {
            includeSegmentBounds(bounds, points[index - 1], points[index]);
        }

        if (element.closed && points.length > 1) {
            includeSegmentBounds(bounds, points[points.length - 1], points[0]);
        }

        return finishBounds(bounds);
    }

    function translateElement(element, dx, dy) {
        var offsetX = finiteNumber(dx, 0);
        var offsetY = finiteNumber(dy, 0);

        if (!element || typeof element !== "object") {
            return element;
        }

        if (element.type === "rect") {
            element.x = finiteNumber(element.x, 0) + offsetX;
            element.y = finiteNumber(element.y, 0) + offsetY;
        } else if (element.type === "ellipse") {
            element.cx = finiteNumber(element.cx, 0) + offsetX;
            element.cy = finiteNumber(element.cy, 0) + offsetY;
        } else if (element.type === "line") {
            element.x1 = finiteNumber(element.x1, 0) + offsetX;
            element.y1 = finiteNumber(element.y1, 0) + offsetY;
            element.x2 = finiteNumber(element.x2, 0) + offsetX;
            element.y2 = finiteNumber(element.y2, 0) + offsetY;
        } else if (element.type === "arc") {
            element.cx = finiteNumber(element.cx, 0) + offsetX;
            element.cy = finiteNumber(element.cy, 0) + offsetY;
        } else if (element.type === "polygon" || element.type === "path") {
            transformPoints(element.points, function (point) {
                point.x = finiteNumber(point.x, 0) + offsetX;
                point.y = finiteNumber(point.y, 0) + offsetY;
                translateHandle(point, "in", offsetX, offsetY);
                translateHandle(point, "out", offsetX, offsetY);
            });
        }

        return element;
    }

    function scaleElementToBounds(element, fromBounds, toBounds) {
        var from = normalizeBounds(fromBounds);
        var to = normalizeBounds(toBounds);
        var mapX;
        var mapY;
        var scaleX;
        var scaleY;
        var x1;
        var y1;
        var x2;
        var y2;
        var radiusScale;

        if (!element || typeof element !== "object" || !from || !to) {
            return element;
        }

        mapX = makeAxisMapper(from.x, from.width, to.x, to.width);
        mapY = makeAxisMapper(from.y, from.height, to.y, to.height);
        scaleX = from.width === 0 ? 1 : to.width / from.width;
        scaleY = from.height === 0 ? 1 : to.height / from.height;

        if (element.type === "rect") {
            x1 = mapX(finiteNumber(element.x, 0));
            y1 = mapY(finiteNumber(element.y, 0));
            x2 = mapX(finiteNumber(element.x, 0) + finiteNumber(element.width, 0));
            y2 = mapY(finiteNumber(element.y, 0) + finiteNumber(element.height, 0));
            element.x = Math.min(x1, x2);
            element.y = Math.min(y1, y2);
            element.width = Math.abs(x2 - x1);
            element.height = Math.abs(y2 - y1);
            radiusScale = Math.min(Math.abs(scaleX), Math.abs(scaleY));
            element.rx = Math.abs(finiteNumber(element.rx, 0) * radiusScale);
        } else if (element.type === "ellipse") {
            element.cx = mapX(finiteNumber(element.cx, 0));
            element.cy = mapY(finiteNumber(element.cy, 0));
            element.rx = Math.abs(finiteNumber(element.rx, 0) * scaleX);
            element.ry = Math.abs(finiteNumber(element.ry, 0) * scaleY);
        } else if (element.type === "line") {
            element.x1 = mapX(finiteNumber(element.x1, 0));
            element.y1 = mapY(finiteNumber(element.y1, 0));
            element.x2 = mapX(finiteNumber(element.x2, 0));
            element.y2 = mapY(finiteNumber(element.y2, 0));
        } else if (element.type === "arc") {
            if (Math.abs(Math.abs(scaleX) - Math.abs(scaleY)) > 0.000000001) {
                return element;
            }
            element.cx = mapX(finiteNumber(element.cx, 0));
            element.cy = mapY(finiteNumber(element.cy, 0));
            element.radius = Math.abs(finiteNumber(element.radius, 0)) * Math.abs(scaleX);
        } else if (element.type === "polygon" || element.type === "path") {
            transformPoints(element.points, function (point) {
                point.x = mapX(finiteNumber(point.x, 0));
                point.y = mapY(finiteNumber(point.y, 0));
                mapHandle(point, "in", mapX, mapY);
                mapHandle(point, "out", mapX, mapY);
            });
        }

        return element;
    }

    function mirrorElementX(element, axisX) {
        var axis = finiteNumber(axisX, 0);

        if (!element || typeof element !== "object") {
            return element;
        }

        if (element.type === "rect") {
            element.x = 2 * axis - finiteNumber(element.x, 0) - finiteNumber(element.width, 0);
        } else if (element.type === "ellipse") {
            element.cx = 2 * axis - finiteNumber(element.cx, 0);
        } else if (element.type === "line") {
            element.x1 = 2 * axis - finiteNumber(element.x1, 0);
            element.x2 = 2 * axis - finiteNumber(element.x2, 0);
        } else if (element.type === "arc") {
            element.cx = 2 * axis - finiteNumber(element.cx, 0);
            element.startAngle = Math.PI - finiteNumber(element.startAngle, 0);
            element.sweepAngle = -finiteNumber(element.sweepAngle, 0);
        } else if (element.type === "polygon" || element.type === "path") {
            transformPoints(element.points, function (point) {
                point.x = 2 * axis - finiteNumber(point.x, 0);
                mirrorHandle(point, "in", axis);
                mirrorHandle(point, "out", axis);
            });
        }

        return element;
    }

    function subdividePathSegment(points, index, closed) {
        var currentIndex = Number(index);
        var nextIndex;
        var current;
        var next;
        var control1;
        var control2;
        var firstHalfControl;
        var middleControl;
        var secondHalfControl;
        var firstInner;
        var secondInner;
        var split;
        var inserted;

        if (!Array.isArray(points) || !Number.isInteger(currentIndex) || currentIndex < 0 || currentIndex >= points.length) {
            return -1;
        }

        nextIndex = currentIndex + 1;
        if (nextIndex >= points.length) {
            if (!closed || points.length < 2) {
                return -1;
            }
            nextIndex = 0;
        }

        current = points[currentIndex];
        next = points[nextIndex];
        if (!current || !next) {
            return -1;
        }

        if (!segmentHasCurve(current, next)) {
            inserted = {
                x: (finiteNumber(current.x, 0) + finiteNumber(next.x, 0)) / 2,
                y: (finiteNumber(current.y, 0) + finiteNumber(next.y, 0)) / 2
            };
        } else {
            control1 = handlePoint(current, "out");
            control2 = handlePoint(next, "in");
            firstHalfControl = midpoint({ x: current.x, y: current.y }, control1);
            middleControl = midpoint(control1, control2);
            secondHalfControl = midpoint(control2, { x: next.x, y: next.y });
            firstInner = midpoint(firstHalfControl, middleControl);
            secondInner = midpoint(middleControl, secondHalfControl);
            split = midpoint(firstInner, secondInner);

            current.outX = firstHalfControl.x;
            current.outY = firstHalfControl.y;
            next.inX = secondHalfControl.x;
            next.inY = secondHalfControl.y;
            inserted = {
                x: split.x,
                y: split.y,
                inX: firstInner.x,
                inY: firstInner.y,
                outX: secondInner.x,
                outY: secondInner.y
            };
        }

        points.splice(currentIndex + 1, 0, inserted);
        return currentIndex + 1;
    }

    function isProjectDocument(value) {
        var hasCurrentElements;
        var hasLegacyElements;
        var versionIsSupported;

        if (!value || typeof value !== "object" || Array.isArray(value)) {
            return false;
        }

        hasCurrentElements = Array.isArray(value.elements);
        hasLegacyElements = Array.isArray(value.shapes);
        versionIsSupported = value.version === PROJECT_VERSION
            || value.version === 1
            || (value.version === undefined && hasLegacyElements);

        return versionIsSupported
            && (hasCurrentElements || hasLegacyElements)
            && Number.isFinite(Number(value.width))
            && Number(value.width) > 0
            && Number.isFinite(Number(value.height))
            && Number(value.height) > 0;
    }

    function elementToSvg(element) {
        var attributes;
        var geometry;
        var points;

        if (!element || typeof element !== "object" || element.visible === false || SHAPE_TYPES.indexOf(element.type) < 0) {
            return "";
        }

        attributes = commonSvgAttributes(element);

        if (element.type === "rect") {
            geometry = [
                attribute("x", formatNumber(element.x)),
                attribute("y", formatNumber(element.y)),
                attribute("width", formatNumber(Math.max(0, finiteNumber(element.width, 0)))),
                attribute("height", formatNumber(Math.max(0, finiteNumber(element.height, 0))))
            ];
            if (Math.abs(finiteNumber(element.rx, 0)) > 0) {
                geometry.push(attribute("rx", formatNumber(Math.abs(finiteNumber(element.rx, 0)))));
            }
            return "<rect " + geometry.concat(attributes).join(" ") + " />";
        }

        if (element.type === "ellipse") {
            geometry = [
                attribute("cx", formatNumber(element.cx)),
                attribute("cy", formatNumber(element.cy)),
                attribute("rx", formatNumber(Math.abs(finiteNumber(element.rx, 0)))),
                attribute("ry", formatNumber(Math.abs(finiteNumber(element.ry, 0))))
            ];
            return "<ellipse " + geometry.concat(attributes).join(" ") + " />";
        }

        if (element.type === "line") {
            geometry = [
                attribute("x1", formatNumber(element.x1)),
                attribute("y1", formatNumber(element.y1)),
                attribute("x2", formatNumber(element.x2)),
                attribute("y2", formatNumber(element.y2))
            ];
            return "<line " + geometry.concat(attributes).join(" ") + " />";
        }

        if (element.type === "arc") {
            geometry = arcPathData(element);
            if (!geometry) {
                return "";
            }
            return "<path " + [attribute("d", geometry)].concat(attributes).join(" ") + " />";
        }

        if (element.type === "polygon") {
            points = normalizePoints(element.points, false).map(function (point) {
                return formatNumber(point.x) + "," + formatNumber(point.y);
            }).join(" ");
            return "<polygon " + [attribute("points", points)].concat(attributes).join(" ") + " />";
        }

        geometry = pathData(element.points, element.closed);
        if (!geometry) {
            return "";
        }
        return "<path " + [attribute("d", geometry)].concat(attributes).join(" ") + " />";
    }

    function serializeSvg(documentValue, options) {
        var document = normalizeDocument(documentValue);
        var settings = options && typeof options === "object" ? options : {};
        var title = Object.prototype.hasOwnProperty.call(settings, "title") ? settings.title : document.title;
        var background = settings.background;
        var lines = [];
        var rootAttributes = [
            attribute("xmlns", SVG_NS),
            attribute("width", formatNumber(document.width)),
            attribute("height", formatNumber(document.height)),
            attribute("viewBox", "0 0 " + formatNumber(document.width) + " " + formatNumber(document.height))
        ];

        lines.push("<svg " + rootAttributes.join(" ") + ">");

        if (title !== undefined && title !== null && String(title).trim() !== "") {
            lines.push("  <title>" + escapeXml(title) + "</title>");
        }

        appendBackground(lines, background, document.width, document.height);

        document.elements.forEach(function (element) {
            var svg = elementToSvg(element);
            if (svg) {
                lines.push("  " + svg);
            }
        });

        lines.push("</svg>");
        return lines.join("\n");
    }

    function normalizeDocument(value) {
        var source = value && typeof value === "object" && !Array.isArray(value) ? value : {};
        var normalized = clone(source);
        var sourceElements = Array.isArray(source.elements)
            ? source.elements
            : (Array.isArray(source.shapes) ? source.shapes : []);
        var usedIds = Object.create(null);

        normalized.version = PROJECT_VERSION;
        normalized.width = positiveNumber(source.width, 512);
        normalized.height = positiveNumber(source.height, 512);
        normalized.title = typeof source.title === "string" ? source.title : "Untitled Artwork";
        normalized.reference = source.reference && typeof source.reference === "object"
            ? clone(source.reference)
            : null;
        normalized.elements = sourceElements.map(function (element) {
            return normalizeElement(element, usedIds);
        }).filter(Boolean);

        if (Object.prototype.hasOwnProperty.call(normalized, "shapes")) {
            delete normalized.shapes;
        }

        return normalized;
    }

    function normalizeElement(value, usedIds) {
        var type;
        var id;
        var element;
        var width;
        var height;

        if (!value || typeof value !== "object") {
            return null;
        }

        type = String(value.type || "").toLowerCase();
        if (SHAPE_TYPES.indexOf(type) < 0) {
            return null;
        }

        id = typeof value.id === "string" && value.id.trim() ? value.id : uid(type);
        if (usedIds[id]) {
            id = uid(type);
        }
        usedIds[id] = true;

        element = {
            id: id,
            type: type,
            name: typeof value.name === "string" ? value.name : capitalize(type),
            visible: value.visible !== false,
            locked: value.locked === true,
            style: normalizeStyle(value.style, type, value.closed)
        };

        if (type === "rect") {
            element.x = finiteNumber(value.x, 0);
            element.y = finiteNumber(value.y, 0);
            width = finiteNumber(value.width, 0);
            height = finiteNumber(value.height, 0);
            if (width < 0) {
                element.x += width;
            }
            if (height < 0) {
                element.y += height;
            }
            element.width = Math.abs(width);
            element.height = Math.abs(height);
            element.rx = Math.min(
                Math.abs(finiteNumber(value.rx, 0)),
                element.width / 2,
                element.height / 2
            );
        } else if (type === "ellipse") {
            element.cx = finiteNumber(value.cx, 0);
            element.cy = finiteNumber(value.cy, 0);
            element.rx = Math.abs(finiteNumber(value.rx, 0));
            element.ry = Math.abs(finiteNumber(value.ry, 0));
        } else if (type === "line") {
            element.x1 = finiteNumber(value.x1, 0);
            element.y1 = finiteNumber(value.y1, 0);
            element.x2 = finiteNumber(value.x2, 0);
            element.y2 = finiteNumber(value.y2, 0);
        } else if (type === "arc") {
            element.cx = finiteNumber(value.cx, 0);
            element.cy = finiteNumber(value.cy, 0);
            element.radius = Math.abs(finiteNumber(value.radius, 0));
            element.startAngle = finiteNumber(value.startAngle, 0);
            element.sweepAngle = finiteNumber(value.sweepAngle, 0);
            if (element.radius < 0.000001 || Math.abs(element.sweepAngle) < 0.000001) {
                return null;
            }
            element.sweepAngle = Math.sign(element.sweepAngle)
                * Math.min(Math.abs(element.sweepAngle), TAU - 0.000001);
        } else if (type === "polygon") {
            element.points = normalizePoints(value.points, false);
            if (element.points.length < 3) {
                return null;
            }
        } else {
            element.points = normalizePoints(value.points, true);
            if (element.points.length < 2) {
                return null;
            }
            element.closed = value.closed === true;
        }

        return element;
    }

    function normalizeStyle(value, type, closed) {
        var source = value && typeof value === "object" ? value : {};
        var openShape = type === "line" || type === "arc" || (type === "path" && closed !== true);
        var lineCaps = ["butt", "round", "square"];
        var lineJoins = ["miter", "round", "bevel"];
        var lineCap = String(source.lineCap || DEFAULT_STYLE.lineCap).toLowerCase();
        var lineJoin = String(source.lineJoin || DEFAULT_STYLE.lineJoin).toLowerCase();
        var dash = normalizeDash(source.dash);

        return {
            fill: typeof source.fill === "string" ? source.fill : (openShape ? "none" : DEFAULT_STYLE.fill),
            fillOpacity: normalizeOpacity(source.fillOpacity, DEFAULT_STYLE.fillOpacity),
            stroke: typeof source.stroke === "string" ? source.stroke : DEFAULT_STYLE.stroke,
            strokeOpacity: normalizeOpacity(source.strokeOpacity, DEFAULT_STYLE.strokeOpacity),
            strokeWidth: Math.max(0, finiteNumber(source.strokeWidth, DEFAULT_STYLE.strokeWidth)),
            lineCap: lineCaps.indexOf(lineCap) >= 0 ? lineCap : DEFAULT_STYLE.lineCap,
            lineJoin: lineJoins.indexOf(lineJoin) >= 0 ? lineJoin : DEFAULT_STYLE.lineJoin,
            dash: dash
        };
    }

    function normalizeDash(value) {
        if (Array.isArray(value)) {
            return value.map(function (part) {
                return Math.max(0, finiteNumber(part, 0));
            }).filter(function (part) {
                return part > 0;
            }).join(" ");
        }

        if (typeof value === "number" && Number.isFinite(value)) {
            return value > 0 ? String(value) : "";
        }

        return typeof value === "string" ? value.trim() : DEFAULT_STYLE.dash;
    }

    function normalizeOpacity(value, fallback) {
        var numeric = Number(value);
        return Number.isFinite(numeric) ? clamp(numeric, 0, 1) : fallback;
    }

    function normalizePoints(points, includeHandles) {
        if (!Array.isArray(points)) {
            return [];
        }

        return points.map(function (value) {
            var point;

            if (!value || typeof value !== "object" || !Number.isFinite(Number(value.x)) || !Number.isFinite(Number(value.y))) {
                return null;
            }

            point = {
                x: Number(value.x),
                y: Number(value.y)
            };

            if (includeHandles) {
                copyHandle(value, point, "in");
                copyHandle(value, point, "out");
            }

            return point;
        }).filter(Boolean);
    }

    function copyHandle(source, target, prefix) {
        var xKey = prefix + "X";
        var yKey = prefix + "Y";

        if (Number.isFinite(Number(source[xKey])) && Number.isFinite(Number(source[yKey]))) {
            target[xKey] = Number(source[xKey]);
            target[yKey] = Number(source[yKey]);
        }
    }

    function appendPathSegment(commands, from, to) {
        var control1;
        var control2;

        if (!segmentHasCurve(from, to)) {
            commands.push("L " + coordinatePair(to.x, to.y));
            return;
        }

        control1 = handlePoint(from, "out");
        control2 = handlePoint(to, "in");
        commands.push(
            "C " + coordinatePair(control1.x, control1.y) + " "
            + coordinatePair(control2.x, control2.y) + " "
            + coordinatePair(to.x, to.y)
        );
    }

    function segmentHasCurve(from, to) {
        return hasHandle(from, "out") || hasHandle(to, "in");
    }

    function hasHandle(point, prefix) {
        return Number.isFinite(point[prefix + "X"]) && Number.isFinite(point[prefix + "Y"]);
    }

    function handlePoint(point, prefix) {
        return hasHandle(point, prefix)
            ? { x: point[prefix + "X"], y: point[prefix + "Y"] }
            : { x: point.x, y: point.y };
    }

    function midpoint(first, second) {
        return {
            x: (finiteNumber(first.x, 0) + finiteNumber(second.x, 0)) / 2,
            y: (finiteNumber(first.y, 0) + finiteNumber(second.y, 0)) / 2
        };
    }

    function coordinatePair(x, y) {
        return formatNumber(x) + " " + formatNumber(y);
    }

    function arcCoordinatePair(x, y) {
        return formatArcNumber(x) + " " + formatArcNumber(y);
    }

    function formatArcNumber(value) {
        var numeric = finiteNumber(value, 0);
        return String(round(numeric, 12));
    }

    function formatNumber(value) {
        var numeric = finiteNumber(value, 0);
        return String(round(numeric, 3));
    }

    function boundsFromPoints(points) {
        var bounds;

        if (!points || points.length === 0) {
            return null;
        }

        bounds = emptyBounds();
        points.forEach(function (point) {
            includePoint(bounds, point.x, point.y);
        });
        return finishBounds(bounds);
    }

    function emptyBounds() {
        return {
            minX: Infinity,
            minY: Infinity,
            maxX: -Infinity,
            maxY: -Infinity
        };
    }

    function includePoint(bounds, x, y) {
        if (!Number.isFinite(x) || !Number.isFinite(y)) {
            return;
        }

        bounds.minX = Math.min(bounds.minX, x);
        bounds.minY = Math.min(bounds.minY, y);
        bounds.maxX = Math.max(bounds.maxX, x);
        bounds.maxY = Math.max(bounds.maxY, y);
    }

    function includeSegmentBounds(bounds, from, to) {
        var control1;
        var control2;
        var extrema;

        if (!segmentHasCurve(from, to)) {
            includePoint(bounds, from.x, from.y);
            includePoint(bounds, to.x, to.y);
            return;
        }

        control1 = handlePoint(from, "out");
        control2 = handlePoint(to, "in");
        includePoint(bounds, from.x, from.y);
        includePoint(bounds, to.x, to.y);
        extrema = cubicExtrema(from, control1, control2, to);
        extrema.forEach(function (t) {
            includePoint(
                bounds,
                cubicValue(from.x, control1.x, control2.x, to.x, t),
                cubicValue(from.y, control1.y, control2.y, to.y, t)
            );
        });
    }

    function cubicExtrema(start, control1, control2, end) {
        var values = [];

        addQuadraticRoots(values, start.x, control1.x, control2.x, end.x);
        addQuadraticRoots(values, start.y, control1.y, control2.y, end.y);

        return values.filter(function (value, index) {
            return value > 0 && value < 1 && values.indexOf(value) === index;
        });
    }

    function addQuadraticRoots(output, start, control1, control2, end) {
        var a = -start + 3 * control1 - 3 * control2 + end;
        var b = 2 * (start - 2 * control1 + control2);
        var c = control1 - start;
        var discriminant;
        var root;

        if (Math.abs(a) < 1e-12) {
            if (Math.abs(b) >= 1e-12) {
                output.push(-c / b);
            }
            return;
        }

        discriminant = b * b - 4 * a * c;
        if (discriminant < 0) {
            return;
        }

        root = Math.sqrt(discriminant);
        output.push((-b + root) / (2 * a));
        output.push((-b - root) / (2 * a));
    }

    function cubicValue(start, control1, control2, end, t) {
        var inverse = 1 - t;
        return inverse * inverse * inverse * start
            + 3 * inverse * inverse * t * control1
            + 3 * inverse * t * t * control2
            + t * t * t * end;
    }

    function makeBounds(minX, minY, maxX, maxY) {
        return {
            x: minX,
            y: minY,
            width: maxX - minX,
            height: maxY - minY
        };
    }

    function finishBounds(bounds) {
        if (!Number.isFinite(bounds.minX) || !Number.isFinite(bounds.minY)
            || !Number.isFinite(bounds.maxX) || !Number.isFinite(bounds.maxY)) {
            return null;
        }

        return makeBounds(bounds.minX, bounds.minY, bounds.maxX, bounds.maxY);
    }

    function transformPoints(points, transform) {
        if (!Array.isArray(points)) {
            return;
        }

        points.forEach(function (point) {
            if (point && typeof point === "object") {
                transform(point);
            }
        });
    }

    function translateHandle(point, prefix, dx, dy) {
        var xKey = prefix + "X";
        var yKey = prefix + "Y";

        if (Number.isFinite(Number(point[xKey])) && Number.isFinite(Number(point[yKey]))) {
            point[xKey] = Number(point[xKey]) + dx;
            point[yKey] = Number(point[yKey]) + dy;
        }
    }

    function mapHandle(point, prefix, mapX, mapY) {
        var xKey = prefix + "X";
        var yKey = prefix + "Y";

        if (Number.isFinite(Number(point[xKey])) && Number.isFinite(Number(point[yKey]))) {
            point[xKey] = mapX(Number(point[xKey]));
            point[yKey] = mapY(Number(point[yKey]));
        }
    }

    function mirrorHandle(point, prefix, axis) {
        var xKey = prefix + "X";

        if (Number.isFinite(Number(point[xKey]))) {
            point[xKey] = 2 * axis - Number(point[xKey]);
        }
    }

    function normalizeBounds(value) {
        if (!value || typeof value !== "object") {
            return null;
        }

        return {
            x: finiteNumber(value.x, 0),
            y: finiteNumber(value.y, 0),
            width: finiteNumber(value.width, 0),
            height: finiteNumber(value.height, 0)
        };
    }

    function makeAxisMapper(fromStart, fromSize, toStart, toSize) {
        if (fromSize === 0) {
            if (toSize === 0) {
                return function (value) {
                    return value + toStart - fromStart;
                };
            }

            return function () {
                return toStart + toSize / 2;
            };
        }

        return function (value) {
            return toStart + (value - fromStart) * toSize / fromSize;
        };
    }

    function commonSvgAttributes(element) {
        var style = normalizeStyle(element.style, element.type, element.closed);
        var attributes = [];

        if (typeof element.id === "string" && element.id !== "") {
            attributes.push(attribute("id", element.id));
        }

        if (typeof element.name === "string" && element.name !== "") {
            attributes.push(attribute("data-name", element.name));
        }

        attributes.push(attribute("fill", element.type === "line" || element.type === "arc" ? "none" : style.fill));
        attributes.push(attribute("fill-opacity", formatNumber(style.fillOpacity)));
        attributes.push(attribute("stroke", style.stroke));
        attributes.push(attribute("stroke-opacity", formatNumber(style.strokeOpacity)));
        attributes.push(attribute("stroke-width", formatNumber(style.strokeWidth)));
        attributes.push(attribute("stroke-linecap", style.lineCap));
        attributes.push(attribute("stroke-linejoin", style.lineJoin));

        if (style.dash) {
            attributes.push(attribute("stroke-dasharray", style.dash));
        }

        return attributes;
    }

    function attribute(name, value) {
        return name + "=\"" + escapeXml(value) + "\"";
    }

    function escapeXml(value) {
        return String(value)
            .replace(/[\u0000-\u0008\u000B\u000C\u000E-\u001F]/g, "")
            .replace(/&/g, "&amp;")
            .replace(/</g, "&lt;")
            .replace(/>/g, "&gt;")
            .replace(/\"/g, "&quot;")
            .replace(/'/g, "&apos;");
    }

    function appendBackground(lines, background, width, height) {
        var color;
        var opacity;
        var attributes;

        if (background === undefined || background === null || background === false || background === "") {
            return;
        }

        if (background && typeof background === "object") {
            color = typeof background.color === "string"
                ? background.color
                : (typeof background.fill === "string" ? background.fill : "#ffffff");
            opacity = normalizeOpacity(background.opacity, 1);
        } else {
            color = background === true ? "#ffffff" : String(background);
            opacity = 1;
        }

        attributes = [
            attribute("x", "0"),
            attribute("y", "0"),
            attribute("width", formatNumber(width)),
            attribute("height", formatNumber(height)),
            attribute("fill", color)
        ];

        if (opacity !== 1) {
            attributes.push(attribute("fill-opacity", formatNumber(opacity)));
        }

        lines.push("  <rect " + attributes.join(" ") + " />");
    }

    function finiteNumber(value, fallback) {
        var numeric = Number(value);
        return Number.isFinite(numeric) ? numeric : fallback;
    }

    function positiveNumber(value, fallback) {
        var numeric = Number(value);
        return Number.isFinite(numeric) && numeric > 0 ? numeric : fallback;
    }

    function capitalize(value) {
        return value.charAt(0).toUpperCase() + value.slice(1);
    }

    return Object.freeze({
        PROJECT_VERSION: PROJECT_VERSION,
        uid: uid,
        createDocument: createDocument,
        clone: clone,
        clamp: clamp,
        snap: snap,
        round: round,
        pathData: pathData,
        parseEditablePathData: parseEditablePathData,
        circularArcFromChord: circularArcFromChord,
        arcPoint: arcPoint,
        arcPathData: arcPathData,
        parseEditableArcData: parseEditableArcData,
        elementSnapAnchors: elementSnapAnchors,
        nearestSnapAnchor: nearestSnapAnchor,
        elementBounds: elementBounds,
        translateElement: translateElement,
        scaleElementToBounds: scaleElementToBounds,
        mirrorElementX: mirrorElementX,
        subdividePathSegment: subdividePathSegment,
        elementToSvg: elementToSvg,
        serializeSvg: serializeSvg,
        normalizeDocument: normalizeDocument,
        isProjectDocument: isProjectDocument
    });
}));
