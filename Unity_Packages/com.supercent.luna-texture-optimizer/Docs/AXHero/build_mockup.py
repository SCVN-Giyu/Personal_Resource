"""Builds LunaOptimizer-mockup.html: the browser page with a snapshot of real data built in, so it opens from the
file alone (no Unity, no server). Unity must be running the browser server while this script collects the data.

Usage: python3 -I build_mockup.py [port]   (default 47790)
Writes LunaOptimizer-mockup.html next to this script.
"""
import base64
import datetime
import json
import os
import re
import subprocess
import sys
import tempfile
import urllib.parse
import urllib.request

HERE = os.path.dirname(os.path.abspath(__file__))
PAGE = os.path.join(HERE, "..", "..", "Editor", "Web", "LunaOptimizer.html")
OUT = os.path.join(HERE, "LunaOptimizer-mockup.html")
PREVIEWED_TEXTURES = 16      # largest ones get Luna input/output images in their details
PREVIEW_PX = 320
DETAILED_ASSETS = 40
MAX_BYTES = 10 * 1024 * 1024


def main():
    port = int(sys.argv[1]) if len(sys.argv) > 1 else 47790
    base = "http://127.0.0.1:%d" % port
    page = urllib.request.urlopen(base + "/").read().decode("utf-8")
    key = re.search(r'var KEY = "([a-f0-9]+)"', page).group(1)

    def get(endpoint, **params):
        query = urllib.parse.urlencode(dict(params, key=key))
        return urllib.request.urlopen(base + endpoint + "?" + query, timeout=120).read()

    snap = {}

    def put(endpoint, body, mime="application/json", **params):
        snap[map_key(endpoint, params)] = "data:%s;base64,%s" % (mime, base64.b64encode(body).decode("ascii"))

    state = json.loads(get("/api/state"))
    put("/api/state", get("/api/state"))
    put("/api/overview", get("/api/overview"))
    put("/api/assets", get("/api/assets"))
    put("/api/exclusions", get("/api/exclusions"))
    put("/api/history", get("/api/history"))
    put("/api/sound-bitrates", b"[]")

    for row in state["textureRows"]:
        path = row["path"]
        put("/api/thumb", get("/api/thumb", path=path), "image/png", path=path)
        put("/api/texture", get("/api/texture", path=path), path=path)
    for row in sorted(state["textureRows"], key=lambda r: -r["predicted"])[:PREVIEWED_TEXTURES]:
        for kind in ("input", "output"):
            png = shrink(get("/api/texture-image", path=row["path"], kind=kind))
            put("/api/texture-image", png, "image/png", path=row["path"], kind=kind)
    for row in state["soundRows"]:
        put("/api/sound", get("/api/sound", path=row["path"]), path=row["path"])
    assets = json.loads(get("/api/assets"))
    for row in sorted(assets["rows"], key=lambda r: -r["playable"])[:DETAILED_ASSETS]:
        put("/api/asset", get("/api/asset", path=row["path"]), path=row["path"])
        try:
            put("/api/asset-preview", get("/api/asset-preview", path=row["path"]), "image/png", path=row["path"])
        except Exception:
            pass

    taken = datetime.datetime.now().strftime("%Y-%m-%d %H:%M")
    html = page.replace(key, "snapshot").replace("Connected to Unity", "Snapshot (offline)")
    # url(): answer from the snapshot (images and JSON as data: URLs); unknown requests get an empty answer.
    html = html.replace('function url(path, params) {',
                        'function url(path, params) { if (window.__SNAP_URL) return window.__SNAP_URL(path, params);', 1)
    shim = SHIM.replace("__SNAP__", json.dumps(snap)).replace("__TAKEN__", taken)
    html = html.replace("<script>", shim + "\n<script>", 1)
    with open(OUT, "w", encoding="utf-8") as f:
        f.write(html)
    size = os.path.getsize(OUT)
    print("%s  %.2f MB  (%d snapshot entries)%s" % (OUT, size / 1048576.0, len(snap), "  OVER 10 MB" if size > MAX_BYTES else ""))


def map_key(endpoint, params):
    # Raw values (no percent-encoding: Python and JavaScript encode characters such as "!" differently).
    kept = sorted((k, str(v)) for k, v in params.items() if k not in ("key", "v", "r"))
    return endpoint + "|" + "&".join(k + "=" + v for k, v in kept)


def shrink(png):
    """Scales a PNG down to PREVIEW_PX with macOS sips (keeps the mockup small)."""
    with tempfile.TemporaryDirectory() as folder:
        path = os.path.join(folder, "image.png")
        with open(path, "wb") as f:
            f.write(png)
        subprocess.run(["sips", "-Z", str(PREVIEW_PX), path], check=True, capture_output=True)
        with open(path, "rb") as f:
            return f.read()


SHIM = """<script>
// Snapshot mode: data captured from the live tool on __TAKEN__; nothing here talks to Unity.
(function () {
  var SNAP = __SNAP__;
  var EMPTY_JSON = "data:application/json;base64," + btoa("null");
  var EMPTY_PNG = "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNkYAAAAAYAAjCB0C8AAAAASUVORK5CYII=";
  window.__SNAP_URL = function (path, params) {
    var kept = Object.keys(params || {}).filter(function (k) { return k !== "v" && k !== "r"; }).sort();
    var key = path + "|" + kept.map(function (k) { return k + "=" + params[k]; }).join("&");
    if (SNAP[key]) return SNAP[key];
    return /thumb|image|preview/.test(path) ? EMPTY_PNG : path === "/api/audio" ? "data:audio/wav;base64," : EMPTY_JSON;
  };
  var realFetch = window.fetch.bind(window);
  window.fetch = function (input, init) {
    if (String(input).indexOf("/api/action") === 0)
      return Promise.resolve(new Response(JSON.stringify({ ok: false, message: "This is a snapshot taken __TAKEN__. Open Luna Optimizer from Unity (Supercent → Luna Optimizer (Browser)) to make changes." }), { headers: { "Content-Type": "application/json" } }));
    return realFetch(input, init);
  };
  document.addEventListener("DOMContentLoaded", function () {
    var bar = document.createElement("div");
    bar.textContent = "Snapshot of the live tool · " + "__TAKEN__" + " · real project data, buttons disabled. In Unity the same page is live.";
    bar.style.cssText = "background:linear-gradient(90deg,#4c8dde,#7c5cff);color:#fff;text-align:center;font:600 12px -apple-system,sans-serif;padding:6px 12px";
    document.body.insertBefore(bar, document.body.firstChild);
  });
})();
</script>"""


if __name__ == "__main__":
    main()
