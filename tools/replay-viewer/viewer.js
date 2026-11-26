// Knez replay viewer: plays a `knez-frames` v1 file (battle-sim.md §17, written by FrameRecorder in the CLI).
// View-only tool: floating point is fine here. World axes: X east, Y north (screen Y is flipped); angles in degrees,
// 0 = east, counter-clockwise. Between ticks, positions and facings are interpolated for smooth playback.
(() => {
  'use strict';

  const $ = (id) => document.getElementById(id);
  const canvas = $('canvas');
  const ctx = canvas.getContext('2d');
  const stage = $('stage');
  const params = new URLSearchParams(location.search);

  // Side 0 (player) cool colours, side 1 (enemy) warm, further sides neutral; one shade per squad within its side.
  const SIDE_PALETTES = [
    ['#2f6db3', '#2a9d8f', '#5b5bd6', '#3f8f4f', '#1f4e79'],
    ['#c0392b', '#d9822b', '#a83279', '#8c5a2b', '#7b241c'],
    ['#6d6d6d', '#8e7cc3', '#b8860b', '#4c7a8a', '#556b2f'],
  ];
  const DEAD = '#9a9a9a';

  let data = null;           // the parsed frames file
  let rowCache = { index: -1, rows: null }; // one frame's lookup maps: interpolation only ever needs the next frame
  let fighterInfo = new Map(); // id → static fighter entry
  let squadInfo = new Map();   // id → static squad entry + colour + fighter count
  let loadError = null;
  let t = 0;                 // playhead in frame indices (fractional while playing)
  let playing = false;
  let speed = 1;
  let dirty = true;
  let lastTime = null;
  let hoverId = null;
  const view = { scale: 10, ox: 0, oy: 0, fitted: true };
  const show = { labels: true, anchors: true, grid: true, ids: false, overlaps: true, morale: true };
  const OVERLAP_TOLERANCE = 0.05; // metres; the sim's MovementMetrics.OverlapTolerance
  let drag = null;

  // ---------- loading ----------

  function setMessage(text, isError) {
    const el = $('message');
    el.hidden = !text;
    el.textContent = text || '';
    el.classList.toggle('error', !!isError);
    loadError = isError ? text : null;
  }

  function validate(d) {
    if (!d || d.format !== 'knez-frames') throw new Error('Not a knez-frames file (missing "format": "knez-frames").');
    if (d.version !== 1) throw new Error(`Unsupported frames version ${d.version}; this viewer reads version 1.`);
    if (!Array.isArray(d.frames) || d.frames.length === 0) throw new Error('The file has no frames.');
  }

  function load(d, sourceName) {
    validate(d);
    // Build everything in locals first: a malformed file must not leave the viewer half-loaded.
    const fighters = new Map(d.fighters.map((f) => [f.id, f]));
    const squads = new Map();
    const perSide = new Map();
    for (const s of d.squads) {
      const index = perSide.get(s.side) || 0;
      perSide.set(s.side, index + 1);
      const palette = SIDE_PALETTES[Math.min(Math.max(s.side, 0), SIDE_PALETTES.length - 1)];
      squads.set(s.id, { ...s, color: palette[index % palette.length], size: 0 });
    }
    for (const f of d.fighters) {
      const squad = squads.get(f.squad);
      if (squad) squad.size++; // the starting strength: totals don't shrink when dead fighters are compacted away
    }

    // The battle's result, if the file has one (M3): the tick a BattleEnded event came and the winning side.
    let ended = null;
    for (const f of d.frames) {
      const e = (f.events || []).find((ev) => ev.type === 'BattleEnded');
      if (e) { ended = { tick: f.tick, winner: e.winner }; break; }
    }

    data = d;
    data.ended = ended;
    fighterInfo = fighters;
    squadInfo = squads;
    rowCache = { index: -1, rows: null };
    hoverId = null;
    lastTime = null;

    $('title').textContent = `${d.scenario} — ${sourceName}`;
    document.title = `${d.scenario} · Knez replay`;
    $('seed').textContent = String(d.seed);
    $('scrub').max = String(d.frames.length - 1);
    const faulted = $('faulted');
    faulted.hidden = !d.faulted;
    faulted.textContent = d.faulted ? `Faulted in tick ${d.faulted.tick}: ${d.faulted.error}` : '';

    const startTick = Number(params.get('tick'));
    t = Number.isFinite(startTick) && params.has('tick') ? indexOfTick(startTick) : 0;
    playing = params.get('play') === '1';
    view.fitted = true;
    fit();
    setMessage('');
    renderSquadList();
    syncControls();
    render();
  }

  async function loadUrl(url) {
    setMessage(`Loading ${url}…`);
    try {
      const response = await fetch(url);
      if (!response.ok) throw new Error(`HTTP ${response.status} for ${url}`);
      load(await response.json(), url);
    } catch (err) {
      setMessage(`Could not load ${url}: ${err.message}`, true);
    }
  }

  function loadFile(file) {
    file.text()
      .then((text) => load(JSON.parse(text), file.name))
      .catch((err) => setMessage(`Could not read ${file.name}: ${err.message}`, true));
  }

  function indexOfTick(tick) {
    const frames = data.frames;
    for (let i = 0; i < frames.length; i++) if (frames[i].tick >= tick) return i;
    return frames.length - 1;
  }

  // ---------- frame sampling ----------

  function rows(i) {
    if (rowCache.index !== i) {
      const frame = data.frames[i];
      rowCache = {
        index: i,
        rows: {
          fighters: new Map(frame.fighters.map((r) => [r[0], r])),
          squads: new Map(frame.squads.map((r) => [r[0], r])),
        },
      };
    }
    return rowCache.rows;
  }

  function lerpAngle(a, b, f) {
    const delta = ((b - a + 540) % 360) - 180; // shortest way round
    return (((a + delta * f) % 360) + 360) % 360; // keep 0..360 for display
  }

  /** Rows of frame floor(t), moved toward frame floor(t)+1 by the fractional part. */
  function sample(kind, time) {
    const i = Math.floor(time);
    const f = time - i;
    const a = data.frames[i][kind];
    if (f === 0 || i + 1 >= data.frames.length) return a;
    const next = rows(i + 1)[kind];
    return a.map((r) => {
      const n = next.get(r[0]);
      if (!n) return r;
      const out = r.slice();
      out[1] = r[1] + (n[1] - r[1]) * f;
      out[2] = r[2] + (n[2] - r[2]) * f;
      out[3] = lerpAngle(r[3], n[3], f);
      return out;
    });
  }

  // ---------- view transform ----------

  function fit() {
    if (!data) return;
    const w = canvas.clientWidth;
    const h = canvas.clientHeight;
    const pad = 36;
    const { width, height } = data.map;
    view.scale = Math.max(0.5, Math.min((w - 2 * pad) / width, (h - 2 * pad) / height));
    view.ox = (w - width * view.scale) / 2;
    view.oy = (h + height * view.scale) / 2;
    dirty = true;
  }

  const sx = (x) => view.ox + x * view.scale;
  const sy = (y) => view.oy - y * view.scale;
  const wx = (px) => (px - view.ox) / view.scale;
  const wy = (py) => (view.oy - py) / view.scale;

  // ---------- drawing ----------

  function resizeCanvas() {
    const dpr = window.devicePixelRatio || 1;
    const w = canvas.clientWidth;
    const h = canvas.clientHeight;
    if (canvas.width !== Math.round(w * dpr) || canvas.height !== Math.round(h * dpr)) {
      canvas.width = Math.round(w * dpr);
      canvas.height = Math.round(h * dpr);
      if (view.fitted) fit();
    }
    ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
  }

  function draw() {
    resizeCanvas();
    ctx.clearRect(0, 0, canvas.clientWidth, canvas.clientHeight);
    if (!data) return;

    drawMap();
    const squads = sample('squads', t);
    const fighters = sample('fighters', t);
    if (show.anchors) {
      drawOrders(squads, routesAt(t)); // sampled rows: the anchor is interpolated, the order columns come from the frame
      drawAnchors(squads);
    }
    drawFighters(fighters);
    if (show.labels) drawLabels(squads);
  }

  function drawMap() {
    const { width, height } = data.map;
    ctx.fillStyle = '#ece4d0';
    ctx.fillRect(sx(0), sy(height), width * view.scale, height * view.scale);

    if (show.grid) {
      const minor = view.scale >= 6;
      for (let x = 0; x <= width + 1e-9; x += 1) {
        const major = Math.abs(x % 5) < 1e-9;
        if (!major && !minor) continue;
        line(sx(x), sy(0), sx(x), sy(height), major ? 'rgba(80,60,20,0.18)' : 'rgba(80,60,20,0.07)');
      }
      for (let y = 0; y <= height + 1e-9; y += 1) {
        const major = Math.abs(y % 5) < 1e-9;
        if (!major && !minor) continue;
        line(sx(0), sy(y), sx(width), sy(y), major ? 'rgba(80,60,20,0.18)' : 'rgba(80,60,20,0.07)');
      }
      ctx.fillStyle = '#6a6f76';
      ctx.font = '11px system-ui, sans-serif';
      ctx.textAlign = 'center';
      ctx.textBaseline = 'top';
      for (let x = 0; x <= width + 1e-9; x += 10) ctx.fillText(`${x}`, sx(x), sy(0) + 4);
      ctx.textAlign = 'right';
      ctx.textBaseline = 'middle';
      for (let y = 0; y <= height + 1e-9; y += 10) ctx.fillText(`${y}`, sx(0) - 5, sy(y));
    }

    // Walls: [minX, minY, maxX, maxY, kind] in metres (absent in older files; kind since M3 step 11, else an obstacle).
    // Houses in timber brown, obstacles (rocks, fences, palisades) in stone grey.
    for (const [x0, y0, x1, y1, kind] of data.map.walls || []) {
      const house = kind === 'house';
      ctx.fillStyle = house ? '#8a5a2b' : '#6f7378';
      ctx.fillRect(sx(x0), sy(y1), (x1 - x0) * view.scale, (y1 - y0) * view.scale);
      ctx.strokeStyle = house ? '#4e3218' : '#45484c';
      ctx.lineWidth = 1;
      ctx.strokeRect(sx(x0), sy(y1), (x1 - x0) * view.scale, (y1 - y0) * view.scale);
    }

    ctx.strokeStyle = '#8a7f66';
    ctx.lineWidth = 1.5;
    ctx.strokeRect(sx(0), sy(height), width * view.scale, height * view.scale);

    // Compass: north is up.
    ctx.fillStyle = '#6a6f76';
    ctx.font = '600 11px system-ui, sans-serif';
    ctx.textAlign = 'left';
    ctx.textBaseline = 'bottom';
    ctx.fillText('N ↑', sx(width) + 6, sy(height) + 12);
  }

  function line(x1, y1, x2, y2, color) {
    ctx.strokeStyle = color;
    ctx.lineWidth = 1;
    ctx.beginPath();
    ctx.moveTo(x1, y1);
    ctx.lineTo(x2, y2);
    ctx.stroke();
  }

  /** Planned routes of the frame at or before `time` (files from before step 6 have none): id → [x, y, x, y, …]. */
  function routesAt(time) {
    const frame = data.frames[Math.min(Math.floor(time), data.frames.length - 1)];
    const routes = new Map();
    for (const [id, ...coords] of frame.paths || []) routes.set(id, coords);
    return routes;
  }

  /**
   * Move orders: a dashed line from the anchor along the planned route (straight when there is none yet) to the
   * target, a ring there and a tick for the arrival facing.
   */
  function drawOrders(squads, routes) {
    for (const [id, x, y, , ordered, tx, ty, tf] of squads) {
      const info = squadInfo.get(id);
      if (!info || ordered !== 1) continue;
      const px = sx(tx);
      const py = sy(ty);
      ctx.strokeStyle = info.color;
      ctx.globalAlpha = 0.7;
      ctx.lineWidth = 1.5;
      ctx.setLineDash([2, 4]);
      ctx.beginPath();
      ctx.moveTo(sx(x), sy(y));
      const route = routes.get(id) || [];
      for (let k = 0; k + 1 < route.length; k += 2) ctx.lineTo(sx(route[k]), sy(route[k + 1]));
      ctx.lineTo(px, py);
      ctx.stroke();
      ctx.setLineDash([]);
      const ring = Math.max(5, 0.3 * view.scale);
      ctx.beginPath();
      ctx.arc(px, py, ring, 0, Math.PI * 2);
      ctx.stroke();
      const rad = (tf * Math.PI) / 180;
      ctx.beginPath();
      ctx.moveTo(px, py);
      ctx.lineTo(px + Math.cos(rad) * ring * 2.2, py - Math.sin(rad) * ring * 2.2);
      ctx.lineWidth = 2;
      ctx.stroke();
      ctx.globalAlpha = 1;
    }
  }

  function drawAnchors(squads) {
    for (const [id, x, y, facing] of squads) {
      const info = squadInfo.get(id);
      if (!info) continue;
      const rad = (facing * Math.PI) / 180;
      const px = sx(x);
      const py = sy(y);
      const len = 1.4 * view.scale;
      const tipX = px + Math.cos(rad) * len;
      const tipY = py - Math.sin(rad) * len;

      ctx.strokeStyle = info.color;
      ctx.fillStyle = info.color;
      ctx.lineWidth = 2;
      ctx.setLineDash([4, 3]);
      ctx.beginPath();
      ctx.moveTo(px, py);
      ctx.lineTo(tipX, tipY);
      ctx.stroke();
      ctx.setLineDash([]);

      // Arrow head.
      const head = Math.max(5, 0.35 * view.scale);
      ctx.beginPath();
      ctx.moveTo(tipX, tipY);
      ctx.lineTo(tipX - Math.cos(rad - 0.5) * head, tipY + Math.sin(rad - 0.5) * head);
      ctx.lineTo(tipX - Math.cos(rad + 0.5) * head, tipY + Math.sin(rad + 0.5) * head);
      ctx.closePath();
      ctx.fill();

      // Anchor marker: a diamond at the centre of the front rank.
      const d = Math.max(3.5, 0.18 * view.scale);
      ctx.beginPath();
      ctx.moveTo(px, py - d);
      ctx.lineTo(px + d, py);
      ctx.lineTo(px, py + d);
      ctx.lineTo(px - d, py);
      ctx.closePath();
      ctx.fillStyle = '#fff';
      ctx.fill();
      ctx.lineWidth = 1.5;
      ctx.stroke();
    }
  }

  /**
   * Allies passing through each other's ranks (allowed, decided 2026-11-21): same side, different squads, one holding his
   * ground (fighter row column 5) and the other passing through (column 6, the sim's own flag). Older files lack them.
   */
  function passThrough(a, b) {
    const ia = fighterInfo.get(a[0]) || {};
    const ib = fighterInfo.get(b[0]) || {};
    if (a[6] === undefined || b[6] === undefined || !ia.squad || !ib.squad) return false;
    if (ia.side !== ib.side || ia.squad === ib.squad) return false;
    return (a[5] === 1 && b[6] === 1) || (b[5] === 1 && a[6] === 1);
  }

  /** Ids of living fighters pressed more than 5 cm into another living one (same rule as the sim's metric). */
  function overlapping(fighters) {
    const hit = new Set();
    const living = fighters.filter((r) => r[4]);
    for (let i = 0; i < living.length; i++) {
      const a = living[i];
      const ra = (fighterInfo.get(a[0]) || { radius: 0.3 }).radius;
      for (let j = i + 1; j < living.length; j++) {
        const b = living[j];
        const rb = (fighterInfo.get(b[0]) || { radius: 0.3 }).radius;
        if (passThrough(a, b)) continue;
        if (Math.hypot(a[1] - b[1], a[2] - b[2]) < ra + rb - OVERLAP_TOLERANCE) {
          hit.add(a[0]);
          hit.add(b[0]);
        }
      }
    }
    return hit;
  }

  function drawFighters(fighters) {
    const pressed = show.overlaps ? overlapping(fighters) : new Set();
    // The dead first, so they never cover the living.
    const ordered = fighters.filter((r) => !r[4]).concat(fighters.filter((r) => r[4]));
    for (const row of ordered) {
      const [id, x, y, facing, alive] = row;
      // M3 rows end with morale (9), its state (10) and the wound (11); older files have none.
      const morale = row.length > 11 ? row[9] : null;
      const state = row.length > 11 ? row[10] : 'steady';
      const wound = row.length > 11 ? row[11] : 'none';
      if (state === 'fled') continue; // he left the field
      const info = fighterInfo.get(id);
      // A Retainer (M3 step 10) moves between squads: colour him by the squad he stands in now (row[7]), if any.
      const squad = info && info.retainer ? (row.length > 8 && row[7] > 0 ? squadInfo.get(row[7]) : null) : info && squadInfo.get(info.squad);
      const routing = alive && state === 'routing';
      const color = alive ? (routing ? '#ffffff' : squad ? squad.color : '#555') : DEAD;
      const r = Math.max(2, (info ? info.radius : 0.3) * view.scale);
      const px = sx(x);
      const py = sy(y);
      const rad = (facing * Math.PI) / 180;

      ctx.globalAlpha = alive ? 1 : 0.55;
      ctx.beginPath();
      ctx.arc(px, py, r, 0, Math.PI * 2);
      ctx.fillStyle = color;
      ctx.fill();
      ctx.lineWidth = routing ? 2.5 : 1;
      ctx.strokeStyle = routing ? (squad ? squad.color : '#555') : 'rgba(0,0,0,0.55)';
      ctx.stroke();
      if (info && info.retainer && alive) {
        // A Retainer: a gold ring round him.
        ctx.beginPath();
        ctx.arc(px, py, r + 2.5, 0, Math.PI * 2);
        ctx.strokeStyle = '#d4a017';
        ctx.lineWidth = 2;
        ctx.stroke();
      }

      if (alive && state === 'rallying') {
        ctx.setLineDash([3, 3]);
        ctx.beginPath();
        ctx.arc(px, py, r + 2, 0, Math.PI * 2);
        ctx.strokeStyle = '#ffffff';
        ctx.lineWidth = 1.5;
        ctx.stroke();
        ctx.setLineDash([]);
      }

      if (alive && (wound === 'light' || wound === 'heavy')) {
        ctx.beginPath();
        ctx.arc(px, py, r * (wound === 'heavy' ? 0.45 : 0.25), 0, Math.PI * 2);
        ctx.fillStyle = '#c62828';
        ctx.fill();
      }

      if (alive && show.morale && morale !== null) {
        const w = r * 2;
        const top = py + r + 2;
        ctx.fillStyle = 'rgba(0,0,0,0.25)';
        ctx.fillRect(px - r, top, w, 3);
        ctx.fillStyle = morale >= 600 ? '#2e7d32' : morale >= 400 ? '#f9a825' : morale >= 200 ? '#ef6c00' : '#c62828';
        ctx.fillRect(px - r, top, (w * Math.max(0, Math.min(1000, morale))) / 1000, 3);
      }

      if (alive) {
        ctx.beginPath();
        ctx.moveTo(px, py);
        ctx.lineTo(px + Math.cos(rad) * r * 1.6, py - Math.sin(rad) * r * 1.6);
        ctx.lineWidth = Math.max(1.5, r * 0.22);
        ctx.strokeStyle = '#1f2328';
        ctx.stroke();
      } else {
        ctx.beginPath();
        ctx.moveTo(px - r * 0.6, py - r * 0.6);
        ctx.lineTo(px + r * 0.6, py + r * 0.6);
        ctx.moveTo(px + r * 0.6, py - r * 0.6);
        ctx.lineTo(px - r * 0.6, py + r * 0.6);
        ctx.strokeStyle = '#444';
        ctx.lineWidth = 1.5;
        ctx.stroke();
      }
      ctx.globalAlpha = 1;

      if (pressed.has(id)) {
        ctx.beginPath();
        ctx.arc(px, py, r + 2.5, 0, Math.PI * 2);
        ctx.strokeStyle = '#d0021b';
        ctx.lineWidth = 2;
        ctx.stroke();
      }

      if (id === hoverId) {
        ctx.beginPath();
        ctx.arc(px, py, r + 4, 0, Math.PI * 2);
        ctx.strokeStyle = '#1f2328';
        ctx.lineWidth = 2;
        ctx.stroke();
      }

      if (show.ids && view.scale >= 16) {
        ctx.fillStyle = '#fff';
        ctx.font = `600 ${Math.max(9, Math.min(13, r * 0.9))}px system-ui, sans-serif`;
        ctx.textAlign = 'center';
        ctx.textBaseline = 'middle';
        ctx.fillText(String(id), px, py);
      }
    }
  }

  function drawLabels(squads) {
    ctx.font = '600 12px system-ui, sans-serif';
    ctx.textAlign = 'center';
    ctx.textBaseline = 'bottom';
    for (const [id, x, y, facing] of squads) {
      const info = squadInfo.get(id);
      if (!info) continue;
      const rad = (facing * Math.PI) / 180;
      // In front of the squad, past the facing arrow.
      const px = sx(x + Math.cos(rad) * 2.4);
      const py = sy(y + Math.sin(rad) * 2.4) + 6;
      const text = info.name;
      const w = ctx.measureText(text).width;
      ctx.fillStyle = 'rgba(251,250,246,0.85)';
      ctx.fillRect(px - w / 2 - 4, py - 16, w + 8, 17);
      ctx.fillStyle = info.color;
      ctx.fillText(text, px, py);
    }
  }

  // ---------- panels ----------

  function renderSquadList() {
    const list = $('squads');
    list.textContent = '';
    for (const s of squadInfo.values()) {
      const li = document.createElement('li');
      const swatch = document.createElement('span');
      swatch.className = 'swatch';
      swatch.style.background = s.color;
      const name = document.createElement('span');
      name.textContent = s.name;
      const meta = document.createElement('span');
      meta.className = 'meta';
      meta.dataset.squad = String(s.id);
      li.append(swatch, name, meta);
      list.append(li);
    }
  }

  function updatePanels() {
    if (!data) return;
    const i = Math.min(Math.round(t), data.frames.length - 1);
    const frame = data.frames[i];
    const lastTick = data.frames[data.frames.length - 1].tick;
    const tps = data.ticksPerSecond;
    $('tick').textContent = `${frame.tick} / ${lastTick}`;
    $('time').textContent = `${(frame.tick / tps).toFixed(2)} s / ${(lastTick / tps).toFixed(2)} s`;
    $('hash').textContent = frame.hash;

    const alive = new Map();
    const moraleSum = new Map();
    const running = new Map();
    for (const row of frame.fighters) {
      const [id, , , , isAlive] = row;
      const info = fighterInfo.get(id);
      if (!info || !isAlive) continue;
      alive.set(info.squad, (alive.get(info.squad) || 0) + 1);
      if (row.length > 11) {
        moraleSum.set(info.squad, (moraleSum.get(info.squad) || 0) + row[9]);
        if (row[10] === 'routing') running.set(info.squad, (running.get(info.squad) || 0) + 1);
      }
    }
    const result = $('result');
    if (result) {
      const e = data.ended;
      result.textContent = !e ? 'undecided' : frame.tick < e.tick ? `decided at ${(e.tick / tps).toFixed(1)} s`
        : e.winner < 0 ? 'nobody holds the field' : `side ${e.winner} holds the field (${(e.tick / tps).toFixed(1)} s)`;
    }
    const current = new Map(frame.squads.map((r) => [r[0], r])); // rows gain [formation, files] in step 7 files
    for (const meta of document.querySelectorAll('#squads .meta')) {
      const id = Number(meta.dataset.squad);
      const s = squadInfo.get(id);
      const row = current.get(id);
      const formation = row && row.length > 8 ? row[8] : s.formation;
      const files = row && row.length > 9 && row[9] > 0 ? ` (${row[9]} files for a gap)` : '';
      const n = alive.get(id) || 0;
      const morale = moraleSum.has(id) && n > 0 ? ` · morale ${Math.round(moraleSum.get(id) / n)}` : '';
      const run = running.get(id) ? ` · ${running.get(id)} running` : '';
      const broken = row && row.length > 10 && row[10] === 1 ? ' · BROKEN' : '';
      meta.textContent = `side ${s.side} · ${formation}${files} · ${n}/${s.size}${morale}${run}${broken}`;
    }

    const events = $('events');
    events.textContent = '';
    if (frame.events.length === 0) {
      const li = document.createElement('li');
      li.className = 'muted';
      li.textContent = 'none';
      events.append(li);
    }
    for (const e of frame.events) {
      const li = document.createElement('li');
      const details = Object.entries(e).filter(([k]) => k !== 'type').map(([k, v]) => `${k} ${v}`).join(', ');
      li.textContent = details ? `${e.type}: ${details}` : e.type;
      events.append(li);
    }

    updateHoverPanel();
  }

  function updateHoverPanel() {
    const el = $('hover');
    if (!data || hoverId === null) {
      el.textContent = 'Point at a fighter.';
      el.classList.add('muted');
      return;
    }
    const info = fighterInfo.get(hoverId);
    const row = sample('fighters', t).find((r) => r[0] === hoverId);
    if (!info || !row) {
      el.textContent = 'Point at a fighter.';
      el.classList.add('muted');
      return;
    }
    const squad = squadInfo.get(info.squad);
    el.classList.remove('muted');
    el.innerHTML = '';
    const lines = [
      // From M3 step 9 the fighter table names his body (def), experience and kit; older files have only def (his type).
      info.retainer ? `Retainer ${info.retainer} (${info.trait}), #${hoverId}, ${info.experience}, ${info.kit}, side ${info.side}`
        : info.kit ? `Fighter #${hoverId}, ${info.experience}, ${info.kit} (${info.def}), side ${info.side}`
        : `Fighter #${hoverId} (${info.def}), side ${info.side}`,
      // Rows from step 7 on carry the current squad and slot; older files only have the starting ones.
      row.length > 8
        ? (row[7] > 0 ? `Squad ${squadInfo.get(row[7]).name} (#${row[7]}), slot ${row[8]}` : `Fell from ${squad ? squad.name : 'no squad'}`)
        : squad ? `Squad ${squad.name} (#${squad.id}), slot ${info.slot}` : 'No squad',
      `Position ${row[1].toFixed(3)}, ${row[2].toFixed(3)} m`,
      `Facing ${row[3].toFixed(2)}°, radius ${info.radius} m`,
      row[4] ? (row[5] === 1 ? 'In the fight, holding his ground' : row[6] === 1 ? 'In the fight, passing through allied ranks' : 'In the fight')
        : row.length > 11 && row[10] === 'fled' ? 'Fled the field' : 'Fallen',
    ];
    if (row.length > 11) lines.push(`Morale ${row[9]} (${row[10]}), wound: ${row[11]}`);
    for (const text of lines) {
      const div = document.createElement('div');
      div.textContent = text;
      el.append(div);
    }
  }

  function syncControls() {
    if (!data) return;
    $('scrub').value = String(Math.round(t));
    $('play').textContent = playing ? '⏸' : '▶';
    $('speed').value = String(speed);
  }

  // ---------- playback ----------

  function seek(index) {
    if (!data) return;
    t = Math.max(0, Math.min(data.frames.length - 1, index));
    dirty = true;
    syncControls();
  }

  function setPlaying(on) {
    if (!data) return;
    if (on && t >= data.frames.length - 1) t = 0; // play from the start again
    if (!on) t = Math.round(t); // a paused view always shows a real tick, matching the panel and the hash
    playing = on;
    lastTime = null;
    dirty = true;
    syncControls();
  }

  // Playback assumes one frame per tick (the format guarantees it): the playhead moves in frame indices.
  function tickLoop(now) {
    requestAnimationFrame(tickLoop); // first, so one bad frame can't stop the viewer for good
    if (playing && data) {
      if (lastTime !== null) {
        const elapsed = Math.min(now - lastTime, 250); // a hidden tab pauses rAF: don't jump on return
        t += (elapsed / 1000) * data.ticksPerSecond * speed;
        if (t >= data.frames.length - 1) {
          t = data.frames.length - 1;
          playing = false;
        }
        syncControls();
      }
      lastTime = now;
      dirty = true;
    }
    if (dirty) render();
  }

  /** Draws the canvas and fills the panels now. Also called outside the animation loop: a hidden browser pane runs no
   * animation frames, and agents read the panels right after loading or seeking. */
  function render() {
    dirty = false;
    try {
      draw();
      updatePanels();
    } catch (err) {
      playing = false;
      setMessage(`Viewer error: ${err.message}`, true);
      console.error(err);
    }
  }

  // ---------- input ----------

  $('file').addEventListener('change', (e) => {
    if (e.target.files[0]) loadFile(e.target.files[0]);
  });

  stage.addEventListener('dragover', (e) => {
    e.preventDefault();
    stage.classList.add('dragover');
  });
  stage.addEventListener('dragleave', () => stage.classList.remove('dragover'));
  stage.addEventListener('drop', (e) => {
    e.preventDefault();
    stage.classList.remove('dragover');
    if (e.dataTransfer.files[0]) loadFile(e.dataTransfer.files[0]);
  });

  // Buttons give focus back after a click, so Space only reaches the document handler (no double toggle).
  for (const button of document.querySelectorAll('button')) button.addEventListener('click', () => button.blur());

  $('first').addEventListener('click', () => { setPlaying(false); seek(0); });
  $('back').addEventListener('click', () => { setPlaying(false); seek(Math.ceil(t) - 1); });
  $('forward').addEventListener('click', () => { setPlaying(false); seek(Math.floor(t) + 1); });
  $('last').addEventListener('click', () => { setPlaying(false); seek(Infinity); });
  $('play').addEventListener('click', () => setPlaying(!playing));
  $('scrub').addEventListener('input', (e) => { setPlaying(false); seek(Number(e.target.value)); });
  $('speed').addEventListener('change', (e) => { speed = Number(e.target.value); });
  $('fit').addEventListener('click', () => { view.fitted = true; fit(); });

  for (const key of ['labels', 'anchors', 'grid', 'ids', 'overlaps', 'morale']) {
    const box = $(key);
    if (params.has(key)) box.checked = params.get(key) !== '0';
    show[key] = box.checked;
    box.addEventListener('change', () => { show[key] = box.checked; dirty = true; });
  }

  const speedParam = Number(params.get('speed'));
  if ([0.25, 0.5, 1, 2, 4].includes(speedParam)) speed = speedParam;

  document.addEventListener('keydown', (e) => {
    if (!data || e.target.tagName === 'SELECT') return;
    const step = e.shiftKey ? 20 : 1;
    switch (e.key) {
      case ' ': setPlaying(!playing); break;
      case 'ArrowLeft': setPlaying(false); seek(Math.ceil(t) - step); break;
      case 'ArrowRight': setPlaying(false); seek(Math.floor(t) + step); break;
      case 'Home': setPlaying(false); seek(0); break;
      case 'End': setPlaying(false); seek(Infinity); break;
      case 'f': case 'F': view.fitted = true; fit(); break;
      default: return;
    }
    e.preventDefault();
  });

  canvas.addEventListener('wheel', (e) => {
    if (!data) return;
    e.preventDefault();
    const rect = canvas.getBoundingClientRect();
    const px = e.clientX - rect.left;
    const py = e.clientY - rect.top;
    const worldX = wx(px);
    const worldY = wy(py);
    const factor = Math.exp(-e.deltaY * 0.0015);
    view.scale = Math.max(0.5, Math.min(400, view.scale * factor));
    view.ox = px - worldX * view.scale; // keep the point under the cursor fixed
    view.oy = py + worldY * view.scale;
    view.fitted = false;
    dirty = true;
  }, { passive: false });

  canvas.addEventListener('mousedown', (e) => {
    drag = { x: e.clientX, y: e.clientY, ox: view.ox, oy: view.oy };
    canvas.classList.add('dragging');
  });
  window.addEventListener('mouseup', () => {
    drag = null;
    canvas.classList.remove('dragging');
  });
  window.addEventListener('mousemove', (e) => {
    if (drag) {
      view.ox = drag.ox + (e.clientX - drag.x);
      view.oy = drag.oy + (e.clientY - drag.y);
      view.fitted = false;
      dirty = true;
    }
  });
  canvas.addEventListener('dblclick', () => { view.fitted = true; fit(); });

  canvas.addEventListener('mousemove', (e) => {
    if (!data || drag) return;
    const rect = canvas.getBoundingClientRect();
    const x = wx(e.clientX - rect.left);
    const y = wy(e.clientY - rect.top);
    let best = null;
    let bestDistance = Infinity;
    for (const [id, fx, fy] of sample('fighters', t)) {
      const info = fighterInfo.get(id);
      const reach = Math.max((info ? info.radius : 0.3) * 1.3, 6 / view.scale);
      const d = Math.hypot(fx - x, fy - y);
      if (d <= reach && d < bestDistance) {
        best = id;
        bestDistance = d;
      }
    }
    if (best !== hoverId) {
      hoverId = best;
      dirty = true;
    }
  });
  canvas.addEventListener('mouseleave', () => {
    if (hoverId !== null) {
      hoverId = null;
      dirty = true;
    }
  });

  new ResizeObserver(() => { dirty = true; }).observe(stage);

  // Exposed for agents and debugging in the browser console: current tick and a seek function.
  window.knezViewer = {
    get tick() { return data ? data.frames[Math.min(Math.round(t), data.frames.length - 1)].tick : null; },
    seekTick(tick) { if (data) { setPlaying(false); seek(indexOfTick(tick)); render(); } },
    get loaded() { return data !== null; },
    get error() { return loadError; }, // set when loading failed: stop waiting for `loaded`
  };

  const framesUrl = params.get('frames');
  if (framesUrl) loadUrl(framesUrl);
  requestAnimationFrame(tickLoop);
})();
