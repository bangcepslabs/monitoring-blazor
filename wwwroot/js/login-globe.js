(function () {
    "use strict";

    const instances = new WeakMap();
    const TAU = Math.PI * 2;
    const LAND_MASK_URL = "https://gist.githubusercontent.com/powersa/a96641ec2d9b4584e683/raw/110m_land.json";
    const LAND_MASK_CACHE_KEY = "opseye.login.land-mask.110m";

    // Intentionally coarse coastline masks. They keep the asset tiny while
    // making the point cloud read as a digital earth rather than a sphere.
    const LAND_POLYGONS = [
        // North America
        [[-168, 72], [-150, 70], [-140, 62], [-130, 55], [-124, 48], [-117, 32], [-105, 24], [-94, 18], [-84, 16], [-78, 26], [-66, 45], [-53, 52], [-60, 62], [-82, 70], [-105, 78], [-140, 78]],
        // Central America and the Caribbean bridge
        [[-100, 24], [-91, 18], [-87, 11], [-81, 8], [-77, 14], [-80, 22], [-88, 28]],
        // South America
        [[-81, 12], [-70, 8], [-58, 10], [-48, 1], [-35, -8], [-38, -22], [-47, -34], [-58, -50], [-70, -55], [-75, -42], [-79, -20]],
        // Greenland
        [[-74, 83], [-24, 84], [-18, 72], [-42, 59], [-60, 63]],
        // Europe
        [[-11, 36], [2, 43], [14, 44], [25, 52], [42, 56], [34, 70], [18, 71], [4, 62], [-8, 58], [-18, 48]],
        // Africa
        [[-17, 35], [2, 37], [19, 33], [34, 29], [43, 12], [52, 5], [43, -12], [34, -29], [20, -35], [8, -35], [-4, -25], [-14, -5], [-18, 14]],
        // Asia, including the large northern landmass
        [[25, 56], [42, 52], [55, 56], [72, 54], [90, 55], [110, 52], [130, 49], [145, 54], [160, 61], [178, 67], [155, 75], [118, 77], [80, 75], [48, 70], [35, 64]],
        // Middle East and India
        [[34, 42], [48, 40], [62, 36], [76, 30], [88, 24], [96, 18], [88, 7], [77, 9], [67, 20], [52, 23], [42, 30]],
        // South-east Asia islands / peninsula mass
        [[96, 22], [110, 20], [123, 8], [135, 5], [133, -7], [118, -10], [105, 0], [96, 8]],
        // Japan and the far east islands
        [[135, 45], [146, 44], [146, 32], [137, 30], [131, 35]],
        // Australia
        [[113, -12], [128, -11], [143, -15], [153, -25], [150, -39], [137, -44], [120, -35], [112, -24]],
        // Madagascar
        [[44, -12], [51, -14], [50, -26], [44, -25]]
    ];

    const NETWORK_NODES = [
        [-74, 40], [-46, -23], [-3, 51], [31, 30], [55, 25], [77, 29],
        [103, 1], [139, 35], [151, -33], [18, -26]
    ];

    function clamp(value, min, max) {
        return Math.max(min, Math.min(max, value));
    }

    function readColor(name, fallback, element) {
        const value = getComputedStyle(element || document.documentElement).getPropertyValue(name).trim();
        const rgb = value.match(/rgba?\(\s*(\d+)\s*,\s*(\d+)\s*,\s*(\d+)/i);
        if (rgb) {
            return [Number(rgb[1]), Number(rgb[2]), Number(rgb[3])];
        }

        const hex = value.replace("#", "");
        if (/^[0-9a-f]{6}$/i.test(hex)) {
            return [parseInt(hex.slice(0, 2), 16), parseInt(hex.slice(2, 4), 16), parseInt(hex.slice(4, 6), 16)];
        }

        return fallback;
    }

    function pointInPolygon(lon, lat, polygon) {
        let inside = false;
        for (let i = 0, j = polygon.length - 1; i < polygon.length; j = i++) {
            const xi = polygon[i][0];
            const yi = polygon[i][1];
            const xj = polygon[j][0];
            const yj = polygon[j][1];
            const crosses = ((yi > lat) !== (yj > lat)) && lon < (xj - xi) * (lat - yi) / (yj - yi) + xi;
            if (crosses) {
                inside = !inside;
            }
        }
        return inside;
    }

    function preparePolygons(polygons) {
        return polygons.map((points) => {
            let minLon = 180;
            let maxLon = -180;
            let minLat = 90;
            let maxLat = -90;
            for (const point of points) {
                minLon = Math.min(minLon, point[0]);
                maxLon = Math.max(maxLon, point[0]);
                minLat = Math.min(minLat, point[1]);
                maxLat = Math.max(maxLat, point[1]);
            }
            return { points, minLon, maxLon, minLat, maxLat };
        });
    }

    function createSphereLight(context, state, radius, centerX, centerY) {
        const light = context.createRadialGradient(
            centerX - radius * 0.24,
            centerY - radius * 0.25,
            radius * 0.08,
            centerX,
            centerY,
            radius * 1.08
        );
        light.addColorStop(0, `rgba(${state.accent[0]}, ${state.accent[1]}, ${state.accent[2]}, 0.12)`);
        light.addColorStop(0.58, `rgba(${state.accent[0]}, ${state.accent[1]}, ${state.accent[2]}, 0.035)`);
        light.addColorStop(0.9, "rgba(0, 0, 0, 0.025)");
        light.addColorStop(1, "rgba(0, 0, 0, 0)");
        return light;
    }

    function isLand(lon, lat, polygons) {
        for (let index = 0; index < polygons.length; index += 1) {
            const polygon = polygons[index];
            if (lon < polygon.minLon || lon > polygon.maxLon || lat < polygon.minLat || lat > polygon.maxLat) {
                continue;
            }
            if (pointInPolygon(lon, lat, polygon.points)) {
                return true;
            }
        }
        return false;
    }

    function toSphere(lon, lat, type, size, glyph) {
        const longitude = lon * Math.PI / 180;
        const latitude = lat * Math.PI / 180;
        return {
            x: Math.cos(latitude) * Math.sin(longitude),
            y: Math.sin(latitude),
            z: Math.cos(latitude) * Math.cos(longitude),
            type,
            glyph: glyph || "",
            size: size || 1,
            phase: (lon * 0.173 + lat * 0.371) % TAU
        };
    }

    function createPoints(count, polygons) {
        const candidates = [];
        const step = count > 3000 ? 1.02 : 1.9;

        for (let lat = -58; lat <= 82; lat += step) {
            const longitudeStep = step / Math.max(0.35, Math.cos(lat * Math.PI / 180));
            for (let lon = -180; lon < 180; lon += longitudeStep) {
                if (isLand(lon, lat, polygons)) {
                    const hash = Math.abs(Math.sin((lon + 17.3) * 12.9898 + (lat - 8.1) * 78.233));
                    const jitterLon = (hash - 0.5) * step * 0.34;
                    const jitterLat = (Math.abs(Math.sin(hash * 19.17)) - 0.5) * step * 0.28;
                    // Text drawing is considerably more expensive than a
                    // pixel. Keep the numeric texture as a sparse detail.
                    const glyph = hash > 0.93 ? ["0", "1", "2", "3"][Math.floor(hash * 31) % 4] : "";
                    candidates.push(toSphere(lon + jitterLon, lat + jitterLat, "land", 0.9 + ((Math.abs(Math.round(lon + lat)) % 7) / 22), glyph));
                }
            }
        }

        // Keep the intended density stable across mask revisions.
        const stride = Math.max(1, Math.floor(candidates.length / count));
        const land = [];
        for (let index = 0; index < candidates.length && land.length < count; index += stride) {
            land.push(candidates[index]);
        }

        // A restrained ocean shell preserves the round silhouette without
        // turning the globe back into a uniform sphere.
        const oceanCount = Math.round(count * 0.22);
        for (let index = 0; index < oceanCount; index += 1) {
            const y = 1 - (index / Math.max(1, oceanCount - 1)) * 2;
            const radius = Math.sqrt(Math.max(0, 1 - y * y));
            const theta = 2.399963229728653 * index;
            const point = {
                x: Math.cos(theta) * radius,
                y,
                z: Math.sin(theta) * radius,
                type: "ocean",
                glyph: "",
                size: 0.7 + (index % 5) * 0.035,
                phase: index * 0.47
            };
            land.push(point);
        }

        return land;
    }

    function extractGeoJsonPolygons(geoJson) {
        const polygons = [];
        const features = geoJson?.type === "FeatureCollection"
            ? geoJson.features
            : geoJson?.type === "Feature"
                ? [geoJson]
                : [{ geometry: geoJson }];

        for (const feature of features || []) {
            const geometry = feature?.geometry;
            if (!geometry) {
                continue;
            }
            if (geometry.type === "Polygon" && geometry.coordinates?.[0]?.length > 2) {
                polygons.push(geometry.coordinates[0]);
            } else if (geometry.type === "MultiPolygon") {
                for (const polygon of geometry.coordinates || []) {
                    if (polygon?.[0]?.length > 2) {
                        polygons.push(polygon[0]);
                    }
                }
            }
        }

        return polygons;
    }

    async function loadAccurateLandMask(state) {
        let geoJson = null;
        try {
            const cached = sessionStorage.getItem(LAND_MASK_CACHE_KEY);
            if (cached) {
                geoJson = JSON.parse(cached);
            }
        } catch {
            // Fall back to the bundled coarse mask if storage is unavailable.
        }

        if (!geoJson) {
            try {
                const response = await fetch(LAND_MASK_URL, {
                    signal: state.abortController.signal,
                    cache: "force-cache"
                });
                if (!response.ok) {
                    return;
                }
                geoJson = await response.json();
                try {
                    sessionStorage.setItem(LAND_MASK_CACHE_KEY, JSON.stringify(geoJson));
                } catch {
                    // The current page can still use the downloaded mask.
                }
            } catch {
                return;
            }
        }

        const polygons = extractGeoJsonPolygons(geoJson);
        if (!state.disposed && polygons.length > 0) {
            state.landPolygons = preparePolygons(polygons);
            state.rebuild();
        }
    }

    function projectInto(point, state, radius, centerX, centerY, output) {
        const x = point.x * state.cosY - point.z * state.sinY;
        const zRotated = point.x * state.sinY + point.z * state.cosY;
        const y = point.y * state.cosX - zRotated * state.sinX;
        const z = point.y * state.sinX + zRotated * state.cosX;
        const depth = (z + 1) * 0.5;
        const perspective = 1.02 + depth * 0.1;
        output.x = centerX + x * radius * perspective;
        output.y = centerY + y * radius * perspective;
        output.z = z;
        output.depth = depth;
        output.perspective = perspective;
        return output;
    }

    function init(canvas) {
        if (!canvas) {
            return;
        }

        dispose(canvas);
        const context = canvas.getContext("2d", { alpha: true });
        if (!context) {
            return;
        }

        const reducedMotionQuery = window.matchMedia("(prefers-reduced-motion: reduce)");
        const state = {
            canvas,
            context,
            points: [],
            nodes: [],
            width: 0,
            height: 0,
            pixelRatio: 1,
            angle: 0.08,
            baseTilt: -0.2,
            lastTime: 0,
            lastRender: 0,
            // 30fps aligns cleanly with common 60Hz displays and is ample
            // for this deliberately slow background rotation.
            frameInterval: 1000 / 30,
            frame: 0,
            disposed: false,
            reducedMotion: reducedMotionQuery.matches,
            pointerX: 0,
            pointerY: 0,
            landPolygons: preparePolygons(LAND_POLYGONS),
            abortController: new AbortController(),
            projected: { x: 0, y: 0, z: 0, depth: 0, perspective: 1 },
            nodeA: { x: 0, y: 0, z: 0, depth: 0, perspective: 1 },
            nodeB: { x: 0, y: 0, z: 0, depth: 0, perspective: 1 },
            cosY: 1,
            sinY: 0,
            cosX: 1,
            sinX: 0,
            sphereLight: null,
            accent: readColor("--login-globe-color", [232, 243, 255], canvas.closest(".login-visual-panel")),
            secondary: readColor("--text-secondary", [148, 163, 184])
        };

        function resize() {
            const rect = canvas.getBoundingClientRect();
            state.width = Math.max(1, rect.width);
            state.height = Math.max(1, rect.height);
            state.pixelRatio = Math.min(window.devicePixelRatio || 1, 1.75);
            canvas.width = Math.floor(state.width * state.pixelRatio);
            canvas.height = Math.floor(state.height * state.pixelRatio);
            context.setTransform(state.pixelRatio, 0, 0, state.pixelRatio, 0, 0);

            const compact = state.width < 640 || window.innerWidth < 700;
            // Canvas 2D renders each particle on the CPU. This density keeps
            // the globe readable while avoiding a login-page GPU/CPU spike.
            const count = compact ? 1000 : 4400;
            state.points = createPoints(count, state.landPolygons);
            state.nodes = NETWORK_NODES.map((node) => toSphere(node[0], node[1], "node", 1.7));
            state.sphereLight = createSphereLight(context, state, radiusFor(state), centerXFor(state), state.height * 0.5);
            draw(0);
        }

        function radiusFor(currentState) {
            return Math.min(currentState.height * 0.43, currentState.width * 0.52, 390);
        }

        function centerXFor(currentState) {
            return currentState.width * 0.7;
        }

        function draw(time) {
            if (state.disposed) {
                return;
            }

            if (time !== 0 && state.lastRender > 0 && time - state.lastRender < state.frameInterval) {
                state.frame = window.requestAnimationFrame(draw);
                return;
            }
            state.lastRender = time;

            const ctx = state.context;
            const radius = radiusFor(state);
            const centerX = centerXFor(state);
            const centerY = state.height * 0.5;
            const now = time * 0.001;
            state.cosY = Math.cos(state.angle);
            state.sinY = Math.sin(state.angle);
            state.cosX = Math.cos(state.baseTilt);
            state.sinX = Math.sin(state.baseTilt);
            ctx.clearRect(0, 0, state.width, state.height);

            // A restrained spherical light field gives the particle surface a
            // readable globe volume before the individual pixels are drawn.
            ctx.fillStyle = state.sphereLight;
            ctx.beginPath();
            ctx.arc(centerX, centerY, radius * 1.02, 0, TAU);
            ctx.fill();

            ctx.fillStyle = `rgb(${state.accent[0]}, ${state.accent[1]}, ${state.accent[2]})`;
            for (let index = 0; index < state.points.length; index += 1) {
                const point = state.points[index];
                const projected = projectInto(point, state, radius, centerX, centerY, state.projected);
                const depth = projected.depth;
                const twinkle = 0.94 + Math.sin(now * 0.55 + point.phase) * 0.06;
                const normalizedX = (projected.x - centerX) / Math.max(1, radius);
                const normalizedY = (projected.y - centerY) / Math.max(1, radius);
                const edgeDistance = Math.min(1, normalizedX * normalizedX + normalizedY * normalizedY);
                const edgeFalloff = 0.78 + Math.sqrt(Math.max(0, 1 - edgeDistance)) * 0.22;
                const landAlpha = depth > 0.68 ? 0.84 + depth * 0.14 : depth > 0.32 ? 0.44 + depth * 0.34 : 0.1 + depth * 0.25;
                const alpha = point.type === "ocean" ? 0.035 + depth * 0.09 : landAlpha * twinkle;
                const size = point.size * (point.type === "ocean" ? 0.48 : 0.66 + depth * 0.92);

                ctx.globalAlpha = clamp(alpha * edgeFalloff, 0.025, 0.98);
                if (point.glyph && depth > 0.24) {
                    ctx.font = `${Math.max(point.type === "ocean" ? 1.8 : 2.2, size * (point.type === "ocean" ? 1.85 : 2.25))}px ui-monospace, SFMono-Regular, Menlo, Consolas, monospace`;
                    ctx.textAlign = "center";
                    ctx.textBaseline = "middle";
                    ctx.fillText(point.glyph, projected.x, projected.y);
                } else {
                    ctx.beginPath();
                    ctx.arc(projected.x, projected.y, size, 0, TAU);
                    ctx.fill();
                }
            }

            // Only a few faint connections remain visible at a time. They
            // are clipped by depth so the network sits inside the globe.
            ctx.lineWidth = 0.7;
            for (let index = 0; index < state.nodes.length - 1; index += 2) {
                const first = projectInto(state.nodes[index], state, radius, centerX, centerY, state.nodeA);
                const second = projectInto(state.nodes[index + 1], state, radius, centerX, centerY, state.nodeB);
                const visibility = Math.min(first.depth, second.depth);
                if (visibility < 0.3) {
                    continue;
                }

                const controlX = (first.x + second.x) * 0.5;
                const controlY = (first.y + second.y) * 0.5 - radius * 0.11;
                ctx.globalAlpha = 0.08 + visibility * 0.12;
                ctx.strokeStyle = `rgb(${state.accent[0]}, ${state.accent[1]}, ${state.accent[2]})`;
                ctx.beginPath();
                ctx.moveTo(first.x, first.y);
                ctx.quadraticCurveTo(controlX, controlY, second.x, second.y);
                ctx.stroke();
            }

            for (let index = 0; index < state.nodes.length; index += 1) {
                const node = projectInto(state.nodes[index], state, radius, centerX, centerY, state.nodeA);
                if (node.depth < 0.22) {
                    continue;
                }
                ctx.globalAlpha = 0.46 + node.depth * 0.42;
                ctx.beginPath();
                ctx.arc(node.x, node.y, 1.25 + node.depth * 1.35, 0, TAU);
                ctx.fill();
            }

            ctx.globalAlpha = 1;
            if (!state.reducedMotion) {
                state.angle += 0.0001 * Math.max(0, time - state.lastTime);
                state.frame = window.requestAnimationFrame(draw);
            }
            state.lastTime = time;
        }

        function onMotionPreferenceChange(event) {
            state.reducedMotion = event.matches;
            window.cancelAnimationFrame(state.frame);
            state.frame = 0;
            draw(performance.now());
            if (!state.reducedMotion) {
                state.lastTime = performance.now();
                state.frame = window.requestAnimationFrame(draw);
            }
        }

        const observer = new ResizeObserver(resize);
        observer.observe(canvas.parentElement || canvas);
        reducedMotionQuery.addEventListener?.("change", onMotionPreferenceChange);

        state.dispose = function () {
            if (state.disposed) {
                return;
            }
            state.disposed = true;
            window.cancelAnimationFrame(state.frame);
            state.abortController.abort();
            observer.disconnect();
            reducedMotionQuery.removeEventListener?.("change", onMotionPreferenceChange);
            state.points.length = 0;
            state.nodes.length = 0;
            context.clearRect(0, 0, state.width, state.height);
            context.globalAlpha = 1;
        };

        instances.set(canvas, state);
        state.rebuild = resize;
        resize();
        loadAccurateLandMask(state);
        if (!state.reducedMotion) {
            state.lastTime = performance.now();
            state.frame = window.requestAnimationFrame(draw);
        }
    }

    function dispose(canvas) {
        const state = instances.get(canvas);
        if (state) {
            state.dispose();
            instances.delete(canvas);
        }
    }

    window.loginGlobe = { init, dispose };
})();
