// Preloaded (node -r) when the dashboard compiles the CURRENT project scripts in its sandbox with
// Luna's own tasks: `code:compose` (collect, copy, csproj; `code-v2:compose` with Luna Compiler V2) then
// `code:build` (transform + compile).
// Adjusts those two tasks only:
//  - code:build no longer requires account:authorize (a size measurement must not contact Luna's servers);
//  - after code:compose, Luna's project diagnostics step is skipped;
//  - before code:build, every compiled file must lie inside the sandbox ($LUNA_LIVE_SANDBOX), otherwise the
//    run aborts: Luna's SyntaxTransformer rewrites the listed files in place;
//  - after code:build, the compiled user code is copied to $LUNA_LIVE_SCRIPTS_TARGET for package:stats.
'use strict';
const fs = require('fs');
const path = require('path');

const SANDBOX = process.env.LUNA_LIVE_SANDBOX && path.resolve(process.env.LUNA_LIVE_SANDBOX);
const TARGET = process.env.LUNA_LIVE_SCRIPTS_TARGET;
const COMPILER = process.env.LUNA_LIVE_COMPILER; // sandbox LunaCompiler folder

function decodeXml(text) {
  return text.replace(/&quot;/g, '"').replace(/&apos;/g, "'").replace(/&lt;/g, '<').replace(/&gt;/g, '>').replace(/&amp;/g, '&');
}

function assertInsideSandbox() {
  const csproj = path.join(COMPILER, 'Scripts', 'Scripts.csproj');
  const text = fs.readFileSync(csproj, 'utf8');
  const outside = [];
  let count = 0;
  for (const m of text.matchAll(/<Compile\s+Include="([^"]+)"/g)) {
    count++;
    const file = path.resolve(path.dirname(csproj), decodeXml(m[1]).replace(/\\/g, '/'));
    // Allowed: the sandbox project copy and the sandbox's private compiler folder (Luna's template sources).
    if (!file.startsWith(SANDBOX + path.sep) && !file.startsWith(path.resolve(COMPILER) + path.sep)) outside.push(file);
  }
  const fail = message => {
    console.error(message); // Luna's task runner does not print task errors itself
    throw new Error(message);
  };
  if (count === 0) fail('LUNA_LIVE: the composed project lists no scripts');
  if (outside.length) fail('LUNA_LIVE: refusing to transform files outside the sandbox: ' + outside.slice(0, 3).join(', '));
}

function wrap(createTask) {
  return function (...args) {
    const ns = this && this.currentNamespace ? this.currentNamespace.path : (global.jake && jake.currentNamespace && jake.currentNamespace.path);
    const name = args[1];
    if (process.env.LUNA_LIVE_TIMING) {
      const actionIndex = args.findIndex((a, i) => i > 1 && typeof a === 'function');
      if (actionIndex >= 0) {
        const action = args[actionIndex];
        args[actionIndex] = async function (...actionArgs) {
          const started = Date.now();
          try { return await action.apply(this, actionArgs); }
          catch (e) { process.stderr.write(`LUNA_LIVE_ERROR ${ns ? ns + ':' : ''}${name}: ${e && e.stack || e}\n`); throw e; }
          finally { process.stderr.write(`LUNA_LIVE_TIMING ${ns ? ns + ':' : ''}${name} ${Date.now() - started}ms\n`); }
        };
      }
    }
    // Compiler V2 projects (luna.json unity.enableLunaCompilerV2) compose with code-v2:compose instead:
    // Unity's C# compiler builds the scripts, the assembly is decompiled into LunaTemp, Bridge compiles that.
    if ((ns === 'code' || ns === 'code-v2') && (name === 'build' || name === 'compose')) {
      const prereqIndex = args.findIndex((a, i) => i > 1 && Array.isArray(a));
      const actionIndex = args.findIndex((a, i) => i > 1 && typeof a === 'function');
      if (name === 'build' && prereqIndex >= 0) args[prereqIndex] = args[prereqIndex].filter(p => p !== 'account:authorize');
      if (actionIndex >= 0) {
        const action = args[actionIndex];
        args[actionIndex] = async function (...actionArgs) {
          if (name === 'build') assertInsideSandbox();
          const result = await action.apply(this, actionArgs);
          if (name === 'compose') global.deobfiscationProcess = true; // skips diagnostics in code:build only
          if (name === 'build' && TARGET) {
            fs.mkdirSync(path.dirname(TARGET), { recursive: true });
            fs.copyFileSync(path.join(COMPILER, 'bin', 'UnityScriptsCompiler.js'), TARGET);
          }
          return result;
        };
      }
    }
    return createTask.apply(this, args);
  };
}

// jake.createTask may be assigned before or after global.jake itself, so trap both.
function trapCreateTask(jakeObject) {
  let current = jakeObject.createTask;
  Object.defineProperty(jakeObject, 'createTask', {
    configurable: true,
    enumerable: true,
    get() { return current; },
    set(fn) { current = typeof fn === 'function' ? wrap(fn) : fn; },
  });
  if (typeof current === 'function') current = wrap(current);
}

if (SANDBOX && COMPILER) {
  let jakeValue;
  Object.defineProperty(global, 'jake', {
    configurable: true,
    get() { return jakeValue; },
    set(value) {
      jakeValue = value;
      if (value && typeof value === 'object') trapCreateTask(value);
    },
  });
}

// Debugging aid (LUNA_LIVE_TIMING only): Luna reports some task failures through log.info, which its log level hides.
if (process.env.LUNA_LIVE_TIMING) {
  let logValue;
  Object.defineProperty(global, 'log', {
    configurable: true,
    get() { return logValue; },
    set(value) {
      logValue = value;
      if (value && typeof value.info === 'function' && !value.info.__lunaLive) {
        const info = value.info.bind(value);
        value.info = (...a) => { if (a.some(x => typeof x === 'string' && /Error|at /.test(x))) process.stderr.write('LUNA_LOG_INFO ' + a.join(' ') + '\n'); return info(...a); };
        value.info.__lunaLive = true;
      }
    },
  });
}
