#!/usr/bin/env python3
"""Stops real secrets from being committed. Identical in every mod repo.

    python3 .githooks/secret-scan.py --staged   check the lines about to be committed (the pre-commit hook runs this)
    python3 .githooks/secret-scan.py            check every text file git tracks

It never prints a secret: only the file, the line number and which rule fired.
Escape hatches (use sparingly): put "secret-scan:allow" on a line, or run  SKIP_SECRET_SCAN=1 git commit
Enable the hook once per clone:  git config core.hooksPath .githooks
"""
import os
import re
import subprocess
import sys

if os.environ.get("SKIP_SECRET_SCAN") == "1":
    print("secret-scan: skipped (SKIP_SECRET_SCAN=1)", file=sys.stderr)
    sys.exit(0)

# Files that must never be committed, whatever they contain. Names containing ".example" are templates and fine.
FORBIDDEN_NAME = re.compile(r"^(\.env(\..*)?|Directory\.Build\.props|mod-uploader\.log|credentials\.json|\.htpasswd|.*\.pem|.*\.p12|id_rsa|id_ed25519)$")
FORBIDDEN_PATH = ("workshop/workshop.json",)          # your hand-written Workshop text stays out of git
RULES = [  # (what, regex) applied to each added line
    ("a private key", r"-----BEGIN [A-Z ]*PRIVATE KEY-----"),
    ("a GitHub token", r"gh[pousr]_[A-Za-z0-9]{36,}|github_pat_[A-Za-z0-9_]{50,}"),
    ("an AWS key", r"AKIA[0-9A-Z]{16}"),
    ("a Slack token", r"xox[baprs]-[A-Za-z0-9-]{10,}"),
    ("an API key", r"\bsk-[A-Za-z0-9]{32,}|AIza[0-9A-Za-z_-]{35}"),
    ("a password hash", r"\$apr1\$|\$2[aby]\$[0-9]{2}\$"),
    ("a value assigned to a secret-looking name", r"""(?i)(api_?key|secret|passwd|password|token)["']?\s*(=>|:|=)\s*["'][^"']{8,}["']"""),
]
PLACEHOLDER = re.compile(r"CHANGE_ME|YOUR_|PASTE_|<hidden>|secret-scan:allow|getenv\(|Environment\.GetEnvironmentVariable|\.example")

problems = 0


def report(path, line, what):
    global problems
    print(f"  {path}:{line}  {what}", file=sys.stderr)
    problems += 1


def check_line(path, n, text):
    if PLACEHOLDER.search(text):
        return
    for what, rx in RULES:
        if re.search(rx, text):
            report(path, n, what)


def check_name(path):
    base = os.path.basename(path)
    if path in FORBIDDEN_PATH or (FORBIDDEN_NAME.match(base) and ".example" not in base):
        report(path, 0, "this file must stay out of git (it holds secrets or your own text; keep it ignored)")
        return True
    return False


def git(*args):
    return subprocess.run(["git", *args], capture_output=True, check=True).stdout


def staged():
    for path in git("diff", "--cached", "--name-only", "--diff-filter=ACMR", "-z").decode("utf-8", "replace").split("\0"):
        if path:
            check_name(path)
    path, n = None, 0
    for raw in git("diff", "--cached", "--diff-filter=ACMR", "-U0", "--no-color", "--no-ext-diff").decode("utf-8", "replace").splitlines():
        if raw.startswith("+++ b/"):
            path = raw[6:]
        elif raw.startswith("+++ "):
            path = None
        elif raw.startswith("@@"):
            m = re.match(r"@@ -\d+(?:,\d+)? \+(\d+)", raw)
            n = int(m.group(1)) if m else 0
        elif raw.startswith("+") and path:
            check_line(path, n, raw[1:])
            n += 1


def everything():
    for path in git("ls-files", "-z").decode("utf-8", "replace").split("\0"):
        if not path or check_name(path):
            continue
        try:
            data = open(path, "rb").read()
        except OSError:
            continue
        if b"\0" in data[:4096]:
            continue
        for i, line in enumerate(data.decode("utf-8", "replace").splitlines(), 1):
            check_line(path, i, line)


os.chdir(git("rev-parse", "--show-toplevel").decode().strip())
staged() if "--staged" in sys.argv else everything()
if problems:
    print(f"\nsecret-scan: {problems} problem(s). Nothing was committed.", file=sys.stderr)
    print("  Keep real keys and paths out of the repo (or in .gitignore); templates are named *.example.*", file=sys.stderr)
    print("  Already committed a secret? Rotate it. Removing the file does not remove it from git history.", file=sys.stderr)
    sys.exit(1)
