// Preloaded (node -r) into a private copy of Luna's pipeline whose script-minify call is routed here.
// Runs Luna's minify unchanged, then measures each script file on its own with the same Terser and
// options, and splits the user code (Bridge output) by C# type. Results go to $LUNA_SIZE_DUMP.
// Sizes are Brotli quality 11 like Luna's compressAsset; the dashboard scales them to Luna's exact total.
'use strict';
const crypto = require('crypto');
const fs = require('fs');
const path = require('path');
const zlib = require('zlib');

// Terser is deterministic: same input + options = same output. Results are cached by content hash in
// $LUNA_SIZE_CACHE, so unchanged engine files (and an unchanged bundle) are never minified twice.
const CACHE = process.env.LUNA_SIZE_CACHE;
const hashOf = (...parts) => {
  const h = crypto.createHash('sha1');
  for (const p of parts) h.update(typeof p === 'string' ? p : JSON.stringify(p)).update('\0');
  return h.digest('hex');
};
function cacheGet(key) {
  if (!CACHE) return null;
  try { return fs.readFileSync(path.join(CACHE, key), 'utf8'); } catch (e) { return null; }
}
function cacheSet(key, value) {
  if (!CACHE) return;
  try {
    fs.mkdirSync(CACHE, { recursive: true });
    const partial = path.join(CACHE, key + '.' + process.pid);
    fs.writeFileSync(partial, value);
    fs.renameSync(partial, path.join(CACHE, key));
  } catch (e) { /* caching is best effort */ }
}

const brotli = code => zlib.brotliCompressSync(Buffer.from(code, 'utf8'), { params: {
  [zlib.constants.BROTLI_PARAM_MODE]: zlib.constants.BROTLI_MODE_GENERIC,
  [zlib.constants.BROTLI_PARAM_QUALITY]: 11,
  [zlib.constants.BROTLI_PARAM_LGWIN]: 22 } }).length;

const USER_CODE = /UnityScriptsCompiler\.js$/;
const TYPE_START = /Bridge\.define\("([^"]+)"/g;

// Bridge appends reflection metadata for every type after the last definition.
function splitUserCode(code) {
  const marks = [];
  let m;
  while ((m = TYPE_START.exec(code))) marks.push({ name: m[1], at: m.index });
  if (marks.length === 0) return { types: [], metadataBr: 0, otherBr: brotli(code) };

  let metadataAt = code.indexOf('setMetadata', marks[marks.length - 1].at);
  if (metadataAt >= 0) {
    const statementStart = Math.max(code.lastIndexOf(';', metadataAt), code.lastIndexOf('}', metadataAt));
    if (statementStart > marks[marks.length - 1].at) metadataAt = statementStart + 1;
  } else {
    metadataAt = code.length;
  }
  const types = marks.map((mark, i) => {
    const end = i + 1 < marks.length ? marks[i + 1].at : metadataAt;
    return { name: mark.name, br: brotli(code.slice(mark.at, end)) };
  });
  return {
    types,
    metadataBr: metadataAt < code.length ? brotli(code.slice(metadataAt)) : 0,
    otherBr: marks[0].at > 0 ? brotli(code.slice(0, marks[0].at)) : 0,
  };
}

const TERSER_VERSION = 'luna-terser-1';

globalThis.__lunaMinify = async (terser, fn, input, options, relations) => {
  const fullKey = 'full-' + hashOf(TERSER_VERSION, options, input);
  const cachedCode = cacheGet(fullKey);
  const full = cachedCode !== null ? { code: cachedCode } : await terser[fn](input, options);
  if (cachedCode === null && full.code && !full.error) cacheSet(fullKey, full.code);
  const out = process.env.LUNA_SIZE_DUMP;
  if (!out || !full.code) return full;
  try {
    const keys = Object.keys(input);
    const files = [];
    let user = null;
    for (let i = 0; i < keys.length; i++) {
      const code = input[keys[i]];
      const src = relations && relations[i] ? (relations[i].bundleSrc || relations[i].src) : String(keys[i]);
      const isUser = USER_CODE.test(src);
      const fileKey = 'file-' + hashOf(TERSER_VERSION, options, keys[i], code, isUser);
      const cached = cacheGet(fileKey);
      let entry = cached ? JSON.parse(cached) : null;
      if (!entry) {
        const single = await terser[fn]({ [keys[i]]: code }, options);
        entry = { raw: Buffer.byteLength(code, 'utf8'), min: single.code ? Buffer.byteLength(single.code, 'utf8') : 0, br: single.code ? brotli(single.code) : 0 };
        if (isUser && single.code) entry.user = splitUserCode(single.code);
        cacheSet(fileKey, JSON.stringify(entry));
      }
      files.push({ src, raw: entry.raw, min: entry.min, br: entry.br });
      if (entry.user) user = entry.user;
    }
    fs.writeFileSync(out, JSON.stringify({ combinedMin: Buffer.byteLength(full.code, "utf8"), files, user }));
  } catch (e) {
    fs.writeFileSync(out, JSON.stringify({ error: String(e && e.stack || e) }));
  }
  return full;
};
