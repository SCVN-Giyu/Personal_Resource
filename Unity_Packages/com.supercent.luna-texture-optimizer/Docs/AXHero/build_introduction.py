"""Builds introduction.html (AX Hero, step 02): one self-contained page, images embedded as Base64.

Usage: python3 -I build_introduction.py <screenshot.png> [<screenshot.png> ...]   (captions: CAPTIONS, by file name)
Writes introduction.html next to this script.
"""
import base64
import html
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, "introduction.html")
MAX_BYTES = 10 * 1024 * 1024


def data_uri(path):
    with open(path, "rb") as f:
        return "data:image/png;base64," + base64.b64encode(f.read()).decode("ascii")


def figure(path, caption):
    if not path:
        return ""
    return ('<figure><img src="%s" alt="%s"><figcaption>%s</figcaption></figure>'
            % (data_uri(path), html.escape(caption), html.escape(caption)))


PAGE = """<!doctype html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>Luna Optimizer</title>
<style>
  :root {
    --bg: #f6f7f9; --surface: #fff; --text: #1b1f27; --muted: #5f6775; --border: #e3e6ec;
    --accent: #2f6fc0; --accent-soft: rgba(47,111,192,.09); --good: #2f8f3e; --warn: #b8680a;
  }
  @media (prefers-color-scheme: dark) {
    :root { --bg: #15171c; --surface: #1e2127; --text: #e8eaef; --muted: #9ba2ae; --border: #2e333c;
      --accent: #5b9be6; --accent-soft: rgba(91,155,230,.13); --good: #5cc46b; --warn: #f2a61a; }
  }
  * { box-sizing: border-box; }
  body { margin: 0; background: var(--bg); color: var(--text);
    font: 16px/1.6 -apple-system, BlinkMacSystemFont, "Segoe UI", Inter, Roboto, sans-serif; -webkit-font-smoothing: antialiased; }
  .wrap { max-width: 1040px; margin: 0 auto; padding: 56px 24px 80px; }
  header { margin-bottom: 36px; }
  .eyebrow { color: var(--accent); font-weight: 700; font-size: 13px; letter-spacing: .08em; text-transform: uppercase; }
  h1 { font-size: 44px; line-height: 1.1; margin: 10px 0 14px; letter-spacing: -.5px; }
  .lead { font-size: 19px; color: var(--muted); max-width: 760px; margin: 0; }
  .meta { display: flex; gap: 8px; flex-wrap: wrap; margin-top: 18px; }
  .pill { padding: 4px 12px; border-radius: 999px; border: 1px solid var(--border); background: var(--surface); font-size: 13px; color: var(--muted); }
  section { background: var(--surface); border: 1px solid var(--border); border-radius: 16px; padding: 28px 30px; margin-top: 20px; }
  h2 { display: flex; align-items: center; gap: 12px; font-size: 22px; margin: 0 0 14px; }
  .n { width: 30px; height: 30px; border-radius: 9px; display: grid; place-items: center; background: var(--accent-soft); color: var(--accent); font-size: 15px; font-weight: 800; flex-shrink: 0; }
  p { margin: 0 0 12px; }
  ul { margin: 0; padding-left: 20px; }
  li { margin: 6px 0; }
  figure { margin: 6px 0 0; }
  figure + figure { margin-top: 22px; }
  img { width: 100%; height: auto; display: block; border-radius: 12px; border: 1px solid var(--border); }
  figcaption { color: var(--muted); font-size: 13px; margin-top: 8px; }
  .cols { display: grid; grid-template-columns: 1fr 1fr; gap: 18px; }
  @media (max-width: 760px) { .cols { grid-template-columns: 1fr; } h1 { font-size: 34px; } section { padding: 22px 20px; } }
  .box { border: 1px solid var(--border); border-radius: 12px; padding: 16px 18px; }
  .box h3 { margin: 0 0 6px; font-size: 16px; }
  .stats { display: grid; grid-template-columns: repeat(4, 1fr); gap: 14px; margin-bottom: 16px; }
  @media (max-width: 760px) { .stats { grid-template-columns: 1fr 1fr; } }
  .stat { border: 1px solid var(--border); border-radius: 12px; padding: 14px 16px; }
  .stat b { display: block; font-size: 26px; letter-spacing: -.3px; }
  .stat span { color: var(--muted); font-size: 13px; }
  .good { color: var(--good); }
  .warn { color: var(--warn); }
  .muted { color: var(--muted); }
  footer { color: var(--muted); font-size: 13px; text-align: center; margin-top: 30px; }
</style>
</head>
<body>
<div class="wrap">
  <header>
    <div class="eyebrow">AX Hero · Service intro</div>
    <h1>Luna Optimizer</h1>
    <p class="lead">See what makes a Luna playable heavy and shrink it in a few clicks, without a full Luna build.
      It works inside Unity or in a web browser while Unity is open.</p>
    <div class="meta">
      <span class="pill">Unity 2022.3 editor tool</span>
      <span class="pill">Luna Playable 7.2</span>
      <span class="pill">Browser page served by Unity</span>
      <span class="pill">Built with Claude Code</span>
    </div>
  </header>

  <section>
    <h2><span class="n">1</span>Service image</h2>
    {{IMAGES}}
  </section>

  <section>
    <h2><span class="n">2</span>Problem</h2>
    <p>Playable ads have hard size caps per ad network (for example 5 MB). Finding what to cut is slow:</p>
    <ul>
      <li>The only way to know the real size is a full Luna build, which takes minutes, and its report is hard to act on.</li>
      <li>Texture and sound settings live in Luna's own window, one asset at a time, with no preview of the result.</li>
      <li>It is unclear which change saves the most, and what breaks if an asset is removed.</li>
      <li>Each try-build-check loop costs minutes, so teams stop optimizing early or ship over the cap.</li>
    </ul>
  </section>

  <section>
    <h2><span class="n">3</span>Solution</h2>
    <p>Luna Optimizer runs Luna's own pipeline locally and shows the result immediately.</p>
    <div class="cols">
      <div class="box"><h3>Measure without building</h3><p class="muted">Quick predict runs Luna's size statistics on the project: total against the network cap and what ships per asset type.</p></div>
      <div class="box"><h3>One-click optimizations</h3><p class="muted">Pick the smallest texture format at a chosen quality, trim silence from sounds, make sounds mono. Every change is previewed with its exact saving before it is saved.</p></div>
      <div class="box"><h3>Safe changes</h3><p class="muted">Nothing is written until "Save to Luna", saves go through Luna's own settings, and every save can be undone.</p></div>
      <div class="box"><h3>In Unity or in the browser</h3><p class="muted">Simple and Manual modes inside Unity, or every feature in a browser tab (Supercent → Luna Optimizer (Browser)) while Unity runs it: Overview, Textures, Sounds, Models &amp; Other, Exclusions, scene switch and Undo.</p></div>
    </div>
  </section>

  <section>
    <h2><span class="n">4</span>Expected impact</h2>
    <div class="stats">
      <div class="stat"><b class="good">~0.2 s</b><span>scene estimate without a build: 1,703 KB vs 1,711 KB actual</span></div>
      <div class="stat"><b>0 B</b><span>difference from Luna's real output for textures</span></div>
      <div class="stat"><b>772,255 B</b><span>scripts, the same number Luna's build reports</span></div>
      <div class="stat"><b>53,000 B</b><span>Opus sound, the same size as the built file</span></div>
    </div>
    <ul>
      <li>Optimizing and navigating is much faster: decisions are made from a ranked list, not from trial builds.</li>
      <li>Designers and artists can see the effect of a format, size or trim change before anyone builds.</li>
      <li>Fewer "over the cap" surprises at the end of a project. Already used on ThemeparkManager and SmashDown.</li>
    </ul>
  </section>

  <section>
    <h2><span class="n">5</span>Limitations &amp; resources</h2>
    <div class="cols">
      <div class="box"><h3>Limitations</h3>
        <ul>
          <li>Unity must be open: the browser page is a remote control for the editor, on this computer only.</li>
          <li><span class="warn">The total estimate is larger than the final build</span> (the real build is smaller); a final Luna build is still needed before delivery.</li>
          <li>Verified with Luna 7.2.0 on macOS; other versions need a check.</li>
          <li>Opus audio is smaller but needs recent iOS; devices not yet tested.</li>
        </ul>
      </div>
      <div class="box"><h3>Resources for a real rollout</h3>
        <ul>
          <li>Shared package linked into each project (one copy, updates reach every project).</li>
          <li>A Luna install with Node and ffmpeg (already part of Luna).</li>
          <li>Time to check the estimate gap on more projects and test on Windows.</li>
          <li>Optional: a hosted, read-only size report so PMs can see results without Unity.</li>
        </ul>
      </div>
    </div>
  </section>

  <footer>Luna Optimizer · Supercent · built with Claude Code</footer>
</div>
</body>
</html>
"""


CAPTIONS = {
    "capture-browser.png": "Luna Optimizer in the browser (Chrome), served by the Unity editor: the size against the network cap, one-click optimizations and what ships, from the real ThemeparkManager project.",
    "capture-browser-texture.png": "Every texture, with its format and max size editable in the table. The details panel shows Luna's real input and output side by side, the predicted size, and the chain that ships it.",
    "capture-browser-sound.png": "Sounds: format, bitrate, channels and trim per sound; listen to the source and Luna's output, and drag the trim on the waveform.",
    "capture-unity.png": "The same tool inside Unity (Manual mode).",
}


def main():
    if len(sys.argv) < 2:
        sys.exit(__doc__)
    images = "".join(figure(path, CAPTIONS.get(os.path.basename(path), "")) for path in sys.argv[1:])
    page = PAGE.replace("{{IMAGES}}", images)
    with open(OUT, "w", encoding="utf-8") as f:
        f.write(page)
    size = os.path.getsize(OUT)
    print("%s  %.2f MB%s" % (OUT, size / 1048576.0, "  (OVER 10 MB)" if size > MAX_BYTES else ""))


if __name__ == "__main__":
    main()
