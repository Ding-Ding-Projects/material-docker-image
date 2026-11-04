// Composes website/social-preview.png (1200x630) from a real captured screenshot of the built application.
// Pure Node, no canvas: decode the source PNG, area-average it down, place it on a flat background, encode with zlib.
// Deterministic for a given source screenshot and Node/zlib version; the committed PNG is what ships, and the checks only validate it.
import fs from 'node:fs';
import path from 'node:path';
import zlib from 'node:zlib';
import {fileURLToPath} from 'node:url';

const root = path.dirname(path.dirname(fileURLToPath(import.meta.url)));
const sourceName = process.argv[2] || 'evidence/screenshots/build.png';
const target = path.join(root, 'website', 'social-preview.png');
const WIDTH = 1200, HEIGHT = 630, BAND = 14;
const BACKGROUND = [16, 23, 34], ACCENT = [36, 87, 191];

function crc32(buffer) {
  let crc = 0xffffffff;
  for (const byte of buffer) { crc ^= byte; for (let i = 0; i < 8; i++) crc = (crc >>> 1) ^ (0xedb88320 & -(crc & 1)); }
  return (crc ^ 0xffffffff) >>> 0;
}
function chunk(type, data) {
  const head = Buffer.alloc(8); head.writeUInt32BE(data.length, 0); head.write(type, 4, 'ascii');
  const tail = Buffer.alloc(4); tail.writeUInt32BE(crc32(Buffer.concat([head.subarray(4), data])), 0);
  return Buffer.concat([head, data, tail]);
}
function decode(bytes) {
  if (!bytes.subarray(0, 8).equals(Buffer.from([137, 80, 78, 71, 13, 10, 26, 10]))) throw new Error('Not a PNG');
  let offset = 8, header = null; const parts = [];
  while (offset < bytes.length) {
    const length = bytes.readUInt32BE(offset), type = bytes.toString('ascii', offset + 4, offset + 8), data = bytes.subarray(offset + 8, offset + 8 + length);
    if (type === 'IHDR') header = {width: data.readUInt32BE(0), height: data.readUInt32BE(4), depth: data[8], colour: data[9], interlace: data[12]};
    else if (type === 'IDAT') parts.push(data);
    offset += 12 + length;
  }
  if (!header || header.depth !== 8 || ![2, 6].includes(header.colour) || header.interlace) throw new Error('Only 8-bit non-interlaced RGB or RGBA sources are supported');
  const channels = header.colour === 6 ? 4 : 3, stride = header.width * channels;
  const raw = zlib.inflateSync(Buffer.concat(parts)), pixels = Buffer.alloc(stride * header.height);
  for (let y = 0; y < header.height; y++) {
    const filter = raw[y * (stride + 1)], line = raw.subarray(y * (stride + 1) + 1, (y + 1) * (stride + 1));
    for (let x = 0; x < stride; x++) {
      const left = x >= channels ? pixels[y * stride + x - channels] : 0, up = y ? pixels[(y - 1) * stride + x] : 0, upLeft = y && x >= channels ? pixels[(y - 1) * stride + x - channels] : 0;
      let value = line[x];
      if (filter === 1) value += left; else if (filter === 2) value += up; else if (filter === 3) value += (left + up) >> 1;
      else if (filter === 4) { const p = left + up - upLeft, pa = Math.abs(p - left), pb = Math.abs(p - up), pc = Math.abs(p - upLeft); value += pa <= pb && pa <= pc ? left : pb <= pc ? up : upLeft; }
      pixels[y * stride + x] = value & 255;
    }
  }
  return {width: header.width, height: header.height, channels, pixels};
}
function encode(width, height, rgb) {
  const raw = Buffer.alloc((width * 3 + 1) * height);
  for (let y = 0; y < height; y++) { raw[y * (width * 3 + 1)] = 0; rgb.copy(raw, y * (width * 3 + 1) + 1, y * width * 3, (y + 1) * width * 3); }
  const header = Buffer.alloc(13); header.writeUInt32BE(width, 0); header.writeUInt32BE(height, 4); header[8] = 8; header[9] = 2;
  return Buffer.concat([Buffer.from([137, 80, 78, 71, 13, 10, 26, 10]), chunk('IHDR', header), chunk('IDAT', zlib.deflateSync(raw, {level: 9})), chunk('IEND', Buffer.alloc(0))]);
}

const source = decode(fs.readFileSync(path.join(root, sourceName)));
// Scale the capture to the full card width, then keep the top HEIGHT-BAND rows so the application chrome and workspace stay visible.
const scale = WIDTH / source.width, scaledHeight = Math.round(source.height * scale), visible = HEIGHT - BAND;
const out = Buffer.alloc(WIDTH * HEIGHT * 3);
for (let y = 0; y < HEIGHT; y++) for (let x = 0; x < WIDTH; x++) {
  const index = (y * WIDTH + x) * 3;
  if (y >= visible || y >= scaledHeight) { out.set(y >= visible ? ACCENT : BACKGROUND, index); continue; }
  const x0 = Math.floor(x / scale), x1 = Math.max(x0 + 1, Math.ceil((x + 1) / scale)), y0 = Math.floor(y / scale), y1 = Math.max(y0 + 1, Math.ceil((y + 1) / scale));
  let r = 0, g = 0, b = 0, count = 0;
  for (let sy = y0; sy < Math.min(y1, source.height); sy++) for (let sx = x0; sx < Math.min(x1, source.width); sx++) {
    const at = (sy * source.width + sx) * source.channels; r += source.pixels[at]; g += source.pixels[at + 1]; b += source.pixels[at + 2]; count++;
  }
  out[index] = Math.round(r / count); out[index + 1] = Math.round(g / count); out[index + 2] = Math.round(b / count);
}
fs.writeFileSync(target, encode(WIDTH, HEIGHT, out));
console.log('Wrote ' + path.relative(root, target) + ' from ' + sourceName + '.');
