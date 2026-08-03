# Tools

## `anomaly_vanity_id.py`

Standalone Python utility for generating case-insensitive vanity `@anomaly` user IDs.

What it does:
- Mirrors the repo's `SHA256(publicKey)` -> Base58 -> `@anomaly` derivation.
- Generates fresh `secp256r1` identity blobs compatible with `user.json`.
- Shows a live single-line status bar in interactive terminals for attempts, rate, elapsed time, and hit counts.
- Reports the first `contains` hit for each desired word.
- Keeps searching until every desired word also has a prefix hit.
- Can skip contains checks entirely with `--prefix-only`.
- Can keep reporting additional prefix hits with `--continue-after-found` so you can hunt for nicer casing.

Search mode requires Python plus `cryptography`:

```powershell
python -m pip install -r tools/requirements-anomaly-vanity.txt
```

Example search:

```powershell
python tools/anomaly_vanity_id.py search user admin --workers 8 --jsonl vanity-results.jsonl
```

Prefix-only search:

```powershell
python tools/anomaly_vanity_id.py search user admin --prefix-only --workers 8
```

Continue reporting additional prefix hits after the first match for each target:

```powershell
python tools/anomaly_vanity_id.py search user --prefix-only --continue-after-found --max-runtime 60
```

Without `--max-runtime`, `--continue-after-found` runs until you stop it with `Ctrl+C`.

Interactive output behavior:
- In a normal terminal, progress stays on one live status line and only new hits scroll.
- If output is redirected or captured, the tool falls back to periodic progress lines.

Example search from a word list:

```powershell
python tools/anomaly_vanity_id.py search --words-file desired-words.txt --workers 8
```

`desired-words.txt` is one word per line. Blank lines and lines starting with `#` are ignored.

Case-insensitive matching details:
- `user`, `UsEr`, and `USER` are treated the same.
- Allowed characters are `a-z` and digits `1-9`.
- `0` can never appear in the Base58 core, so any target containing `0` is rejected.

Each hit prints:
- match type (`contains` or `prefix`)
- generated Base58 core
- final `@anomaly` user ID
- base64 identity blob ready for `user.json`

You can also derive an ID from an existing identity without needing `cryptography`:

```powershell
python tools/anomaly_vanity_id.py derive --user-json "$env:APPDATA\Anomaly\user.json"
```
