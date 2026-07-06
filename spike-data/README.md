# spike-data

Drop-off folder for ferrying diagnostic logs from the Windows/ACT box to the Mac for analysis.

## Layout

One subdirectory per capture session, named `YYYY-MM-DD/`, each containing:

- `notes.md` — what the session was (context, plugin version, what to look for)
- The ferried logs: overlay `spike-*.jsonl` / diagnostic JSONL from `%APPDATA%\Advanced Combat Tracker\eq2auras\logs\`, plus optionally a zipped raw EQ2 game log for cross-referencing

## How to ferry

Upload via GitHub web (navigate into the session folder → Add file → Upload files), or `git add` locally — the repo's `.gitignore` has an exception for `spike-data/**/*.jsonl`. GitHub's web upload caps at ~25 MB per file; zip anything bigger (raw game logs compress ~10:1).

These are **transient analysis artifacts** — log files get removed from tracking once analyzed; the `notes.md` stubs stay as an index of what was captured and where the findings went.
