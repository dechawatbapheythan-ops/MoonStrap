"""
MoonStrap MCP server: lets an AI assistant launch Roblox / Roblox Studio through MoonStrap.

Tools
  launch_roblox(version?)     - start the Roblox player (optionally a specific build, this launch only)
  launch_studio()             - start Roblox Studio
  list_roblox_versions(...)   - builds installed in MoonStrap + older builds available from Roblox

Speaks MCP over stdio (newline-delimited JSON-RPC 2.0). Standard library only, Windows only.
"""

import ctypes
import ctypes.wintypes as wt
import json
import os
import re
import subprocess
import sys
import time
import urllib.request

LOCAL = os.environ.get("LOCALAPPDATA", "")
MOONSTRAP_EXE = os.path.join(LOCAL, "Programs", "MoonStrap", "MoonStrap.exe")
DATA_DIR = os.path.join(LOCAL, "MoonStrap")
VERSIONS_DIR = os.path.join(DATA_DIR, "Versions")
SETTINGS_FILE = os.path.join(DATA_DIR, "Settings.json")
LOG_FILE = os.path.join(DATA_DIR, "MoonStrap.log")
HISTORY_URL = "https://setup.rbxcdn.com/DeployHistory.txt"

SERVER_INFO = {"name": "moonstrap", "version": "1.0.0"}
LAUNCH_TIMEOUT_S = 300  # first launch of a build may need to download ~150 MB

# ---------------------------------------------------------------- Win32 helpers

user32 = ctypes.WinDLL("user32", use_last_error=True)
kernel32 = ctypes.WinDLL("kernel32", use_last_error=True)
PROCESS_QUERY_LIMITED_INFORMATION = 0x1000
EnumWindowsProc = ctypes.WINFUNCTYPE(wt.BOOL, wt.HWND, wt.LPARAM)


def _visible_window_pids() -> set[int]:
    """PIDs owning at least one visible, titled top-level window."""
    pids: set[int] = set()

    def cb(hwnd, _):
        if user32.IsWindowVisible(hwnd) and user32.GetWindowTextLengthW(hwnd) > 0:
            pid = wt.DWORD()
            user32.GetWindowThreadProcessId(hwnd, ctypes.byref(pid))
            pids.add(pid.value)
        return True

    user32.EnumWindows(EnumWindowsProc(cb), 0)
    return pids


def _exe_path(pid: int) -> str | None:
    h = kernel32.OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, False, pid)
    if not h:
        return None
    try:
        buf = ctypes.create_unicode_buffer(1024)
        size = wt.DWORD(len(buf))
        if kernel32.QueryFullProcessImageNameW(h, 0, buf, ctypes.byref(size)):
            return buf.value
    finally:
        kernel32.CloseHandle(h)
    return None


def _pids_named(image: str) -> set[int]:
    out = subprocess.run(
        ["tasklist", "/FI", f"IMAGENAME eq {image}", "/FO", "CSV", "/NH"],
        capture_output=True, text=True, creationflags=subprocess.CREATE_NO_WINDOW,
    ).stdout
    pids = set()
    for line in out.splitlines():
        parts = [p.strip('"') for p in line.split('","')]
        if len(parts) > 1 and parts[1].isdigit():
            pids.add(int(parts[1]))
    return pids


# ---------------------------------------------------------------- MoonStrap data

def _settings() -> dict:
    try:
        with open(SETTINGS_FILE, encoding="utf-8-sig") as f:
            return json.load(f)
    except Exception:
        return {}


def _file_version(exe: str) -> str | None:
    """
    Reads the FileVersion *string* (e.g. "0, 710, 0, 7100702" -> "0.710.0.7100702").
    Roblox's last component is bigger than 16 bits, so the binary VS_FIXEDFILEINFO version is truncated.
    """
    ver = ctypes.WinDLL("version")
    size = ver.GetFileVersionInfoSizeW(exe, None)
    if not size:
        return None
    buf = ctypes.create_string_buffer(size)
    if not ver.GetFileVersionInfoW(exe, 0, size, buf):
        return None
    ptr, ln = ctypes.c_void_p(), wt.UINT()
    if not ver.VerQueryValueW(buf, "\\VarFileInfo\\Translation", ctypes.byref(ptr), ctypes.byref(ln)) or ln.value < 4:
        return None
    lang, cp = ctypes.cast(ptr, ctypes.POINTER(wt.WORD * 2)).contents
    sptr, sln = ctypes.c_wchar_p(), wt.UINT()
    if not ver.VerQueryValueW(buf, f"\\StringFileInfo\\{lang:04x}{cp:04x}\\FileVersion", ctypes.byref(sptr), ctypes.byref(sln)):
        return None
    return ".".join(p.strip() for p in (sptr.value or "").split(",")) or None


def _installed_builds() -> list[dict]:
    s = _settings()
    builds = []
    if not os.path.isdir(VERSIONS_DIR):
        return builds
    for name in os.listdir(VERSIONS_DIR):
        d = os.path.join(VERSIONS_DIR, name)
        if not os.path.exists(os.path.join(d, ".moonstrap-complete")):
            continue
        for product, exe in (("Roblox", "RobloxPlayerBeta.exe"), ("Studio", "RobloxStudioBeta.exe")):
            p = os.path.join(d, exe)
            if os.path.exists(p):
                tags = []
                if product == "Roblox" and name == s.get("LatestPlayerVersion"): tags.append("latest")
                if product == "Studio" and name == s.get("InstalledStudioVersion"): tags.append("latest")
                if product == "Roblox" and name == s.get("PinnedVersion"): tags.append("pinned in MoonStrap")
                if name == s.get("InstalledVersion") and product == "Roblox": tags.append("last launched")
                builds.append({"product": product, "id": name, "version": _file_version(p), "tags": tags})
    return builds


def _history(limit: int, search: str | None) -> list[dict]:
    with urllib.request.urlopen(HISTORY_URL, timeout=15) as r:
        text = r.read().decode("utf-8", "replace")
    pat = re.compile(r"New WindowsPlayer (version-[0-9a-f]{16}) at ([^,]+), file version: (\d+), (\d+), (\d+), (\d+)")
    seen, rows = set(), []
    for m in pat.finditer(text):
        if m.group(1) in seen:
            continue
        seen.add(m.group(1))
        rows.append({"id": m.group(1), "date": m.group(2).strip(), "version": ".".join(m.group(i) for i in range(3, 7))})
    rows.reverse()  # newest first
    if search:
        rows = [r for r in rows if search in r["version"] or search in r["id"] or search in r["date"]]
    return rows[:limit]


# ---------------------------------------------------------------- tools

def _launch(product: str, version: str | None) -> str:
    if not os.path.exists(MOONSTRAP_EXE):
        raise RuntimeError(f"MoonStrap isn't installed at {MOONSTRAP_EXE}")

    image = "RobloxStudioBeta.exe" if product == "studio" else "RobloxPlayerBeta.exe"
    label = "Roblox Studio" if product == "studio" else "Roblox"
    before = _pids_named(image)
    log_size = os.path.getsize(LOG_FILE) if os.path.exists(LOG_FILE) else 0

    args = [MOONSTRAP_EXE, "-studio" if product == "studio" else "-player"]
    if version:
        args += ["-version", version]
    # fully detached, and never inherit our stdout (that's the MCP channel)
    moon = subprocess.Popen(
        args, stdin=subprocess.DEVNULL, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, close_fds=True,
        creationflags=subprocess.DETACHED_PROCESS | subprocess.CREATE_NEW_PROCESS_GROUP,
    )

    deadline = time.time() + LAUNCH_TIMEOUT_S
    while time.time() < deadline:
        new = _pids_named(image) - before
        if new:
            shown = new & _visible_window_pids()
            if shown:
                pid = next(iter(shown))
                path = _exe_path(pid) or ""
                build = os.path.basename(os.path.dirname(path)) if path else "unknown build"
                ver = _file_version(path) if path else None
                return f"{label} is open (pid {pid}, {build}{f', {ver}' if ver else ''})."

        # MoonStrap logs errors before showing its error dialog
        if os.path.exists(LOG_FILE) and os.path.getsize(LOG_FILE) > log_size:
            with open(LOG_FILE, encoding="utf-8", errors="replace") as f:
                f.seek(log_size)
                err = f.read().strip().splitlines()
            first = next((l for l in err if "Exception:" in l), err[0] if err else "unknown error")
            raise RuntimeError(f"MoonStrap couldn't start {label}: {first.split('Exception:', 1)[-1].strip()}")

        if moon.poll() is not None and not (_pids_named(image) - before):
            raise RuntimeError(f"MoonStrap exited before {label} opened (it may have been cancelled).")
        time.sleep(0.5)

    return f"{label} is still starting after {LAUNCH_TIMEOUT_S}s (probably still downloading). Check the MoonStrap window."


TOOLS = [
    {
        "name": "launch_roblox",
        "description": (
            "Launch the Roblox player through MoonStrap (applies the user's FastFlags, mods and GPU settings) "
            "and wait until its window is on screen. By default uses whatever MoonStrap is set to (latest, or the "
            "build pinned in MoonStrap). Pass `version` to run a specific build for this launch only: \"latest\", "
            "a version number like \"0.710\" or \"0.710.0.7100702\", or an ID like \"version-497d232246664187\". "
            "Use list_roblox_versions to see options. Older builds usually can't join live games."
        ),
        "inputSchema": {
            "type": "object",
            "properties": {"version": {"type": "string", "description": "latest | 0.710 | 0.710.0.7100702 | version-xxxxxxxxxxxxxxxx"}},
            "additionalProperties": False,
        },
        "annotations": {"title": "Launch Roblox", "readOnlyHint": False, "openWorldHint": True},
    },
    {
        "name": "launch_studio",
        "description": "Launch Roblox Studio (latest) through MoonStrap and wait until its window is on screen.",
        "inputSchema": {"type": "object", "properties": {}, "additionalProperties": False},
        "annotations": {"title": "Launch Roblox Studio", "readOnlyHint": False, "openWorldHint": True},
    },
    {
        "name": "list_roblox_versions",
        "description": (
            "List Roblox builds: those already installed in MoonStrap (instant to launch) and older Windows builds "
            "available from Roblox's public deploy history (downloaded on first launch). Roblox hides the newest "
            "builds from its public history, so recent ones only appear if installed."
        ),
        "inputSchema": {
            "type": "object",
            "properties": {
                "limit": {"type": "integer", "minimum": 1, "maximum": 200, "default": 20, "description": "Max history rows"},
                "search": {"type": "string", "description": "Filter history by version number, ID or date, e.g. \"0.70\""},
            },
            "additionalProperties": False,
        },
        "annotations": {"title": "List Roblox versions", "readOnlyHint": True, "openWorldHint": True},
    },
]


def call_tool(name: str, args: dict) -> str:
    if name == "launch_roblox":
        v = args.get("version")
        return _launch("player", v.strip() if isinstance(v, str) and v.strip() else None)
    if name == "launch_studio":
        return _launch("studio", None)
    if name == "list_roblox_versions":
        installed = _installed_builds()
        lines = ["Installed in MoonStrap (instant):"]
        for b in installed:
            tags = f"  [{', '.join(b['tags'])}]" if b["tags"] else ""
            lines.append(f"  {b['product']:<7} {b['version'] or '?':<18} {b['id']}{tags}")
        if not installed:
            lines.append("  (none)")
        try:
            hist = _history(int(args.get("limit", 20)), args.get("search"))
            lines.append("")
            lines.append("Available from Roblox (older builds, downloaded on first launch):")
            lines += [f"  {h['version']:<18} {h['date']:<24} {h['id']}" for h in hist] or ["  (no matches)"]
        except Exception as e:
            lines.append(f"\n(Couldn't load Roblox's version history: {e})")
        return "\n".join(lines)
    raise ValueError(f"Unknown tool: {name}")


# ---------------------------------------------------------------- JSON-RPC over stdio

def _send(msg: dict) -> None:
    sys.stdout.write(json.dumps(msg, ensure_ascii=False) + "\n")
    sys.stdout.flush()


def _handle(req: dict) -> dict | None:
    method, rid = req.get("method"), req.get("id")
    if rid is None:  # notification (e.g. notifications/initialized): no reply
        return None
    if method == "initialize":
        return {"jsonrpc": "2.0", "id": rid, "result": {
            "protocolVersion": req.get("params", {}).get("protocolVersion", "2025-06-18"),
            "capabilities": {"tools": {}},
            "serverInfo": SERVER_INFO,
            "instructions": "Launch Roblox or Roblox Studio on this PC via the MoonStrap launcher.",
        }}
    if method == "ping":
        return {"jsonrpc": "2.0", "id": rid, "result": {}}
    if method == "tools/list":
        return {"jsonrpc": "2.0", "id": rid, "result": {"tools": TOOLS}}
    if method == "tools/call":
        p = req.get("params", {})
        try:
            text, is_error = call_tool(p.get("name", ""), p.get("arguments") or {}), False
        except Exception as e:
            text, is_error = str(e), True
        return {"jsonrpc": "2.0", "id": rid, "result": {"content": [{"type": "text", "text": text}], "isError": is_error}}
    return {"jsonrpc": "2.0", "id": rid, "error": {"code": -32601, "message": f"Method not found: {method}"}}


def main() -> None:
    sys.stdin.reconfigure(encoding="utf-8")
    sys.stdout.reconfigure(encoding="utf-8", newline="\n")
    for line in sys.stdin:
        line = line.strip()
        if not line:
            continue
        try:
            req = json.loads(line)
        except json.JSONDecodeError:
            _send({"jsonrpc": "2.0", "id": None, "error": {"code": -32700, "message": "Parse error"}})
            continue
        resp = _handle(req)
        if resp is not None:
            _send(resp)


if __name__ == "__main__":
    main()
