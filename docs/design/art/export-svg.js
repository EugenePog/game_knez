// Пересобрать SVG-листы состава из paperdoll.js:  node docs/design/art/export-svg.js
var fs = require('fs');
var path = require('path');
var PD = require('./paperdoll.js');
var out = path.join(__dirname, 'units');
if (!fs.existsSync(out)) fs.mkdirSync(out);
PD.ROSTER.forEach(function (e) {
  var s = PD.rosterSpec(e);
  fs.writeFileSync(path.join(out, e.id + '-front.svg'), PD.front(s, { title: e.title + ': ' + e.sub }) + '\n');
  fs.writeFileSync(path.join(out, e.id + '-top.svg'), PD.top(s, { title: e.title + ', сверху' }) + '\n');
});
// общий лист: фигуры в ряд, под каждой фишка сверху
var W = 240, H = 352, TH = 150, n = PD.ROSTER.length;
var sheet = '<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 ' + (W * n) + ' ' + (H + TH + 70) + '" role="img">' +
  '<title>Game_knez: нынешний состав, вид спереди и сверху</title>' +
  '<rect width="100%" height="100%" fill="#E6E9E3"/>';
PD.ROSTER.forEach(function (e, i) {
  var s = PD.rosterSpec(e);
  sheet += '<svg x="' + (i * W) + '" y="0" width="' + W + '" height="' + H + '" viewBox="0 0 240 352">' +
    PD.front(s).replace(/^<svg[^>]*>/, '').replace(/<\/svg>$/, '') + '</svg>';
  sheet += '<rect x="' + (i * W + 60) + '" y="' + (H + 4) + '" width="120" height="' + TH + '" fill="#7B8760"/>';
  sheet += '<svg x="' + (i * W + 60) + '" y="' + (H + 4) + '" width="120" height="' + TH + '" viewBox="-60 -95 120 150">' +
    PD.top(s).replace(/^<svg[^>]*>/, '').replace(/<\/svg>$/, '') + '</svg>';
  sheet += '<text x="' + (i * W + 120) + '" y="' + (H + TH + 30) + '" text-anchor="middle" font-family="sans-serif" font-size="15" fill="#24221F">' + e.title + '</text>';
  sheet += '<text x="' + (i * W + 120) + '" y="' + (H + TH + 50) + '" text-anchor="middle" font-family="sans-serif" font-size="11" fill="#5B6058">' + e.sub + '</text>';
});
sheet += '</svg>\n';
fs.writeFileSync(path.join(__dirname, 'roster.svg'), sheet);
// мирные жители
PD.PEOPLE.forEach(function (e) {
  var s = PD.rosterSpec(e);
  fs.writeFileSync(path.join(out, e.id + '-front.svg'), PD.front(s, { title: e.title + ': ' + e.sub }) + '\n');
  fs.writeFileSync(path.join(out, e.id + '-top.svg'), PD.top(s, { title: e.title + ', сверху' }) + '\n');
});
function sheetOf(list, title) {
  var n = list.length, o = '<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 ' + (W * n) + ' ' + (H + TH + 70) + '" role="img">' +
    '<title>' + title + '</title><rect width="100%" height="100%" fill="#E6E9E3"/>';
  list.forEach(function (e, i) {
    var s = PD.rosterSpec(e);
    o += '<svg x="' + (i * W) + '" y="0" width="' + W + '" height="' + H + '" viewBox="0 0 240 352">' + PD.front(s).replace(/^<svg[^>]*>/, '').replace(/<\/svg>$/, '') + '</svg>';
    o += '<rect x="' + (i * W + 60) + '" y="' + (H + 4) + '" width="120" height="' + TH + '" fill="#7B8760"/>';
    o += '<svg x="' + (i * W + 60) + '" y="' + (H + 4) + '" width="120" height="' + TH + '" viewBox="-60 -95 120 150">' + PD.top(s).replace(/^<svg[^>]*>/, '').replace(/<\/svg>$/, '') + '</svg>';
    o += '<text x="' + (i * W + 120) + '" y="' + (H + TH + 30) + '" text-anchor="middle" font-family="sans-serif" font-size="15" fill="#24221F">' + e.title + '</text>';
    o += '<text x="' + (i * W + 120) + '" y="' + (H + TH + 50) + '" text-anchor="middle" font-family="sans-serif" font-size="11" fill="#5B6058">' + e.sub + '</text>';
  });
  return o + '</svg>\n';
}
fs.writeFileSync(path.join(__dirname, 'people.svg'), sheetOf(PD.PEOPLE, 'Game_knez: мирные жители, вид спереди и сверху'));
// постройки и предметы
var OB = require('./objects.js');
var od = path.join(__dirname, 'objects');
if (!fs.existsSync(od)) fs.mkdirSync(od);
OB.OBJECTS.forEach(function (o) {
  fs.writeFileSync(path.join(od, o.id + '-view.svg'), OB.view(o.id) + '\n');
  fs.writeFileSync(path.join(od, o.id + '-plan.svg'), OB.plan(o.id) + '\n');
});
fs.writeFileSync(path.join(__dirname, 'village.svg'), OB.village(PD) + '\n');
console.log('written', (PD.ROSTER.length + PD.PEOPLE.length) * 2 + OB.OBJECTS.length * 2 + 3, 'files');
