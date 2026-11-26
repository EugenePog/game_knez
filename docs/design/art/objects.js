/*
 * Game_knez — постройки и предметы карты удела (эскиз, не игровой ассет).
 *
 * Каждый предмет рисуется двумя видами:
 *   view(id) — вид 3/4 (изометрия; эталон для 3D-модели и карточка в интерфейсе);
 *   plan(id) — вид сверху в метрах (1 м = 10 единиц) для 2D-карты фазы 1; пунктир — прямоугольник стены
 *              для симуляции (MapDef.walls: kind "house" или "obstacle").
 * Размеры — в метрах, как в MapDef. Работает в браузере (window.Objects) и в node (module.exports).
 */
(function (root) {
  'use strict';

  var INK = '#2A2420';
  var C = {
    log: '#8C6A48', logOld: '#5E4A38', plank: '#9A7650', post: '#7A5B3E', haft: '#9C7446',
    thatch: '#C2A15E', turf: '#748449', boards: '#7A644B', clay: '#B98A63', daub: '#BFA88A',
    stone: '#9A968C', stoneDark: '#6F6B63', ore: '#8E5A3A', moss: '#6F8048',
    water: '#5F84A0', shallow: '#86A6B4', grass: '#7B8760', grassDark: '#66724F', earth: '#8A7353',
    hay: '#C9B26A', rye: '#B89A55', barley: '#CDB878',
    pine: '#3F5A3A', spruce: '#2F4A35', birch: '#7E9A4E', birchBark: '#E6E2D6', pineBark: '#9A5A3A',
    iron: '#767C83', ironDark: '#474C52', fire: '#E08A2E', fireHot: '#F2C14E', smoke: '#D9D6CE',
    dark: '#2E241B', fish: '#B9BEC0', wool: '#E3D7BD', leather: '#8A5A35'
  };
  var ROOF = {
    thatch: { lit: '#C7A762', dim: '#A3864A', line: '#8A6F37' },
    turf:   { lit: '#7A8B4C', dim: '#5E6D3B', line: '#4E5C32' },
    boards: { lit: '#7E6850', dim: '#5E4C39', line: '#45372A' }
  };

  function rgb(h) { h = h.replace('#', ''); return [parseInt(h.slice(0, 2), 16), parseInt(h.slice(2, 4), 16), parseInt(h.slice(4, 6), 16)]; }
  function hex(c) { return '#' + c.map(function (v) { v = Math.max(0, Math.min(255, Math.round(v))); return (v < 16 ? '0' : '') + v.toString(16); }).join(''); }
  function shade(h, k) {
    var c = rgb(h);
    if (k < 0) return hex(c.map(function (v) { return v * (1 + k); }));
    return hex(c.map(function (v) { return v + (255 - v) * k; }));
  }
  function f1(v) { return (Math.round(v * 10) / 10).toString(); }

  // ---------------------------------------------------------------- изометрия
  function Iso(k) { this.k = k || 14; this.o = ''; this.b = [1e9, 1e9, -1e9, -1e9]; }
  Iso.prototype.xy = function (x, y, z) {
    var k = this.k, sx = (x - y) * k * 0.866, sy = (x + y) * k * 0.5 - (z || 0) * k;
    this.track(sx, sy);
    return [sx, sy];
  };
  Iso.prototype.track = function (sx, sy) {
    var b = this.b;
    if (sx < b[0]) b[0] = sx; if (sy < b[1]) b[1] = sy; if (sx > b[2]) b[2] = sx; if (sy > b[3]) b[3] = sy;
  };
  Iso.prototype.p = function (x, y, z) { var q = this.xy(x, y, z); return f1(q[0]) + ',' + f1(q[1]); };
  Iso.prototype.poly = function (pts, fill, sw, extra) {
    var self = this;
    var d = 'M' + pts.map(function (q) { return self.p(q[0], q[1], q[2]); }).join(' L') + ' Z';
    this.o += '<path d="' + d + '" fill="' + fill + '"' +
      (sw === 0 ? '' : ' stroke="' + INK + '" stroke-width="' + (sw || 1.1) + '" stroke-linejoin="round"') + (extra || '') + '/>';
    return this;
  };
  Iso.prototype.seg = function (list, stroke, w, extra) {
    var self = this, d = '';
    list.forEach(function (s) { d += 'M' + self.p(s[0][0], s[0][1], s[0][2]) + ' L' + self.p(s[1][0], s[1][1], s[1][2]) + ' '; });
    extra = extra || '';
    var cap = extra.indexOf('stroke-linecap') >= 0 ? '' : ' stroke-linecap="round"';
    if (d) this.o += '<path d="' + d + '" fill="none" stroke="' + stroke + '" stroke-width="' + f1(w) + '"' + cap + extra + '/>';
    return this;
  };
  // толстая «палка» с контуром
  Iso.prototype.stick = function (a, b, color, w) {
    this.seg([[a, b]], INK, w + 2.2).seg([[a, b]], color, w);
    return this;
  };
  Iso.prototype.raw = function (s) { this.o += s; return this; };
  // круг в горизонтальной плоскости
  Iso.prototype.disc = function (x, y, z, r, fill, sw, extra) {
    var c = this.xy(x, y, z), rx = r * this.k * 1.2247, ry = r * this.k * 0.7071;
    this.track(c[0] - rx, c[1] - ry); this.track(c[0] + rx, c[1] + ry);
    this.o += '<ellipse cx="' + f1(c[0]) + '" cy="' + f1(c[1]) + '" rx="' + f1(rx) + '" ry="' + f1(ry) + '" fill="' + fill + '"' +
      (sw === 0 ? '' : ' stroke="' + INK + '" stroke-width="' + (sw || 1.1) + '"') + (extra || '') + '/>';
    return this;
  };
  // вертикальный цилиндр (усечённый конус, если r1 задан)
  Iso.prototype.cyl = function (x, y, z0, z1, r, fill, r1, sw) {
    var k = this.k, b = this.xy(x, y, z0), t = this.xy(x, y, z1);
    r1 = r1 == null ? r : r1;
    var rx0 = r * k * 1.2247, ry0 = r * k * 0.7071, rx1 = r1 * k * 1.2247, ry1 = r1 * k * 0.7071;
    this.track(b[0] - rx0, b[1] + ry0); this.track(b[0] + rx0, b[1] + ry0); this.track(t[0] - rx1, t[1] - ry1); this.track(t[0] + rx1, t[1]);
    var d = 'M' + f1(t[0] - rx1) + ',' + f1(t[1]) + ' L' + f1(b[0] - rx0) + ',' + f1(b[1]) +
      ' A' + f1(rx0) + ',' + f1(ry0) + ' 0 0 0 ' + f1(b[0] + rx0) + ',' + f1(b[1]) + ' L' + f1(t[0] + rx1) + ',' + f1(t[1]) + ' Z';
    this.o += '<path d="' + d + '" fill="' + fill + '" stroke="' + INK + '" stroke-width="' + (sw || 1.1) + '" stroke-linejoin="round"/>';
    // тень справа
    var ds = 'M' + f1(t[0] + rx1 * 0.25) + ',' + f1(t[1] + ry1 * 0.97) + ' L' + f1(b[0] + rx0 * 0.25) + ',' + f1(b[1] + ry0 * 0.97) +
      ' A' + f1(rx0) + ',' + f1(ry0) + ' 0 0 0 ' + f1(b[0] + rx0) + ',' + f1(b[1]) + ' L' + f1(t[0] + rx1) + ',' + f1(t[1]) + ' Z';
    this.o += '<path d="' + ds + '" fill="#1B140F" opacity="0.18"/>';
    this.o += '<ellipse cx="' + f1(t[0]) + '" cy="' + f1(t[1]) + '" rx="' + f1(rx1) + '" ry="' + f1(ry1) + '" fill="' + shade(fill, 0.15) +
      '" stroke="' + INK + '" stroke-width="' + (sw || 1.1) + '"/>';
    return this;
  };
  // конус (стог, ель, костёр)
  Iso.prototype.cone = function (x, y, z0, h, r, fill, sw) {
    var k = this.k, b = this.xy(x, y, z0), a = this.xy(x, y, z0 + h);
    var rx = r * k * 1.2247, ry = r * k * 0.7071;
    this.track(b[0] - rx, b[1] + ry); this.track(b[0] + rx, b[1] + ry);
    var d = 'M' + f1(a[0]) + ',' + f1(a[1]) + ' L' + f1(b[0] - rx) + ',' + f1(b[1]) + ' A' + f1(rx) + ',' + f1(ry) + ' 0 0 0 ' +
      f1(b[0] + rx) + ',' + f1(b[1]) + ' Z';
    this.o += '<path d="' + d + '" fill="' + fill + '" stroke="' + INK + '" stroke-width="' + (sw || 1.1) + '" stroke-linejoin="round"/>';
    var ds = 'M' + f1(a[0]) + ',' + f1(a[1]) + ' L' + f1(b[0] + rx * 0.2) + ',' + f1(b[1] + ry * 0.98) + ' A' + f1(rx) + ',' + f1(ry) + ' 0 0 0 ' +
      f1(b[0] + rx) + ',' + f1(b[1]) + ' Z';
    this.o += '<path d="' + ds + '" fill="#1B140F" opacity="0.18"/>';
    return this;
  };
  // неровный ком в экранных координатах вокруг точки (кроны, валуны)
  Iso.prototype.blob = function (x, y, z, rw, rh, fill, seed, sw, lumps, op) {
    var c = this.xy(x, y, z), R = rw * this.k, H = rh * this.k, n = lumps || 9, d = '', s = seed || 1;
    function rnd() { s = (s * 16807) % 2147483647; return s / 2147483647; }
    var pts = [];
    for (var i = 0; i < n; i++) {
      var a = (i / n) * Math.PI * 2, m = 0.86 + rnd() * 0.22;
      pts.push([c[0] + Math.cos(a) * R * m, c[1] + Math.sin(a) * H * m]);
    }
    this.track(c[0] - R * 1.1, c[1] - H * 1.1); this.track(c[0] + R * 1.1, c[1] + H * 1.1);
    for (var j = 0; j < n; j++) {
      var p0 = pts[j], p1 = pts[(j + 1) % n];
      var mx = (p0[0] + p1[0]) / 2, my = (p0[1] + p1[1]) / 2;
      var ox = mx - c[0], oy = my - c[1], l = Math.sqrt(ox * ox + oy * oy) || 1;
      var bx = mx + ox / l * R * 0.16, by = my + oy / l * H * 0.16;
      d += (j === 0 ? 'M' + f1(p0[0]) + ',' + f1(p0[1]) : '') + ' Q' + f1(bx) + ',' + f1(by) + ' ' + f1(p1[0]) + ',' + f1(p1[1]);
    }
    this.o += '<path d="' + d + ' Z" fill="' + fill + '"' + (op ? ' opacity="' + op + '"' : '') + (sw === 0 ? '' : ' stroke="' + INK + '" stroke-width="' + (sw || 1.1) + '"') + '/>';
    return this;
  };
  // тень предмета на земле
  Iso.prototype.castShadow = function (x, y, rw, rh, seed) { return this.blob(x, y, 0, rw, rh, '#1B140F', seed || 2, 0, 9, 0.2); };
  // крона из нескольких комков: [[dx, dy, z, rw, rh], ...]
  Iso.prototype.crown = function (x, y, parts, color, seed) {
    var self = this;
    parts.forEach(function (q, i) {
      var t = i / Math.max(1, parts.length - 1);
      self.blob(x + q[0], y + q[1], q[2], q[3], q[4], shade(color, -0.12 + t * 0.26), (seed || 3) + i * 3, 1.1, 10);
    });
    return this;
  };
  // валуны: [x, y, ширина, высота, seed]; сначала все тени, потом камни от дальних к ближним
  Iso.prototype.rocks = function (list, color, moss) {
    var self = this, sorted = list.slice().sort(function (p, q) { return (p[0] + p[1]) - (q[0] + q[1]); });
    sorted.forEach(function (b) { self.blob(b[0] + 0.45, b[1] + 0.35, 0.01, b[2] * 1.15, b[2] * 0.62, '#1B140F', b[4], 0, 9, 0.22); });
    sorted.forEach(function (b) {
      self.blob(b[0], b[1], b[3] * 0.5, b[2], b[3] * 0.62, shade(color, -0.22), b[4], 1.2);
      self.blob(b[0] - b[2] * 0.06, b[1] - b[2] * 0.06, b[3] * 0.62, b[2] * 0.86, b[3] * 0.5, color, b[4] + 4, 0);
      self.blob(b[0] - b[2] * 0.25, b[1] - b[2] * 0.25, b[3] * 0.85, b[2] * 0.42, b[3] * 0.22, shade(color, 0.22), b[4] + 1, 0);
      if (moss) self.blob(b[0] + b[2] * 0.3, b[1] + b[2] * 0.12, b[3] * 0.32, b[2] * 0.38, b[3] * 0.2, C.moss, b[4] + 2, 0);
    });
    return this;
  };
  // окружность в вертикальной плоскости y = const (колесо)
  Iso.prototype.wheel = function (cx, y, cz, r, fill) {
    var pts = [], spokes = [];
    for (var i = 0; i < 24; i++) { var a = i / 24 * Math.PI * 2; pts.push([cx + Math.cos(a) * r, y, cz + Math.sin(a) * r]); }
    this.poly(pts, fill, 1.4);
    for (var j = 0; j < 4; j++) { var b = j / 4 * Math.PI; spokes.push([[cx + Math.cos(b) * r * 0.9, y, cz + Math.sin(b) * r * 0.9], [cx - Math.cos(b) * r * 0.9, y, cz - Math.sin(b) * r * 0.9]]); }
    this.seg(spokes, shade(fill, -0.35), 1.6);
    this.disc(cx, y, cz, 0.0001, fill, 0);
    var hub = [];
    for (var q = 0; q < 12; q++) { var c = q / 12 * Math.PI * 2; hub.push([cx + Math.cos(c) * r * 0.18, y + 0.02, cz + Math.sin(c) * r * 0.18]); }
    this.poly(hub, shade(fill, -0.25), 1);
    return this;
  };
  Iso.prototype.svg = function (title, pad) {
    pad = pad == null ? 10 : pad;
    var b = this.b, w = b[2] - b[0] + 2 * pad, h = b[3] - b[1] + 2 * pad;
    return '<svg xmlns="http://www.w3.org/2000/svg" viewBox="' + f1(b[0] - pad) + ' ' + f1(b[1] - pad) + ' ' + f1(w) + ' ' + f1(h) +
      '" role="img"><title>' + title + '</title>' + this.o + '</svg>';
  };

  // площадка земли под предметом
  Iso.prototype.ground = function (x0, y0, x1, y1, fill) {
    this.poly([[x0, y0, 0], [x1, y0, 0], [x1, y1, 0], [x0, y1, 0]], fill || C.grass, 0, ' opacity="0.55"');
    return this;
  };
  // тень на земле (сдвиг на юго-восток экрана)
  Iso.prototype.shadow = function (x0, y0, x1, y1, h) {
    var s = (h || 2) * 0.45;
    this.poly([[x0 + s, y0, 0], [x1 + s, y0, 0], [x1 + s, y1 + s * 0.4, 0], [x0 + s, y1 + s * 0.4, 0]], '#1B140F', 0, ' opacity="0.16"');
    return this;
  };

  // стены: бревенчатые (logs) или из плах (planks)
  Iso.prototype.walls = function (x, y, z, w, d, h, kind, color) {
    var c = color || (kind === 'planks' ? C.plank : C.log);
    var L = shade(c, 0.1), R = shade(c, -0.14), dk = shade(c, -0.4);
    this.poly([[x, y + d, z], [x + w, y + d, z], [x + w, y + d, z + h], [x, y + d, z + h]], L);
    this.poly([[x + w, y, z], [x + w, y + d, z], [x + w, y + d, z + h], [x + w, y, z + h]], R);
    var segs = [], zz, xx;
    if (kind === 'planks') {
      for (xx = x + 0.4; xx < x + w - 0.1; xx += 0.4) segs.push([[xx, y + d, z], [xx, y + d, z + h]]);
      for (var yy = y + 0.4; yy < y + d - 0.1; yy += 0.4) segs.push([[x + w, yy, z], [x + w, yy, z + h]]);
      this.seg(segs, dk, 0.9);
      this.seg([[[x, y + d, z + h - 0.08], [x + w, y + d, z + h - 0.08]], [[x + w, y, z + h - 0.08], [x + w, y + d, z + h - 0.08]]], dk, 2);
    } else {
      for (zz = z + 0.25; zz < z + h - 0.05; zz += 0.25) {
        segs.push([[x, y + d, zz], [x + w, y + d, zz]]);
        segs.push([[x + w, y, zz], [x + w, y + d, zz]]);
      }
      this.seg(segs, dk, 1);
      // концы брёвен на углах
      var ends = [];
      for (zz = z + 0.12; zz < z + h; zz += 0.5) {
        ends.push([[x + w, y + d, zz], [x + w + 0.28, y + d, zz]]);
        ends.push([[x + w, y + d, zz + 0.25], [x + w, y + d + 0.28, zz + 0.25]]);
        ends.push([[x, y + d, zz], [x - 0.28, y + d, zz]]);
        ends.push([[x + w, y, zz + 0.25], [x + w, y - 0.28, zz + 0.25]]);
      }
      var wE = this.k * 0.2;
      this.seg(ends, INK, wE + 1.8, ' stroke-linecap="butt"').seg(ends, L, wE, ' stroke-linecap="butt"');
    }
    return this;
  };

  // двускатная крыша, конёк вдоль x
  Iso.prototype.gable = function (x, y, zw, w, d, rise, oh, mat, wallKind, wallColor, opt) {
    opt = opt || {};
    var R = ROOF[mat] || ROOF.thatch;
    var yc = y + d / 2, zr = zw + rise, s = rise / (d / 2), ze = zw - s * oh;
    var x0 = x - oh, x1 = x + w + oh;
    this.poly([[x0, yc, zr], [x1, yc, zr], [x1, y - oh, ze], [x0, y - oh, ze]], R.dim);
    // фронтон
    var wc = wallColor || (wallKind === 'planks' ? C.plank : C.log);
    if (!opt.open) {
      this.poly([[x + w, y, zw], [x + w, y + d, zw], [x + w, yc, zr]], shade(wc, -0.14));
      var g = [];
      for (var zz = zw + 0.25; zz < zr - 0.1; zz += 0.25) {
        var t = (zz - zw) / s;
        g.push([[x + w, y + t, zz], [x + w, y + d - t, zz]]);
      }
      this.seg(g, shade(wc, -0.4), 0.9);
      // дымник в фронтоне
      if (opt.smokeGable) this.poly([[x + w, yc - 0.3, zr - 0.9], [x + w, yc + 0.3, zr - 0.9], [x + w, yc + 0.3, zr - 0.5], [x + w, yc - 0.3, zr - 0.5]], C.dark, 0.8);
    }
    // передний скат
    this.poly([[x0, yc, zr], [x1, yc, zr], [x1, y + d + oh, ze], [x0, y + d + oh, ze]], R.lit);
    var lines = [], step = mat === 'boards' ? 0.32 : 0.42;
    if (mat === 'turf') {
      var tufts = [], sd = 7;
      for (var i = 0; i < w * d * 0.9; i++) {
        sd = (sd * 16807) % 2147483647; var u = sd / 2147483647;
        sd = (sd * 16807) % 2147483647; var v = sd / 2147483647;
        var tx = x0 + u * (x1 - x0), ty = yc + 0.2 + v * (d / 2 + oh - 0.35), tz = zr - (ty - yc) * s;
        tufts.push([[tx, ty, tz], [tx + 0.12, ty, tz + 0.16]]);
      }
      this.seg(tufts, R.line, 1.2).seg(tufts.slice(0, tufts.length / 2 | 0), shade(R.lit, 0.25), 0.9);
    } else {
      for (var xx = x0 + step; xx < x1 - 0.05; xx += step) lines.push([[xx, yc, zr], [xx, y + d + oh, ze]]);
      this.seg(lines, R.line, mat === 'boards' ? 1 : 0.8);
      if (mat === 'boards') this.seg([[[x0, yc + d / 4, zr - s * d / 4], [x1, yc + d / 4, zr - s * d / 4]]], R.line, 1);
    }
    // кромки: свес и причелина
    this.seg([[[x0, y + d + oh, ze], [x1, y + d + oh, ze]]], INK, mat === 'thatch' ? 3.2 : 2.4);
    this.seg([[[x1, yc, zr], [x1, y + d + oh, ze]], [[x1, yc, zr], [x1, y - oh, ze]]], mat === 'thatch' ? INK : shade(C.plank, -0.2), mat === 'thatch' ? 1.4 : 3);
    this.seg([[[x0, yc, zr], [x1, yc, zr]]], INK, 2.6);
    if (opt.crossed !== false && mat !== 'thatch') {
      this.stick([x1, yc + 0.55, zr - 0.35], [x1, yc - 0.45, zr + 0.55], shade(C.plank, -0.1), 2.2);
      this.stick([x1, yc - 0.55, zr - 0.35], [x1, yc + 0.45, zr + 0.55], shade(C.plank, -0.1), 2.2);
    }
    if (mat === 'thatch') {
      this.seg([[[x0, yc, zr + 0.05], [x1, yc, zr + 0.05]]], R.line, 4).seg([[[x0, yc, zr + 0.05], [x1, yc, zr + 0.05]]], shade(R.lit, -0.1), 2.6);
    }
    (opt.smoke || []).forEach(function (sx) {
      this.poly([[sx - 0.35, yc + 0.05, zr - 0.05], [sx + 0.35, yc + 0.05, zr - 0.05], [sx + 0.35, yc + 0.6, zr - 0.6 * s], [sx - 0.35, yc + 0.6, zr - 0.6 * s]], C.dark, 0.8);
      var a = this.xy(sx, yc, zr + 0.4);
      this.o += '<path d="M' + f1(a[0]) + ',' + f1(a[1]) + ' c-6,-8 6,-14 0,-24 c-5,-8 6,-14 2,-22" fill="none" stroke="' + C.smoke +
        '" stroke-width="5" stroke-linecap="round" opacity="0.75"/>';
      this.track(a[0] - 8, a[1] - 50);
    }, this);
    return this;
  };

  // дверь на фасаде y = y1 (face 'front') или на торце x = x1 (face 'end')
  Iso.prototype.door = function (face, plane, a, b, z, h) {
    var pts = face === 'front'
      ? [[a, plane, z], [b, plane, z], [b, plane, z + h], [a, plane, z + h]]
      : [[plane, a, z], [plane, b, z], [plane, b, z + h], [plane, a, z + h]];
    this.poly(pts, C.dark, 1.2);
    var fr = face === 'front'
      ? [[[a, plane, z], [a, plane, z + h]], [[b, plane, z], [b, plane, z + h]], [[a, plane, z + h], [b, plane, z + h]]]
      : [[[plane, a, z], [plane, a, z + h]], [[plane, b, z], [plane, b, z + h]], [[plane, a, z + h], [plane, b, z + h]]];
    this.seg(fr, shade(C.plank, -0.1), 2.4);
    return this;
  };

  // ---------------------------------------------------------------- план (вид сверху)
  var K = 10; // единиц на метр
  function Plan(showSim) { this.o = ''; this.walls = []; this.showSim = showSim !== false; }
  Plan.prototype.rect = function (x, y, w, h, fill, sw, extra) {
    this.o += '<rect x="' + f1(x * K) + '" y="' + f1(y * K) + '" width="' + f1(w * K) + '" height="' + f1(h * K) + '" fill="' + fill + '"' +
      (sw === 0 ? '' : ' stroke="' + INK + '" stroke-width="' + (sw || 1) + '"') + (extra || '') + '/>';
    return this;
  };
  Plan.prototype.circ = function (x, y, r, fill, sw, extra) {
    this.o += '<circle cx="' + f1(x * K) + '" cy="' + f1(y * K) + '" r="' + f1(r * K) + '" fill="' + fill + '"' +
      (sw === 0 ? '' : ' stroke="' + INK + '" stroke-width="' + (sw || 1) + '"') + (extra || '') + '/>';
    return this;
  };
  Plan.prototype.lines = function (list, stroke, w, extra) {
    var d = '';
    list.forEach(function (s) { d += 'M' + f1(s[0] * K) + ',' + f1(s[1] * K) + ' L' + f1(s[2] * K) + ',' + f1(s[3] * K) + ' '; });
    if (d) this.o += '<path d="' + d + '" fill="none" stroke="' + stroke + '" stroke-width="' + w + '" stroke-linecap="round"' + (extra || '') + '/>';
    return this;
  };
  Plan.prototype.path = function (d, fill, sw, extra) {
    this.o += '<path d="' + d + '" fill="' + fill + '"' + (sw === 0 ? '' : ' stroke="' + INK + '" stroke-width="' + (sw || 1) + '"') + (extra || '') + '/>';
    return this;
  };
  Plan.prototype.blob = function (x, y, r, fill, seed, sw, lumps, op) {
    var n = lumps || 9, s = seed || 3, d = '', pts = [];
    function rnd() { s = (s * 16807) % 2147483647; return s / 2147483647; }
    for (var i = 0; i < n; i++) { var a = i / n * Math.PI * 2, m = 0.85 + rnd() * 0.25; pts.push([x * K + Math.cos(a) * r * K * m, y * K + Math.sin(a) * r * K * m]); }
    for (var j = 0; j < n; j++) {
      var p0 = pts[j], p1 = pts[(j + 1) % n], mx = (p0[0] + p1[0]) / 2, my = (p0[1] + p1[1]) / 2;
      var ox = mx - x * K, oy = my - y * K, l = Math.sqrt(ox * ox + oy * oy) || 1;
      d += (j === 0 ? 'M' + f1(p0[0]) + ',' + f1(p0[1]) : '') + ' Q' + f1(mx + ox / l * r * K * 0.18) + ',' + f1(my + oy / l * r * K * 0.18) + ' ' + f1(p1[0]) + ',' + f1(p1[1]);
    }
    return this.path(d + ' Z', fill, sw, op ? ' opacity="' + op + '"' : '');
  };
  // двускатная крыша сверху; конёк вдоль x
  Plan.prototype.gable = function (x, y, w, d, oh, mat, h, opt) {
    opt = opt || {};
    var R = ROOF[mat] || ROOF.thatch, sh = (h || 4) * 0.3;
    this.rect(x - oh + sh, y - oh + sh, w + 2 * oh, d + 2 * oh, '#1B140F', 0, ' opacity="0.22"');
    this.rect(x - oh, y - oh, w + 2 * oh, d / 2 + oh, R.lit, 1.2);
    this.rect(x - oh, y + d / 2, w + 2 * oh, d / 2 + oh, R.dim, 1.2);
    var l = [], step = mat === 'boards' ? 0.32 : 0.45;
    if (mat === 'turf') {
      var sd = 11;
      for (var i = 0; i < w * d * 0.8; i++) {
        sd = (sd * 16807) % 2147483647; var u = sd / 2147483647; sd = (sd * 16807) % 2147483647; var v = sd / 2147483647;
        var tx = x - oh + 0.2 + u * (w + 2 * oh - 0.4), ty = y - oh + 0.2 + v * (d + 2 * oh - 0.4);
        l.push([tx, ty, tx + 0.15, ty - 0.12]);
      }
      this.lines(l, R.line, 1.1);
    } else {
      for (var xx = x - oh + step; xx < x + w + oh - 0.05; xx += step) { l.push([xx, y - oh, xx, y + d / 2 - 0.05]); l.push([xx, y + d / 2 + 0.05, xx, y + d + oh]); }
      this.lines(l, R.line, 0.8);
    }
    this.lines([[x - oh, y + d / 2, x + w + oh, y + d / 2]], INK, 2.2);
    (opt.smoke || []).forEach(function (sx) { this.rect(sx - 0.35, y + d / 2 - 0.3, 0.7, 0.6, C.dark, 0.8); }, this);
    return this;
  };
  Plan.prototype.door = function (x, y, w, h) { return this.rect(x, y, w, h, shade(C.plank, -0.2), 0.8); };
  Plan.prototype.sim = function (x, y, w, d) {
    this.walls.push([x, y, w, d]);
    if (!this.showSim) return this;
    this.o += '<rect class="simfoot" x="' + f1(x * K) + '" y="' + f1(y * K) + '" width="' + f1(w * K) + '" height="' + f1(d * K) +
      '" fill="none" stroke="#E0442A" stroke-width="1.4" stroke-dasharray="4 3"/>';
    return this;
  };

  // ---------------------------------------------------------------- каталог
  // sim: house — дом (стена kind "house"), obstacle — препятствие, free — проходимо, water — вода (в симуляции пока нет)
  var OBJECTS = [];
  function def(o) { OBJECTS.push(o); }

  // ---- Жильё и постройки
  def({ id: 'longhouse', group: 'home', name: 'Длинный дом клана', size: [20, 7], h: 5.8, sim: 'house',
    where: 'наш берег, у дороги к городу; живут вождь и дружинники, пир для старейшин',
    note: 'Стены из плах, кровля дёрновая — дом пришлых, не похожий на местные срубы.',
    view: function (c) {
      c.ground(-1.5, -1.5, 21.5, 8.5).shadow(0, 0, 20, 7, 5);
      c.walls(0, 0, 0, 20, 7, 2.2, 'planks');
      c.door('front', 7, 9.3, 10.7, 0, 1.9);
      c.gable(0, 0, 2.2, 20, 7, 3.6, 0.5, 'turf', 'planks', null, { smoke: [6, 14] });
    },
    plan: function (q) {
      q.gable(0, 0, 20, 7, 0.5, 'turf', 5.8, { smoke: [6, 14] });
      q.door(9.3, 7.5, 1.4, 0.6); q.sim(0, 0, 20, 7);
    } });
  def({ id: 'ruler-hall', group: 'home', name: 'Дом правителя', size: [12, 8], h: 6.4, sim: 'house',
    where: 'большое селение правителя на северо-восточном берегу',
    note: 'Высокий сруб с тёсовой кровлей и перекрещёнными досками на коньке.',
    view: function (c) {
      c.ground(-1.5, -1.5, 13.5, 9.5).shadow(0, 0, 12, 8, 6);
      c.walls(0, 0, 0, 12, 8, 2.6, 'logs');
      c.door('end', 12, 3.4, 4.6, 0, 2);
      c.gable(0, 0, 2.6, 12, 8, 3.8, 0.6, 'boards', 'logs', null, { smokeGable: true });
    },
    plan: function (q) { q.gable(0, 0, 12, 8, 0.6, 'boards', 6.4); q.door(12.6, 3.4, 0.6, 1.2); q.sim(0, 0, 12, 8); } });
  def({ id: 'house', group: 'home', name: 'Изба', size: [6, 5], h: 4.8, sim: 'house',
    where: 'все селения; 5–10 в малом, 15–25 у правителя',
    note: 'Сруб с соломенной кровлей, дверь в торце, волоковое окно на фасаде, дым через фронтон.',
    view: function (c) {
      c.ground(-1.2, -1.2, 7.2, 6.2).shadow(0, 0, 6, 5, 4.8);
      c.walls(0, 0, 0, 6, 5, 2, 'logs');
      c.door('end', 6, 1.9, 3.0, 0, 1.6);
      c.poly([[2.2, 5, 1.1], [2.8, 5, 1.1], [2.8, 5, 1.4], [2.2, 5, 1.4]], C.dark, 0.8);
      c.gable(0, 0, 2, 6, 5, 2.8, 0.5, 'thatch', 'logs', null, { smokeGable: true });
    },
    plan: function (q) { q.gable(0, 0, 6, 5, 0.5, 'thatch', 4.8); q.door(6.5, 1.9, 0.5, 1.1); q.sim(0, 0, 6, 5); } });
  def({ id: 'house-small', group: 'home', name: 'Малая изба', size: [4.5, 4], h: 3.8, sim: 'house',
    where: 'охотники, бортники, рыбаки, рудокопы — малые селения',
    note: 'Низкий сруб с кровлей из драни или коры.',
    view: function (c) {
      c.ground(-1, -1, 5.5, 5).shadow(0, 0, 4.5, 4, 3.8);
      c.walls(0, 0, 0, 4.5, 4, 1.8, 'logs');
      c.door('end', 4.5, 1.5, 2.4, 0, 1.4);
      c.gable(0, 0, 1.8, 4.5, 4, 2, 0.4, 'boards', 'logs', null, { crossed: false, smokeGable: true });
    },
    plan: function (q) { q.gable(0, 0, 4.5, 4, 0.4, 'boards', 3.8); q.door(4.9, 1.5, 0.4, 0.9); q.sim(0, 0, 4.5, 4); } });
  def({ id: 'granary', group: 'home', name: 'Клеть на столбах', size: [3, 3], h: 3.8, sim: 'house',
    where: 'в каждом селении — закрома: здесь ждёт сборщика дань',
    note: 'Подклеть на столбах спасает зерно от сырости и мышей.',
    view: function (c) {
      c.ground(-1, -1, 4.5, 4).shadow(0, 0, 3, 3, 3.8);
      [[0.15, 0.15], [2.85, 0.15], [0.15, 2.85], [2.85, 2.85]].forEach(function (pp) { c.cyl(pp[0], pp[1], 0, 0.9, 0.16, C.post); });
      c.walls(0, 0, 0.9, 3, 3, 1.6, 'logs');
      c.door('end', 3, 1.0, 1.9, 1.0, 1.1);
      c.stick([4.1, 1.45, 0], [3.0, 1.45, 0.95], C.post, c.k * 0.22);
      c.seg([[[3.85, 1.45, 0.25], [3.65, 1.45, 0.25]], [[3.55, 1.45, 0.55], [3.35, 1.45, 0.55]]], INK, 1.2);
      c.gable(0, 0, 2.5, 3, 3, 1.3, 0.4, 'boards', 'logs', null, { crossed: false });
    },
    plan: function (q) { q.gable(0, 0, 3, 3, 0.4, 'boards', 3.8); q.rect(3.1, 1.3, 1.1, 0.3, C.post, 0.8); q.sim(0, 0, 3, 3); } });
  def({ id: 'storehouse', group: 'home', name: 'Хранилище клана', size: [5, 4], h: 4.2, sim: 'house',
    where: 'рядом с длинным домом: запасы и запасное оружие, ёмкость в ношах',
    note: 'Сруб без окон, двустворчатая дверь с железными полосами.',
    view: function (c) {
      c.ground(-1, -1, 6, 5).shadow(0, 0, 5, 4, 4.2);
      c.walls(0, 0, 0, 5, 4, 2.2, 'logs');
      c.door('front', 4, 1.6, 3.4, 0, 1.8);
      c.seg([[[1.6, 4, 0.5], [3.4, 4, 0.5]], [[1.6, 4, 1.3], [3.4, 4, 1.3]], [[2.5, 4, 0], [2.5, 4, 1.8]]], C.ironDark, 1.8);
      c.gable(0, 0, 2.2, 5, 4, 2, 0.4, 'boards', 'logs');
    },
    plan: function (q) { q.gable(0, 0, 5, 4, 0.4, 'boards', 4.2); q.door(1.6, 4.4, 1.8, 0.4); q.sim(0, 0, 5, 4); } });
  def({ id: 'sauna', group: 'home', name: 'Баня', size: [3.5, 3], h: 3, sim: 'house',
    where: 'у воды в каждом селении',
    note: 'По-чёрному: брёвна закопчены, дым выходит через дверь и щели.',
    view: function (c) {
      c.ground(-1, -1, 5.5, 4).shadow(0, 0, 3.5, 3, 3);
      c.walls(0, 0, 0, 3.5, 3, 1.7, 'logs', C.logOld);
      c.door('end', 3.5, 1.0, 1.9, 0, 1.2);
      c.gable(0, 0, 1.7, 3.5, 3, 1.2, 0.3, 'turf', 'logs', C.logOld, { smoke: [1.4] });
      c.walls(3.9, 2.2, 0, 1.2, 0.6, 0.8, 'logs', C.log);
    },
    plan: function (q) { q.gable(0, 0, 3.5, 3, 0.3, 'turf', 3, { smoke: [1.4] }); q.rect(3.9, 2.2, 1.2, 0.6, C.log, 0.8); q.sim(0, 0, 3.5, 3); } });
  def({ id: 'smithy', group: 'craft', name: 'Кузница', size: [5, 4], h: 4.2, sim: 'house',
    where: 'у селения рудокопов; своя кузница клана — возможная доходная точка',
    note: 'Навес на столбах: горн из глины, наковальня на колоде, мехи.',
    view: function (c) {
      c.ground(-1, -1, 6, 6, C.earth).shadow(0, 0, 5, 4, 4);
      c.cyl(0.15, 0.15, 0, 2.4, 0.14, C.post); c.cyl(4.85, 0.15, 0, 2.4, 0.14, C.post); c.cyl(0.15, 3.85, 0, 2.4, 0.14, C.post);
      c.walls(2.6, 2.2, 0, 1.6, 1.1, 0.8, 'logs', C.clay);
      c.disc(3.4, 2.75, 0.81, 0.4, C.fire, 1).disc(3.4, 2.75, 0.82, 0.2, C.fireHot, 0);
      c.blob(2.4, 0.9, 0.45, 0.45, 0.3, C.leather, 5, 1);
      c.cyl(4.85, 3.85, 0, 2.4, 0.14, C.post);
      c.gable(0, 0, 2.4, 5, 4, 1.8, 0.4, 'thatch', 'logs', null, { open: true });
      c.cyl(2.6, 5.0, 0, 0.6, 0.26, C.log);
      c.walls(2.35, 4.9, 0.6, 0.5, 0.2, 0.16, 'planks', C.ironDark);
      c.poly([[2.35, 4.9, 0.76], [2.85, 4.9, 0.76], [2.85, 5.1, 0.76], [2.35, 5.1, 0.76]], C.iron, 1);
    },
    plan: function (q) {
      q.gable(0, 0, 5, 4, 0.4, 'thatch', 4.2);
      q.circ(0.15, 0.15, 0.15, C.post, 0.8).circ(4.85, 0.15, 0.15, C.post, 0.8).circ(0.15, 3.85, 0.15, C.post, 0.8).circ(4.85, 3.85, 0.15, C.post, 0.8);
      q.circ(2.6, 5.0, 0.26, C.log, 0.8).rect(2.35, 4.9, 0.5, 0.2, C.iron, 0.8);
      q.sim(0, 0, 5, 4);
    } });
  def({ id: 'bloomery', group: 'craft', name: 'Домница', size: [3, 3], h: 1.6, sim: 'obstacle',
    where: 'у рудокопов на севере: из болотной руды получают крицу',
    note: 'Глиняная шахтная печь, рядом шлак и куча угля.',
    view: function (c) {
      c.ground(-0.8, -0.8, 3.8, 3.8, C.earth);
      c.cone(0.6, 0.7, 0, 0.9, 0.7, '#2B2724');
      c.cyl(1.5, 1.5, 0, 1.5, 0.62, C.clay, 0.42);
      c.poly([[1.5 + 0.38, 1.5 + 0.38, 0], [1.5 + 0.12, 1.5 + 0.6, 0], [1.5 + 0.12, 1.5 + 0.6, 0.35], [1.5 + 0.38, 1.5 + 0.38, 0.35]], C.fire, 1);
      c.disc(1.5, 1.5, 1.5, 0.22, C.dark, 0.8);
      var a = c.xy(1.5, 1.5, 1.7);
      c.raw('<path d="M' + f1(a[0]) + ',' + f1(a[1]) + ' c-5,-7 5,-12 0,-20 c-4,-7 5,-12 2,-18" fill="none" stroke="' + C.smoke + '" stroke-width="4" stroke-linecap="round" opacity="0.75"/>');
      c.track(a[0], a[1] - 40);
      c.blob(2.6, 2.4, 0.15, 0.3, 0.18, '#3B302A', 4, 1).blob(2.9, 2.0, 0.12, 0.22, 0.14, '#4A3A30', 9, 1);
    },
    plan: function (q) {
      q.circ(0.6, 0.7, 0.7, '#2B2724', 1).circ(1.7, 1.7, 0.62, '#1B140F', 0, ' opacity="0.2"').circ(1.5, 1.5, 0.62, C.clay, 1.2).circ(1.5, 1.5, 0.22, C.dark, 0.8);
      q.blob(2.6, 2.4, 0.35, '#3B302A', 4, 1); q.sim(0.9, 0.9, 1.2, 1.2);
    } });

  // ---- Промыслы и хозяйство
  def({ id: 'fish-racks', group: 'craft', name: 'Вешала с рыбой', size: [4, 1.5], h: 1.9, sim: 'obstacle',
    where: 'рыбаки у озера и на реке; рыбный промысел клана',
    note: 'Жерди на кольях, рыба вялится рядами.',
    view: function (c) {
      c.ground(-0.8, -0.8, 4.8, 2.3, C.earth);
      c.cyl(0, 0.75, 0, 1.9, 0.07, C.post); c.cyl(2, 0.75, 0, 1.9, 0.07, C.post);
      [1.85, 1.25].forEach(function (zb) {
        c.stick([-0.1, 0.75, zb], [4.1, 0.75, zb], C.post, c.k * 0.08);
        for (var xx = 0.15; xx < 3.95; xx += 0.24) {
          if (Math.abs(xx - 2) < 0.1) continue;
          c.poly([[xx, 0.75, zb - 0.04], [xx + 0.06, 0.75, zb - 0.2], [xx, 0.75, zb - 0.42], [xx - 0.06, 0.75, zb - 0.2]], C.fish, 0.6);
        }
      });
      c.cyl(4, 0.75, 0, 1.9, 0.07, C.post);
    },
    plan: function (q) {
      q.rect(0.3, 0.95, 4, 0.2, '#1B140F', 0, ' opacity="0.2"');
      q.lines([[0, 0.75, 4, 0.75]], C.post, 3); q.circ(0, 0.75, 0.1, C.post, 0.8).circ(2, 0.75, 0.1, C.post, 0.8).circ(4, 0.75, 0.1, C.post, 0.8);
      var f = []; for (var xx = 0.15; xx < 3.95; xx += 0.24) f.push([xx, 0.55, xx, 0.95]); q.lines(f, C.fish, 1.6);
      q.sim(0, 0.65, 4, 0.2);
    } });
  def({ id: 'boat', group: 'craft', name: 'Лодка', size: [6, 1.4], h: 1, sim: 'obstacle',
    where: 'на берегу у рыбаков и у верфи; лодки переправляются где угодно',
    note: 'Набойная лодка из досок внакрой, на 4–6 гребцов.',
    view: function (c) {
      c.ground(-0.8, -0.8, 6.8, 2.2, '#9C8A68');
      var g = [[0, 0.7], [1, 0.15], [3, 0], [5, 0.15], [6, 0.7], [5, 1.25], [3, 1.4], [1, 1.25]];
      c.poly(g.map(function (q) { return [q[0], q[1], 0.6]; }), '#5A4632', 1.2);
      c.seg([[[2, 0.1, 0.52], [2, 1.3, 0.52]], [[3.2, 0.05, 0.52], [3.2, 1.35, 0.52]], [[4.3, 0.12, 0.52], [4.3, 1.28, 0.52]]], C.plank, 2.4);
      var front = [[0, 0.7, 0.6], [1, 1.25, 0.6], [3, 1.4, 0.6], [5, 1.25, 0.6], [6, 0.7, 0.6], [5.6, 0.7, 0], [4.8, 0.9, 0], [3, 0.95, 0], [1.2, 0.9, 0], [0.4, 0.7, 0]];
      c.poly(front, C.plank, 1.2);
      c.seg([[[0.2, 0.7, 0.4], [1.1, 1.13, 0.4]], [[1.1, 1.13, 0.4], [3, 1.27, 0.4]], [[3, 1.27, 0.4], [4.9, 1.13, 0.4]], [[4.9, 1.13, 0.4], [5.8, 0.7, 0.4]],
             [[0.3, 0.7, 0.2], [1.15, 1.02, 0.2]], [[1.15, 1.02, 0.2], [3, 1.1, 0.2]], [[3, 1.1, 0.2], [4.85, 1.02, 0.2]], [[4.85, 1.02, 0.2], [5.7, 0.7, 0.2]]], shade(C.plank, -0.35), 1);
      c.stick([0, 0.7, 0.6], [-0.3, 0.7, 1.05], C.plank, c.k * 0.12).stick([6, 0.7, 0.6], [6.3, 0.7, 1.05], C.plank, c.k * 0.12);
    },
    plan: function (q) {
      q.path('M3,3 L13,1.5 L33,0 L53,1.5 L63,7 L53,12.5 L33,14 L13,12.5 Z', '#1B140F', 0, ' opacity="0.2" transform="translate(4,4)"');
      q.path('M0,7 L10,1.5 L30,0 L50,1.5 L60,7 L50,12.5 L30,14 L10,12.5 Z', C.plank, 1.2);
      q.path('M4,7 L12,3 L30,2 L48,3 L56,7 L48,11 L30,12 L12,11 Z', '#5A4632', 0.8);
      q.lines([[2, 0.3, 2, 1.1], [3.2, 0.25, 3.2, 1.15], [4.3, 0.3, 4.3, 1.1]], C.plank, 2.4);
      q.sim(0, 0, 6, 1.4);
    } });
  def({ id: 'bort-tree', group: 'nature', name: 'Бортное дерево', size: [5, 5], h: 12, sim: 'obstacle',
    where: 'бортники в глуши и в большом лесу: мёд и воск',
    note: 'Дупло-борть на высоте 4 м закрыто доской (должеей), на стволе — знак владельца.',
    view: function (c) {
      c.ground(-0.5, -0.5, 5.5, 5.5);
      c.castShadow(4.2, 4.2, 2.6, 1.4);
      c.cyl(2.5, 2.5, 0, 9, 0.28, C.pineBark, 0.2);
      c.poly([[2.62, 2.78, 3.8], [2.78, 2.62, 3.8], [2.78, 2.62, 4.6], [2.62, 2.78, 4.6]], C.plank, 1);
      c.seg([[[2.66, 2.79, 2.6], [2.8, 2.65, 2.8]], [[2.66, 2.79, 2.8], [2.8, 2.65, 2.6]]], INK, 1.4);
      c.crown(2.5, 2.5, [[-1.2, -0.1, 8.0, 1.5, 0.8], [1.0, 0.3, 8.7, 1.6, 0.85], [-0.3, 0, 9.6, 2.6, 1.25], [0.2, 0.2, 10.8, 1.9, 0.95], [-0.1, 0.1, 11.7, 1.1, 0.55]], C.pine, 3);
    },
    plan: function (q) { q.blob(3.0, 3.0, 2.2, '#1B140F', 3, 0, 9, 0.22); q.blob(2.5, 2.5, 2.2, C.pine, 3, 1); q.blob(2.3, 2.3, 1.2, shade(C.pine, 0.15), 6, 0); q.circ(2.5, 2.5, 0.28, C.pineBark, 0.8); q.sim(2.2, 2.2, 0.6, 0.6); } });
  def({ id: 'haystack', group: 'craft', name: 'Стог', size: [3, 3], h: 3.8, sim: 'obstacle',
    where: 'у пахарей и у длинного дома; на жатве — в поле',
    note: 'Сено вокруг шеста; горит — враг может поджечь.',
    view: function (c) {
      c.ground(-0.6, -0.6, 3.6, 3.6);
      c.cone(1.5, 1.5, 0, 3.1, 1.5, C.hay);
      var segs = []; for (var i = 0; i < 7; i++) { var a = -0.6 + i * 0.35; segs.push([[1.5, 1.5, 3.0], [1.5 + Math.cos(a) * 1.4, 1.5 + Math.sin(a) * 1.4, 0.3]]); }
      c.seg(segs, shade(C.hay, -0.25), 1);
      c.stick([1.5, 1.5, 2.9], [1.5, 1.5, 3.8], C.post, c.k * 0.1);
    },
    plan: function (q) {
      q.circ(1.9, 1.9, 1.5, '#1B140F', 0, ' opacity="0.22"').circ(1.5, 1.5, 1.5, C.hay, 1.2);
      var l = []; for (var i = 0; i < 12; i++) { var a = i / 12 * Math.PI * 2; l.push([1.5 + Math.cos(a) * 0.3, 1.5 + Math.sin(a) * 0.3, 1.5 + Math.cos(a) * 1.4, 1.5 + Math.sin(a) * 1.4]); }
      q.lines(l, shade(C.hay, -0.25), 1); q.circ(1.5, 1.5, 0.12, C.post, 0.8); q.sim(0.5, 0.5, 2, 2);
    } });
  def({ id: 'woodpile', group: 'craft', name: 'Поленница', size: [3, 0.8], h: 1.2, sim: 'obstacle',
    where: 'у каждого дома и у бани',
    note: 'Дрова на зиму; укрытие по пояс.',
    view: function (c) {
      c.ground(-0.5, -0.5, 3.5, 1.3);
      c.walls(0, 0, 0, 3, 0.8, 1.2, 'planks', C.log);
      c.poly([[0, 0, 1.2], [3, 0, 1.2], [3, 0.8, 1.2], [0, 0.8, 1.2]], shade(C.log, 0.05));
      var dots = '';
      for (var zz = 0.12; zz < 1.2; zz += 0.2) for (var xx = 0.1; xx < 3; xx += 0.2) {
        var p = c.xy(xx + (Math.round(zz * 10) % 4 ? 0.1 : 0), 0.8, zz);
        dots += '<circle cx="' + f1(p[0]) + '" cy="' + f1(p[1]) + '" r="' + f1(c.k * 0.085) + '" fill="#C9A577" stroke="' + INK + '" stroke-width="0.6"/>';
      }
      c.raw(dots);
    },
    plan: function (q) { q.rect(0.3, 0.3, 3, 0.8, '#1B140F', 0, ' opacity="0.2"'); q.rect(0, 0, 3, 0.8, C.log, 1); var l = []; for (var xx = 0.3; xx < 3; xx += 0.3) l.push([xx, 0, xx, 0.8]); q.lines(l, shade(C.log, -0.35), 0.8); q.sim(0, 0, 3, 0.8); } });
  def({ id: 'well', group: 'craft', name: 'Колодец с журавлём', size: [5.5, 1.4], h: 3.6, sim: 'obstacle',
    where: 'в больших селениях и у длинного дома',
    note: 'Сруб колодца и рычаг-журавль с грузом.',
    view: function (c) {
      c.ground(-0.6, -0.6, 6, 2);
      c.walls(0, 0, 0, 1.2, 1.2, 0.8, 'logs');
      c.poly([[0, 0, 0.8], [1.2, 0, 0.8], [1.2, 1.2, 0.8], [0, 1.2, 0.8]], C.log);
      c.poly([[0.2, 0.2, 0.8], [1.0, 0.2, 0.8], [1.0, 1.0, 0.8], [0.2, 1.0, 0.8]], '#2B3A44', 0.8);
      c.cyl(2.2, 0.6, 0, 3.0, 0.11, C.post);
      c.stick([0.6, 0.6, 3.6], [5.0, 0.6, 1.95], C.haft, c.k * 0.08);
      c.seg([[[0.6, 0.6, 3.6], [0.6, 0.6, 1.3]]], INK, 0.8);
      c.walls(0.45, 0.45, 1.05, 0.3, 0.3, 0.3, 'planks', C.plank);
      c.blob(5.0, 0.6, 1.85, 0.32, 0.25, C.stone, 2, 1);
    },
    plan: function (q) { q.rect(0.2, 0.2, 1.2, 1.2, '#1B140F', 0, ' opacity="0.2"'); q.rect(0, 0, 1.2, 1.2, C.log, 1); q.rect(0.2, 0.2, 0.8, 0.8, '#2B3A44', 0.8); q.lines([[0.6, 0.6, 5, 0.6]], C.haft, 2.4); q.circ(2.2, 0.6, 0.12, C.post, 0.8); q.circ(5, 0.6, 0.3, C.stone, 0.8); q.sim(0, 0, 1.2, 1.2); } });
  def({ id: 'cart', group: 'craft', name: 'Повозка', size: [3.6, 1.6], h: 1.4, sim: 'obstacle',
    where: 'обоз дани, дорога к городу',
    note: 'Двуколка: на ней везут дань и добычу — цель жадного вожака.',
    view: function (c) {
      c.ground(-0.6, -0.6, 4.2, 2.2, C.earth);
      c.wheel(1.2, 0.05, 0.6, 0.6, C.plank);
      c.stick([2.2, 0.3, 0.75], [3.7, 0.3, 0.1], C.haft, c.k * 0.08);
      c.walls(0.2, 0.15, 0.7, 2.0, 1.3, 0.35, 'planks');
      c.poly([[0.2, 0.15, 1.05], [2.2, 0.15, 1.05], [2.2, 1.45, 1.05], [0.2, 1.45, 1.05]], shade(C.plank, -0.25));
      c.blob(1.0, 0.8, 1.2, 0.5, 0.3, C.wool, 3, 1).blob(1.6, 0.7, 1.15, 0.4, 0.26, '#C9A55E', 5, 1);
      c.stick([2.2, 1.3, 0.75], [3.7, 1.3, 0.1], C.haft, c.k * 0.08);
      c.wheel(1.2, 1.55, 0.6, 0.6, C.plank);
    },
    plan: function (q) { q.rect(0.5, 0.45, 2.0, 1.3, '#1B140F', 0, ' opacity="0.2"'); q.rect(0.2, 0.15, 2.0, 1.3, C.plank, 1); q.rect(0.9, -0.05, 0.6, 0.15, C.post, 0.8); q.rect(0.9, 1.45, 0.6, 0.15, C.post, 0.8); q.lines([[2.2, 0.3, 3.7, 0.3], [2.2, 1.3, 3.7, 1.3]], C.haft, 2); q.sim(0.2, 0.15, 2, 1.3); } });
  def({ id: 'pen', group: 'craft', name: 'Загон', size: [6, 5], h: 1.2, sim: 'obstacle',
    where: 'у пахарей: скот на ночь',
    note: 'Плетёная изгородь с проходом; стены загона — препятствия, внутри можно стоять.',
    view: function (c) {
      c.ground(-0.6, -0.6, 6.6, 5.6);
      function fence(a, b) {
        var dx = b[0] - a[0], dy = b[1] - a[1], n = Math.round(Math.sqrt(dx * dx + dy * dy) / 0.9);
        c.poly([[a[0], a[1], 0.15], [b[0], b[1], 0.15], [b[0], b[1], 1.1], [a[0], a[1], 1.1]], '#8C6E4A', 1);
        var w = []; for (var zz = 0.3; zz < 1.1; zz += 0.18) w.push([[a[0], a[1], zz], [b[0], b[1], zz]]);
        c.seg(w, '#5E4630', 1);
        for (var i = 0; i <= n; i++) { var t = i / n; c.stick([a[0] + dx * t, a[1] + dy * t, 0], [a[0] + dx * t, a[1] + dy * t, 1.3], C.post, c.k * 0.06); }
      }
      fence([0, 0], [6, 0]); fence([0, 0], [0, 5]);
      c.poly([[0.3, 0.3, 0.01], [5.7, 0.3, 0.01], [5.7, 4.7, 0.01], [0.3, 4.7, 0.01]], C.hay, 0, ' opacity="0.45"');
      [[2, 2, 1], [3.6, 3, 2]].forEach(function (sh) {
        c.blob(sh[0], sh[1], 0.55, 0.55, 0.33, C.wool, sh[2] * 5, 1);
        c.blob(sh[0] + 0.45, sh[1] + 0.25, 0.7, 0.18, 0.15, '#3B302A', sh[2], 1);
      });
      fence([6, 0], [6, 5]); fence([0, 5], [2.5, 5]); fence([3.7, 5], [6, 5]);
    },
    plan: function (q) {
      q.rect(0, 0, 6, 5, C.hay, 0, ' opacity="0.45"');
      q.lines([[0, 0, 6, 0], [0, 0, 0, 5], [6, 0, 6, 5], [0, 5, 2.5, 5], [3.7, 5, 6, 5]], '#5E4630', 3);
      q.blob(2, 2, 0.5, C.wool, 5, 0.8).blob(3.6, 3, 0.5, C.wool, 10, 0.8);
      q.sim(0, -0.05, 6, 0.1).sim(-0.05, 0, 0.1, 5).sim(5.95, 0, 0.1, 5).sim(0, 4.95, 2.5, 0.1).sim(3.7, 4.95, 2.3, 0.1);
    } });

  // ---- Укрепления и переправы
  def({ id: 'palisade', group: 'fort', name: 'Частокол', size: [6, 0.4], h: 3, sim: 'obstacle',
    where: 'укрепления у моста, брода, у выхода к городу, на холме; городище правителя',
    note: 'Заострённые брёвна в ряд, прихвачены поперечиной. Отрезок 6 м.',
    view: function (c) {
      c.ground(-0.8, -0.8, 6.8, 1.4, C.earth).shadow(0, 0, 6, 0.4, 3);
      for (var xx = 0.15; xx < 6; xx += 0.3) { c.cyl(xx, 0.2, 0, 2.6, 0.15, C.log); c.cone(xx, 0.2, 2.6, 0.4, 0.15, shade(C.log, 0.15), 0.9); }
      c.stick([0, 0.36, 1.8], [6, 0.36, 1.8], C.post, c.k * 0.14);
    },
    plan: function (q) {
      q.rect(0.6, 0.6, 6, 0.4, '#1B140F', 0, ' opacity="0.22"');
      for (var xx = 0.15; xx < 6; xx += 0.3) q.circ(xx, 0.2, 0.15, C.log, 0.8);
      q.sim(0, 0.05, 6, 0.3);
    } });
  def({ id: 'gate', group: 'fort', name: 'Ворота', size: [7, 1.2], h: 4.6, sim: 'obstacle',
    where: 'в частоколе укреплений и городища; проход 3 м',
    note: 'Створки из плах под маленькой кровлей; открытые — проход для строя в три ряда.',
    view: function (c) {
      c.ground(-0.8, -0.8, 7.8, 2, C.earth).shadow(0, 0, 7, 1.2, 4);
      for (var xx = 0.15; xx < 2; xx += 0.3) { c.cyl(xx, 0.6, 0, 2.6, 0.15, C.log); c.cone(xx, 0.6, 2.6, 0.4, 0.15, shade(C.log, 0.15), 0.9); }
      c.cyl(1.95, 0.6, 0, 3.6, 0.2, C.post);
      c.poly([[2.1, 0.6, 0], [4.9, 0.6, 0], [4.9, 0.6, 2.7], [2.1, 0.6, 2.7]], C.plank, 1.2);
      var pl = []; for (var x2 = 2.45; x2 < 4.9; x2 += 0.35) pl.push([[x2, 0.6, 0], [x2, 0.6, 2.7]]);
      c.seg(pl, shade(C.plank, -0.35), 0.9);
      c.seg([[[2.1, 0.6, 0.6], [4.9, 0.6, 0.6]], [[2.1, 0.6, 2.1], [4.9, 0.6, 2.1]], [[3.5, 0.6, 0], [3.5, 0.6, 2.7]]], shade(C.plank, -0.45), 2.4);
      c.cyl(5.05, 0.6, 0, 3.6, 0.2, C.post);
      c.walls(1.8, 0.35, 3.4, 3.4, 0.5, 0.2, 'planks', C.post);
      c.gable(1.6, 0, 3.6, 3.8, 1.2, 0.8, 0.2, 'boards', 'planks', null, { crossed: false, open: true });
      for (var x3 = 5.35; x3 < 7; x3 += 0.3) { c.cyl(x3, 0.6, 0, 2.6, 0.15, C.log); c.cone(x3, 0.6, 2.6, 0.4, 0.15, shade(C.log, 0.15), 0.9); }
    },
    plan: function (q) {
      for (var xx = 0.15; xx < 2; xx += 0.3) q.circ(xx, 0.6, 0.15, C.log, 0.8);
      for (var x3 = 5.35; x3 < 7; x3 += 0.3) q.circ(x3, 0.6, 0.15, C.log, 0.8);
      q.rect(2.3, 0.3, 3.8, 1.2, '#1B140F', 0, ' opacity="0.2"');
      q.gable(1.6, 0, 3.8, 1.2, 0.1, 'boards', 4.6);
      q.circ(1.95, 0.6, 0.2, C.post, 0.8).circ(5.05, 0.6, 0.2, C.post, 0.8);
      q.sim(0, 0.45, 2.1, 0.3).sim(4.9, 0.45, 2.1, 0.3);
    } });
  def({ id: 'watchtower', group: 'fort', name: 'Сторожевая вышка', size: [3, 3], h: 7.4, sim: 'obstacle',
    where: 'холм с обзором, у моста и брода: раннее предупреждение',
    note: 'Площадка на 5 м с ограждением и рогом; лестница-бревно с зарубками.',
    view: function (c) {
      c.ground(-0.8, -0.8, 4.5, 4.5, C.earth).shadow(0, 0, 3, 3, 6);
      c.cyl(0.25, 0.25, 0, 5, 0.15, C.post); c.cyl(2.75, 0.25, 0, 5, 0.15, C.post); c.cyl(0.25, 2.75, 0, 5, 0.15, C.post);
      c.seg([[[0.25, 2.75, 0.4], [2.75, 2.75, 4.4]], [[2.75, 0.25, 0.4], [2.75, 2.75, 4.4]], [[0.25, 2.75, 4.4], [2.75, 2.75, 0.4]], [[2.75, 0.25, 4.4], [2.75, 2.75, 0.4]]], INK, c.k * 0.08 + 2).seg(
        [[[0.25, 2.75, 0.4], [2.75, 2.75, 4.4]], [[2.75, 0.25, 0.4], [2.75, 2.75, 4.4]], [[0.25, 2.75, 4.4], [2.75, 2.75, 0.4]], [[2.75, 0.25, 4.4], [2.75, 2.75, 0.4]]], C.post, c.k * 0.08);
      c.cyl(2.75, 2.75, 0, 5, 0.15, C.post);
      c.walls(-0.1, -0.1, 4.8, 3.2, 3.2, 0.18, 'planks', C.plank);
      c.poly([[-0.1, -0.1, 4.98], [3.1, -0.1, 4.98], [3.1, 3.1, 4.98], [-0.1, 3.1, 4.98]], shade(C.plank, 0.1));
      c.walls(-0.1, -0.1, 4.98, 3.2, 0.08, 0.9, 'planks', C.plank);
      c.walls(-0.1, -0.1, 4.98, 0.08, 3.2, 0.9, 'planks', C.plank);
      [[0.1, 0.1], [2.9, 0.1], [0.1, 2.9], [2.9, 2.9]].forEach(function (pp) { c.stick([pp[0], pp[1], 5.9], [pp[0], pp[1], 6.6], C.post, c.k * 0.08); });
      c.walls(-0.1, 3.02, 4.98, 3.2, 0.08, 0.9, 'planks', C.plank);
      c.walls(3.02, -0.1, 4.98, 0.08, 3.2, 0.9, 'planks', C.plank);
      c.stick([2.0, 4.4, 0], [2.0, 3.1, 5.0], C.log, c.k * 0.2);
      c.gable(0, 0, 6.6, 3, 3, 0.8, 0.3, 'boards', 'planks', null, { crossed: false, open: true });
    },
    plan: function (q) {
      q.rect(1.8, 1.8, 3.4, 3.4, '#1B140F', 0, ' opacity="0.22"');
      q.gable(0, 0, 3, 3, 0.3, 'boards', 7.4);
      q.rect(1.9, 3.1, 0.25, 1.3, C.log, 0.8);
      q.sim(0.1, 0.1, 2.8, 2.8);
    } });
  def({ id: 'beacon', group: 'fort', name: 'Сигнальный костёр', size: [2.4, 2.4], h: 2.2, sim: 'obstacle',
    where: 'на холмах между селениями: тревога идёт дымом от селения к селению',
    note: 'Шалаш из жердей и хвороста в кольце камней; подожжённый — сигнал сбора ополчения.',
    view: function (c) {
      c.ground(-0.4, -0.4, 2.8, 2.8, C.earth);
      for (var i = 0; i < 10; i++) { var a = i / 10 * Math.PI * 2; c.blob(1.2 + Math.cos(a) * 1.1, 1.2 + Math.sin(a) * 1.1, 0.1, 0.16, 0.12, C.stone, i + 2, 0.8); }
      c.cone(1.2, 1.2, 0, 1.6, 0.8, '#4A3A2A');
      var poles = []; for (var j = 0; j < 9; j++) { var b = j / 9 * Math.PI * 2 + 0.3; poles.push([[1.2 + Math.cos(b) * 0.85, 1.2 + Math.sin(b) * 0.85, 0], [1.2 - Math.cos(b) * 0.1, 1.2 - Math.sin(b) * 0.1, 2.2]]); }
      c.seg(poles, INK, c.k * 0.07 + 1.8).seg(poles, C.post, c.k * 0.07);
    },
    plan: function (q) {
      for (var i = 0; i < 10; i++) { var a = i / 10 * Math.PI * 2; q.circ(1.2 + Math.cos(a) * 1.1, 1.2 + Math.sin(a) * 1.1, 0.16, C.stone, 0.6); }
      q.circ(1.5, 1.5, 0.85, '#1B140F', 0, ' opacity="0.2"').circ(1.2, 1.2, 0.85, '#4A3A2A', 1);
      var l = []; for (var j = 0; j < 9; j++) { var b = j / 9 * Math.PI * 2 + 0.3; l.push([1.2 + Math.cos(b) * 0.85, 1.2 + Math.sin(b) * 0.85, 1.2, 1.2]); }
      q.lines(l, C.post, 1.6); q.sim(0.4, 0.4, 1.6, 1.6);
    } });
  def({ id: 'bridge', group: 'fort', name: 'Мост', size: [12, 6], h: 2.4, sim: 'water',
    where: 'посередине реки — главная переправа и главная точка контроля',
    note: 'Настил 3,6 м на сваях: строй в линию шире моста идёт колонной. Вода по бокам — граница прохода.',
    view: function (c) {
      c.poly([[0, 0, 0], [12, 0, 0], [12, 6, 0], [0, 6, 0]], C.water, 0);
      var rip = []; for (var i = 0; i < 9; i++) { var xx = 0.6 + (i * 1.37) % 11, yy = 0.4 + (i * 2.3) % 5.2; rip.push([[xx, yy, 0], [xx + 0.8, yy, 0]]); }
      c.seg(rip, shade(C.water, 0.35), 1.4);
      var piles = [2, 5, 8, 11];
      piles.forEach(function (px) { c.cyl(px, 1.3, -0.1, 1.4, 0.15, C.post); });
      c.stick([0, 1.15, 1.95], [12, 1.15, 1.95], C.post, c.k * 0.08);
      piles.forEach(function (px) { c.stick([px, 1.15, 1.5], [px, 1.15, 2.1], C.post, c.k * 0.08); });
      c.walls(0, 1.2, 1.4, 12, 3.6, 0.18, 'planks', C.plank);
      c.poly([[0, 1.2, 1.58], [12, 1.2, 1.58], [12, 4.8, 1.58], [0, 4.8, 1.58]], shade(C.plank, 0.08));
      var pl = []; for (var x2 = 0.4; x2 < 12; x2 += 0.4) pl.push([[x2, 1.2, 1.58], [x2, 4.8, 1.58]]);
      c.seg(pl, shade(C.plank, -0.35), 0.9);
      piles.forEach(function (px) { c.cyl(px, 4.7, -0.1, 1.4, 0.15, C.post); });
      piles.forEach(function (px) { c.stick([px, 4.85, 1.5], [px, 4.85, 2.1], C.post, c.k * 0.08); });
      c.stick([0, 4.85, 1.95], [12, 4.85, 1.95], C.post, c.k * 0.08);
    },
    plan: function (q) {
      q.rect(0, 0, 12, 6, C.water, 0);
      var rip = []; for (var i = 0; i < 9; i++) { var xx = 0.6 + (i * 1.37) % 11, yy = 0.4 + (i * 2.3) % 5.2; rip.push([xx, yy, xx + 0.8, yy]); }
      q.lines(rip, shade(C.water, 0.35), 1.4);
      q.rect(0.4, 1.6, 12, 3.6, '#1B140F', 0, ' opacity="0.25"');
      q.rect(0, 1.2, 12, 3.6, C.plank, 1.2);
      var pl = []; for (var x2 = 0.4; x2 < 12; x2 += 0.4) pl.push([x2, 1.2, x2, 4.8]); q.lines(pl, shade(C.plank, -0.35), 0.8);
      q.lines([[0, 1.25, 12, 1.25], [0, 4.75, 12, 4.75]], C.post, 2.4);
      q.sim(0, 0, 12, 1.1).sim(0, 4.9, 12, 1.1);
    } });
  def({ id: 'ford', group: 'fort', name: 'Брод', size: [10, 6], h: 0.8, sim: 'water',
    where: 'у истока реки — вторая переправа',
    note: 'Мелководье с камнями, края отмечены вешками. Проходимо; позже, может быть, медленнее.',
    view: function (c) {
      c.poly([[0, 0, 0], [10, 0, 0], [10, 6, 0], [0, 6, 0]], C.water, 0);
      c.poly([[0, 1.8, 0.01], [10, 1.4, 0.01], [10, 4.6, 0.01], [0, 4.2, 0.01]], C.shallow, 0);
      for (var i = 0; i < 14; i++) c.blob(0.5 + i * 0.68, 2.2 + ((i * 7) % 5) * 0.4, 0.05, 0.22, 0.13, C.stone, i + 3, 0.8);
      [0.5, 3.5, 6.5, 9.5].forEach(function (xx, j) {
        c.stick([xx, 1.7 - j * 0.1, 0], [xx, 1.7 - j * 0.1, 1.0], C.post, c.k * 0.06);
        c.stick([xx, 4.3 + j * 0.1, 0], [xx, 4.3 + j * 0.1, 1.0], C.post, c.k * 0.06);
      });
    },
    plan: function (q) {
      q.rect(0, 0, 10, 6, C.water, 0);
      q.path('M0,18 L100,14 L100,46 L0,42 Z', C.shallow, 0);
      for (var i = 0; i < 14; i++) q.blob(0.5 + i * 0.68, 2.2 + ((i * 7) % 5) * 0.4, 0.2, C.stone, i + 3, 0.6);
      [0.5, 3.5, 6.5, 9.5].forEach(function (xx, j) { q.circ(xx, 1.7 - j * 0.1, 0.1, C.post, 0.8).circ(xx, 4.3 + j * 0.1, 0.1, C.post, 0.8); });
    } });

  // ---- Препятствия и природа
  def({ id: 'wattle', group: 'nature', name: 'Плетень', size: [6, 0.2], h: 1.3, sim: 'obstacle',
    where: 'вокруг дворов, огородов и загонов во всех селениях',
    note: 'Ивовые прутья на кольях, по пояс: стоит строй, но не держит удар.',
    view: function (c) {
      c.ground(-0.6, -0.6, 6.6, 0.8).shadow(0, 0, 6, 0.2, 1.3);
      c.poly([[0, 0.1, 0.12], [6, 0.1, 0.12], [6, 0.1, 1.15], [0, 0.1, 1.15]], '#8C6E4A', 1);
      var w = '';
      for (var zz = 0.25; zz < 1.15; zz += 0.16) {
        var d = '';
        for (var xx = 0; xx <= 6.01; xx += 0.75) { var p = c.xy(xx, 0.1, zz + ((Math.round(xx / 0.75) + Math.round(zz / 0.16)) % 2 ? 0.05 : -0.05)); d += (d ? ' L' : 'M') + f1(p[0]) + ',' + f1(p[1]); }
        w += '<path d="' + d + '" fill="none" stroke="#5E4630" stroke-width="1.6" stroke-linejoin="round"/>';
      }
      c.raw(w);
      for (var x2 = 0; x2 <= 6.01; x2 += 0.75) c.stick([x2, 0.1, 0], [x2, 0.1, 1.32], C.post, c.k * 0.06);
    },
    plan: function (q) { q.rect(0.3, 0.3, 6, 0.2, '#1B140F', 0, ' opacity="0.2"'); q.rect(0, 0, 6, 0.2, '#8C6E4A', 1); var p = []; for (var xx = 0; xx <= 6.01; xx += 0.75) p.push([xx, 0.1]); p.forEach(function (pp) { q.circ(pp[0], pp[1], 0.08, C.post, 0.6); }); q.sim(0, 0, 6, 0.2); } });
  def({ id: 'boulders', group: 'nature', name: 'Валуны', size: [5, 4], h: 1.6, sim: 'obstacle',
    where: 'поля и опушки — ледниковые камни; уже есть в песочнице как «obstacle»',
    note: 'Камни, которые пахари стаскивали к меже. Укрытие и препятствие.',
    view: function (c) {
      c.ground(-0.6, -0.6, 5.6, 4.6);
      c.rocks([[1.2, 1.2, 0.9, 1.0, 3], [3.3, 1.0, 0.7, 0.8, 5], [2.4, 2.8, 1.1, 1.3, 7], [4.2, 3.0, 0.5, 0.6, 9]], C.stone, true);
    },
    plan: function (q) {
      [[1.2, 1.2, 0.9, 3], [3.3, 1.0, 0.7, 5], [2.4, 2.8, 1.1, 7], [4.2, 3.0, 0.5, 9]].forEach(function (b) {
        q.blob(b[0] + 0.3, b[1] + 0.3, b[2], '#1B140F', b[3], 0, 9, 0.22); q.blob(b[0], b[1], b[2], C.stone, b[3], 1.1); q.blob(b[0] - 0.2, b[1] - 0.2, b[2] * 0.45, shade(C.stone, 0.2), b[3] + 1, 0);
      });
      q.sim(0.3, 0.3, 4.4, 3.4);
    } });
  def({ id: 'ore', group: 'nature', name: 'Камни с рудой', size: [5, 4], h: 1.4, sim: 'obstacle',
    where: 'на севере, за селением правителя: железо — поздний и спорный приз',
    note: 'Ржавые выходы руды, яма и куча добытого.',
    view: function (c) {
      c.ground(-0.6, -0.6, 5.6, 4.6, C.earth);
      c.disc(3.6, 2.8, 0.01, 0.9, '#3B302A', 1);
      c.rocks([[1.2, 1.3, 0.9, 1.2, 4], [2.6, 0.9, 0.7, 0.9, 8]], C.ore, false);
      for (var i = 0; i < 7; i++) c.blob(1.6 + (i % 3) * 0.35, 2.8 + (i % 2) * 0.3, 0.12 + (i % 3) * 0.05, 0.2, 0.14, shade(C.ore, -0.2), i + 5, 0.8);
    },
    plan: function (q) {
      q.circ(3.6, 2.8, 0.9, '#3B302A', 1);
      [[1.2, 1.3, 0.9, 4], [2.6, 0.9, 0.7, 8]].forEach(function (b) { q.blob(b[0] + 0.3, b[1] + 0.3, b[2], '#1B140F', b[3], 0, 9, 0.22); q.blob(b[0], b[1], b[2], C.ore, b[3], 1.1); });
      for (var i = 0; i < 7; i++) q.circ(1.6 + (i % 3) * 0.35, 2.8 + (i % 2) * 0.3, 0.16, shade(C.ore, -0.2), 0.6);
      q.sim(0.3, 0.3, 3.2, 1.9);
    } });
  def({ id: 'pine', group: 'nature', name: 'Сосна', size: [5, 5], h: 12, sim: 'obstacle',
    where: 'леса на северо-востоке и юго-западе; боры на песке у реки',
    note: 'Высокий голый ствол, крона наверху: под соснами строй проходит, ствол — препятствие 0,5 м.',
    view: function (c) {
      c.ground(0, 0, 5, 5);
      c.castShadow(4.2, 4.2, 2.6, 1.4);
      c.cyl(2.5, 2.5, 0, 9.5, 0.24, C.pineBark, 0.16);
      c.stick([2.5, 2.5, 7.2], [1.4, 2.4, 8.2], C.pineBark, c.k * 0.08).stick([2.5, 2.5, 8.0], [3.5, 2.8, 8.9], C.pineBark, c.k * 0.08);
      c.crown(2.5, 2.5, [[-1.2, -0.1, 8.2, 1.5, 0.8], [1.0, 0.3, 8.9, 1.6, 0.85], [-0.3, 0, 9.8, 2.6, 1.25], [0.2, 0.2, 11.0, 1.9, 0.95], [-0.1, 0.1, 11.9, 1.1, 0.55]], C.pine, 3);
    },
    plan: function (q) { q.blob(3.0, 3.0, 2.2, '#1B140F', 3, 0, 9, 0.22); q.blob(2.5, 2.5, 2.2, C.pine, 3, 1); q.blob(2.3, 2.3, 1.2, shade(C.pine, 0.15), 6, 0); q.sim(2.25, 2.25, 0.5, 0.5); } });
  def({ id: 'spruce', group: 'nature', name: 'Ель', size: [5, 5], h: 11, sim: 'obstacle',
    where: 'сырые низины и большой лес',
    note: 'Ветви до земли: ель — препятствие во всю крону и укрытие от глаз.',
    view: function (c) {
      c.ground(0, 0, 5, 5);
      c.castShadow(3.6, 3.6, 2.6, 1.4, 4);
      c.cyl(2.5, 2.5, 0, 1, 0.2, C.post);
      [[0.6, 4.2, 2.1], [3.2, 3.6, 1.7], [5.4, 3.2, 1.3], [7.4, 2.8, 0.9], [9.0, 2.0, 0.5]].forEach(function (t, i) {
        c.cone(2.5, 2.5, t[0], t[1], t[2], i % 2 ? C.spruce : shade(C.spruce, 0.1));
      });
    },
    plan: function (q) {
      q.circ(3.0, 3.0, 2.1, '#1B140F', 0, ' opacity="0.25"');
      var d = ''; for (var i = 0; i < 16; i++) { var a = i / 16 * Math.PI * 2, r = (i % 2 ? 1.6 : 2.15) * K; d += (i ? ' L' : 'M') + f1(2.5 * K + Math.cos(a) * r) + ',' + f1(2.5 * K + Math.sin(a) * r); }
      q.path(d + ' Z', C.spruce, 1);
      var l = []; for (var j = 0; j < 8; j++) { var b = j / 8 * Math.PI * 2; l.push([2.5, 2.5, 2.5 + Math.cos(b) * 1.6, 2.5 + Math.sin(b) * 1.6]); }
      q.lines(l, shade(C.spruce, 0.2), 1); q.sim(0.6, 0.6, 3.8, 3.8);
    } });
  def({ id: 'birch', group: 'nature', name: 'Берёза', size: [4.5, 4.5], h: 10, sim: 'obstacle',
    where: 'опушки, края полей, гари',
    note: 'Светлая лёгкая крона; ствол — препятствие 0,4 м.',
    view: function (c) {
      c.ground(0, 0, 4.5, 4.5);
      c.castShadow(3.6, 3.6, 2.2, 1.2, 5);
      c.cyl(2.25, 2.25, 0, 8, 0.17, C.birchBark, 0.1);
      c.stick([2.25, 2.25, 5.0], [1.2, 2.1, 6.4], C.birchBark, c.k * 0.07).stick([2.25, 2.25, 5.6], [3.2, 2.5, 6.9], C.birchBark, c.k * 0.07);
      var marks = []; for (var zz = 0.5; zz < 7.5; zz += 0.7) marks.push([[2.27, 2.43, zz], [2.43, 2.27, zz + 0.05]]);
      c.seg(marks, INK, 1.6);
      c.crown(2.25, 2.25, [[-1.0, 0, 6.4, 1.3, 1.0], [1.0, 0.2, 6.8, 1.3, 1.0], [0, 0, 7.8, 2.0, 1.35], [-0.4, 0.3, 9.0, 1.4, 0.95], [0.4, 0, 9.9, 0.9, 0.6]], C.birch, 7);
    },
    plan: function (q) { q.blob(2.7, 2.7, 1.9, '#1B140F', 7, 0, 9, 0.22); q.blob(2.25, 2.25, 1.9, C.birch, 7, 1); q.blob(2.0, 2.0, 1.0, shade(C.birch, 0.18), 9, 0); q.sim(2.05, 2.05, 0.4, 0.4); } });
  def({ id: 'field', group: 'nature', name: 'Поле', size: [12, 8], h: 1, sim: 'free',
    where: 'у селения правителя и на нашем берегу; на жатве — все в поле',
    note: 'Узкие полосы рожь и ячмень, межи; суслоны на сжатой полосе. Проходимо.',
    view: function (c) {
      c.poly([[0, 0, 0], [12, 0, 0], [12, 8, 0], [0, 8, 0]], C.earth, 0);
      for (var i = 0; i < 6; i++) {
        var y0 = i * 1.33 + 0.08, y1 = y0 + 1.18, col = i === 4 ? '#A98F57' : i % 2 ? C.rye : C.barley;
        c.poly([[0.1, y0, 0.02], [11.9, y0, 0.02], [11.9, y1, 0.02], [0.1, y1, 0.02]], col, 0);
        if (i !== 4) { var r = []; for (var xx = 0.5; xx < 12; xx += 0.5) r.push([[xx, y0 + 0.1, 0.02], [xx + 0.2, y1 - 0.1, 0.02]]); c.seg(r, shade(col, -0.2), 0.9); }
      }
      for (var s = 0; s < 6; s++) c.cone(1.2 + s * 1.9, 5.98, 0, 0.9, 0.35, C.hay, 0.9);
    },
    plan: function (q) {
      q.rect(0, 0, 12, 8, C.earth, 0);
      for (var i = 0; i < 6; i++) {
        var y0 = i * 1.33 + 0.08, col = i === 4 ? '#A98F57' : i % 2 ? C.rye : C.barley;
        q.rect(0.1, y0, 11.8, 1.18, col, 0);
        if (i !== 4) { var r = []; for (var xx = 0.5; xx < 12; xx += 0.5) r.push([xx, y0 + 0.1, xx + 0.2, y0 + 1.08]); q.lines(r, shade(col, -0.2), 0.9); }
      }
      for (var s = 0; s < 6; s++) q.circ(1.2 + s * 1.9, 5.98, 0.35, C.hay, 0.8);
    } });

  var GROUPS = { home: 'Жильё и постройки', craft: 'Промыслы и хозяйство', fort: 'Укрепления и переправы', nature: 'Природа и препятствия' };
  var SIM = { house: 'дом — стена «house»', obstacle: 'препятствие — стена «obstacle»', free: 'проходимо', water: 'вода: проход только по настилу или броду' };

  function find(id) { for (var i = 0; i < OBJECTS.length; i++) if (OBJECTS[i].id === id) return OBJECTS[i]; return null; }

  function view(id, opts) {
    opts = opts || {};
    var o = find(id), c = new Iso(opts.k || 14);
    o.view(c);
    return c.svg(o.name + ', вид 3/4', opts.pad);
  }
  // содержимое плана: {w, d, inner}; рамка — размер предмета + поле на тени и кроны
  function planInner(id) { var o = find(id), q = new Plan(); o.plan(q); return { w: o.size[0], d: o.size[1], inner: q.o, walls: q.walls }; }
  // прямоугольники стен в метрах плана (x восток, y юг от левого верхнего угла предмета)
  function walls(id) { return planInner(id).walls; }
  function plan(id, opts) {
    opts = opts || {};
    var o = find(id), q = new Plan(opts.sim), m = opts.margin == null ? 1.2 : opts.margin;
    o.plan(q);
    var x = -m * K, y = -m * K, w = (o.size[0] + 2 * m) * K, h = (o.size[1] + 2 * m) * K;
    return '<svg xmlns="http://www.w3.org/2000/svg" viewBox="' + f1(x) + ' ' + f1(y) + ' ' + f1(w) + ' ' + f1(h) + '"' +
      (opts.width ? ' width="' + opts.width + '"' : '') + ' role="img"><title>' + o.name + ', сверху</title>' +
      (opts.ground === false ? '' : '<rect x="' + f1(x) + '" y="' + f1(y) + '" width="' + f1(w) + '" height="' + f1(h) + '" fill="' + C.grass + '"/>') +
      q.o + '</svg>';
  }

  // ---------------------------------------------------------------- селение сверху (пример карты)
  // rot 0 — как в каталоге; 90 — повёрнут по часовой (занимает d × w)
  var VILLAGE = {
    size: [64, 44],
    items: [
      ['field', 45, 5, 0], ['field', 45, 14, 0], ['boulders', 56, 24, 0], ['pen', 35, 3, 0],
      ['house', 16, 8, 0], ['house', 27, 5, 90], ['house', 23, 22, 0], ['house-small', 7, 21, 90], ['house-small', 35, 12, 0],
      ['granary', 22, 13.5, 0], ['well', 15, 17, 0], ['woodpile', 16, 14, 0], ['haystack', 39, 20, 0], ['sauna', 34, 29.5, 0],
      ['wattle', 14, 6, 0], ['wattle', 20, 6, 0], ['wattle', 13.8, 6, 90], ['wattle', 30, 12.5, 0], ['wattle', 36, 18, 90],
      ['fish-racks', 25, 32.5, 0], ['boat', 16, 35.2, 0], ['bridge', 50, 34, 90],
      ['pine', 0, 0, 0], ['pine', 5, -1.5, 0], ['spruce', -1, 6, 0], ['pine', 1, 12, 0], ['spruce', 0, 27, 0], ['birch', 4, 30, 0],
      ['birch', 39, 26, 0], ['birch', 59, -1, 0], ['pine', 59, 28, 0], ['spruce', 9, 0, 0]
    ],
    people: [
      ['farmer', 50.5, 12.4, 90], ['spinner', 17.5, 19.6, 20], ['water', 16.6, 16.2, 180], ['elder', 20.4, 14.4, 200],
      ['fisher', 27, 31.4, 170], ['housewife', 29.6, 24.4, 260], ['girl', 25.4, 19.2, 300], ['apron-woman', 12.5, 25.8, 40]
    ],
    militia: [[10.5, 13.5], [11.5, 13.5], [10.5, 14.7], [11.5, 14.7]]
  };

  function village(PD, opts) {
    opts = opts || {};
    var W = VILLAGE.size[0], H = VILLAGE.size[1], o = opts.sim ? '' : '<style>.pd-village .simfoot{display:none}</style>';
    o += '<rect x="0" y="0" width="' + W * K + '" height="' + H * K + '" fill="' + C.grass + '"/>';
    // тропы
    o += '<path d="M500,440 L480,330 L460,260 L330,200 L190,190 L120,200" fill="none" stroke="' + C.earth + '" stroke-width="24" stroke-linecap="round" stroke-linejoin="round" opacity="0.55"/>';
    o += '<path d="M330,200 L300,290 L270,330" fill="none" stroke="' + C.earth + '" stroke-width="16" stroke-linecap="round" opacity="0.5"/>';
    // река с берегом
    o += '<path d="M0,352 C120,346 220,360 330,354 C430,348 540,358 640,350 L640,440 L0,440 Z" fill="' + C.water + '" stroke="' + shade(C.water, -0.3) + '" stroke-width="2"/>';
    var rip = '';
    for (var i = 0; i < 14; i++) rip += 'M' + (20 + i * 45) + ',' + (378 + (i % 3) * 18) + ' l22,0 ';
    o += '<path d="' + rip + '" stroke="' + shade(C.water, 0.35) + '" stroke-width="2" stroke-linecap="round"/>';
    VILLAGE.items.forEach(function (it) {
      var p = planInner(it[0]), x = it[1] * K, y = it[2] * K;
      if (it[3] === 90) o += '<g transform="translate(' + f1(x + p.d * K) + ',' + f1(y) + ') rotate(90)">' + p.inner + '</g>';
      else o += '<g transform="translate(' + f1(x) + ',' + f1(y) + ')">' + p.inner + '</g>';
    });
    if (PD) {
      var byId = {};
      PD.PEOPLE.forEach(function (e) { byId[e.id] = PD.rosterSpec(e); });
      VILLAGE.people.forEach(function (pp) {
        o += '<g transform="translate(' + f1(pp[1] * K) + ',' + f1(pp[2] * K) + ') rotate(' + pp[3] + ') scale(0.1)">' + PD.topInner(byId[pp[0]]) + '</g>';
      });
      VILLAGE.militia.forEach(function (m, i) {
        var s = PD.make({ role: 'militia', side: 'ruler', kit: i % 2 ? 'spear-shield' : 'axe-shield', exp: 'new', seed: 500 + i * 13 });
        o += '<g transform="translate(' + f1(m[0] * K) + ',' + f1(m[1] * K) + ') rotate(90) scale(0.1)">' + PD.topInner(s) + '</g>';
      });
    }
    // север и масштаб
    o += '<g transform="translate(606,40)"><path d="M0,-20 L7,4 L0,0 L-7,4 Z" fill="' + C.wool + '" stroke="' + INK + '" stroke-width="1.2"/>' +
      '<text x="0" y="22" text-anchor="middle" font-family="sans-serif" font-size="13" font-weight="700" fill="' + C.wool + '">С</text></g>';
    o += '<g transform="translate(20,418)"><rect width="100" height="6" fill="' + C.wool + '"/><rect width="50" height="6" fill="' + INK + '"/>' +
      '<text x="0" y="-6" font-family="sans-serif" font-size="13" fill="' + C.wool + '">10 м</text></g>';
    return '<svg xmlns="http://www.w3.org/2000/svg" class="pd-village" viewBox="0 0 ' + W * K + ' ' + H * K + '" role="img"><title>Селение у реки, вид сверху</title>' + o + '</svg>';
  }

  var api = { OBJECTS: OBJECTS, GROUPS: GROUPS, SIM: SIM, COLORS: C, K: K, find: find, view: view, plan: plan, planInner: planInner, walls: walls,
    VILLAGE: VILLAGE, village: village };
  root.Objects = api;
  if (typeof module !== 'undefined' && module.exports) module.exports = api;
})(typeof window !== 'undefined' ? window : globalThis);
