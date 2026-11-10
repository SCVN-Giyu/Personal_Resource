#!/usr/bin/env node
/**
 * Seeded Luna Browser Chaos Fuzzer (run_luna_chaos.mjs)
 *
 * Hunts rare timing, race-condition, orientation-flip, and input-spamming bugs
 * directly on an exported Luna Playable HTML file or localhost URL.
 *
 * Features:
 * 1. Deterministic PRNG Seed (overrides Math.random in browser so any rare failure is 100% reproducible).
 * 2. Synthetic Variable-dt / Jitter Clock (simulates normal 16ms frames, 4ms high-Hz frames, and 250ms–450ms GC/shader spikes).
 * 3. Chaotic Input Monkey:
 *    - Rapid multi-tap bursts at the exact same pixel and random canvas coordinates
 *    - Fast pointer drag sweeps & mid-drag interruptions (pointerdown without pointerup, or vice-versa)
 *    - Portrait (390x844) <-> Landscape (844x390) mid-frame orientation flips
 * 4. Multi-Layer Failure Detectors:
 *    - Uncaught JS exceptions (`pageerror`) & `console.error`
 *    - Common silent Luna/WebGL traps (`is not a function`, `NaN`, `null is not an object`, `RuntimeError: unreachable`)
 *    - Optional `window.__CHAOS_INVARIANTS__()` hook if exposed by the playable
 * 5. Black-Box Flight Recorder:
 *    - When a seed fails, saves the exact `--seed <N>` replay command, the action/frame trace, and a failure screenshot.
 *
 * Usage:
 *   node run_luna_chaos.mjs <path-to-build.html-or-url> [--runs 25] [--frames 180] [--seed 1000] [--headed] [--out ./chaos_reports]
 */

import fs from "node:fs";
import path from "node:path";
import { pathToFileURL } from "node:url";

function parseArgs(argv) {
  const args = {
    target: null,
    runs: 20,
    frames: 160,
    seed: 1337,
    singleSeed: false,
    headed: false,
    outDir: path.resolve(process.cwd(), "chaos_reports"),
  };

  for (let i = 2; i < argv.length; i++) {
    const a = argv[i];
    if (!a.startsWith("--") && !args.target) {
      args.target = a;
    } else if (a === "--runs" && argv[i + 1]) {
      args.runs = parseInt(argv[++i], 10);
    } else if (a === "--frames" && argv[i + 1]) {
      args.frames = parseInt(argv[++i], 10);
    } else if (a === "--seed" && argv[i + 1]) {
      args.seed = parseInt(argv[++i], 10);
      args.singleSeed = true;
    } else if (a === "--headed") {
      args.headed = true;
    } else if (a === "--out" && argv[i + 1]) {
      args.outDir = path.resolve(argv[++i]);
    }
  }

  if (!args.target) {
    console.error(
      "Usage: node run_luna_chaos.mjs <build.html | http://...> [--runs 20] [--frames 160] [--seed <N>] [--headed] [--out ./chaos_reports]"
    );
    process.exit(1);
  }
  if (args.singleSeed) {
    args.runs = 1;
  }
  return args;
}

// Mulberry32 deterministic PRNG
function makeRng(seed) {
  let a = seed >>> 0;
  return function rng() {
    let t = (a += 0x6d2b79f5);
    t = Math.imul(t ^ (t >>> 15), t | 1);
    t ^= t + Math.imul(t ^ (t >>> 7), t | 61);
    return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
  };
}

// Injected into the browser page BEFORE Luna scripts execute
function browserInitHook(seed) {
  // 1. Seeded Math.random so physics/gameplay randomness is deterministic per seed
  let s = seed >>> 0;
  Math.random = function () {
    let t = (s += 0x6d2b79f5);
    t = Math.imul(t ^ (t >>> 15), t | 1);
    t ^= t + Math.imul(t ^ (t >>> 7), t | 61);
    return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
  };

  // 2. Virtualized Time Offset so chaos runner can inject lag spikes (dt = 250ms) or micro-frames (dt = 4ms)
  let virtualTimeOffsetMs = 0;
  const origPerfNow = performance.now.bind(performance);
  const origDateNow = Date.now.bind(Date);

  performance.now = function () {
    return origPerfNow() + virtualTimeOffsetMs;
  };
  Date.now = function () {
    return origDateNow() + Math.round(virtualTimeOffsetMs);
  };

  window.__CHAOS_ADVANCE_TIME__ = function (deltaMs) {
    virtualTimeOffsetMs += deltaMs;
  };

  // 3. Catch unhandled promise rejections inside the browser
  window.__CHAOS_ERRORS__ = [];
  window.addEventListener("unhandledrejection", (e) => {
    window.__CHAOS_ERRORS__.push(
      `UnhandledRejection: ${e.reason ? e.reason.stack || e.reason : "unknown"}`
    );
  });
}

async function loadPlaywright() {
  try {
    return await import("playwright");
  } catch {
    console.error(
      "ERROR: 'playwright' package not found in current module path.\n" +
        "Install it once globally or in your workspace with:\n" +
        "  npm i -D playwright && npx playwright install chromium"
    );
    process.exit(2);
  }
}

async function runSingleSeed(browser, targetUrl, seed, totalFrames, outDir) {
  const rng = makeRng(seed);
  const context = await browser.newContext({
    viewport: { width: 390, height: 844 }, // Start in mobile portrait
    hasTouch: true,
    isMobile: true,
  });

  const page = await context.newPage();
  const flightLog = [];
  const errors = [];

  const recordEvent = (frame, desc) => {
    flightLog.push(`[Frame ${String(frame).padStart(3, "0")}] ${desc}`);
    if (flightLog.length > 40) flightLog.shift();
  };

  page.on("pageerror", (err) => {
    errors.push(`PageError: ${err.message}\n${err.stack || ""}`);
  });

  page.on("console", (msg) => {
    const type = msg.type();
    const text = msg.text();
    if (type === "error") {
      errors.push(`ConsoleError: ${text}`);
    } else if (
      /is not a function|NaN|RuntimeError|unreachable|NullReferenceException|IndexOutOfRangeException/i.test(
        text
      )
    ) {
      errors.push(`SuspiciousLog (${type}): ${text}`);
    }
  });

  await page.addInitScript(browserInitHook, seed);
  await page.goto(targetUrl, { waitUntil: "domcontentloaded", timeout: 30000 });
  await page.waitForTimeout(300); // Allow initial WebGL context setup

  let vp = { width: 390, height: 844, mode: "portrait" };

  for (let f = 1; f <= totalFrames; f++) {
    const roll = rng();

    // 1. 12% chance: Inject a Frame-Time Spike (200ms - 450ms) or Micro-Frame (2ms)
    if (roll < 0.12) {
      const spikeMs = rng() < 0.75 ? Math.round(200 + rng() * 250) : 2;
      recordEvent(f, `TIME_WARP delta=+${spikeMs}ms`);
      await page.evaluate((ms) => window.__CHAOS_ADVANCE_TIME__(ms), spikeMs);
    }

    // 2. 6% chance: Orientation Flip (Portrait <-> Landscape) mid-gameplay
    else if (roll < 0.18) {
      vp =
        vp.mode === "portrait"
          ? { width: 844, height: 390, mode: "landscape" }
          : { width: 390, height: 844, mode: "portrait" };
      recordEvent(f, `ORIENTATION_FLIP -> ${vp.mode} (${vp.width}x${vp.height})`);
      await page.setViewportSize({ width: vp.width, height: vp.height });
    }

    // 3. 35% chance: Angry Multi-Tap Burst (2 to 5 rapid taps in same frame window)
    else if (roll < 0.53) {
      const taps = 2 + Math.floor(rng() * 4);
      const x = Math.round((0.1 + rng() * 0.8) * vp.width);
      const y = Math.round((0.15 + rng() * 0.75) * vp.height);
      recordEvent(f, `MULTI_TAP_BURST count=${taps} at (${x}, ${y})`);
      for (let t = 0; t < taps; t++) {
        await page.mouse.click(x, y, { delay: Math.floor(rng() * 8) });
      }
    }

    // 4. 25% chance: Fast Pointer Drag /Interrupted Drag
    else if (roll < 0.78) {
      const x1 = Math.round((0.15 + rng() * 0.7) * vp.width);
      const y1 = Math.round((0.2 + rng() * 0.6) * vp.height);
      const x2 = Math.round((0.15 + rng() * 0.7) * vp.width);
      const y2 = Math.round((0.2 + rng() * 0.6) * vp.height);
      const interruptMidDrag = rng() < 0.25;
      recordEvent(
        f,
        `POINTER_DRAG (${x1},${y1})->(${x2},${y2}) interrupted=${interruptMidDrag}`
      );
      await page.mouse.move(x1, y1);
      await page.mouse.down();
      await page.mouse.move(x2, y2, { steps: 3 });
      if (!interruptMidDrag) {
        await page.mouse.up();
      }
    }

    // 5. Check custom game invariants if exposed on window.__CHAOS_INVARIANTS__
    const pageErrors = await page.evaluate(() => {
      const errs = [...(window.__CHAOS_ERRORS__ || [])];
      window.__CHAOS_ERRORS__ = [];
      if (typeof window.__CHAOS_INVARIANTS__ === "function") {
        try {
          const res = window.__CHAOS_INVARIANTS__();
          if (res && res.ok === false) {
            errs.push(`InvariantViolation: ${res.reason || JSON.stringify(res)}`);
          }
        } catch (e) {
          errs.push(`InvariantHookError: ${e.message}`);
        }
      }
      return errs;
    });
    errors.push(...pageErrors);

    if (errors.length > 0) {
      fs.mkdirSync(outDir, { recursive: true });
      const screenshotPath = path.join(outDir, `fail_seed_${seed}_frame_${f}.png`);
      const reportPath = path.join(outDir, `fail_seed_${seed}_frame_${f}.json`);
      await page.screenshot({ path: screenshotPath }).catch(() => {});

      const report = {
        seed,
        failedAtFrame: f,
        replayCommand: `node run_luna_chaos.mjs "${targetUrl}" --seed ${seed} --frames ${totalFrames} --headed`,
        errors,
        flightLog,
        screenshotPath,
      };
      fs.writeFileSync(reportPath, JSON.stringify(report, null, 2));
      await context.close();
      return { ok: false, report, reportPath };
    }

    await page.waitForTimeout(16);
  }

  await context.close();
  return { ok: true, seed };
}

async function main() {
  const args = parseArgs(process.argv);
  const { chromium } = await loadPlaywright();

  const targetUrl =
    args.target.startsWith("http://") ||
    args.target.startsWith("https://") ||
    args.target.startsWith("file://")
      ? args.target
      : pathToFileURL(path.resolve(args.target)).href;

  console.log(`=== Luna Seeded Browser Chaos Fuzzer ===`);
  console.log(`Target:  ${targetUrl}`);
  console.log(`Runs:    ${args.runs} (starting seed: ${args.seed}, frames/run: ${args.frames})`);
  console.log(`Mode:    ${args.headed ? "HEADED (Visual Replay)" : "HEADLESS"}\n`);

  const browser = await chromium.launch({ headless: !args.headed });
  const failures = [];

  for (let i = 0; i < args.runs; i++) {
    const currentSeed = args.seed + i;
    process.stdout.write(`  [Run ${i + 1}/${args.runs}] Seed ${currentSeed} ... `);
    const res = await runSingleSeed(
      browser,
      targetUrl,
      currentSeed,
      args.frames,
      args.outDir
    );
    if (res.ok) {
      console.log(`PASS`);
    } else {
      console.log(`FAIL at Frame ${res.report.failedAtFrame}!`);
      console.log(`    Error:  ${res.report.errors[0]}`);
      console.log(`    Report: ${res.reportPath}`);
      console.log(`    Replay: ${res.report.replayCommand}`);
      failures.push(res.report);
    }
  }

  await browser.close();

  console.log(`\n=== Summary ===`);
  console.log(`Total Runs: ${args.runs} | Passed: ${args.runs - failures.length} | Failed: ${failures.length}`);
  if (failures.length > 0) {
    process.exit(1);
  }
}

main();
