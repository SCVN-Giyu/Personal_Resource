// Luna compile cache: stands in for MSBuild in Luna's build.
// Luna recompiles all C# to JS on every build (/t:Rebuild), even when no script changed.
// This wrapper fingerprints everything the compile reads; on a match it restores the files the
// previous compile wrote and replays its console output, otherwise it runs the real MSBuild and
// stores the result. Any problem in the cache itself falls back to the real MSBuild.
'use strict';
const { spawn } = require('child_process');
const crypto = require('crypto');
const fs = require('fs');
const os = require('os');
const path = require('path');

const REAL_MSBUILD = process.env.LUNA_REAL_MSBUILD || '/Library/Frameworks/Mono.framework/Versions/Current/bin/msbuild';
const CACHE_ROOT = process.env.LUNA_COMPILE_CACHE_DIR || path.join(os.homedir(), 'Library', 'Caches', 'LunaCompileCache');
const MAX_ENTRIES = 5;
const SKIPPED_FOLDERS = new Set(['packages', 'node_modules']);
const LOG = path.join(CACHE_ROOT, 'cache.log');
const VERSION = '3'; // 2: keys independent of where the project and compiler folders are; 3: also <Link> paths

const args = process.argv.slice(2);

function log(message) {
  try {
    fs.mkdirSync(CACHE_ROOT, { recursive: true });
    fs.appendFileSync(LOG, `${new Date().toISOString()} ${message}\n`);
  } catch (e) { /* logging must never break a build */ }
}

// Runs the real MSBuild, streaming its output to Luna and capturing it for the cache.
function runReal(capture) {
  return new Promise(resolve => {
    const child = spawn(REAL_MSBUILD, args, { stdio: ['inherit', 'pipe', 'pipe'] });
    const out = [], err = [];
    child.stdout.on('data', d => { process.stdout.write(d); if (capture) out.push(d); });
    child.stderr.on('data', d => { process.stderr.write(d); if (capture) err.push(d); });
    child.on('error', e => { process.stderr.write(String(e)); resolve({ code: 1, stdout: '', stderr: '' }); });
    child.on('close', code => resolve({ code: code === null ? 1 : code, stdout: Buffer.concat(out).toString('utf8'), stderr: Buffer.concat(err).toString('utf8') }));
  });
}

// Luna reads MSBuild's console output (it looks for "Build FAILED."), so it must arrive complete.
// Writes to a pipe are asynchronous in Node; exiting right away would cut the output short.
function exitAfterFlush(code) {
  process.stdout.write('', () => process.stderr.write('', () => process.exit(code)));
}

function decodeXml(text) {
  return text.replace(/&quot;/g, '"').replace(/&apos;/g, "'").replace(/&lt;/g, '<').replace(/&gt;/g, '>').replace(/&amp;/g, '&');
}

// csproj paths may use Windows separators even on macOS.
function toLocalPath(p, baseDir) {
  const local = decodeXml(p).replace(/\\/g, '/');
  return path.isAbsolute(local) ? local : path.resolve(baseDir, local);
}

// The same compile can run in two places: Luna's real build (compiler in the Luna install, sources in the
// project's LunaTemp) and the size dashboard's sandbox (private compiler copy, sandbox LunaTemp). Keys replace
// both folders with placeholders, so a compile done by one is reused by the other.
function roots(csproj, text) {
  const compilerRoot = path.dirname(path.dirname(csproj));
  const m = text.replace(/\\/g, '/').match(/<Compile\s+Include="(\/[^"]*?)\/LunaTemp\/stage3\/processed-scripts\//);
  return { compilerRoot, projectRoot: m ? m[1] : null };
}

// Each root appears with and without its leading slash (csproj <Link> elements drop it).
function rootForms(root) {
  return root ? [root, root.replace(/^\//, '')] : [];
}

function normalizer({ compilerRoot, projectRoot }) {
  // Longest first: one root can contain the other.
  const pairs = [...rootForms(compilerRoot).map(r => [r, '<compiler>']), ...rootForms(projectRoot).map(r => [r, '<project>'])]
    .sort((x, y) => y[0].length - x[0].length);
  return value => pairs.reduce((result, [root, token]) => result.split(root).join(token), value.replace(/\\/g, '/'));
}

// Rewrites paths from one location to another. Two passes through placeholder tokens, longest root first:
// one root can contain the other (the dashboard's compiler copy lives inside the project folder). Each root is
// handled in its slash, backslash and no-leading-slash forms.
function relocate(text, from, to) {
  const forms = root => [root, root.replace(/\//g, '\\'), root.replace(/^\//, ''), root.replace(/^\//, '').replace(/\//g, '\\')];
  const pairs = [];
  let index = 0;
  for (const [a, b] of [[from.compilerRoot, to.compilerRoot], [from.projectRoot, to.projectRoot]]) {
    if (!a || !b || a === b) continue;
    const fromForms = forms(a), toForms = forms(b);
    fromForms.forEach((f, i) => pairs.push(['\u0001' + (index++) + '\u0001', f, toForms[i]]));
  }
  pairs.sort((x, y) => y[1].length - x[1].length);
  let result = text;
  for (const [token, a] of pairs) result = result.split(a).join(token);
  for (const [token, , b] of pairs) result = result.split(token).join(b);
  return result;
}

// Every input the compile reads: the project, its sources, its references, Bridge's settings.
function fingerprint(csproj) {
  const projectDir = path.dirname(csproj);
  const text = fs.readFileSync(csproj, 'utf8');
  const where = roots(csproj, text);
  const norm = normalizer(where);
  const hash = crypto.createHash('sha256');
  const normalizedArgs = args.map(a => a === csproj || path.resolve(a) === csproj ? '<csproj>' : norm(a));
  hash.update(`v${VERSION}\0${REAL_MSBUILD}\0${normalizedArgs.join('\0')}\0`);
  hash.update(norm(text));

  const inputs = [];
  for (const m of text.matchAll(/<Compile\s+Include="([^"]+)"/g)) inputs.push(toLocalPath(m[1], projectDir));
  for (const m of text.matchAll(/<HintPath>([^<]+)<\/HintPath>/g)) inputs.push(toLocalPath(m[1].trim(), projectDir));
  inputs.push(path.join(projectDir, 'bridge.json'));

  // The compiler itself and Luna's type maps (user.typemap.json is written by the compile, not read).
  const compilerRoot = path.dirname(projectDir);
  const packagesDir = path.join(compilerRoot, 'packages');
  for (const pkg of fs.existsSync(packagesDir) ? fs.readdirSync(packagesDir) : []) {
    const tools = path.join(packagesDir, pkg, 'tools');
    if (!/^Bridge/i.test(pkg) || !fs.existsSync(tools)) continue;
    for (const name of fs.readdirSync(tools).sort()) {
      const stat = fs.statSync(path.join(tools, name));
      if (stat.isFile()) hash.update(`\0${pkg}/${name}\0${stat.size}\0${stat.mtimeMs}`);
    }
  }
  const typemaps = path.join(compilerRoot, 'typemaps');
  for (const name of fs.existsSync(typemaps) ? fs.readdirSync(typemaps).sort() : []) {
    if (name !== 'user.typemap.json' && name.endsWith('.json')) inputs.push(path.join(typemaps, name));
  }

  for (const file of inputs) {
    hash.update(`\0${norm(file)}\0`);
    // A missing input changes the key instead of failing; MSBuild will report it on a miss.
    if (fs.existsSync(file)) hash.update(fs.readFileSync(file));
    else hash.update('<missing>');
  }
  return { key: hash.digest('hex'), files: inputs.length, where };
}

// Modification times of everything under the compiler folder, except package payloads.
function snapshot(root) {
  const result = new Map();
  (function walk(dir) {
    for (const entry of fs.readdirSync(dir, { withFileTypes: true })) {
      const full = path.join(dir, entry.name);
      if (entry.isDirectory()) {
        if (!SKIPPED_FOLDERS.has(entry.name)) walk(full);
      } else if (entry.isFile()) {
        const stat = fs.statSync(full);
        result.set(path.relative(root, full), stat.mtimeMs + ':' + stat.size);
      }
    }
  })(root);
  return result;
}

function copyFile(from, to) {
  fs.mkdirSync(path.dirname(to), { recursive: true });
  fs.copyFileSync(from, to);
}

function store(entryDir, root, written, result, where) {
  const partial = entryDir + '.partial-' + process.pid;
  fs.rmSync(partial, { recursive: true, force: true });
  for (const rel of written) copyFile(path.join(root, rel), path.join(partial, 'files', rel));
  fs.writeFileSync(path.join(partial, 'manifest.json'), JSON.stringify({ files: written, stdout: result.stdout, stderr: result.stderr, created: Date.now(),
    compilerRoot: where.compilerRoot, projectRoot: where.projectRoot }));
  fs.rmSync(entryDir, { recursive: true, force: true });
  fs.renameSync(partial, entryDir);
}

function restore(entryDir, root, where) {
  const manifest = JSON.parse(fs.readFileSync(path.join(entryDir, 'manifest.json'), 'utf8'));
  for (const rel of manifest.files) {
    if (!fs.existsSync(path.join(entryDir, 'files', rel))) throw new Error('cache entry incomplete: ' + rel);
  }
  const from = { compilerRoot: manifest.compilerRoot, projectRoot: manifest.projectRoot };
  for (const rel of manifest.files) {
    const target = path.join(root, rel);
    copyFile(path.join(entryDir, 'files', rel), target);
    // MSBuild's clean step deletes every file listed here: it must name files in THIS location.
    if (/FileListAbsolute\.txt$/.test(rel)) fs.writeFileSync(target, relocate(fs.readFileSync(target, 'utf8'), from, where));
  }
  manifest.stdout = relocate(manifest.stdout, from, where);
  manifest.stderr = relocate(manifest.stderr, from, where);
  // Mark the entry as recently used so pruning keeps it.
  const now = new Date();
  fs.utimesSync(entryDir, now, now);
  return manifest;
}

function prune() {
  const entries = fs.readdirSync(CACHE_ROOT, { withFileTypes: true })
    .filter(e => e.isDirectory() && /^[0-9a-f]{64}$/.test(e.name))
    .map(e => ({ dir: path.join(CACHE_ROOT, e.name), time: fs.statSync(path.join(CACHE_ROOT, e.name)).mtimeMs }))
    .sort((a, b) => b.time - a.time);
  for (const old of entries.slice(MAX_ENTRIES)) fs.rmSync(old.dir, { recursive: true, force: true });
}

async function main() {
  const csproj = args.find(a => /\.csproj$/i.test(a));
  const isRebuild = args.some(a => /^[/-]t(arget)?:Rebuild$/i.test(a));
  if (!csproj || !isRebuild || process.env.LUNA_COMPILE_CACHE === 'off') {
    exitAfterFlush((await runReal(false)).code);
    return;
  }

  const started = Date.now();
  const root = path.dirname(path.dirname(path.resolve(csproj)));
  let key, entryDir, before, where;
  try {
    const print = fingerprint(path.resolve(csproj));
    key = print.key;
    where = print.where;
    entryDir = path.join(CACHE_ROOT, key);
    if (fs.existsSync(path.join(entryDir, 'manifest.json'))) {
      const manifest = restore(entryDir, root, print.where);
      process.stdout.write(manifest.stdout);
      process.stderr.write(manifest.stderr);
      log(`HIT  ${key.slice(0, 12)} inputs=${print.files} files=${manifest.files.length} ${Date.now() - started}ms`);
      exitAfterFlush(0);
      return;
    }
    before = snapshot(root);
  } catch (e) {
    log(`ERROR before compile, running MSBuild uncached: ${e && e.stack || e}`);
    exitAfterFlush((await runReal(false)).code);
    return;
  }

  const result = await runReal(true);
  const failed = result.code !== 0 || /Build FAILED\./.test(result.stdout);
  if (failed) {
    log(`MISS ${key.slice(0, 12)} compile failed (exit ${result.code}), not cached`);
    exitAfterFlush(result.code);
    return;
  }
  try {
    const after = snapshot(root);
    // Whole output folders, not just the changed files: MSBuild skips copying references that are
    // already up to date, and a restore must also work after those folders were deleted.
    const folders = new Set([...after.keys()].filter(rel => before.get(rel) !== after.get(rel)).map(rel => path.dirname(rel)));
    const written = [...after.keys()].filter(rel => folders.has(path.dirname(rel)));
    store(entryDir, root, written, result, where);
    prune();
    log(`MISS ${key.slice(0, 12)} compiled and stored ${written.length} files in ${Date.now() - started}ms`);
  } catch (e) {
    log(`ERROR storing cache entry: ${e && e.stack || e}`);
  }
  exitAfterFlush(result.code);
}

main();
