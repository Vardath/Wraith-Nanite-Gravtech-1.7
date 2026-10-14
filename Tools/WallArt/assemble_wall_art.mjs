#!/usr/bin/env node
/** Pack authored WNG material masters into native RimWorld wall UVs.
 * Image generation supplies the painting; this only derives tile masks,
 * exposed-edge bevels, exact periodic seams, clamped gutters and native overlays.
 * Usage: node Tools/WallArt/assemble_wall_art.mjs [--import-sources DIRECTORY]
 *        [--icons-only | --corners-only] [--preview DIRECTORY]
 */
import { spawnSync } from 'node:child_process';
import { mkdirSync, existsSync } from 'node:fs';
import { dirname, resolve, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const ROOT = resolve(dirname(fileURLToPath(import.meta.url)), '../..');
const argv = process.argv.slice(2);
function option(name) { const i = argv.indexOf(name); return i < 0 ? null : argv[i + 1]; }
const importRoot = option('--import-sources');
const previewRoot = option('--preview');
const iconsOnly = argv.includes('--icons-only');
const cornersOnly = argv.includes('--corners-only');
const im = spawnSync('magick', ['-version']).status === 0 ? 'magick' : 'convert';
const SAMPLE = 192, STRIDE = 256, INSET = 32, ATLAS = 1024;
const RUN_WIDTH = SAMPLE * 6;
const GAP = 6; // Native isolated tile: 180/192 width, 87.9% opaque area.
const families = [
  { name: 'Precursor', atlas: 'WNG_PrecursorHull_Atlas', icon: 'WNG_PrecursorWall',
    legacy: 'Things/Building/Precursor/Structure/WNG_PrecursorWall',
    source: 'exec-72dae1f7-638f-4145-be41-cd708017ace4.png',
    crop: '2172x195+0+264', bodyRows: [.34, .58], railRows: [.01, .30], faceRows: [.72, .96] },
  { name: 'Wraith', atlas: 'WNG_OrganicHull_Atlas', icon: 'WNG_WraithLivingWall',
    legacy: 'Things/Building/Wraith/Structure/WNG_LivingWall',
    source: 'exec-c03b5ce6-d41d-4c91-bbee-e1d48c3e263c.png',
    crop: '2172x292+0+221', bodyRows: [.61, .85], railRows: [.04, .49], faceRows: [.69, .96] },
  { name: 'Goauld', atlas: 'WNG_GoauldHull_Atlas', icon: 'WNG_GoauldGravshipHull',
    source: 'exec-d3297504-5138-4428-adc3-54ca6ce4a843.png',
    crop: '2172x250+0+232', bodyRows: [.24, .50], railRows: [.01, .29], faceRows: [.69, .96] },
];
function convert(args, input) {
  const r = spawnSync(im, args, { input, maxBuffer: 32 * 1024 * 1024 });
  if (r.status !== 0) throw new Error(`${im}: ${r.stderr.toString()}`);
  return r.stdout;
}
function read(path, size = SAMPLE) {
  const data = convert([path, '-resize', `${size}x${size}!`, '-alpha', 'on', '-depth', '8', 'rgba:-']);
  if (data.length !== size * size * 4) throw new Error(`Invalid RGBA size: ${path}`);
  return Buffer.from(data);
}
function write(path, pixels, size) {
  writeRectangle(path, pixels, size, size);
}
function writeRectangle(path, pixels, width, height) {
  mkdirSync(dirname(path), { recursive: true });
  // Preserve reviewed PNG bytes across ImageMagick versions if decoded pixels
  // are already current. Narrow legacy CI must not create compression-only commits.
  if (existsSync(path) && convert([path, '-format', '%wx%h', 'info:']).toString() === `${width}x${height}` &&
      convert([path, '-alpha', 'on', '-depth', '8', 'rgba:-']).equals(pixels)) return;
  convert(['-size', `${width}x${height}`, '-depth', '8', 'rgba:-', '-define', 'png:color-type=6',
    '-define', 'png:exclude-chunks=date,time', path], pixels);
}
const at = (x, y, size = SAMPLE) => 4 * (y * size + x);
function periodic(source) {
  // Match all opposite edges without mirroring or flipping the authored painting.
  // Fade the correction into twelve pixels; exact boundary colors are shared.
  const a = Buffer.from(source), band = 12;
  for (let k = 0; k < band; k++) {
    const weight = .5 * (1 - k / band) ** 2;
    for (let y = 0; y < SAMPLE; y++) for (let c = 0; c < 3; c++) {
      const p = at(k, y) + c, q = at(SAMPLE - 1 - k, y) + c;
      const left = a[p], right = a[q];
      a[p] = Math.round(left * (1 - weight) + right * weight);
      a[q] = Math.round(right * (1 - weight) + left * weight);
    }
  }
  for (let k = 0; k < band; k++) {
    const weight = .5 * (1 - k / band) ** 2;
    for (let x = 0; x < SAMPLE; x++) for (let c = 0; c < 3; c++) {
      const p = at(x, k) + c, q = at(x, SAMPLE - 1 - k) + c;
      const top = a[p], bottom = a[q];
      a[p] = Math.round(top * (1 - weight) + bottom * weight);
      a[q] = Math.round(bottom * (1 - weight) + top * weight);
    }
  }
  for (let p = 3; p < a.length; p += 4) a[p] = 255;
  return a;
}
function quietBody(source) {
  // The body is a quiet structural surface. Painted coping/face detail lives
  // on exposed contours, rather than a medallion repeated at every cell center.
  const out = Buffer.from(source), mean = [0, 0, 0];
  for (let p = 0; p < source.length; p += 4) for (let c = 0; c < 3; c++) mean[c] += source[p + c];
  for (let c = 0; c < 3; c++) mean[c] /= SAMPLE * SAMPLE;
  for (let p = 0; p < out.length; p += 4) {
    for (let c = 0; c < 3; c++) out[p + c] = Math.round(mean[c] + Math.max(-2, Math.min(2, (source[p + c] - mean[c]) * .06)));
    out[p + 3] = 255;
  }
  return periodic(out);
}
function tile(master, mask, run, family) {
  const out = Buffer.alloc(SAMPLE * SAMPLE * 4);
  const x0 = mask & 8 ? 0 : GAP, x1 = mask & 2 ? SAMPLE - 1 : SAMPLE - 1 - GAP;
  const y0 = mask & 1 ? 0 : GAP, y1 = mask & 4 ? SAMPLE - 1 : SAMPLE - 1 - GAP;
  for (let y = y0; y <= y1; y++) for (let x = x0; x <= x1; x++) {
    const p = at(x, y);
    const distances = [];
    if (!(mask & 1)) distances.push([y - y0, 'N']);
    if (!(mask & 8)) distances.push([x - x0, 'W']);
    if (!(mask & 2)) distances.push([x1 - x, 'E']);
    if (!(mask & 4)) distances.push([y1 - y, 'S']);
    distances.sort((a, b) => a[0] - b[0]);
    const [d, direction] = distances[0] ?? [99, 'N'];
    const lit = direction === 'N' || direction === 'W';
    // Structural north/west coping and south/east vertical face, only where
    // that face is exposed. Linked faces have no endcap, trim or cross-band.
    const band = 18, t = Math.min(1, d / band);
    const along = direction === 'N' || direction === 'S' ? x : SAMPLE - 1 - y;
    const rows = lit ? family.railRows : family.faceRows;
    const ry = Math.min(SAMPLE - 1, Math.round((lit ? rows[0] + t * (rows[1] - rows[0]) : rows[1] - t * (rows[1] - rows[0])) * (SAMPLE - 1)));
    const rx = 2 * SAMPLE + along;
    const q = at(rx, ry, RUN_WIDTH);
    const edgeWeight = d < band ? Math.min(1, (band - d) / 4) : 0;
    const factor = d === 0 ? .25 : d === 1 ? .55 : lit ? 1 : .93;
    for (let c = 0; c < 3; c++) {
      const painted = run[q + c] * factor;
      out[p + c] = Math.min(255, Math.round(master[p + c] * (1 - edgeWeight) + painted * edgeWeight));
    }
    out[p + 3] = 255;
  }
  return out;
}
function atlas(tiles) {
  const out = Buffer.alloc(ATLAS * ATLAS * 4);
  for (let mask = 0; mask < 16; mask++) {
    const x0 = mask % 4 * STRIDE, y0 = (3 - Math.floor(mask / 4)) * STRIDE;
    for (let y = 0; y < STRIDE; y++) for (let x = 0; x < STRIDE; x++) {
      const sx = Math.min(SAMPLE - 1, Math.max(0, x - INSET));
      const sy = Math.min(SAMPLE - 1, Math.max(0, y - INSET));
      tiles[mask].copy(out, at(x + x0, y + y0, ATLAS), at(sx, sy), at(sx, sy) + 4);
    }
  }
  return out;
}
function corner(master, direction, partial, run, family) {
  // A quad spans 2x2 map cells. Native NW contains N,W and optionally NW.
  // Full corners include the diagonal-neighbor quarter; partial corners are
  // an open broad strip joining the two cardinal neighbors, never a thin pipe.
  const size = 512, out = Buffer.alloc(size * size * 4);
  const east = direction.endsWith('east'), south = direction.startsWith('south');
  const hi = 1.32, lo = .68;
  for (let y = 0; y < size; y++) for (let x = 0; x < size; x++) {
    const u = (east ? size - 1 - x : x) / (size - 1);
    const v = (south ? size - 1 - y : y) / (size - 1);
    const diagonal = u + v;
    const outer = (hi - diagonal) * size / Math.SQRT2;
    const inner = partial ? (diagonal - lo) * size / Math.SQRT2 : 999;
    const distance = Math.min(outer, inner);
    if (distance <= -.5) continue;
    const p = at(x, y, size);
    // Keep global material orientation and one repetition per world cell.
    const sx = Math.floor((x % (size / 2)) * SAMPLE / (size / 2));
    const sy = Math.floor((y % (size / 2)) * SAMPLE / (size / 2));
    const q = at(sx, sy);
    // The diagonal uses the same quiet cap plus an exposed architectural rim;
    // it never repeats a square panel image across the 2x2 corner quad.
    const t = Math.max(0, Math.min(1, distance / 24));
    const nearestIsOuter = outer <= inner;
    const lit = nearestIsOuter ? south : !south;
    const rows = lit ? family.railRows : family.faceRows;
    const ry = Math.round((lit ? rows[0] + t * (rows[1] - rows[0]) : rows[1] - t * (rows[1] - rows[0])) * (SAMPLE - 1));
    const tangent = Math.round((u - v) * size);
    const along = ((tangent % SAMPLE) + SAMPLE) % SAMPLE;
    const rq = at(2 * SAMPLE + along, ry, RUN_WIDTH);
    const blend = distance < 24 ? Math.min(1, (24 - distance) / 6) : 0;
    const factor = distance < 2 ? .27 : distance < 4 ? .6 : 1;
    for (let c = 0; c < 3; c++) out[p + c] = Math.min(255, Math.round(master[q + c] * (1 - blend) + run[rq + c] * factor * blend));
    out[p + 3] = distance < .5 ? Math.round((distance + .5) * 255) : 255;
  }
  return out;
}
function scene(tiles, cells) {
  const rows = cells.length, cols = cells[0].length;
  const size = Math.max(rows, cols) * SAMPLE;
  const out = Buffer.alloc(size * size * 4);
  for (let y = 0; y < size; y++) for (let x = 0; x < size; x++) {
    const p = at(x, y, size), checker = ((Math.floor(x / 24) + Math.floor(y / 24)) % 2);
    out[p] = out[p + 1] = out[p + 2] = checker ? 46 : 40; out[p + 3] = 255;
  }
  for (let cy = 0; cy < rows; cy++) for (let cx = 0; cx < cols; cx++) {
    if (!cells[cy][cx]) continue;
    const mask = (cells[cy - 1]?.[cx] ? 1 : 0) | (cells[cy]?.[cx + 1] ? 2 : 0) |
      (cells[cy + 1]?.[cx] ? 4 : 0) | (cells[cy]?.[cx - 1] ? 8 : 0);
    for (let y = 0; y < SAMPLE; y++) for (let x = 0; x < SAMPLE; x++) {
      const p = at(x, y), q = at(cx * SAMPLE + x, cy * SAMPLE + y, size);
      if (tiles[mask][p + 3]) tiles[mask].copy(out, q, p, p + 4);
    }
  }
  // Mirror the unmodified native CornerFiller: the quiet wall cap is designed
  // to accept its constant-UV covers without painting obvious blank squares.
  const filler = at(SAMPLE / 2, Math.round(SAMPLE * .4));
  for (let cy = 0; cy < rows; cy++) for (let cx = 0; cx < cols; cx++) {
    if (!cells[cy][cx]) continue;
    for (const dx of [-1, 1]) for (const dy of [-1, 1]) {
      if (!cells[cy + dy]?.[cx] || !cells[cy]?.[cx + dx] || !cells[cy + dy]?.[cx + dx]) continue;
      const centerX = (cx + .5 + dx * .25) * SAMPLE;
      const centerY = (cy + .5 + dy * .25 - .09) * SAMPLE;
      for (let y = Math.floor(centerY - SAMPLE / 4); y < centerY + SAMPLE / 4; y++)
        for (let x = Math.floor(centerX - SAMPLE / 4); x < centerX + SAMPLE / 4; x++) {
          if (x < 0 || y < 0 || x >= size || y >= size) continue;
          tiles[15].copy(out, at(x, y, size), filler, filler + 4);
        }
    }
  }
  return { out, size };
}
for (const f of families) {
  const path = join(ROOT, 'ArtSource/Walls', f.name, 'master.png');
  const runPath = join(ROOT, 'ArtSource/Walls', f.name, 'wall_run.png');
  if (importRoot) {
    const generated = join(importRoot, f.source);
    const run = convert([generated, '-crop', f.crop, '+repage', '-resize', `${RUN_WIDTH}x${SAMPLE}!`, '-alpha', 'on', '-depth', '8', 'rgba:-']);
    // Periodic painted profiles at connected ends, independently of the quiet
    // body. The six-cell authored source remains intact away from this segment.
    for (let k = 0; k < 12; k++) for (let y = 0; y < SAMPLE; y++) for (let c = 0; c < 3; c++) {
      const p = at(2 * SAMPLE + k, y, RUN_WIDTH) + c;
      const q = at(3 * SAMPLE - 1 - k, y, RUN_WIDTH) + c;
      const weight = .5 * (1 - k / 12) ** 2, left = run[p], right = run[q];
      run[p] = Math.round(left * (1 - weight) + right * weight);
      run[q] = Math.round(right * (1 - weight) + left * weight);
    }
    writeRectangle(runPath, run, RUN_WIDTH, SAMPLE);
    const bodyY = Math.round(f.bodyRows[0] * SAMPLE), bodyH = Math.round((f.bodyRows[1] - f.bodyRows[0]) * SAMPLE);
    const body = convert([runPath, '-crop', `${SAMPLE * 3}x${bodyH}+${SAMPLE * 2}+${bodyY}`, '+repage', '-resize', `${SAMPLE}x${SAMPLE}!`, '-alpha', 'on', '-depth', '8', 'rgba:-']);
    write(path, quietBody(body), SAMPLE);
  }
  if (!existsSync(path)) throw new Error(`Authored material master missing: ${path}`);
  const run = convert([runPath, '-alpha', 'on', '-depth', '8', 'rgba:-']);
  if (run.length !== RUN_WIDTH * SAMPLE * 4) throw new Error(`Wall run must be ${RUN_WIDTH}x${SAMPLE}: ${runPath}`);
  const master = read(path), tiles = Array.from({ length: 16 }, (_, mask) => tile(master, mask, run, f));
  const tex = join(ROOT, 'Textures/Things/Building', f.name, 'Gravship');
  if (!iconsOnly && !cornersOnly) write(join(tex, `${f.atlas}.png`), atlas(tiles), ATLAS);
  if (!cornersOnly) {
    // The settlement wall now owns a separate atlas and icon. Never replace
    // these from the gravship family source. The gravship keeps its old tile art.
    const menuIcon = f.name === 'Wraith' ? 'WNG_WraithGravshipHull' : f.icon;
    write(join(ROOT, 'Textures/UI/WNG/Build', `${menuIcon}.png`), tiles[0], SAMPLE);
    if (f.legacy && f.name !== 'Wraith')
      write(join(ROOT, 'Textures', `${f.legacy}.png`), tiles[0], SAMPLE);
  }
  if (!iconsOnly) for (const direction of ['northwest', 'northeast', 'southwest', 'southeast']) {
    for (const partial of [false, true]) write(join(tex, 'HullCorners',
      `AngledGravshipHull_${partial ? 'Partial_' : ''}${direction}.png`), corner(master, direction, partial, run, f), 512);
  }
  if (previewRoot) {
    mkdirSync(previewRoot, { recursive: true });
    write(join(previewRoot, `${f.name}-isolated.png`), tiles[0], SAMPLE);
    const cells = [
      [0,0,0,0,0,0,0,0], [0,1,1,1,1,1,1,0], [0,1,0,0,1,0,1,0],
      [0,1,1,1,1,0,1,0], [0,1,1,0,1,0,1,0], [0,1,0,0,1,0,1,0],
      [0,1,1,1,1,1,1,0], [0,0,0,0,0,0,0,0],
    ];
    const { out, size } = scene(tiles, cells);
    const full = join(previewRoot, `${f.name}-topology-full.png`);
    write(full, out, size);
    convert([full, '-resize', '512x512', join(previewRoot, `${f.name}-topology.png`)]);
  }
  console.log(`Packed ${f.name}: 16 native wall masks, isolated icon and eight native angled overlays.`);
}
