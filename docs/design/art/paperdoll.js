/*
 * Game_knez — бумажная кукла бойца (эскиз, не игровой ассет).
 *
 * Одна база (тело) и сменные слои: одежда, доспех, головной убор, оружие, щит, украшения.
 * Из одного описания (spec) рисуются два вида:
 *   front(spec) — фигура спереди (карточка бойца, ростер, вербовка, эталон для 3D-модели);
 *   top(spec)   — фишка вида сверху для 2D-схватки фазы 1 (смотрит вверх, 1 м = 100 единиц).
 * make({role, side, kit, exp, seed}) собирает spec по роли, стороне, набору из combat.json и опыту.
 *
 * Работает в браузере (window.Paperdoll) и в node (module.exports). Цвета — только из палитры COLORS.
 */
(function (root) {
  'use strict';

  var INK = '#2A2420';

  // ---------- Палитра: растительные красители, минеральные краски щитов, металлы ----------
  var COLORS = {
    // Шерсть и лён без краски
    'wool-cream':   { hex: '#E3D7BD', name: 'Шерсть некрашеная, светлая', group: 'cloth' },
    'wool-grey':    { hex: '#A9A396', name: 'Шерсть серая', group: 'cloth' },
    'wool-brown':   { hex: '#7B5D45', name: 'Шерсть бурая', group: 'cloth' },
    'wool-dark':    { hex: '#3F3631', name: 'Шерсть тёмная', group: 'cloth' },
    'linen':        { hex: '#D6CCB2', name: 'Лён небелёный', group: 'cloth' },
    // Растительные красители
    'madder':       { hex: '#A63A2B', name: 'Марена — красный', group: 'dye' },
    'madder-light': { hex: '#C66B4E', name: 'Марена, слабый раствор', group: 'dye' },
    'woad':         { hex: '#3F5D7E', name: 'Вайда — синий', group: 'dye' },
    'woad-dark':    { hex: '#283D57', name: 'Вайда, густой синий', group: 'dye' },
    'woad-light':   { hex: '#7F9AB2', name: 'Вайда, голубой', group: 'dye' },
    'weld':         { hex: '#CFA634', name: 'Резеда — жёлтый', group: 'dye' },
    'green':        { hex: '#5A7340', name: 'Вайда по резеде — зелёный', group: 'dye' },
    'bark':         { hex: '#6A4A30', name: 'Кора, скорлупа ореха — бурый', group: 'dye' },
    'lichen':       { hex: '#6E445E', name: 'Лишайник — лиловый', group: 'dye' },
    // Привозной шёлк (отделка у знатных)
    'silk-red':     { hex: '#7E1E3A', name: 'Шёлк багряный', group: 'silk' },
    'silk-gold':    { hex: '#C9982E', name: 'Шёлк золотистый', group: 'silk' },
    'silk-green':   { hex: '#2E6A55', name: 'Шёлк зелёный', group: 'silk' },
    // Минеральные краски для щитов
    'ochre-yellow': { hex: '#C8963A', name: 'Жёлтая охра', group: 'pigment' },
    'ochre-red':    { hex: '#9E3B26', name: 'Красная охра', group: 'pigment' },
    'chalk':        { hex: '#E8E2D2', name: 'Мел, известь', group: 'pigment' },
    'soot':         { hex: '#2B2826', name: 'Сажа', group: 'pigment' },
    'verdigris':    { hex: '#4F8A78', name: 'Ярь-медянка (редкая)', group: 'pigment' },
    'wood':         { hex: '#C29A66', name: 'Дерево без краски', group: 'pigment' },
    // Металлы, кожа, мех
    'iron':         { hex: '#767C83', name: 'Железо', group: 'metal' },
    'iron-dark':    { hex: '#474C52', name: 'Железо в тени', group: 'metal' },
    'iron-light':   { hex: '#B8BEC4', name: 'Железо, блик', group: 'metal' },
    'bronze':       { hex: '#A87735', name: 'Бронза', group: 'metal' },
    'silver':       { hex: '#C7CBCF', name: 'Серебро', group: 'metal' },
    'gold':         { hex: '#D4AF4A', name: 'Золото', group: 'metal' },
    'leather':      { hex: '#8A5A35', name: 'Кожа', group: 'leather' },
    'leather-dark': { hex: '#5B3B24', name: 'Кожа тёмная', group: 'leather' },
    'fur':          { hex: '#7A5A3C', name: 'Мех', group: 'leather' },
    'haft':         { hex: '#9C7446', name: 'Древко (ясень)', group: 'leather' },
    // Кожа лица и волосы
    'skin-1': { hex: '#EDC9AC', name: 'Кожа 1', group: 'skin' },
    'skin-2': { hex: '#E1B394', name: 'Кожа 2', group: 'skin' },
    'skin-3': { hex: '#CC9773', name: 'Кожа 3', group: 'skin' },
    'skin-4': { hex: '#A9724F', name: 'Кожа 4', group: 'skin' },
    'skin-5': { hex: '#7E5238', name: 'Кожа 5', group: 'skin' },
    'blond':       { hex: '#D6B66C', name: 'Светлые', group: 'hair' },
    'ash':         { hex: '#BDAA84', name: 'Пепельные', group: 'hair' },
    'light-brown': { hex: '#9A6C42', name: 'Русые', group: 'hair' },
    'brown':       { hex: '#5E4029', name: 'Каштановые', group: 'hair' },
    'dark':        { hex: '#33261E', name: 'Тёмные', group: 'hair' },
    'red':         { hex: '#A5512B', name: 'Рыжие', group: 'hair' },
    'grey':        { hex: '#A7A39B', name: 'Седые', group: 'hair' }
  };

  var SIDES = {
    clan:    { name: 'Клан игрока',  c1: 'ochre-yellow', c2: 'soot',  pattern: 'halves', band: 'soot' },
    ruler:   { name: 'Правитель',    c1: 'ochre-red',    c2: 'chalk', pattern: 'rim',    band: 'ochre-red' },
    bandits: { name: 'Разбойники',   c1: 'wood',         c2: 'ochre-red', pattern: 'scraps', band: 'ochre-red' }
  };

  // Наборы из content/defs/combat.json (оружие, щит, доспех, голова)
  var KITS = {
    'axe-shield':          { weapon: 'axe',   body: 'none',   head: 'none' },
    'spear-shield':        { weapon: 'spear', body: 'none',   head: 'none' },
    'axe-shield-padded':   { weapon: 'axe',   body: 'padded', head: 'cap' },
    'spear-shield-padded': { weapon: 'spear', body: 'padded', head: 'cap' },
    'sword-shield-padded': { weapon: 'sword', body: 'padded', head: 'cap' },
    'sword-shield-mail':   { weapon: 'sword', body: 'mail',   head: 'helm' },
    'axe-shield-mail':     { weapon: 'axe',   body: 'mail',   head: 'helm' }
  };

  var LABELS = {
    head: { 'none': 'Без убора', 'hood': 'Капюшон', 'cap-felt': 'Войлочная шапка', 'cap-leather': 'Кожаная шапка',
            'cap-fur': 'Меховая шапка', 'cap-birka': 'Шапка с меховой опушкой', 'helm-spangen': 'Шлем с наносником',
            'helm-guard': 'Шлем с очками и бармицей' },
    outer: { 'none': 'Рубаха', 'padded': 'Стёганка', 'mail': 'Кольчуга', 'caftan': 'Кафтан' },
    weapon: { 'axe': 'Топор', 'spear': 'Копьё', 'sword': 'Меч', 'seax': 'Сакс' },
    pattern: { 'plain': 'Одноцветный', 'halves': 'Половины', 'quarters': 'Четверти', 'spokes': 'Восемь клиньев',
               'rim': 'Кайма', 'band': 'Полоса', 'scraps': 'Старая краска' },
    hairStyle: { 'short': 'Короткие', 'long': 'Длинные', 'bald': 'Лысина' },
    beard: { 'none': 'Без бороды', 'moustache': 'Усы', 'short': 'Короткая', 'full': 'Окладистая', 'long': 'Длинная',
             'forked': 'Раздвоенная' },
    belt: { 'plain': 'Простой', 'fittings': 'С бляшками', 'oriental': 'Наборный' },
    exp: { 'new': 'новичок', 'experienced': 'опытный', 'professional': 'профессионал', 'elite': 'элита' },
    wealth: { 0: 'бедный', 1: 'обычный', 2: 'зажиточный' },
    style: { 'brooches': 'наплечные фибулы и бусы', 'shawl': 'шаль со спиралями', 'rings': 'височные кольца', 'apron': 'передник и цепочки' },
    dress: { 'shift': 'рубаха с поясом', 'apron-dress': 'сарафан на лямках', 'wrap-skirt': 'запашная юбка' },
    fhead: { 'braid': 'коса', 'wreath': 'коса и венчик', 'temple-rings': 'очелье с кольцами', 'scarf': 'платок', 'cap': 'чепец' },
    tool: { '': 'ничего', 'staff': 'посох', 'spade': 'заступ', 'sickle': 'серп', 'distaff': 'прялка' },
    carry: { '': 'ничего', 'basket': 'корзина', 'bucket': 'ведро' },
    brooches: { '': 'нет', 'oval': 'наплечные овальные', 'round': 'круглые с цепочками', 'horseshoe': 'подковообразная' },
    kitHead: { 'none': 'нет', 'cap': 'шапка', 'helm': 'шлем' }
  };

  // ---------- Утилиты ----------
  function col(k) {
    if (!k) return 'none';
    if (k.charAt(0) === '#') return k;
    return COLORS[k] ? COLORS[k].hex : '#FF00FF';
  }
  function rgb(h) {
    h = col(h).replace('#', '');
    return [parseInt(h.slice(0, 2), 16), parseInt(h.slice(2, 4), 16), parseInt(h.slice(4, 6), 16)];
  }
  function hex(c) {
    return '#' + c.map(function (v) {
      v = Math.max(0, Math.min(255, Math.round(v)));
      return (v < 16 ? '0' : '') + v.toString(16);
    }).join('');
  }
  // k < 0 темнее, k > 0 светлее
  function shade(h, k) {
    var c = rgb(h);
    if (k < 0) return hex(c.map(function (v) { return v * (1 + k); }));
    return hex(c.map(function (v) { return v + (255 - v) * k; }));
  }
  function P(d, fill, sw) {
    return '<path d="' + d + '" fill="' + fill + '" stroke="' + INK + '" stroke-width="' + (sw || 1.4) +
      '" stroke-linejoin="round"/>';
  }
  function F(d, fill, extra) { return '<path d="' + d + '" fill="' + fill + '"' + (extra || '') + '/>'; }
  function L(d, stroke, w, extra) {
    extra = extra || '';
    var cap = extra.indexOf('stroke-linecap') >= 0 ? '' : ' stroke-linecap="round"';
    return '<path d="' + d + '" fill="none" stroke="' + stroke + '" stroke-width="' + w + '"' + cap +
      ' stroke-linejoin="round"' + extra + '/>';
  }
  function clip(id, d) { return '<clipPath id="' + id + '"><path d="' + d + '"/></clipPath>'; }
  function shadowRight(id, d, x0, op) {
    return clip(id, d) + '<rect x="' + x0 + '" y="0" width="300" height="400" fill="#1B140F" opacity="' +
      (op || 0.16) + '" clip-path="url(#' + id + ')"/>';
  }
  var uid = 0;
  function nextPrefix() { uid += 1; return 'pd' + uid + '_'; }

  function rng(seed) {
    var a = seed >>> 0;
    return function () {
      a = (a + 0x6D2B79F5) | 0;
      var t = Math.imul(a ^ (a >>> 15), 1 | a);
      t = (t + Math.imul(t ^ (t >>> 7), 61 | t)) ^ t;
      return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
    };
  }
  function pick(r, arr) { return arr[Math.floor(r() * arr.length)]; }
  function chance(r, p) { return r() < p; }
  function clone(o) { return JSON.parse(JSON.stringify(o)); }

  // ---------- Узоры (pattern) ----------
  function patterns(p, s) {
    var out = '';
    // кольчуга
    out += '<pattern id="' + p + 'mail" width="4" height="3" patternUnits="userSpaceOnUse">' +
      '<rect width="4" height="3" fill="' + col('iron') + '"/>' +
      '<g fill="none" stroke="' + col('iron-dark') + '" stroke-width="0.75">' +
      '<circle cx="0" cy="0" r="1.35"/><circle cx="4" cy="0" r="1.35"/><circle cx="0" cy="3" r="1.35"/>' +
      '<circle cx="4" cy="3" r="1.35"/><circle cx="2" cy="1.5" r="1.35"/></g>' +
      '<circle cx="1.4" cy="1" r="0.35" fill="' + col('iron-light') + '"/></pattern>';
    // обмотки
    out += '<pattern id="' + p + 'wrap" width="5" height="5" patternUnits="userSpaceOnUse" patternTransform="rotate(-32)">' +
      '<rect width="5" height="5" fill="' + col(s.wraps) + '"/>' +
      '<rect width="5" height="1.1" fill="' + shade(s.wraps, -0.25) + '"/></pattern>';
    // стёганка (вид сверху)
    out += '<pattern id="' + p + 'quilt" width="5" height="5" patternUnits="userSpaceOnUse">' +
      '<rect width="5" height="5" fill="' + col(s.outerColor || 'linen') + '"/>' +
      '<rect width="0.9" height="5" fill="' + shade(s.outerColor || 'linen', -0.2) + '"/></pattern>';
    // мех
    out += '<pattern id="' + p + 'fur" width="3" height="3" patternUnits="userSpaceOnUse">' +
      '<rect width="3" height="3" fill="' + col('fur') + '"/>' +
      '<path d="M0.5,3 L1,0.6 M2,3 L2.4,1" stroke="' + shade('fur', -0.35) + '" stroke-width="0.6"/></pattern>';
    return out;
  }

  // ---------- Щит: лицевая сторона на эллипсе (cx, cy, rx, ry) ----------
  function shieldFace(sh, p, tag, rx, ry, sw) {
    var c1 = col(sh.c1), c2 = col(sh.c2 || sh.c1);
    var id = p + 'sh' + tag;
    var e = 'M' + (-rx) + ',0 A' + rx + ',' + ry + ' 0 1 1 ' + rx + ',0 A' + rx + ',' + ry + ' 0 1 1 ' + (-rx) + ',0 Z';
    var out = '<clipPath id="' + id + '"><path d="' + e + '"/></clipPath><g clip-path="url(#' + id + ')">';
    var base = sh.pattern === 'scraps' ? col('wood') : c1;
    out += '<rect x="' + (-rx - 2) + '" y="' + (-ry - 2) + '" width="' + (2 * rx + 4) + '" height="' + (2 * ry + 4) +
      '" fill="' + base + '"/>';
    function pt(a, k) { k = k || 1.2; return (Math.sin(a) * rx * k).toFixed(2) + ',' + (-Math.cos(a) * ry * k).toFixed(2); }
    switch (sh.pattern) {
      case 'halves':
        out += '<rect x="0" y="' + (-ry - 2) + '" width="' + (rx + 2) + '" height="' + (2 * ry + 4) + '" fill="' + c2 + '"/>';
        break;
      case 'quarters':
        out += '<rect x="0" y="' + (-ry - 2) + '" width="' + (rx + 2) + '" height="' + (ry + 2) + '" fill="' + c2 + '"/>';
        out += '<rect x="' + (-rx - 2) + '" y="0" width="' + (rx + 2) + '" height="' + (ry + 2) + '" fill="' + c2 + '"/>';
        break;
      case 'spokes':
        for (var i = 0; i < 8; i += 2) {
          var a0 = i * Math.PI / 4, a1 = (i + 1) * Math.PI / 4;
          out += '<path d="M0,0 L' + pt(a0) + ' L' + pt((a0 + a1) / 2, 1.45) + ' L' + pt(a1) + ' Z" fill="' + c2 + '"/>';
        }
        break;
      case 'rim':
        out += '<ellipse rx="' + (rx * 0.86) + '" ry="' + (ry * 0.86) + '" fill="none" stroke="' + c2 +
          '" stroke-width="' + (ry * 0.16).toFixed(2) + '"/>';
        break;
      case 'band':
        out += '<rect x="' + (-rx - 2) + '" y="' + (-ry * 0.2) + '" width="' + (2 * rx + 4) + '" height="' + (ry * 0.4) +
          '" fill="' + c2 + '"/>';
        break;
      case 'scraps':
        // остатки чужой краски на некрашеном дереве
        out += '<path d="M' + pt(5.6) + ' L' + pt(0.2) + ' L' + (rx * 0.15) + ',' + (-ry * 0.1) + ' L' + (-rx * 0.3) +
          ',' + (ry * 0.15) + ' Z" fill="' + c2 + '" opacity="0.85"/>';
        out += '<path d="M' + pt(2.2) + ' L' + pt(2.9) + ' L' + (rx * 0.05) + ',' + (ry * 0.45) + ' Z" fill="' + c2 +
          '" opacity="0.7"/>';
        break;
      default:
        break;
    }
    // доски
    for (var x = -rx + rx * 0.2; x < rx - 1; x += rx * 0.2) {
      out += '<line x1="' + x.toFixed(2) + '" y1="' + (-ry) + '" x2="' + x.toFixed(2) + '" y2="' + ry +
        '" stroke="#2B1B10" stroke-opacity="0.2" stroke-width="' + (sw * 0.6) + '"/>';
    }
    out += '</g>';
    if (sh.rim !== false) {
      out += '<ellipse rx="' + (rx - sw) + '" ry="' + (ry - sw) + '" fill="none" stroke="' + col('leather-dark') +
        '" stroke-width="' + (sw * 1.7) + '" stroke-opacity="0.85"/>';
    }
    out += '<ellipse rx="' + rx + '" ry="' + ry + '" fill="none" stroke="' + INK + '" stroke-width="' + sw + '"/>';
    return out;
  }

  // =====================================================================
  //  ВИД СПЕРЕДИ (viewBox 0 0 240 352; рост ~300 = 1,75 м)
  // =====================================================================
  var TUNIC = 'M100,89 L140,89 C151,91 158,95 161,105 L156,160 C158,184 161,210 163,234 L77,234 C79,210 82,184 84,160 L79,105 C82,95 89,91 100,89 Z';
  var CAFTAN = 'M100,88 L140,88 C151,90 158,95 161,105 L156,160 C159,186 162,212 165,240 L75,240 C78,212 81,186 84,160 L79,105 C82,95 89,90 100,88 Z';
  var PADDED = 'M98,87 L142,87 C153,90 160,95 163,106 L160,160 C162,178 164,194 165,208 L75,208 C76,194 78,178 80,160 L77,106 C80,95 87,90 98,87 Z';
  var MAIL = 'M99,86 L141,86 C152,89 159,95 162,106 L158,160 C160,178 162,196 163,210 L77,210 C78,196 80,178 82,160 L78,106 C81,95 88,89 99,86 Z';
  var SLEEVE_R = 'M82,98 C74,102 70,112 69,124 L63,190 C66,193 74,194 78,192 L84,132 Z';
  var SLEEVE_R_PAD = 'M81,97 C72,101 67,112 66,126 L61,190 C65,194 75,195 79,192 L85,134 Z';
  var SLEEVE_R_MAIL = 'M80,97 C72,101 68,112 67,124 L66,148 C71,151 77,151 81,149 L85,132 Z';
  var SLEEVE_L = 'M158,98 C166,102 170,112 171,124 L174,150 L160,152 L156,130 Z';
  var SLEEVE_L_PAD = 'M159,97 C168,101 172,112 173,126 L176,150 L160,152 L156,130 Z';
  var SLEEVE_L_MAIL = 'M160,97 C168,101 172,112 173,124 L175,147 L160,149 Z';
  var FACE = 'M105,52 C105,40 112,36 120,36 C128,36 135,40 135,52 L135,62 C135,73 128,80 120,80 C112,80 105,73 105,62 Z';
  var SHIELD_CX = 182, SHIELD_CY = 200, SHIELD_RX = 46, SHIELD_RY = 70;

  function outerShape(s) {
    switch (s.outer) {
      case 'padded': return { body: PADDED, sr: SLEEVE_R_PAD, sl: SLEEVE_L_PAD, belt: [80, 160] };
      case 'mail': return { body: MAIL, sr: SLEEVE_R_MAIL, sl: SLEEVE_L_MAIL, belt: [81, 159] };
      case 'caftan': return { body: CAFTAN, sr: SLEEVE_R, sl: SLEEVE_L, belt: [84, 156] };
      default: return { body: null, sr: null, sl: null, belt: [84, 156] };
    }
  }

  function frontLegs(s, p) {
    var o = '';
    var tr = col(s.trousers), trD = shade(s.trousers, -0.12);
    if (s.trouserCut === 'wide') {
      o += P('M95,198 L120,198 C121,222 119,244 116,256 L96,256 C88,240 88,218 95,198 Z', tr);
      o += P('M120,198 L145,198 C152,218 152,240 144,256 L124,256 C121,244 119,222 120,198 Z', trD);
      o += L('M96,236 C100,240 108,242 114,238 M127,238 C133,242 141,240 146,236', shade(s.trousers, -0.3), 1);
    } else {
      o += P('M97,198 L119,198 L116,258 L97,258 Z', tr);
      o += P('M121,198 L143,198 L143,258 L124,258 Z', trD);
    }
    var wl = 'M96,252 L116,252 L112,322 L96,322 Z', wr = 'M124,252 L144,252 L144,322 L128,322 Z';
    o += P(wl, 'url(#' + p + 'wrap)') + P(wr, 'url(#' + p + 'wrap)');
    o += F(wr, '#1B140F', ' opacity="0.12"');
    // подвязки обмоток
    o += L('M96,256 L116,256 M124,256 L144,256', col('leather-dark'), 2);
    var sh = col(s.shoes || 'leather');
    o += P('M95,319 L113,319 L113,331 C113,337 106,340 97,340 C90,340 87,337 89,331 Z', sh);
    o += P('M127,319 L145,319 L151,331 C153,337 150,340 143,340 C134,340 127,337 127,331 Z', shade(s.shoes || 'leather', -0.15));
    return o;
  }

  function trimBand(d, color, w) {
    return L(d, INK, w + 2.4, ' stroke-linecap="butt"') + L(d, color, w, ' stroke-linecap="butt"');
  }

  function frontTorso(s, p) {
    var o = '';
    var sh = outerShape(s);
    var tu = col(s.tunic);
    var trim = s.trim ? col(s.trim) : null;
    var at = s.trimAt || [];
    // левая (щитовая) рука: верх рукава; без щита рука рисуется целиком позже
    if (s.shield) {
      o += P(SLEEVE_L, shade(s.tunic, -0.15));
      if (sh.sl) o += P(sh.sl, s.outer === 'mail' ? 'url(#' + p + 'mail)' : shade(s.outerColor, -0.12));
    }
    // рубаха
    o += P(TUNIC, tu);
    o += L('M98,176 L94,232 M120,178 L120,232 M140,176 L146,232', shade(s.tunic, -0.18), 1);
    o += shadowRight(p + 'tun', TUNIC, 141);
    if (trim && at.indexOf('hem') >= 0 && s.outer !== 'caftan') o += trimBand('M78,229 L162,229', trim, 4.4);
    // ворот рубахи (виден, если нет кольчуги/стёганки/кафтана поверх)
    if (s.outer === 'none' || !s.outer) {
      o += L('M112,89 C114,96 126,96 128,89', INK, 1.2) + L('M120,95 L120,108', INK, 1.1);
      if (trim && at.indexOf('neck') >= 0) {
        o += trimBand('M110,89 C112,98 128,98 130,89', trim, 3.6) + trimBand('M120,97 L120,110', trim, 3);
      }
    }
    // верхняя одежда
    if (s.outer === 'padded') {
      var pc = col(s.outerColor);
      o += P(PADDED, pc);
      o += clip(p + 'padc', PADDED) + '<g clip-path="url(#' + p + 'padc)">';
      for (var x = 86; x < 160; x += 7) {
        if (Math.abs(x - 120) < 3) continue;
        o += L('M' + x + ',96 L' + x + ',208', shade(s.outerColor, -0.2), 0.9);
      }
      o += L('M78,184 L162,184', shade(s.outerColor, -0.2), 0.9);
      o += '</g>';
      o += shadowRight(p + 'pad', PADDED, 141);
      o += L('M120,92 L120,208', INK, 1.2);
      o += L('M117,112 L123,112 M117,132 L123,132', col('leather-dark'), 1.6);
      // стоячий ворот
      o += P('M107,82 L133,82 L135,93 C126,96 114,96 105,93 Z', shade(s.outerColor, -0.06));
      if (s.patched) {
        o += P('M88,120 L102,118 L103,134 L89,135 Z', col('wool-grey'), 1) + P('M132,186 L146,186 L147,199 L131,200 Z', col('wool-brown'), 1);
      }
    } else if (s.outer === 'mail') {
      o += P(MAIL, 'url(#' + p + 'mail)');
      o += shadowRight(p + 'mai', MAIL, 140, 0.22);
      // ворот: под кольчугой видна рубаха/стёганка
      o += P('M109,86 C111,95 129,95 131,86 Z', shade(s.tunic, -0.05), 1.2);
      if (trim && at.indexOf('neck') >= 0) o += trimBand('M110,87 C112,94 128,94 130,87', trim, 3);
      // зубчатый подол
      var hem = 'M77,210';
      for (var hx = 77; hx < 163; hx += 4.3) hem += ' q2.15,3.4 4.3,0';
      o += L(hem, col('iron-dark'), 1.4);
    } else if (s.outer === 'caftan') {
      var cc = col(s.outerColor);
      o += P(CAFTAN, cc);
      o += shadowRight(p + 'caf', CAFTAN, 141);
      o += L('M99,178 L95,238 M138,178 L143,238', shade(s.outerColor, -0.2), 1);
      var edge = 'M108,89 C117,104 132,118 146,134 L150,240';
      o += trimBand(edge, trim || col('silk-gold'), 5.2);
      o += trimBand('M76,236 L164,236', trim || col('silk-gold'), 5);
      o += trimBand('M104,89 C112,94 128,94 136,89', trim || col('silk-gold'), 4);
      [[112, 96], [118, 104], [125, 111], [132, 118]].forEach(function (b) {
        o += '<circle cx="' + b[0] + '" cy="' + b[1] + '" r="2" fill="' + col(s.metal || 'bronze') + '" stroke="' + INK + '" stroke-width="0.8"/>';
      });
    }
    // пояс
    var bx0 = sh.belt[0], bx1 = sh.belt[1];
    var beltY = 158;
    o += P('M' + bx0 + ',' + beltY + ' L' + bx1 + ',' + beltY + ' L' + bx1 + ',' + (beltY + 8) + ' L' + bx0 + ',' + (beltY + 8) + ' Z', col('leather-dark'), 1.2);
    var bm = s.belt === 'plain' ? col('iron') : col(s.metal || 'bronze');
    if (s.belt === 'fittings' || s.belt === 'oriental') {
      var step = s.belt === 'oriental' ? 6 : 9;
      for (var fx = bx0 + 5; fx < bx1 - 4; fx += step) {
        if (fx > 110 && fx < 130) continue;
        o += '<rect x="' + (fx - 1.8) + '" y="' + (beltY + 2) + '" width="3.6" height="4" rx="0.8" fill="' + bm + '" stroke="' + INK + '" stroke-width="0.6"/>';
        if (s.belt === 'oriental') o += L('M' + fx + ',' + (beltY + 8) + ' L' + fx + ',' + (beltY + 12), bm, 1.4);
      }
    }
    o += P('M114,' + (beltY - 2) + ' L126,' + (beltY - 2) + ' L126,' + (beltY + 10) + ' L114,' + (beltY + 10) + ' Z', bm, 1.1);
    o += L('M117,' + (beltY + 4) + ' L123,' + (beltY + 4), INK, 1);
    o += P('M124,' + (beltY + 6) + ' L129,' + (beltY + 6) + ' L129,' + (beltY + 26) + ' L124,' + (beltY + 26) + ' Z', col('leather-dark'), 1);
    o += '<rect x="124" y="' + (beltY + 22) + '" width="5" height="4" fill="' + bm + '"/>';
    // кошель
    o += P('M84,166 L98,166 L100,186 C96,190 86,190 82,186 Z', col('leather'), 1.1);
    o += P('M84,166 L98,166 L99,174 C93,177 88,177 83,174 Z', col('leather-dark'), 1);
    // сакс на поясе, горизонтально
    if (s.sidearm === 'seax') {
      o += P('M100,169 L140,168 C143,170 143,174 139,176 L100,176 Z', col('leather-dark'), 1.1);
      o += L('M106,169.5 L106,175.5 M116,169.2 L116,175.6 M126,169 L126,175.8', col(s.metal || 'bronze'), 1.2);
      o += P('M86,170 L100,169.5 L100,175.5 L86,175 Z', col('haft'), 1.1);
    }
    return o;
  }

  function frontCloakBack(s) {
    if (!s.cloak) return '';
    var lining = s.cloak.lining ? col(s.cloak.lining) : shade(s.cloak.color, -0.3);
    return P('M88,94 C78,140 70,220 62,292 C90,300 150,300 178,292 C170,220 162,140 152,94 Z', lining) +
      P('M88,94 C80,140 74,214 68,286 L80,288 C84,214 88,150 92,100 Z', col(s.cloak.color));
  }

  function frontCloakFront(s) {
    if (!s.cloak) return '';
    var c = col(s.cloak.color);
    var d = 'M90,99 C102,110 126,112 150,103 C157,100 162,100 165,104 C170,112 172,124 173,134 C164,128 152,122 140,121 C120,121 102,114 90,104 Z';
    return P(d, c) + L('M104,110 C118,116 136,116 152,108', shade(s.cloak.color, -0.22), 1) +
      L('M150,103 C156,110 160,118 162,126', shade(s.cloak.color, -0.22), 1);
  }

  function brooch(s, x, y) {
    if (!s.cloak) return '';
    var m = col(s.metal || 'bronze');
    if (s.brooch === 'disc') {
      return '<circle cx="' + x + '" cy="' + y + '" r="5.5" fill="' + m + '" stroke="' + INK + '" stroke-width="1"/>' +
        '<circle cx="' + x + '" cy="' + y + '" r="2" fill="' + shade(s.metal || 'bronze', -0.35) + '"/>';
    }
    return L('M' + (x - 7) + ',' + (y - 7) + ' L' + (x + 8) + ',' + (y + 8), INK, 3) +
      L('M' + (x - 7) + ',' + (y - 7) + ' L' + (x + 8) + ',' + (y + 8), m, 1.6) +
      '<circle cx="' + x + '" cy="' + y + '" r="5.6" fill="none" stroke="' + INK + '" stroke-width="3.6" stroke-dasharray="28 7.2"/>' +
      '<circle cx="' + x + '" cy="' + y + '" r="5.6" fill="none" stroke="' + m + '" stroke-width="2" stroke-dasharray="28 7.2"/>';
  }

  function neckRing(s) {
    if (!s.neckRing) return '';
    var m = col(s.neckRing);
    var d = 'M106,88 C108,101 132,101 134,88';
    return L(d, INK, 4.6) + L(d, m, 3) + L(d, shade(s.neckRing, -0.35), 3, ' stroke-dasharray="1.2 2.4" stroke-linecap="butt"');
  }

  function frontHead(s, p) {
    var o = '';
    var sk = col(s.skin), hr = col(s.hair);
    var h = s.head || 'none';
    var covered = h !== 'none';
    // волосы сзади
    if (s.hairStyle === 'long' && h !== 'hood') {
      o += P('M101,50 C99,34 109,29 120,29 C131,29 141,34 139,50 L142,96 C136,99 130,97 127,92 L113,92 C110,97 104,99 98,96 Z', shade(s.hair, -0.12));
    }
    // шея
    o += P('M112,72 L128,72 L129,93 L111,93 Z', shade(s.skin, -0.12), 1.2);
    // капюшон: основа и пелерина
    if (h === 'hood') {
      var hc = col(s.headColor || 'wool-brown');
      o += P('M101,58 C99,36 108,27 120,27 C132,27 141,36 139,58 C140,72 141,84 150,98 C134,104 106,104 90,98 C99,84 100,72 101,58 Z', hc);
      o += shadowRight(p + 'hood', 'M101,58 C99,36 108,27 120,27 C132,27 141,36 139,58 C140,72 141,84 150,98 C134,104 106,104 90,98 C99,84 100,72 101,58 Z', 132);
    }
    // бармица
    if (h === 'helm-guard') {
      o += P('M100,52 L99,82 C103,92 112,96 120,96 C128,96 137,92 141,82 L140,52 Z', 'url(#' + p + 'mail)');
    }
    // уши
    if (h !== 'hood' && h !== 'helm-guard') {
      o += '<ellipse cx="104.5" cy="60" rx="3.4" ry="5.6" fill="' + sk + '" stroke="' + INK + '" stroke-width="1.1"/>';
      o += '<ellipse cx="135.5" cy="60" rx="3.4" ry="5.6" fill="' + shade(s.skin, -0.08) + '" stroke="' + INK + '" stroke-width="1.1"/>';
    }
    // лицо
    o += P(FACE, sk);
    o += shadowRight(p + 'face', FACE, 128, 0.1);
    o += '<ellipse cx="113.6" cy="59" rx="1.7" ry="1.9" fill="' + INK + '"/><ellipse cx="126.4" cy="59" rx="1.7" ry="1.9" fill="' + INK + '"/>';
    o += L('M109.5,54.2 L117,53.4 M123,53.4 L130.5,54.2', shade(s.hair, -0.3), 1.8);
    o += L('M120,58 L117.8,66.4 C119,67.6 121.2,67.6 122.2,66.4', shade(s.skin, -0.32), 1.2);
    // борода
    var b = s.beard || 'none';
    var cut = ' C134,66 130,70 127,71 C124,69 116,69 113,71 C110,70 106,66 104,58 Z';
    var beards = {
      'short': 'M105,60 C105,74 111,84 120,85 C129,84 135,74 135,60 C133,66 130,70 127,71 C124,69 116,69 113,71 C110,70 107,66 105,60 Z',
      'full': 'M104,58 C103,78 110,95 120,97 C130,95 137,78 136,58' + cut,
      'long': 'M104,58 C103,80 110,104 120,111 C130,104 137,80 136,58' + cut,
      'forked': 'M104,58 C103,80 108,98 114,107 L120,97 L126,107 C132,98 137,80 136,58' + cut
    };
    if (beards[b]) {
      o += P(beards[b], hr, 1.2);
      o += L('M114,78 L115,88 M120,80 L120,92 M126,78 L125,88', shade(s.hair, -0.25), 0.9);
    } else if (b === 'none') {
      o += L('M116.5,72 Q120,73.4 123.5,72', shade(s.skin, -0.4), 1.2);
    }
    if (b !== 'none') {
      o += P('M111,71 C113,67.5 117,67 120,68.2 C123,67 127,67.5 129,71 C126,70.5 123,70.6 120,71 C117,70.6 114,70.5 111,71 Z', hr, 1);
    }
    // волосы спереди
    if (!covered) {
      if (s.hairStyle === 'bald') {
        o += P('M104,58 C103,50 105,46 108,44 L109,56 Z', hr, 1) + P('M136,58 C137,50 135,46 132,44 L131,56 Z', hr, 1);
      } else {
        o += P('M104,56 C102,40 110,32 120,32 C130,32 138,40 136,56 C134,48 130,44 126,43 C120,46 112,45 108,44 C106,47 105,51 104,56 Z', hr, 1.2);
        o += L('M112,37 C116,40 122,40 128,37', shade(s.hair, -0.22), 0.9);
      }
    }
    // головной убор
    var hcol = col(s.headColor || 'wool-brown');
    switch (h) {
      case 'hood':
        o += L('M104,62 C103,44 110,37 120,37 C130,37 137,44 136,62', INK, 6.2) +
             L('M104,62 C103,44 110,37 120,37 C130,37 137,44 136,62', hcol, 4.2);
        break;
      case 'cap-felt':
        o += P('M103,52 C102,36 110,26 120,25 C130,26 138,36 137,52 C126,49 114,49 103,52 Z', hcol);
        o += shadowRight(p + 'cap', 'M103,52 C102,36 110,26 120,25 C130,26 138,36 137,52 C126,49 114,49 103,52 Z', 128);
        o += P('M102,48 C114,45 126,45 138,48 L138,54 C126,51 114,51 102,54 Z', shade(s.headColor || 'wool-brown', -0.18), 1.1);
        break;
      case 'cap-leather':
        o += P('M103,54 C102,36 110,28 120,28 C130,28 138,36 137,54 Z', col('leather'));
        o += shadowRight(p + 'cap', 'M103,54 C102,36 110,28 120,28 C130,28 138,36 137,54 Z', 128);
        o += L('M120,28 L120,51 M106,42 C114,38 126,38 134,42', col('leather-dark'), 2.4);
        o += P('M102,50 L138,50 L138,55 L102,55 Z', col('leather-dark'), 1.1);
        break;
      case 'cap-fur':
        o += P('M102,54 C100,36 109,27 120,27 C131,27 140,36 138,54 Z', 'url(#' + p + 'fur)');
        o += shadowRight(p + 'cap', 'M102,54 C100,36 109,27 120,27 C131,27 140,36 138,54 Z', 128);
        o += L('M102,54 C100,36 109,27 120,27 C131,27 140,36 138,54', shade('fur', 0.25), 2, ' stroke-dasharray="1 2.2"');
        o += L('M102,53 C114,50 126,50 138,53', shade('fur', -0.4), 1.2);
        break;
      case 'cap-birka':
        o += P('M106,48 C108,34 114,22 121,15 C127,23 133,35 134,48 Z', hcol);
        o += shadowRight(p + 'cap', 'M106,48 C108,34 114,22 121,15 C127,23 133,35 134,48 Z', 124);
        o += L('M121,16 L120,46', shade(s.headColor || 'madder', -0.25), 1);
        o += P('M118.5,17 L121,9 L123.5,17 Z', col(s.metal || 'silver'), 1);
        o += P('M101,45 C114,40 126,40 139,45 L139,56 C126,51 114,51 101,56 Z', 'url(#' + p + 'fur)', 1.3);
        o += L('M102,46 C114,41 126,41 138,46', shade('fur', 0.25), 2, ' stroke-dasharray="1 2.2"');
        break;
      case 'helm-spangen':
      case 'helm-guard':
        var dome = h === 'helm-guard' ? 'M102,55 C101,36 109,26 120,25 C131,26 139,36 138,55 Z'
                                      : 'M102,54 C101,37 108,25 120,22 C132,25 139,37 138,54 Z';
        var topY = h === 'helm-guard' ? 25 : 22;
        o += P(dome, col('iron'));
        o += shadowRight(p + 'helm', dome, 126, 0.25);
        o += L('M106,46 C106,38 110,31 115,28', col('iron-light'), 2.2);
        var bandC = h === 'helm-guard' ? col('bronze') : col('iron-dark');
        o += L('M120,' + topY + ' L120,49', INK, 4.4) + L('M120,' + topY + ' L120,49', bandC, 2.6);
        o += L('M120,' + topY + ' C112,31 108,40 107.5,49 M120,' + topY + ' C128,31 132,40 132.5,49', INK, 3.4) +
             L('M120,' + topY + ' C112,31 108,40 107.5,49 M120,' + topY + ' C128,31 132,40 132.5,49', bandC, 1.8);
        o += P('M101,48 L139,48 L139,55 L101,55 Z', col('iron-dark'), 1.2);
        for (var rx = 104; rx <= 136; rx += 8) o += '<circle cx="' + rx + '" cy="51.5" r="0.9" fill="' + col('iron-light') + '"/>';
        if (h === 'helm-spangen') {
          o += P('M117.6,54 L122.4,54 L121.6,69 L118.4,69 Z', col('iron'), 1.1);
        } else {
          // очки
          o += '<path d="M103,54 L137,54 L137,58 C137,65 131,67 127,65 L122.4,61 L121.6,70 L118.4,70 L117.6,61 L113,65 C109,67 103,65 103,58 Z ' +
               'M108.6,57 C107.4,57 106.6,58.2 106.8,59.6 C107.2,62 110.4,63.4 113,62.6 C115.4,61.8 116.2,59.6 115.4,58 C114.8,57 113.4,57 112.2,57 Z ' +
               'M131.4,57 C132.6,57 133.4,58.2 133.2,59.6 C132.8,62 129.6,63.4 127,62.6 C124.6,61.8 123.8,59.6 124.6,58 C125.2,57 126.6,57 127.8,57 Z" ' +
               'fill="' + col('iron') + '" fill-rule="evenodd" stroke="' + INK + '" stroke-width="1.1" stroke-linejoin="round"/>';
          o += L('M104.5,58.5 C106,63.5 110,64.5 113,63.8', col('bronze'), 1.1);
        }
        break;
      default:
        break;
    }
    return o;
  }

  // оружие в правой руке (кисть около 69,199)
  // орудия мирных: в правой руке (кисть около 69,199)
  function frontTool(s) {
    var t = s.tool, wd = col('haft'), o = '';
    if (t === 'staff') {
      o += P('M67.4,62 L71.4,62 L71,341 L67.8,341 Z', shade('haft', -0.1), 1.1);
      o += P('M65.4,64 C64.6,56 66.6,51 69.4,51 C72.2,51 74.2,56 73.4,64 Z', shade('haft', -0.3), 1.1);
    } else if (t === 'spade') {
      o += P('M67.6,156 L71.4,156 L71.2,300 L67.8,300 Z', wd, 1.1);
      o += P('M62,150 L77,150 L77,156 L62,156 Z', wd, 1.1);
      o += P('M60,296 L79,296 L78,332 C74,337 65,337 61,332 Z', shade('wood', -0.1), 1.2);
      o += L('M61.4,332.6 C65,337 74,337 77.6,332.6', col('iron'), 2.6);
    } else if (t === 'sickle') {
      o += P('M67.4,204 L71.6,204 L71.4,226 L67.6,226 Z', wd, 1);
      o += P('M69.5,224 C56,226 44,238 46,256 C47,266 56,272 64,268 C55,265 51,256 53,247 C56,236 63,231 71,230 Z', col('iron-light'), 1.1);
    } else if (t === 'distaff') {
      o += P('M67.8,74 L71.2,74 L71,212 L68,212 Z', wd, 1);
      o += P('M69.5,40 C80,42 84,54 80,66 C84,74 78,84 69.5,84 C61,84 55,74 59,66 C55,54 59,42 69.5,40 Z', col('wool-cream'), 1.1);
      o += L('M63,52 C68,56 72,56 77,52 M62,66 C68,70 72,70 78,66', shade('wool-cream', -0.18), 0.9);
      o += L('M76,80 C80,130 80,190 76,236', shade('wool-cream', -0.25), 0.8);
      o += P('M75,232 L77,232 L76.8,264 L75.2,264 Z', wd, 0.8);
      o += '<ellipse cx="76" cy="256" rx="4.6" ry="2" fill="' + col('bronze') + '" stroke="' + INK + '" stroke-width="0.8"/>';
    }
    return o;
  }

  // ноша в левой руке (кисть около 170,199)
  function frontCarry(s) {
    var c = s.carry, o = '';
    if (c === 'basket') {
      o += L('M156,216 C156,194 185,194 185,216', INK, 4.4) + L('M156,216 C156,194 185,194 185,216', col('haft'), 2.6);
      o += P('M150,214 L191,214 L187,246 C180,251 161,251 154,246 Z', col('wood'), 1.3);
      o += L('M151,222 L190,222 M152,230 L189,230 M153,238 L188,238', shade('wood', -0.3), 1);
      o += L('M160,214 L161,249 M170.5,214 L170.5,250 M181,214 L180,249', shade('wood', -0.22), 0.9);
    } else if (c === 'bucket') {
      o += L('M157,218 C157,194 184,194 184,218', INK, 2.6) + L('M157,218 C157,194 184,194 184,218', col('iron-dark'), 1.3);
      o += P('M155,216 L186,216 L183,252 L158,252 Z', shade('wood', -0.08), 1.3);
      o += L('M155.6,223 L185.4,223 M157.6,245 L183.4,245', col('iron-dark'), 2);
      o += L('M163,216 L164,252 M171,216 L171,252 M178,216 L177,252', shade('wood', -0.25), 0.8);
      o += '<ellipse cx="170.5" cy="216" rx="15.5" ry="3" fill="' + shade('wood', -0.4) + '" stroke="' + INK + '" stroke-width="1"/>';
    }
    return o;
  }

  function frontWeapon(s) {
    var o = '';
    var w = s.weapon;
    if (!w || w === 'none') return frontTool(s);
    var ir = col('iron'), irL = col('iron-light'), irD = col('iron-dark'), wd = col('haft');
    if (w === 'spear') {
      o += P('M66.8,44 L70.6,44 L70.6,341 L66.8,341 Z', wd, 1.1);
      o += P('M66,40 L71.4,40 L71,54 L66.4,54 Z', irD, 1);
      o += P('M62.6,47 L75,47 L75,50 L62.6,50 Z', irD, 1);
      o += P('M68.7,4 C73.8,14 75.4,26 72.4,36 L70.4,41 L67,41 L65,36 C62,26 63.6,14 68.7,4 Z', ir, 1.2);
      o += L('M68.7,9 L68.7,38', irL, 1.2);
      return o;
    }
    var g = '';
    if (w === 'axe') {
      g += P('M-2.2,34 L2.2,34 L2.8,-92 L-2.8,-92 Z', wd, 1.1);
      g += P('M-4.4,-96 L4.4,-96 L4.4,-78 L-4.4,-78 Z', irD, 1.1);
      g += P('M-4.4,-93 L-14,-91 C-20,-93 -25,-97 -28.5,-100 C-32,-90 -33,-75 -30,-62 C-26,-66 -21,-72 -15,-77 L-4.4,-81 Z', ir, 1.2);
      g += L('M-28.4,-97 C-31,-88 -31.6,-75 -29.4,-65', irL, 1.6);
      return o + '<g transform="translate(69,199) rotate(-8)">' + g + '</g>';
    }
    if (w === 'sword') {
      g += P('M-4,-12 L4,-12 L3.4,-128 L0,-140 L-3.4,-128 Z', irL, 1.2);
      g += L('M0,-16 L0,-124', ir, 1.6);
      g += P('M-11,-13 L11,-13 L10,-9 L-10,-9 Z', col(s.metal || 'bronze'), 1.1);
      g += P('M-2.6,-9 L2.6,-9 L2.6,9 L-2.6,9 Z', col('leather-dark'), 1);
      g += P('M-7.5,9 L7.5,9 L7.5,12 L-7.5,12 Z', col(s.metal || 'bronze'), 1);
      g += P('M-6,12 C-6,17 -3,20 0,20 C3,20 6,17 6,12 Z', col(s.metal || 'bronze'), 1);
      return o + '<g transform="translate(69,199) rotate(-10)">' + g + '</g>';
    }
    if (w === 'seax') {
      g += P('M-4,-10 L4,-10 L4,-56 L-3,-74 L-4,-60 Z', irL, 1.2);
      g += L('M2,-14 L2,-54', ir, 1.1);
      g += P('M-5,-12 L5,-12 L5,-9 L-5,-9 Z', col('bronze'), 1);
      g += P('M-3,-9 L3,-9 L3.5,14 L-3.5,14 Z', wd, 1);
      return o + '<g transform="translate(69,199) rotate(-12)">' + g + '</g>';
    }
    return o;
  }

  function armContent(s, p, isLeft) {
    var o = '';
    var trim = s.trim ? col(s.trim) : null;
    var at = s.trimAt || [];
    var sleeve = SLEEVE_R;
    // рукав рубахи
    var sleeveColor = col(s.tunic);
    if (s.outer === 'padded') {
      sleeve = SLEEVE_R_PAD;
      o += P(SLEEVE_R_PAD, col(s.outerColor));
      o += L('M65,140 L83,141 M64,160 L81,161 M63,178 L80,179', shade(s.outerColor, -0.2), 0.9);
    } else if (s.outer === 'caftan') {
      o += P(SLEEVE_R, col(s.outerColor));
      o += trimBand('M64,183.5 L78.6,184.5', trim || col('silk-gold'), 6);
    } else {
      o += P(SLEEVE_R, sleeveColor);
      if (trim && at.indexOf('cuffs') >= 0) o += trimBand('M64.2,184.5 L78.4,185.4', trim, 4.4);
      if (s.outer === 'mail') o += P(SLEEVE_R_MAIL, 'url(#' + p + 'mail)');
    }
    // левая рука в тени
    if (isLeft) o += F(sleeve, '#1B140F', ' opacity="0.13"');
    // браслет
    if (s.armRing && !isLeft) {
      var m = col(s.armRing);
      o += '<ellipse cx="70.6" cy="179" rx="8.4" ry="2.6" fill="none" stroke="' + INK + '" stroke-width="3.8"/>';
      o += '<ellipse cx="70.6" cy="179" rx="8.4" ry="2.6" fill="none" stroke="' + m + '" stroke-width="2.2"/>';
    }
    // кисть: сжата, если что-то держит
    var sk = col(s.skin);
    var holds = isLeft ? !!s.carry : !!((s.weapon && s.weapon !== 'none') || s.tool);
    o += P('M61.5,192 C61.5,189 64,188 66,188 L73,188 C76,188 77.4,190 77.4,193 L77.4,204 C77.4,207 75.4,208.6 72.4,208.6 L66,208.6 C63,208.6 61.5,207 61.5,204 Z', sk, 1.2);
    if (holds) o += L('M62.4,196.5 L76.4,196.5 M62.4,200.8 L76.4,200.8 M62.4,204.8 L75.6,204.8', shade(s.skin, -0.28), 0.9);
    else o += L('M64,203 L64,207 M67.6,204 L67.6,208', shade(s.skin, -0.28), 0.9);
    return o;
  }
  function frontRightArm(s, p) { return armContent(s, p, false); }
  function frontLeftArm(s, p) { return '<g transform="matrix(-1,0,0,1,240,0)">' + armContent(s, p, true) + '</g>'; }

  function frontShield(s, p) {
    if (!s.shield) return '';
    var o = '<g transform="translate(' + SHIELD_CX + ',' + SHIELD_CY + ')">';
    o += '<ellipse cx="4.2" cy="0" rx="' + SHIELD_RX + '" ry="' + SHIELD_RY + '" fill="' + shade('wood', -0.45) + '" stroke="' + INK + '" stroke-width="1.4"/>';
    o += shieldFace(s.shield, p, 'f', SHIELD_RX, SHIELD_RY, 1.5);
    // умбон
    o += '<ellipse rx="11.5" ry="16.5" fill="' + col('iron-dark') + '" stroke="' + INK + '" stroke-width="1.2"/>';
    o += '<ellipse cx="1.6" rx="8.2" ry="12" fill="' + col('iron') + '" stroke="' + INK + '" stroke-width="1"/>';
    o += '<ellipse cx="-1.4" cy="-4.5" rx="2.6" ry="4" fill="' + col('iron-light') + '"/>';
    [[-9.4, -6], [-9.4, 6], [9.8, -6], [9.8, 6]].forEach(function (r) {
      o += '<circle cx="' + r[0] + '" cy="' + r[1] + '" r="0.9" fill="' + col('iron-light') + '"/>';
    });
    o += '</g>';
    return o;
  }

  // ---------- Женская фигура (ниже на 5 %, платье до щиколоток) ----------
  var SHIFT = 'M102,90 L138,90 C148,92 154,96 157,105 L152,160 C156,200 160,260 165,322 L75,322 C80,260 84,200 88,160 L83,105 C86,96 92,92 102,90 Z';
  var APRON_DRESS = 'M90,114 L150,114 L153,160 C157,205 160,252 163,302 L77,302 C80,252 83,205 87,160 Z';
  var SKIRT = 'M87,158 L153,158 C157,205 161,262 166,320 L74,320 C79,262 83,205 87,158 Z';
  var SHAWL = 'M84,95 C96,90 144,90 156,95 C161,104 161,118 157,132 C155,160 155,184 153,204 C145,208 137,206 131,200 C130,162 127,134 120,122 C113,134 110,162 109,200 C103,206 95,208 87,204 C85,184 85,160 83,132 C79,118 79,104 84,95 Z';
  var APRON = 'M99,166 L141,166 L143,266 L97,266 Z';
  var FACE_F = 'M106,52 C106,41 112,37 120,37 C128,37 134,41 134,52 L134,62 C134,72 127,78 120,78 C113,78 106,72 106,62 Z';
  var SCARF = 'M101,58 C99,35 108,27 120,27 C132,27 141,35 139,58 C141,74 144,88 150,100 C134,106 106,106 90,100 C96,88 99,74 101,58 Z';
  var BEADS = ['#C9822B', '#3E6E9E', '#A63A2B', '#E8E2D2', '#2E6A55'];

  function spiralBorder(d, metal) {
    return L(d, INK, 4.2) + L(d, col(metal || 'bronze'), 2.8) +
      L(d, shade(metal || 'bronze', -0.4), 2.8, ' stroke-dasharray="0.9 2.1" stroke-linecap="butt"');
  }
  function beadString(x0, y0, cx, cy, x1, y1, n) {
    var o = '';
    for (var i = 0; i <= n; i++) {
      var t = i / n, a = (1 - t) * (1 - t), b = 2 * (1 - t) * t, c = t * t;
      var x = a * x0 + b * cx + c * x1, y = a * y0 + b * cy + c * y1;
      o += '<circle cx="' + x.toFixed(1) + '" cy="' + y.toFixed(1) + '" r="1.9" fill="' + BEADS[i % BEADS.length] + '" stroke="' + INK + '" stroke-width="0.6"/>';
    }
    return o;
  }

  function femaleHead(s, p) {
    var o = '', sk = col(s.skin), hr = col(s.hair), h = s.fhead || 'braid';
    var hc = col(s.headColor || 'linen');
    if (h === 'braid' || h === 'wreath') {
      o += P('M102,50 C100,35 110,30 120,30 C130,30 140,35 138,50 L140,92 C134,95 129,94 127,90 L113,90 C111,94 106,95 100,92 Z', shade(s.hair, -0.12));
    }
    o += P('M113,70 L127,70 L128,93 L112,93 Z', shade(s.skin, -0.12), 1.2);
    if (h === 'scarf') {
      o += P(SCARF, hc);
      o += shadowRight(p + 'scarf', SCARF, 132);
    }
    if (h !== 'scarf') {
      o += '<ellipse cx="105.2" cy="60" rx="3" ry="5" fill="' + sk + '" stroke="' + INK + '" stroke-width="1.1"/>';
      o += '<ellipse cx="134.8" cy="60" rx="3" ry="5" fill="' + shade(s.skin, -0.08) + '" stroke="' + INK + '" stroke-width="1.1"/>';
    }
    o += P(FACE_F, sk);
    o += shadowRight(p + 'ff', FACE_F, 128, 0.1);
    o += '<ellipse cx="114" cy="59" rx="1.6" ry="1.8" fill="' + INK + '"/><ellipse cx="126" cy="59" rx="1.6" ry="1.8" fill="' + INK + '"/>';
    o += L('M110.5,54.4 L117,53.8 M123,53.8 L129.5,54.4', shade(s.hair, -0.3), 1.2);
    o += L('M120,58.5 L118.4,65.6 C119.4,66.6 121,66.6 121.8,65.8', shade(s.skin, -0.32), 1.1);
    o += L('M116.8,70.4 Q120,71.8 123.2,70.4', shade('madder', -0.1), 1.3);
    if (h === 'scarf') {
      o += L('M104,62 C103,45 110,38 120,38 C130,38 137,45 136,62', INK, 6.4) +
           L('M104,62 C103,45 110,38 120,38 C130,38 137,45 136,62', hc, 4.4);
      return o;
    }
    // волосы спереди, пробор
    o += P('M105,56 C104,40 111,33 120,33 C129,33 136,40 135,56 C133,47 127,43 120,43 C113,43 107,47 105,56 Z', hr, 1.2);
    o += L('M120,33.5 L120,42.5', shade(s.hair, -0.3), 0.9);
    var m = col(s.metal || 'bronze');
    if (h === 'cap') {
      o += P('M104,53 C103,37 110,29 120,29 C130,29 137,37 136,53 C130,48 110,48 104,53 Z', hc);
      o += shadowRight(p + 'fcap', 'M104,53 C103,37 110,29 120,29 C130,29 137,37 136,53 C130,48 110,48 104,53 Z', 128);
      o += L('M107,46 C114,43 126,43 133,46', shade(s.headColor || 'linen', -0.2), 1);
    } else if (h === 'temple-rings') {
      o += L('M104,47 C112,42.5 128,42.5 136,47', INK, 5.6) + L('M104,47 C112,42.5 128,42.5 136,47', col(s.trim || 'madder'), 3.8);
      [[103.4, 60, 4.4], [103.4, 70.5, 3.8], [136.6, 60, 4.4], [136.6, 70.5, 3.8]].forEach(function (r) {
        o += L('M' + r[0] + ',' + (r[1] - 12) + ' L' + r[0] + ',' + (r[1] - r[2]), INK, 0.9);
        o += '<circle cx="' + r[0] + '" cy="' + r[1] + '" r="' + r[2] + '" fill="none" stroke="' + INK + '" stroke-width="2.6"/>';
        o += '<circle cx="' + r[0] + '" cy="' + r[1] + '" r="' + r[2] + '" fill="none" stroke="' + m + '" stroke-width="1.4"/>';
      });
    } else if (h === 'wreath') {
      o += spiralBorder('M104.5,46.5 C112,41.5 128,41.5 135.5,46.5', 'bronze');
    }
    return o;
  }

  function frontFemale(s, p) {
    var o = '<g transform="translate(120,340) scale(0.95) translate(-120,-340)">';
    var dress = s.dress || 'shift';
    var dc = col(s.dressColor || 'woad');
    var trim = s.trim ? col(s.trim) : null;
    o += frontCloakBack(s);
    // башмаки из-под подола
    o += P('M98,316 L115,316 L115,330 C115,336 108,339 100,339 C93,339 91,335 93,329 Z', col('leather'), 1.2);
    o += P('M125,316 L142,316 L147,329 C149,335 147,339 140,339 C132,339 125,336 125,330 Z', shade('leather', -0.15), 1.2);
    // рубаха до пят
    o += P(SHIFT, col(s.tunic));
    o += L('M100,200 L94,320 M120,200 L120,321 M140,200 L146,320', shade(s.tunic, -0.16), 1);
    o += shadowRight(p + 'shift', SHIFT, 141);
    o += L('M113,90 C115,96 125,96 127,90', INK, 1.1);
    if (trim) o += trimBand('M111,90 C113,98 127,98 129,90', trim, 3.2) + trimBand('M76,316 L164,316', trim, 4);
    if (dress === 'apron-dress') {
      o += P(APRON_DRESS, dc);
      o += shadowRight(p + 'ad', APRON_DRESS, 140);
      o += L('M101,128 L97,300 M139,128 L143,300', shade(s.dressColor || 'woad', -0.2), 1);
      if (trim) o += trimBand('M91,117 L149,117', trim, 3.4) + trimBand('M78,297 L162,297', trim, 3.4);
      o += L('M101,92 L103,114 M139,92 L137,114', INK, 5) + L('M101,92 L103,114 M139,92 L137,114', dc, 3.2);
    } else if (dress === 'wrap-skirt') {
      o += P(SKIRT, dc);
      o += shadowRight(p + 'sk', SKIRT, 140);
      o += L('M141,163 L150,319', INK, 1.2);
      if (trim) o += trimBand('M76.4,300 L163.6,300', trim, 3.6) + trimBand('M75.6,309 L164.4,309', trim, 2.4);
    }
    if (dress !== 'apron-dress') {
      // тканый пояс с бахромой
      o += P('M86,156 L154,156 L154,163 L86,163 Z', col(s.beltColor || 'madder'), 1.1);
      o += L('M90,159.5 L150,159.5', shade(s.beltColor || 'madder', 0.35), 1, ' stroke-dasharray="3 2"');
      o += P('M97,162 L103,162 L104,196 L96,196 Z', col(s.beltColor || 'madder'), 1);
      o += L('M96.5,196 L95.5,206 M99,196 L99,207 M101.5,196 L102,207 M103.8,196 L105,205', shade(s.beltColor || 'madder', -0.1), 1);
      // нож в ножнах
      o += P('M134,163 L139,163 L138,190 L135,190 Z', col('leather-dark'), 1);
    }
    if (s.apron) {
      o += P(APRON, col(s.apron));
      o += spiralBorder('M98.4,170 L98.4,265 L141.6,265 L141.6,170', 'bronze');
    }
    o += neckRing(s);
    // руки и ноша
    o += '<g transform="translate(-4.5,0)">' + frontCarry(s) + frontLeftArm(s, p) + '</g>';
    o += '<g transform="translate(4.5,0)">' + frontTool(s) + frontRightArm(s, p) + '</g>';
    // шаль поверх плеч
    if (s.shawl) {
      o += P(SHAWL, col(s.shawl));
      o += shadowRight(p + 'shawl', SHAWL, 140);
      o += spiralBorder('M87.6,203 C95,207 103,206 109,200 C110,162 113,134 120,122 C127,134 130,162 131,200 C137,206 145,207 152.4,203', s.wealth >= 2 ? 'silver' : 'bronze');
      o += L('M86,132 C92,140 96,160 98,198 M154,132 C148,140 144,160 142,198', shade(s.shawl, -0.22), 1);
    }
    o += femaleHead(s, p);
    // фибулы, бусы, цепочки
    var m = col(s.metal || 'bronze');
    if (s.brooches === 'oval') {
      o += beadString(103, 120, 120, 146, 137, 120, 12);
      if (s.wealth >= 1) o += beadString(103, 120, 120, 134, 137, 120, 9);
      [[103, 114], [137, 114]].forEach(function (b) {
        o += '<ellipse cx="' + b[0] + '" cy="' + b[1] + '" rx="7.6" ry="9.4" fill="' + m + '" stroke="' + INK + '" stroke-width="1.1"/>';
        o += '<ellipse cx="' + b[0] + '" cy="' + b[1] + '" rx="4.4" ry="6" fill="none" stroke="' + shade(s.metal || 'bronze', -0.35) + '" stroke-width="1"/>';
        o += '<circle cx="' + b[0] + '" cy="' + b[1] + '" r="1.4" fill="' + shade(s.metal || 'bronze', 0.3) + '"/>';
      });
      // ключи и игольник на цепочке
      o += L('M103,123 L99,176', col('iron-dark'), 1, ' stroke-dasharray="1.6 1"');
      o += P('M97.6,176 L100.6,176 L100.2,192 L98,192 Z', col('iron'), 0.8);
      o += L('M100.2,188 L103,188 M100.2,191 L102.4,191', col('iron'), 1.2);
    } else if (s.brooches === 'round') {
      o += L('M106,112 C112,132 128,132 134,112', m, 1.2, ' stroke-dasharray="1.6 1"');
      o += L('M106,112 C110,146 130,146 134,112', m, 1.2, ' stroke-dasharray="1.6 1"');
      [[113, 129], [120, 131], [127, 129], [116, 140], [124, 140]].forEach(function (q) {
        o += '<path d="M' + q[0] + ',' + q[1] + ' l-2.2,6 l4.4,0 Z" fill="' + m + '" stroke="' + INK + '" stroke-width="0.6"/>';
      });
      [[106, 110], [134, 110]].forEach(function (b) {
        o += '<circle cx="' + b[0] + '" cy="' + b[1] + '" r="5.6" fill="' + m + '" stroke="' + INK + '" stroke-width="1"/>';
        o += '<circle cx="' + b[0] + '" cy="' + b[1] + '" r="2.4" fill="' + shade(s.metal || 'bronze', -0.35) + '"/>';
      });
    } else if (s.brooches === 'horseshoe') {
      var bx = s.shawl ? 120 : 120, by = s.shawl ? 124 : 98;
      o += L('M' + (bx - 6) + ',' + (by + 4) + ' C' + (bx - 8) + ',' + (by - 6) + ' ' + (bx + 8) + ',' + (by - 6) + ' ' + (bx + 6) + ',' + (by + 4), INK, 3.8) +
           L('M' + (bx - 6) + ',' + (by + 4) + ' C' + (bx - 8) + ',' + (by - 6) + ' ' + (bx + 8) + ',' + (by - 6) + ' ' + (bx + 6) + ',' + (by + 4), m, 2.2);
      o += '<circle cx="' + (bx - 6) + '" cy="' + (by + 4.5) + '" r="1.8" fill="' + m + '" stroke="' + INK + '" stroke-width="0.7"/>';
      o += '<circle cx="' + (bx + 6) + '" cy="' + (by + 4.5) + '" r="1.8" fill="' + m + '" stroke="' + INK + '" stroke-width="0.7"/>';
      o += L('M' + (bx - 7) + ',' + (by - 4) + ' L' + (bx + 4) + ',' + (by + 9), INK, 1.4);
    }
    if (s.beads && s.brooches !== 'oval') o += beadString(108, 94, 120, 112, 132, 94, 10);
    // коса поверх плеча
    if (s.fhead === 'braid' || s.fhead === 'wreath') {
      var bd = 'M107,68 C101,86 99,112 100,140 C100,152 101,162 102,170';
      o += L(bd, INK, 9.6) + L(bd, col(s.hair), 7.6) + L(bd, shade(s.hair, -0.3), 7.6, ' stroke-dasharray="1.4 3.2" stroke-linecap="butt"');
      o += P('M99,170 L105,170 L105,175 L99,175 Z', col(s.trim || 'madder'), 0.8);
      o += P('M100,175 L104,175 L105.5,186 L98.5,186 Z', col(s.hair), 0.9);
    }
    return o + '</g>';
  }

  function front(spec, opts) {
    opts = opts || {};
    var s = spec, p = nextPrefix();
    var out = '<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 240 352"' +
      (opts.width ? ' width="' + opts.width + '"' : '') + (opts.height ? ' height="' + opts.height + '"' : '') +
      ' role="img"><title>' + (opts.title || (s.sex === 'f' ? 'Женщина, вид спереди' : 'Боец, вид спереди')) + '</title>';
    out += '<defs>' + patterns(p, s) + '</defs>';
    if (opts.ground !== false) out += '<ellipse cx="122" cy="340" rx="62" ry="7" fill="#000" opacity="0.14"/>';
    if (s.sex === 'f') return out + frontFemale(s, p) + '</svg>';
    out += frontCloakBack(s);
    out += frontLegs(s, p);
    out += frontTorso(s, p);
    if (!s.shield) out += frontCarry(s) + frontLeftArm(s, p);
    out += frontCloakFront(s);
    out += neckRing(s);
    out += frontHead(s, p);
    out += brooch(s, 92, 102);
    out += frontWeapon(s);
    out += frontRightArm(s, p);
    out += frontShield(s, p);
    out += '</svg>';
    return out;
  }

  // =====================================================================
  //  ВИД СВЕРХУ (смотрит вверх; 1 м = 100 единиц; viewBox -60 -95 120 150)
  // =====================================================================
  function garmentFill(s, p) {
    if (s.outer === 'mail') return 'url(#' + p + 'mail)';
    if (s.outer === 'padded') return 'url(#' + p + 'quilt)';
    if (s.outer === 'caftan') return col(s.outerColor);
    return col(s.tunic);
  }

  function topHead(s, p) {
    var o = '';
    var h = s.head || 'none';
    var hy = -3;
    if (h === 'helm-guard') {
      o += '<circle cx="0" cy="' + (hy + 1) + '" r="12.6" fill="url(#' + p + 'mail)" stroke="' + INK + '" stroke-width="1"/>';
    }
    if (s.hairStyle === 'long' && (h === 'none' || h.indexOf('helm') === 0 || h.indexOf('cap') === 0)) {
      o += '<ellipse cx="0" cy="' + (hy + 6) + '" rx="8.5" ry="8" fill="' + shade(s.hair, -0.1) + '" stroke="' + INK + '" stroke-width="1"/>';
    }
    switch (h) {
      case 'none':
        if (s.hairStyle === 'bald') {
          o += '<circle cx="0" cy="' + hy + '" r="9.6" fill="' + col(s.skin) + '" stroke="' + INK + '" stroke-width="1.2"/>';
          o += '<path d="M-9.4,' + (hy + 2) + ' A9.6,9.6 0 0 0 9.4,' + (hy + 2) + '" fill="' + col(s.hair) + '"/>';
        } else {
          o += '<circle cx="0" cy="' + hy + '" r="9.6" fill="' + col(s.hair) + '" stroke="' + INK + '" stroke-width="1.2"/>';
          o += L('M0,' + (hy - 9) + ' L0,' + (hy + 3), shade(s.hair, -0.25), 0.9);
          o += '<ellipse cx="-3" cy="' + (hy - 3) + '" rx="3" ry="2.4" fill="' + shade(s.hair, 0.25) + '" opacity="0.7"/>';
        }
        break;
      case 'hood':
        o += '<path d="M-10.5,' + hy + ' A10.5,10.5 0 0 1 10.5,' + hy + ' L6,' + (hy + 13) + ' L0,' + (hy + 16) + ' L-6,' + (hy + 13) + ' Z" fill="' + col(s.headColor) + '" stroke="' + INK + '" stroke-width="1.2"/>';
        o += L('M-7,' + (hy - 4) + ' C-3,' + (hy - 7) + ' 3,' + (hy - 7) + ' 7,' + (hy - 4), shade(s.headColor, -0.25), 1);
        break;
      case 'cap-felt':
        o += '<circle cx="0" cy="' + hy + '" r="10" fill="' + col(s.headColor) + '" stroke="' + INK + '" stroke-width="1.2"/>';
        o += '<circle cx="0" cy="' + hy + '" r="7.4" fill="none" stroke="' + shade(s.headColor, -0.2) + '" stroke-width="1"/>';
        o += '<circle cx="-1" cy="' + (hy - 1) + '" r="2.6" fill="' + shade(s.headColor, 0.2) + '"/>';
        break;
      case 'cap-leather':
        o += '<circle cx="0" cy="' + hy + '" r="10" fill="' + col('leather') + '" stroke="' + INK + '" stroke-width="1.2"/>';
        o += L('M0,' + (hy - 10) + ' L0,' + (hy + 10) + ' M-10,' + hy + ' L10,' + hy, col('leather-dark'), 2);
        break;
      case 'cap-fur':
        o += '<circle cx="0" cy="' + hy + '" r="10.6" fill="url(#' + p + 'fur)" stroke="' + INK + '" stroke-width="1.2"/>';
        o += '<circle cx="0" cy="' + hy + '" r="9.4" fill="none" stroke="' + shade('fur', 0.25) + '" stroke-width="1.6" stroke-dasharray="1 2"/>';
        break;
      case 'cap-birka':
        o += '<circle cx="0" cy="' + hy + '" r="11" fill="url(#' + p + 'fur)" stroke="' + INK + '" stroke-width="1.2"/>';
        o += '<circle cx="0" cy="' + hy + '" r="7.6" fill="' + col(s.headColor) + '" stroke="' + INK + '" stroke-width="1"/>';
        o += L('M0,' + (hy - 7) + ' L0,' + (hy + 7) + ' M-7,' + hy + ' L7,' + hy, shade(s.headColor, -0.25), 0.8);
        o += '<circle cx="0" cy="' + hy + '" r="1.8" fill="' + col(s.metal || 'silver') + '" stroke="' + INK + '" stroke-width="0.6"/>';
        break;
      case 'helm-spangen':
      case 'helm-guard':
        var band = h === 'helm-guard' ? col('bronze') : col('iron-dark');
        o += '<circle cx="0" cy="' + hy + '" r="10.2" fill="' + col('iron') + '" stroke="' + INK + '" stroke-width="1.2"/>';
        o += '<circle cx="0" cy="' + hy + '" r="10.2" fill="none" stroke="' + col('iron-dark') + '" stroke-width="1.8"/>';
        o += L('M0,' + (hy - 10) + ' L0,' + (hy + 10) + ' M-10,' + hy + ' L10,' + hy, band, 1.8);
        o += '<ellipse cx="-3.4" cy="' + (hy - 3.6) + '" rx="3" ry="2.2" fill="' + col('iron-light') + '" opacity="0.85"/>';
        o += '<path d="M-1.2,' + (hy - 10) + ' L1.2,' + (hy - 10) + ' L0.8,' + (hy - 13.4) + ' L-0.8,' + (hy - 13.4) + ' Z" fill="' + col('iron-dark') + '"/>';
        break;
      default:
        break;
    }
    return o;
  }

  function topWeapon(s) {
    var o = '';
    var w = s.weapon, wd = col('haft');
    if (w === 'spear') {
      o += L('M14.6,12 L19,-74', INK, 4) + L('M14.6,12 L19,-74', wd, 2.4);
      o += P('M19.3,-92 C21.8,-85 22.2,-79 20.3,-73 L18.3,-73 C16.6,-79 16.9,-85 19.3,-92 Z', col('iron-light'), 1);
    } else if (w === 'axe') {
      o += L('M15,8 L19,-32', INK, 4) + L('M15,8 L19,-32', wd, 2.4);
      o += P('M17,-36 L21.4,-36.4 L22,-29.6 L17.6,-29.2 Z', col('iron-dark'), 0.9);
      o += P('M21,-35.6 L29,-37.4 C32,-38.6 34,-40.6 35,-43 C36.6,-37 36.6,-28 35,-23 C33.4,-25.6 31,-28 28,-29.4 L21.6,-30.4 Z', col('iron'), 1.1);
    } else if (w === 'sword') {
      o += P('M15.4,-14 L18.6,-14.3 L21.4,-62 L19.6,-67 L17.6,-62 Z', col('iron-light'), 1);
      o += L('M11.4,-14.2 L22.6,-15.2', INK, 3.2) + L('M11.4,-14.2 L22.6,-15.2', col(s.metal || 'bronze'), 1.8);
    } else if (w === 'seax') {
      o += P('M15.4,-14 L19,-14.3 L19.6,-40 L17.6,-46 L15.6,-40 Z', col('iron-light'), 1);
    }
    return o;
  }

  function topTool(s) {
    var o = '', t = s.tool;
    if (t === 'staff' || t === 'spade') {
      o += '<circle cx="17.4" cy="-16" r="2.6" fill="' + shade('haft', t === 'staff' ? -0.3 : 0) + '" stroke="' + INK + '" stroke-width="0.8"/>';
      if (t === 'spade') o += L('M12.6,-16 L22.2,-16', INK, 2.8) + L('M12.6,-16 L22.2,-16', col('haft'), 1.4);
    } else if (t === 'sickle') {
      o += P('M17,-15 C13,-24 18,-31 26,-30 C21,-28 18,-24 20,-17 Z', col('iron-light'), 0.9);
    } else if (t === 'distaff') {
      o += '<circle cx="17" cy="-19" r="5.4" fill="' + col('wool-cream') + '" stroke="' + INK + '" stroke-width="0.9"/>';
    }
    return o;
  }
  function topCarry(s, x, y) {
    var o = '';
    if (s.carry === 'basket') {
      o += '<circle cx="' + x + '" cy="' + y + '" r="7.4" fill="' + col('wood') + '" stroke="' + INK + '" stroke-width="1"/>';
      o += '<circle cx="' + x + '" cy="' + y + '" r="4.6" fill="none" stroke="' + shade('wood', -0.3) + '" stroke-width="0.9"/>';
      o += L('M' + (x - 7) + ',' + y + ' L' + (x + 7) + ',' + y, col('haft'), 1.4);
    } else if (s.carry === 'bucket') {
      o += '<circle cx="' + x + '" cy="' + y + '" r="6.4" fill="' + shade('wood', -0.1) + '" stroke="' + INK + '" stroke-width="1"/>';
      o += '<circle cx="' + x + '" cy="' + y + '" r="4.6" fill="' + col('woad-light') + '" stroke="' + col('iron-dark') + '" stroke-width="1"/>';
    }
    return o;
  }

  function topFemale(s, p) {
    var o = '<ellipse cx="1.5" cy="6" rx="25" ry="22" fill="#000" opacity="0.18"/>';
    var dc = s.dress === 'apron-dress' || s.dress === 'wrap-skirt' ? col(s.dressColor) : col(s.tunic);
    o += topCarry(s, -22, 5);
    o += '<ellipse cx="0" cy="4" rx="19" ry="15.5" fill="' + dc + '" stroke="' + INK + '" stroke-width="1.2"/>';
    o += '<ellipse cx="16" cy="0" rx="5.2" ry="7.6" fill="' + col(s.tunic) + '" stroke="' + INK + '" stroke-width="1"/>';
    o += '<ellipse cx="-16" cy="0" rx="5.2" ry="7.6" fill="' + col(s.tunic) + '" stroke="' + INK + '" stroke-width="1"/>';
    o += '<ellipse cx="0" cy="1" rx="16.5" ry="9.6" fill="' + (s.shawl ? col(s.shawl) : (s.dress === 'apron-dress' ? dc : col(s.tunic))) + '" stroke="' + INK + '" stroke-width="1.1"/>';
    if (s.shawl) o += '<ellipse cx="0" cy="1" rx="15" ry="8.2" fill="none" stroke="' + col('bronze') + '" stroke-width="1.2" stroke-dasharray="0.8 1.6"/>';
    if (s.fhead === 'braid' || s.fhead === 'wreath') o += L('M0,4 L0,17', INK, 5.4) + L('M0,4 L0,17', col(s.hair), 3.6);
    var h = s.fhead;
    var hc = h === 'scarf' || h === 'cap' ? col(s.headColor || 'linen') : col(s.hair);
    o += '<circle cx="0" cy="-2.5" r="8.6" fill="' + hc + '" stroke="' + INK + '" stroke-width="1.1"/>';
    if (h === 'scarf') o += '<path d="M-5,4.5 L0,10 L5,4.5 Z" fill="' + hc + '" stroke="' + INK + '" stroke-width="0.9"/>';
    if (h === 'wreath' || h === 'temple-rings') o += '<circle cx="0" cy="-2.5" r="7.6" fill="none" stroke="' + col(h === 'wreath' ? 'bronze' : (s.trim || 'madder')) + '" stroke-width="1.6"/>';
    if (h !== 'scarf' && h !== 'cap') o += L('M0,-11 L0,-5', shade(s.hair, -0.3), 0.8);
    o += topTool(s);
    o += '<circle cx="14.6" cy="-9" r="3.6" fill="' + col(s.skin) + '" stroke="' + INK + '" stroke-width="1"/>';
    o += '<circle cx="-14.6" cy="-6" r="3.6" fill="' + col(s.skin) + '" stroke="' + INK + '" stroke-width="1"/>';
    return o;
  }

  function topInner(spec) {
    var s = spec, p = nextPrefix();
    var o = '<defs>' + patterns(p, s) + '</defs>';
    if (s.sex === 'f') return o + topFemale(s, p);
    o += '<ellipse cx="2" cy="7" rx="31" ry="25" fill="#000" opacity="0.18"/>';
    if (!s.shield) o += topCarry(s, -26, 3);
    // правая рука (вверх-вправо — оружие)
    var sleeve = s.outer === 'mail' ? 'url(#' + p + 'mail)' : (s.outer === 'padded' || s.outer === 'caftan') ? col(s.outerColor) : col(s.tunic);
    o += '<ellipse cx="20.5" cy="-1" rx="6.6" ry="9.6" fill="' + sleeve + '" stroke="' + INK + '" stroke-width="1.1"/>';
    var forearm = (s.outer === 'padded' || s.outer === 'caftan') ? col(s.outerColor) : col(s.tunic);
    o += L('M19,-2 L17,-10', INK, 7.4) + L('M19,-2 L17,-10', forearm, 5.4);
    if (!s.shield) {
      o += '<ellipse cx="-20.5" cy="-1" rx="6.6" ry="9.6" fill="' + sleeve + '" stroke="' + INK + '" stroke-width="1.1"/>';
      o += L('M-19,-2 L-17,-10', INK, 7.4) + L('M-19,-2 L-17,-10', forearm, 5.4);
    }
    // корпус
    o += '<ellipse cx="0" cy="3" rx="23.5" ry="13.5" fill="' + garmentFill(s, p) + '" stroke="' + INK + '" stroke-width="1.3"/>';
    if (s.outer === 'caftan' || (!s.outer || s.outer === 'none')) {
      o += '<path d="M0,-10.5 L0,16.5" stroke="' + shade(s.outer === 'caftan' ? s.outerColor : s.tunic, -0.25) + '" stroke-width="0.9"/>';
    }
    // плащ: левое плечо и спина, правое плечо открыто
    if (s.cloak) {
      var cd = 'M11,-8.5 C2,-11.5 -16,-11.5 -23.5,-6 C-29.5,0 -27.5,14 -18.5,21.5 C-8,27.5 8,27.5 18,21.5 C23,17.5 26,11.5 25,5 C20,6 14,3 11,-8.5 Z';
      o += P(cd, col(s.cloak.color), 1.2);
      o += L('M-16,14 C-8,19 6,20 14,15', shade(s.cloak.color, -0.25), 1);
      o += '<circle cx="11" cy="-7.6" r="2.6" fill="' + col(s.metal || 'bronze') + '" stroke="' + INK + '" stroke-width="0.8"/>';
    }
    if (!s.shield) o += '<circle cx="-16.6" cy="-12" r="4.4" fill="' + col(s.skin) + '" stroke="' + INK + '" stroke-width="1.1"/>';
    o += topHead(s, p);
    o += (!s.weapon || s.weapon === 'none') ? topTool(s) : topWeapon(s);
    o += '<circle cx="16.6" cy="-12" r="4.4" fill="' + col(s.skin) + '" stroke="' + INK + '" stroke-width="1.1"/>';
    // щит: впереди слева, видим лицевую сторону под углом
    if (s.shield) {
      o += '<g transform="translate(-9,-23) rotate(-6)">';
      o += '<ellipse cx="0" cy="2.4" rx="29" ry="10.6" fill="' + shade('wood', -0.45) + '" stroke="' + INK + '" stroke-width="1.1"/>';
      o += shieldFace(s.shield, p, 't', 29, 10.6, 1.1);
      o += '<ellipse rx="5.6" ry="2.9" fill="' + col('iron-dark') + '" stroke="' + INK + '" stroke-width="0.9"/>';
      o += '<ellipse cy="-0.7" rx="3.7" ry="2" fill="' + col('iron') + '"/>';
      o += '<ellipse cx="-1.1" cy="-1.3" rx="1.4" ry="0.8" fill="' + col('iron-light') + '"/>';
      o += '</g>';
    }
    return o;
  }

  function top(spec, opts) {
    opts = opts || {};
    return '<svg xmlns="http://www.w3.org/2000/svg" viewBox="' + (opts.viewBox || '-60 -95 120 150') + '"' +
      (opts.width ? ' width="' + opts.width + '"' : '') + (opts.height ? ' height="' + opts.height + '"' : '') +
      ' role="img"><title>' + (opts.title || 'Боец, вид сверху') + '</title>' + topInner(spec) + '</svg>';
  }

  // Щит анфас — значок отряда или стороны в интерфейсе
  function shield(sh, opts) {
    opts = opts || {};
    var p = nextPrefix();
    var o = '<svg xmlns="http://www.w3.org/2000/svg" viewBox="-36 -36 72 72"' +
      (opts.width ? ' width="' + opts.width + '"' : '') + ' role="img"><title>' + (opts.title || 'Щит') + '</title>';
    o += shieldFace(sh, p, 'i', 33, 33, 1.4);
    o += '<circle r="8" fill="' + col('iron-dark') + '" stroke="' + INK + '" stroke-width="1.1"/>';
    o += '<circle cx="0.6" cy="0.6" r="5.6" fill="' + col('iron') + '"/>';
    o += '<circle cx="-1.6" cy="-1.8" r="1.9" fill="' + col('iron-light') + '"/>';
    return o + '</svg>';
  }

  // =====================================================================
  //  Сборка бойца: роль × сторона × набор × опыт
  // =====================================================================
  var LVL = { 'new': 0, 'experienced': 1, 'professional': 2, 'elite': 3 };
  var UNDYED = ['wool-cream', 'wool-grey', 'wool-brown', 'linen', 'wool-dark', 'wool-cream'];

  function make(o) {
    o = o || {};
    var r = rng((o.seed || 1) * 9973 + 17);
    var kit = KITS[o.kit] || KITS['axe-shield'];
    var exp = o.exp || 'new';
    var lvl = LVL[exp] || 0;
    var role = o.role || 'militia';
    var sideId = o.side || 'clan';
    var side = SIDES[sideId];
    var s = {
      role: role, side: sideId, kit: o.kit || 'axe-shield', exp: exp,
      skin: pick(r, ['skin-1', 'skin-1', 'skin-2', 'skin-2', 'skin-3', 'skin-3', 'skin-4', 'skin-5']),
      hair: pick(r, ['blond', 'ash', 'light-brown', 'light-brown', 'brown', 'dark', 'red']),
      hairStyle: pick(r, ['short', 'short', 'long', 'long', 'bald']),
      beard: pick(r, ['none', 'moustache', 'short', 'short', 'full', 'full', 'long', 'forked']),
      tunic: 'wool-cream', trousers: 'wool-grey', trouserCut: 'narrow', wraps: 'wool-cream', shoes: 'leather',
      trim: null, trimAt: [], outer: 'none', outerColor: null, patched: false,
      cloak: null, brooch: 'penannular', belt: 'plain', metal: 'bronze',
      head: 'none', headColor: 'wool-brown',
      weapon: kit.weapon, sidearm: null,
      shield: { pattern: side.pattern, c1: side.c1, c2: side.c2, rim: true },
      armRing: null, neckRing: null
    };
    if (lvl >= 2 && chance(r, 0.35)) s.hair = 'grey';
    if (lvl === 0 && (s.beard === 'long' || s.beard === 'forked')) s.beard = 'short';
    if (role === 'citizen' || role === 'woman') return makeCivilian(s, o, r);

    if (role === 'militia') {
      s.tunic = chance(r, 0.15) ? pick(r, ['madder-light', 'woad-light', 'weld']) : pick(r, UNDYED);
      s.trousers = pick(r, ['wool-grey', 'wool-brown', 'linen', 'wool-dark']);
      s.wraps = pick(r, ['wool-cream', 'wool-grey', 'linen', 'wool-brown']);
      s.outerColor = pick(r, ['linen', 'wool-cream', 'wool-grey', 'wool-brown']);
      s.headColor = pick(r, ['wool-brown', 'wool-grey', 'wool-cream', 'woad-light', 'wool-dark']);
      s.shield = { pattern: 'band', c1: 'wood', c2: side.band, rim: chance(r, 0.4) };
    } else if (role === 'warrior') {
      s.tunic = pick(r, ['madder-light', 'woad-light', 'weld', 'green', 'bark', 'wool-grey', 'woad']);
      s.trousers = pick(r, ['wool-grey', 'wool-brown', 'woad-dark', 'bark', 'linen']);
      s.wraps = pick(r, ['wool-cream', 'linen', 'wool-grey', 'madder-light']);
      s.outerColor = pick(r, ['linen', 'woad-light', 'bark', 'madder-light', 'wool-grey']);
      s.headColor = pick(r, ['wool-brown', 'woad', 'madder', 'wool-dark']);
      s.shield = { pattern: o.pattern || side.pattern, c1: side.c1, c2: side.c2, rim: true };
    } else if (role === 'retainer') {
      s.tunic = pick(r, ['madder', 'woad', 'green', 'weld', 'lichen', 'woad-dark']);
      var tr = ['woad-dark', 'madder', 'wool-cream', 'bark', 'weld', 'green'].filter(function (c) { return c !== s.tunic; });
      s.trousers = pick(r, tr);
      s.trouserCut = chance(r, 0.5) ? 'wide' : 'narrow';
      s.wraps = pick(r, ['wool-cream', 'linen', 'madder-light', 'weld', 'woad-light']);
      s.outerColor = pick(r, ['madder', 'woad-dark', 'green', 'lichen', 'bark']);
      s.headColor = pick(r, ['madder', 'woad', 'green', 'silk-red']);
      s.hairStyle = pick(r, ['long', 'long', 'short', 'short', 'bald']);
      var pats = ['rim', 'quarters', 'spokes', 'band'];
      var swap = chance(r, 0.5);
      s.shield = { pattern: pick(r, pats), c1: swap ? side.c2 : side.c1, c2: swap ? side.c1 : side.c2, rim: true };
      s.cloak = { color: pick(r, ['woad-dark', 'madder', 'green', 'weld', 'bark', 'wool-dark']) };
    } else if (role === 'bandit') {
      s.tunic = pick(r, ['wool-dark', 'wool-brown', 'bark', 'wool-grey']);
      s.trousers = pick(r, ['wool-dark', 'wool-brown', 'bark']);
      s.wraps = pick(r, ['wool-dark', 'wool-brown', 'wool-grey']);
      s.outerColor = pick(r, ['wool-brown', 'wool-dark', 'bark']);
      s.patched = true;
      s.headColor = pick(r, ['wool-dark', 'wool-brown', 'bark']);
      s.shield = { pattern: 'scraps', c1: 'wood', c2: pick(r, ['ochre-red', 'chalk', 'soot', 'ochre-yellow']), rim: chance(r, 0.3) };
      if (chance(r, 0.25)) s.armRing = 'silver';
    }

    // доспех из набора
    if (kit.body === 'padded') s.outer = 'padded';
    else if (kit.body === 'mail') s.outer = 'mail';
    else s.outer = (role === 'retainer' && lvl >= 2) ? 'caftan' : 'none';

    // голова из набора
    if (kit.head === 'none') {
      if (role === 'bandit') s.head = chance(r, 0.6) ? 'hood' : 'none';
      else if (role === 'militia') s.head = chance(r, 0.3) ? 'hood' : 'none';
      else s.head = 'none';
    } else if (kit.head === 'cap') {
      if (role === 'retainer') s.head = 'cap-birka';
      else if (role === 'bandit') s.head = pick(r, ['cap-fur', 'cap-leather']);
      else s.head = pick(r, ['cap-felt', 'cap-leather', 'cap-fur']);
    } else {
      s.head = (role === 'retainer' && lvl >= 3) ? 'helm-guard' : 'helm-spangen';
    }

    // опыт: отделка и украшения
    var dyeTrims = ['weld', 'madder-light', 'woad-light', 'green', 'madder'];
    if (lvl >= 1 && role !== 'bandit') {
      if (chance(r, role === 'militia' ? 0.4 : 0.8)) { s.trim = pick(r, dyeTrims); s.trimAt = ['neck']; }
      if (chance(r, 0.5)) s.belt = 'fittings';
      if (chance(r, role === 'militia' ? 0.35 : 0.6)) s.armRing = 'bronze';
    }
    if (lvl >= 2) {
      s.trim = pick(r, ['weld', 'madder', 'woad', 'green', 'silk-gold']);
      s.trimAt = ['neck', 'cuffs', 'hem'];
      s.belt = 'fittings'; s.metal = 'silver'; s.armRing = 'silver';
      if (!s.cloak && chance(r, 0.7)) s.cloak = { color: pick(r, ['woad-dark', 'bark', 'wool-dark', 'madder']) };
      if (s.weapon !== 'seax') s.sidearm = 'seax';
    }
    if (lvl >= 3) {
      s.trim = pick(r, ['silk-gold', 'silk-green', 'silk-red']);
      s.belt = 'oriental'; s.neckRing = 'silver'; s.armRing = 'gold';
      if (s.cloak) s.cloak.lining = pick(r, ['madder-light', 'weld', 'silk-red']);
    }
    if (role === 'warrior' && lvl >= 1 && chance(r, 0.4) && s.weapon !== 'seax') s.sidearm = 'seax';
    return s;
  }

  // Мирные: без оружия, доспеха и щита. Достаток 0..2 вместо опыта.
  function makeCivilian(s, o, r) {
    var w = o.wealth == null ? 1 : +o.wealth;
    var local = o.side === 'ruler';
    s.wealth = w; s.exp = null; s.kit = null;
    s.weapon = 'none'; s.shield = null; s.outer = 'none'; s.sidearm = null;
    s.tool = pick(r, [null, 'staff', 'spade', 'sickle']);
    s.carry = pick(r, [null, null, 'basket', 'bucket']);
    var dyes = ['woad', 'madder-light', 'weld', 'green', 'bark', 'woad-light'];
    var rich = ['madder', 'woad-dark', 'lichen', 'green', 'woad'];
    if (o.role === 'citizen') {
      s.sex = 'm';
      s.tunic = w === 0 ? pick(r, UNDYED) : w === 1 ? (chance(r, 0.4) ? pick(r, dyes) : pick(r, UNDYED)) : pick(r, rich);
      s.trousers = pick(r, ['wool-grey', 'wool-brown', 'linen', 'wool-dark']);
      s.wraps = pick(r, ['wool-cream', 'wool-grey', 'linen', 'wool-brown']);
      s.head = chance(r, 0.3) ? 'hood' : chance(r, 0.3) ? 'cap-felt' : 'none';
      s.headColor = pick(r, ['wool-brown', 'wool-grey', 'wool-cream', 'woad-light', 'wool-dark']);
      if (w >= 1 && chance(r, 0.5)) { s.trim = pick(r, ['weld', 'madder-light', 'woad-light', 'green']); s.trimAt = ['neck']; }
      if (w >= 1 && chance(r, 0.4)) s.armRing = 'bronze';
      if (w >= 2) {
        s.trim = pick(r, ['weld', 'madder', 'woad', 'silk-gold']); s.trimAt = ['neck', 'cuffs', 'hem'];
        s.belt = 'fittings'; s.metal = 'silver'; s.armRing = 'silver';
        s.cloak = { color: pick(r, ['woad-dark', 'bark', 'wool-dark', 'madder']) };
        s.head = 'cap-fur'; s.tool = 'staff';
      }
      return s;
    }
    // женщина
    s.sex = 'f';
    var style = o.style || (local ? pick(r, ['shawl', 'shawl', 'apron']) : pick(r, ['brooches', 'shawl', 'rings', 'apron']));
    s.style = style;
    s.tool = pick(r, [null, null, 'distaff']);
    s.carry = s.tool ? pick(r, [null, 'basket']) : pick(r, [null, 'basket', 'bucket']);
    s.tunic = pick(r, ['linen', 'linen', 'wool-cream']);
    s.dressColor = w === 0 ? pick(r, ['wool-brown', 'wool-grey', 'bark', 'wool-dark']) : w === 1 ? pick(r, dyes) : pick(r, rich);
    s.beltColor = pick(r, ['madder', 'weld', 'woad', 'green']);
    s.headColor = pick(r, ['linen', 'wool-cream', 'linen']);
    s.hair = pick(r, ['blond', 'ash', 'light-brown', 'light-brown', 'brown', 'dark', 'red']);
    var girl = chance(r, 0.4);
    s.dress = 'shift'; s.shawl = null; s.apron = null; s.brooches = null; s.beads = false; s.neckRing = null;
    s.trim = w >= 1 ? pick(r, ['madder', 'weld', 'woad', 'green']) : null;
    if (style === 'brooches') {
      s.dress = 'apron-dress'; s.brooches = 'oval'; s.fhead = girl ? 'braid' : pick(r, ['scarf', 'cap']);
    } else if (style === 'shawl') {
      s.dress = 'wrap-skirt'; s.shawl = pick(r, w === 0 ? ['wool-cream', 'wool-grey'] : ['wool-cream', 'woad', 'woad-dark', 'linen']);
      s.brooches = 'horseshoe'; s.fhead = girl ? 'wreath' : 'scarf';
      if (w >= 1) s.neckRing = 'bronze';
    } else if (style === 'rings') {
      s.dress = 'shift'; s.fhead = 'temple-rings'; s.beads = w >= 1; s.trim = s.trim || 'madder';
    } else {
      s.dress = 'shift'; s.apron = pick(r, ['woad-dark', 'wool-dark', 'woad']); s.brooches = 'round';
      s.fhead = girl ? 'braid' : 'scarf';
    }
    s.metal = w >= 2 ? 'silver' : 'bronze';
    if (w >= 2 && !s.neckRing && style !== 'brooches') s.neckRing = 'silver';
    return s;
  }

  function apply(spec, set) {
    var s = clone(spec);
    if (!set) return s;
    Object.keys(set).forEach(function (k) {
      var v = set[k];
      if (v && typeof v === 'object' && !Array.isArray(v)) s[k] = Object.assign({}, s[k] || {}, v);
      else s[k] = v;
    });
    return s;
  }

  // Текущий состав схватки фазы 1 (combat.json, сценарии m3, enemies.md)
  var ROSTER = [
    { id: 'militia-axe', chip: 'Ополченец, топор', title: 'Ополченец', sub: 'новичок · топор и щит', side: 'clan',
      make: { role: 'militia', side: 'clan', kit: 'axe-shield', exp: 'new', seed: 4 } },
    { id: 'militia-spear', chip: 'Ополченец, копьё', title: 'Ополченец', sub: 'новичок · копьё и щит', side: 'clan',
      make: { role: 'militia', side: 'clan', kit: 'spear-shield', exp: 'new', seed: 21 } },
    { id: 'militia-axe-padded', chip: 'Опытный, топор', title: 'Ополченец', sub: 'опытный · топор, стёганка, шапка', side: 'clan',
      make: { role: 'militia', side: 'clan', kit: 'axe-shield-padded', exp: 'experienced', seed: 8 } },
    { id: 'militia-spear-padded', chip: 'Опытный, копьё', title: 'Ополченец', sub: 'опытный · копьё, стёганка, шапка', side: 'clan',
      make: { role: 'militia', side: 'clan', kit: 'spear-shield-padded', exp: 'experienced', seed: 15 } },
    { id: 'warrior-sword', chip: 'Боец отряда', title: 'Боец отряда', sub: 'опытный · меч, стёганка, шапка · Hird line в m3', side: 'clan',
      make: { role: 'warrior', side: 'clan', kit: 'sword-shield-padded', exp: 'experienced', seed: 6 } },
    { id: 'hrafn', chip: 'Храфн', title: 'Храфн, дружинник', sub: 'верный · элита · меч, кольчуга, шлем', side: 'clan',
      make: { role: 'retainer', side: 'clan', kit: 'sword-shield-mail', exp: 'elite', seed: 5 },
      set: { tunic: 'madder', trousers: 'woad-dark', trouserCut: 'wide', wraps: 'linen', trim: 'silk-gold',
             cloak: { color: 'woad', lining: 'madder-light' }, hair: 'blond', hairStyle: 'long', beard: 'full',
             skin: 'skin-1', head: 'helm-guard', shield: { pattern: 'rim', c1: 'soot', c2: 'ochre-yellow', rim: true } } },
    { id: 'vesti', chip: 'Вести', title: 'Вести, дружинник', sub: 'осторожный · профессионал · топор, кольчуга, шлем', side: 'clan',
      make: { role: 'retainer', side: 'clan', kit: 'axe-shield-mail', exp: 'professional', seed: 9 },
      set: { tunic: 'green', trousers: 'wool-brown', trouserCut: 'narrow', wraps: 'madder-light', trim: 'weld',
             cloak: { color: 'bark', lining: null }, hair: 'dark', hairStyle: 'short', beard: 'short', skin: 'skin-3',
             head: 'helm-spangen', shield: { pattern: 'quarters', c1: 'ochre-yellow', c2: 'soot', rim: true } } },
    { id: 'bandit', chip: 'Разбойник', title: 'Разбойник', sub: 'опытный · топор, стёганка, шапка', side: 'bandits',
      make: { role: 'bandit', side: 'bandits', kit: 'axe-shield-padded', exp: 'experienced', seed: 3 } },
    { id: 'ruler-retainer', chip: 'Дружинник правителя', title: 'Дружинник правителя', sub: 'профессионал · меч, кольчуга, шлем', side: 'ruler',
      make: { role: 'retainer', side: 'ruler', kit: 'sword-shield-mail', exp: 'professional', seed: 12 },
      set: { tunic: 'lichen', trousers: 'wool-cream', cloak: { color: 'madder', lining: null }, trim: 'silk-gold',
             shield: { pattern: 'spokes', c1: 'ochre-red', c2: 'chalk', rim: true } } },
    { id: 'ruler-militia', chip: 'Ополченец правителя', title: 'Ополченец правителя', sub: 'новичок · копьё и щит', side: 'ruler',
      make: { role: 'militia', side: 'ruler', kit: 'spear-shield', exp: 'new', seed: 30 } }
  ];

  // Мирные жители удела и клана
  var PEOPLE = [
    { id: 'farmer', chip: 'Пахарь', title: 'Пахарь', sub: 'бедный · заступ', side: 'ruler',
      make: { role: 'citizen', side: 'ruler', wealth: 0, seed: 41 }, set: { tool: 'spade', carry: null, head: 'none' } },
    { id: 'fisher', chip: 'Рыбак', title: 'Рыбак', sub: 'обычный · корзина', side: 'ruler',
      make: { role: 'citizen', side: 'ruler', wealth: 1, seed: 52 }, set: { tool: null, carry: 'basket', head: 'hood' } },
    { id: 'elder', chip: 'Старейшина', title: 'Старейшина', sub: 'зажиточный · посох, плащ, меховая шапка', side: 'ruler',
      make: { role: 'citizen', side: 'ruler', wealth: 2, seed: 63 }, set: { hair: 'grey', beard: 'long', carry: null } },
    { id: 'girl', chip: 'Девушка', title: 'Девушка из селения', sub: 'шаль со спиралями, венчик, коса', side: 'ruler',
      make: { role: 'woman', side: 'ruler', wealth: 1, style: 'shawl', seed: 74 }, set: { fhead: 'wreath', hair: 'blond', tool: null, carry: 'basket', shawl: 'wool-cream', dressColor: 'woad' } },
    { id: 'spinner', chip: 'Пряха', title: 'Пряха', sub: 'платок, шаль, запашная юбка, прялка', side: 'ruler',
      make: { role: 'woman', side: 'ruler', wealth: 1, style: 'shawl', seed: 85 }, set: { fhead: 'scarf', tool: 'distaff', carry: null } },
    { id: 'housewife', chip: 'Хозяйка', title: 'Хозяйка из клана', sub: 'сарафан, наплечные фибулы, бусы, ключи', side: 'clan',
      make: { role: 'woman', side: 'clan', wealth: 2, style: 'brooches', seed: 96 }, set: { fhead: 'cap', tool: null, carry: 'basket', dressColor: 'madder' } },
    { id: 'water', chip: 'С ведром', title: 'Женщина из клана', sub: 'очелье с височными кольцами, ведро', side: 'clan',
      make: { role: 'woman', side: 'clan', wealth: 1, style: 'rings', seed: 107 }, set: { tool: null, carry: 'bucket', hair: 'light-brown' } },
    { id: 'apron-woman', chip: 'С передником', title: 'Женщина из клана', sub: 'передник со спиралями, круглые фибулы с цепочками', side: 'clan',
      make: { role: 'woman', side: 'clan', wealth: 1, style: 'apron', seed: 118 }, set: { fhead: 'scarf', tool: null, carry: null } }
  ];

  function rosterSpec(entry) {
    return apply(make(entry.make), entry.set);
  }

  var api = {
    INK: INK, COLORS: COLORS, SIDES: SIDES, KITS: KITS, LABELS: LABELS, ROSTER: ROSTER, PEOPLE: PEOPLE,
    col: col, shade: shade, make: make, apply: apply, rosterSpec: rosterSpec, clone: clone,
    front: front, top: top, topInner: topInner, shield: shield
  };
  root.Paperdoll = api;
  if (typeof module !== 'undefined' && module.exports) module.exports = api;
})(typeof window !== 'undefined' ? window : globalThis);
