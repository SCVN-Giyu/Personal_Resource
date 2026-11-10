#!/usr/bin/env python3
"""study-and-harden helper: deterministic bookkeeping so agents don't hand-edit it.

Usage:
  study.py init <subject-slug> [--title "Readable Title"] [--open]
  study.py question <subject-slug> <concept> "<question in the user's words>" [--hard <code>]
  study.py set <subject-slug> <Q-id> <field> <value> [--note "history note"]
  study.py status <subject-slug>        # session-start list (needs attention / blocked / quiz)
  study.py summary <subject-slug>       # regenerate the Manual Summary table in QA_LOG.md
  study.py open <subject-slug>          # open QA_LOG.md dashboard in Obsidian
  study.py where <subject-slug>         # print the study folder path

Config: ../config.env (KEY="value" lines). Keys: STUDY_ROOT, OBSIDIAN_VAULT_PATH, OBSIDIAN_VAULT_NAME.
"""
import argparse, datetime, os, re, subprocess, sys, urllib.parse

SKILL_DIR = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
REF = os.path.join(SKILL_DIR, "references")

ANSWER_STATES = ["open", "explained", "sourced", "verified", "contested", "blocked", "parked"]
UNDERSTANDING = ["lost", "can-follow", "can-explain", "can-apply"]
HARD = ["concept-gap", "terminology", "code-confusion", "source-contradiction", "tooling-env", "misconception"]
FIELDS = {"answer_state": ANSWER_STATES, "understanding": UNDERSTANDING, "hard_because": HARD}


def load_config():
    cfg = {}
    path = os.path.join(SKILL_DIR, "config.env")
    if os.path.exists(path):
        for line in open(path, encoding="utf-8"):
            m = re.match(r'\s*([A-Z_]+)\s*=\s*"?(.*?)"?\s*$', line)
            if m and not line.lstrip().startswith("#"):
                val = os.path.expandvars(os.path.expanduser(m.group(2)))
                for k, v in cfg.items():
                    val = val.replace("${%s}" % k, v).replace("$" + k, v)
                cfg[m.group(1)] = val
    cfg.setdefault("STUDY_ROOT", os.path.expanduser("~/Documents/Study"))
    for k in ("STUDY_ROOT", "OBSIDIAN_VAULT_PATH", "OBSIDIAN_VAULT_NAME"):
        if os.environ.get(k):
            cfg[k] = os.environ[k]
    return cfg


CFG = load_config()
today = lambda: datetime.date.today().isoformat()


def root(slug):
    return os.path.join(CFG["STUDY_ROOT"], slug)


def read_fm(path):
    text = open(path, encoding="utf-8").read()
    m = re.match(r"---\n(.*?)\n---\n", text, re.S)
    fm = {}
    if m:
        for line in m.group(1).splitlines():
            if ":" in line:
                k, v = line.split(":", 1)
                fm[k.strip()] = v.strip()
    return fm, text


def questions(slug):
    qdir = os.path.join(root(slug), "questions")
    out = []
    if not os.path.isdir(qdir):
        return out
    for name in sorted(os.listdir(qdir)):
        if name.endswith(".md"):
            p = os.path.join(qdir, name)
            fm, text = read_fm(p)
            if fm.get("type") != "qa":
                continue
            qm = re.search(r"## Question\n(.*?)\n\n", text + "\n\n", re.S)
            fm["_q"] = (qm.group(1).strip() if qm else "").replace("\n", " ")
            fm["_path"] = p
            out.append(fm)
    return out


def cmd_init(a):
    r = root(a.slug)
    title = a.title or a.slug
    for d in ("questions", "concepts", "evidence", "lessons"):
        os.makedirs(os.path.join(r, d), exist_ok=True)
    qa = os.path.join(r, "QA_LOG.md")
    if not os.path.exists(qa):
        t = open(os.path.join(REF, "qa-log-template.md"), encoding="utf-8").read()
        open(qa, "w", encoding="utf-8").write(t.replace("<subject-slug>", a.slug).replace("<subject>", title))
    idx = os.path.join(r, "INDEX.md")
    if not os.path.exists(idx):
        open(idx, "w", encoding="utf-8").write(
            f"# Study: {title}\n\n- Target: <platform / budget / scale>\n- Goal: <learn | learn+fix | learn+extend>\n"
            f"- Speed: <light | full>\n- Dashboard: [[QA_LOG]]\n\n## Concepts\n\n"
            "| # | Concept | Status | Dossier |\n|---|---|---|---|\n")
    print(r)
    if a.open:
        cmd_open(a)


def cmd_question(a):
    if a.hard and a.hard not in HARD:
        sys.exit(f"--hard must be one of {HARD}")
    qdir = os.path.join(root(a.slug), "questions")
    if not os.path.isdir(qdir):
        sys.exit(f"Subject not initialised. Run: study.py init {a.slug}")
    nums = [int(m.group(1)) for n in os.listdir(qdir) if (m := re.match(r"Q-(\d+)\.md$", n))]
    qid = "Q-%03d" % (max(nums, default=0) + 1)
    t = open(os.path.join(REF, "qa-entry-template.md"), encoding="utf-8").read()
    t = (t.replace("id: Q-001", f"id: {qid}").replace("<subject-slug>", a.slug)
          .replace("concept: NN-slug", f"concept: {a.concept}")
          .replace("YYYY-MM-DD", today(), 2)
          .replace("<the user's own words>", a.text))
    if a.hard:
        t = t.replace("hard_because: concept-gap", f"hard_because: {a.hard}", 1)
    t = t.replace("questions/Q-001.md", f"questions/{qid}.md")
    t = re.sub(r"- YYYY-MM-DD created", f"- {today()} created", t, count=1)
    t = re.sub(r"^- YYYY-MM-DD ~~.*\n", "", t, flags=re.M)  # drop the example history line
    path = os.path.join(qdir, qid + ".md")
    open(path, "w", encoding="utf-8").write(t)
    cmd_summary(a, quiet=True)
    print(path)


def cmd_set(a):
    path = os.path.join(root(a.slug), "questions", a.qid + ".md")
    if not os.path.exists(path):
        sys.exit(f"No such question: {path}")
    if a.field in FIELDS and a.value not in FIELDS[a.field]:
        sys.exit(f"{a.field} must be one of {FIELDS[a.field]}")
    fm, text = read_fm(path)
    old = fm.get(a.field, "")
    if re.search(rf"^{a.field}:.*$", text, re.M):
        text = re.sub(rf"^{a.field}:.*$", f"{a.field}: {a.value}", text, count=1, flags=re.M)
    else:
        text = text.replace("\n---\n", f"\n{a.field}: {a.value}\n---\n", 1)
    text = re.sub(r"^updated:.*$", f"updated: {today()}", text, count=1, flags=re.M)
    note = f" — {a.note}" if a.note else ""
    text = text.rstrip("\n") + f"\n- {today()} {a.field}: {old or '(none)'} → {a.value}{note}\n"
    open(path, "w", encoding="utf-8").write(text)
    cmd_summary(a, quiet=True)
    print(f"{a.qid}: {a.field} {old} → {a.value}")


def cmd_status(a):
    qs = questions(a.slug)
    att = [q for q in qs if q.get("answer_state") in ("open", "explained", "contested", "blocked") or q.get("understanding") == "lost"]
    blk = [q for q in qs if q.get("answer_state") == "blocked"]
    quiz = [q for q in qs if q.get("understanding") in ("lost", "can-follow")]
    def show(title, rows):
        print(f"\n## {title} ({len(rows)})")
        for q in rows:
            print(f"- {q.get('id')} [{q.get('concept')}] A={q.get('answer_state')} B={q.get('understanding')} "
                  f"next={q.get('next_action')} :: {q['_q'][:90]}")
    print(f"# {a.slug}: {len(qs)} questions  ({root(a.slug)})")
    show("Needs attention", att); show("Blocked: needs the user", blk); show("Quiz material", quiz)


def cmd_summary(a, quiet=False):
    qa = os.path.join(root(a.slug), "QA_LOG.md")
    if not os.path.exists(qa):
        sys.exit(f"Subject not initialised. Run: study.py init {a.slug}")
    rows = ["| ID | Question (short) | Concept | A | B | Hard because | Next action |", "|---|---|---|---|---|---|---|"]
    for q in questions(a.slug):
        short = q["_q"][:70].replace("|", "/")
        rows.append(f"| [[{q.get('id')}]] | {short} | {q.get('concept')} | {q.get('answer_state')} | "
                    f"{q.get('understanding')} | {q.get('hard_because')} | {q.get('next_action')} |")
    text = open(qa, encoding="utf-8").read()
    head = text.split("## Manual Summary", 1)[0]
    text = head + "## Manual Summary (fallback when Dataview is not available; generated by study.py summary)\n\n" + "\n".join(rows) + "\n"
    open(qa, "w", encoding="utf-8").write(text)
    if not quiet:
        print(f"Summary regenerated: {qa}")


def cmd_open(a):
    qa = os.path.join(root(a.slug), "QA_LOG.md")
    vault, name = CFG.get("OBSIDIAN_VAULT_PATH"), CFG.get("OBSIDIAN_VAULT_NAME")
    if not (vault and name and os.path.abspath(qa).startswith(os.path.abspath(vault) + os.sep)):
        print(f"Study folder is not inside the configured Obsidian vault. Open manually: {qa}")
        return
    rel = os.path.relpath(qa, vault)[:-3]
    uri = "obsidian://open?vault=%s&file=%s" % (urllib.parse.quote(name), urllib.parse.quote(rel))
    opener = "open" if sys.platform == "darwin" else "xdg-open"
    try:
        subprocess.run([opener, uri], check=True)
        print(f"Opened {uri}")
    except Exception as e:
        print(f"Could not launch Obsidian ({e}). URI: {uri}")


def main():
    p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    s = p.add_subparsers(dest="cmd", required=True)
    x = s.add_parser("init"); x.add_argument("slug"); x.add_argument("--title"); x.add_argument("--open", action="store_true"); x.set_defaults(f=cmd_init)
    x = s.add_parser("question"); x.add_argument("slug"); x.add_argument("concept"); x.add_argument("text"); x.add_argument("--hard"); x.set_defaults(f=cmd_question)
    x = s.add_parser("set"); x.add_argument("slug"); x.add_argument("qid"); x.add_argument("field"); x.add_argument("value"); x.add_argument("--note"); x.set_defaults(f=cmd_set)
    for n, f in (("status", cmd_status), ("summary", cmd_summary), ("open", cmd_open)):
        x = s.add_parser(n); x.add_argument("slug"); x.set_defaults(f=f)
    x = s.add_parser("where"); x.add_argument("slug"); x.set_defaults(f=lambda a: print(root(a.slug)))
    a = p.parse_args(); a.f(a)


if __name__ == "__main__":
    main()
