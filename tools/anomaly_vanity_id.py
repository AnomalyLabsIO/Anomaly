#!/usr/bin/env python3
"""Generate case-insensitive vanity Anomaly user IDs.

The calculation mirrors the Anomaly client/server code:

1. Generate a secp256r1 identity keypair.
2. Serialize identity as 97 bytes: [32-byte private scalar][65-byte uncompressed public key].
3. Compute SHA-256 over the 65-byte public key.
4. Base58-encode the hash with Anomaly's alphabet.
5. Append "@anomaly".

Search mode reports the first case-insensitive "contains" hit for each target,
but keeps searching until every target also has a case-insensitive prefix hit.
"""

from __future__ import annotations

import argparse
import base64
import hashlib
import json
import multiprocessing as mp
import os
import queue
import shutil
import sys
import time
from dataclasses import dataclass
from pathlib import Path
from typing import Iterable


ANOMALY_SUFFIX = "@anomaly"
BASE58_ALPHABET = "123456789ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz"
CASE_INSENSITIVE_ALLOWED = frozenset("123456789abcdefghijklmnopqrstuvwxyz")
STATE_NONE = 0
STATE_CONTAINS = 1
STATE_PREFIX = 2


@dataclass(frozen=True)
class Target:
    original: str
    normalized: str


class LiveConsole:
    def __init__(self, stream=None, terminal_width: int | None = None):
        self._stream = stream or sys.stdout
        self._interactive = bool(getattr(self._stream, "isatty", lambda: False)())
        self._terminal_width = terminal_width
        self._status_text = ""
        self._last_rendered_length = 0

    @property
    def interactive(self) -> bool:
        return self._interactive

    def set_status(self, text: str) -> None:
        self._status_text = text
        if self._interactive:
            self._render_status()

    def clear_status(self) -> None:
        if not self._interactive or self._last_rendered_length == 0:
            return

        self._stream.write("\r" + (" " * self._last_rendered_length) + "\r")
        self._stream.flush()
        self._last_rendered_length = 0

    def write_lines(self, *lines: str) -> None:
        self.clear_status()
        for line in lines:
            self._stream.write(line + "\n")
        self._stream.flush()
        if self._interactive and self._status_text:
            self._render_status()

    def finish(self) -> None:
        self.clear_status()
        self._status_text = ""

    def _render_status(self) -> None:
        display_text = self._truncate(self._status_text)
        padding = " " * max(0, self._last_rendered_length - len(display_text))
        self._stream.write("\r" + display_text + padding)
        self._stream.flush()
        self._last_rendered_length = len(display_text)

    def _truncate(self, text: str) -> str:
        width = self._terminal_width or shutil.get_terminal_size(fallback=(120, 20)).columns
        if width <= 0 or len(text) <= width:
            return text
        if width <= 3:
            return text[:width]
        return text[: width - 3] + "..."


def base58_encode(data: bytes) -> str:
    """Mirror of Anomaly.Shared.Auth.Base58.Encode."""

    int_data = 0
    for byte in data:
        int_data = int_data * 256 + byte

    result = ""
    while int_data > 0:
        int_data, remainder = divmod(int_data, 58)
        result = BASE58_ALPHABET[remainder] + result

    for byte in data:
        if byte != 0:
            break
        result = "1" + result

    return result


def build_user_id_from_public_key(public_key: bytes) -> tuple[str, str]:
    core = base58_encode(hashlib.sha256(public_key).digest())
    return core, core + ANOMALY_SUFFIX


def build_user_id_from_identity(identity_bytes: bytes) -> tuple[str, str]:
    if len(identity_bytes) < 97:
        raise ValueError(f"Identity blob must be at least 97 bytes, got {len(identity_bytes)}")

    public_key = identity_bytes[32:97]
    return build_user_id_from_public_key(public_key)


def load_targets(words: Iterable[str], words_file: str | None) -> list[Target]:
    raw_words: list[str] = []
    raw_words.extend(words)

    if words_file:
        for line in Path(words_file).read_text(encoding="utf-8").splitlines():
            stripped = line.strip()
            if not stripped or stripped.startswith("#"):
                continue
            raw_words.append(stripped)

    seen: set[str] = set()
    targets: list[Target] = []
    invalid: list[str] = []

    for word in raw_words:
        normalized = word.strip().lower()
        if not normalized:
            continue

        if normalized in seen:
            continue

        if any(ch not in CASE_INSENSITIVE_ALLOWED for ch in normalized):
            invalid.append(word)
            continue

        seen.add(normalized)
        targets.append(Target(original=word, normalized=normalized))

    if invalid:
        invalid_display = ", ".join(repr(word) for word in invalid)
        raise SystemExit(
            "These case-insensitive targets can never appear in an Anomaly user ID: "
            f"{invalid_display}. Allowed characters are a-z and digits 1-9 (0 is impossible)."
        )

    if not targets:
        raise SystemExit("Provide at least one desired word via positional args or --words-file.")

    return targets


def import_search_crypto():
    try:
        from cryptography.hazmat.primitives.asymmetric import ec
    except ModuleNotFoundError as exc:
        raise SystemExit(
            "Search mode requires the 'cryptography' package. Install it with:\n"
            "  python -m pip install -r tools/requirements-anomaly-vanity.txt"
        ) from exc

    return ec


def generate_identity_candidate(ec_module) -> tuple[str, str, str]:
    private_key = ec_module.generate_private_key(ec_module.SECP256R1())
    private_numbers = private_key.private_numbers()
    public_numbers = private_numbers.public_numbers

    private_scalar = private_numbers.private_value.to_bytes(32, byteorder="big")
    public_key = b"\x04" + public_numbers.x.to_bytes(32, byteorder="big") + public_numbers.y.to_bytes(32, byteorder="big")
    identity = private_scalar + public_key

    core, user_id = build_user_id_from_public_key(public_key)
    identity_b64 = base64.b64encode(identity).decode("ascii")
    return core, user_id, identity_b64


def worker_loop(
    worker_id: int,
    target_values: list[str],
    states,
    batch_size: int,
    prefix_only: bool,
    continue_after_found: bool,
    stop_event,
    report_queue,
) -> None:
    ec_module = import_search_crypto()

    try:
        while not stop_event.is_set():
            hits: list[tuple[str, int, str, str, str]] = []
            attempts = 0
            batch_states = [states[index] for index in range(len(target_values))]

            for _ in range(batch_size):
                if stop_event.is_set():
                    break

                core, user_id, identity_b64 = generate_identity_candidate(ec_module)
                attempts += 1
                lower_core = core.lower()

                for index, target in enumerate(target_values):
                    state = states[index] if continue_after_found else batch_states[index]
                    if not continue_after_found and state >= STATE_PREFIX:
                        continue

                    if lower_core.startswith(target):
                        if continue_after_found or state < STATE_PREFIX:
                            hits.append(("prefix", index, core, user_id, identity_b64))
                            if not continue_after_found:
                                batch_states[index] = STATE_PREFIX
                    elif not prefix_only and state == STATE_NONE and target in lower_core:
                        hits.append(("contains", index, core, user_id, identity_b64))
                        batch_states[index] = STATE_CONTAINS

            if attempts:
                report_queue.put((worker_id, attempts, hits))
    except KeyboardInterrupt:
        return
    except Exception as exc:  # pragma: no cover - surfaced to parent for visibility
        report_queue.put((worker_id, 0, [("error", -1, type(exc).__name__, str(exc), "")]))


def jsonl_append(path: str | None, payload: dict[str, object]) -> None:
    if not path:
        return

    with Path(path).open("a", encoding="utf-8") as handle:
        handle.write(json.dumps(payload, separators=(",", ":")) + "\n")


def build_status_text(
    total_attempts: int,
    started: float,
    contains_count: int,
    prefix_count: int,
    total_targets: int,
    continue_after_found: bool,
    prefix_report_count: int,
) -> str:
    elapsed = time.monotonic() - started
    rate = total_attempts / elapsed if elapsed > 0 else 0.0

    parts = [
        f"attempts={total_attempts}",
        f"rate={rate:,.0f}/s",
        f"elapsed={elapsed:.1f}s",
        f"contains={contains_count}/{total_targets}",
        f"prefix={prefix_count}/{total_targets}",
    ]

    if continue_after_found:
        parts.append(f"reported_prefixes={prefix_report_count}")

    return " | ".join(parts)


def print_hit(
    console: LiveConsole,
    kind: str,
    target: Target,
    core: str,
    user_id: str,
    identity_b64: str,
    worker_id: int,
    attempts: int,
) -> None:
    console.write_lines(
        f"[{kind.upper()}] target='{target.original}' worker={worker_id} attempts={attempts} user_id={user_id}",
        f"  core={core}",
        f"  identity_base64={identity_b64}",
    )


def run_search(args: argparse.Namespace) -> int:
    import_search_crypto()

    targets = load_targets(args.words, args.words_file)
    console = LiveConsole()
    worker_count = max(1, args.workers)
    batch_size = max(1, args.batch_size)
    progress_seconds = max(0.25, args.progress_seconds)
    prefix_only = args.prefix_only
    continue_after_found = args.continue_after_found

    mode_parts = ["prefix only" if prefix_only else "prefix + contains"]
    if continue_after_found:
        mode_parts.append("continue after found")

    console.write_lines(
        f"Searching for {len(targets)} target(s) across {worker_count} worker(s). "
        f"Mode: {', '.join(mode_parts)}.",
    )
    console.write_lines("Targets: " + ", ".join(target.original for target in targets))
    if continue_after_found and not args.max_runtime:
        console.write_lines("Continue-after-found is enabled with no max runtime; press Ctrl+C to stop.")

    ctx = mp.get_context("spawn")
    stop_event = ctx.Event()
    report_queue = ctx.Queue()
    states = ctx.Array("b", [STATE_NONE] * len(targets), lock=False)
    target_values = [target.normalized for target in targets]

    processes: list[mp.Process] = []
    for worker_id in range(worker_count):
        process = ctx.Process(
            target=worker_loop,
            args=(
                worker_id,
                target_values,
                states,
                batch_size,
                prefix_only,
                continue_after_found,
                stop_event,
                report_queue,
            ),
            daemon=True,
        )
        process.start()
        processes.append(process)

    contains_results: dict[int, dict[str, object]] = {}
    prefix_results: dict[int, dict[str, object]] = {}
    prefix_report_count = 0
    total_attempts = 0
    started = time.monotonic()
    last_progress = started
    deadline = started + args.max_runtime if args.max_runtime else None

    console.set_status(
        build_status_text(
            total_attempts,
            started,
            len(contains_results),
            len(prefix_results),
            len(targets),
            continue_after_found,
            prefix_report_count,
        )
    )

    try:
        while True:
            prefixes_complete = len(prefix_results) == len(targets)
            if prefixes_complete and not continue_after_found:
                break

            if deadline and time.monotonic() >= deadline:
                if prefixes_complete:
                    console.write_lines("Max runtime reached after collecting prefix hits; stopping.")
                else:
                    console.write_lines("Max runtime reached before every target found a prefix hit.")
                break

            try:
                worker_id, attempts, hits = report_queue.get(timeout=0.5)
            except queue.Empty:
                if all(not process.is_alive() for process in processes):
                    raise RuntimeError("All workers exited before every target found a prefix hit.")

                hits = []
                attempts = 0
                worker_id = -1

            total_attempts += attempts

            for kind, target_index, core, user_id, identity_b64 in hits:
                if kind == "error":
                    raise RuntimeError(f"Worker {worker_id} failed: {core}: {user_id}")

                target = targets[target_index]
                payload = {
                    "target": target.original,
                    "normalized_target": target.normalized,
                    "match_type": kind,
                    "worker": worker_id,
                    "attempts": total_attempts,
                    "core": core,
                    "user_id": user_id,
                    "identity_base64": identity_b64,
                }

                if kind == "contains":
                    if target_index in contains_results or target_index in prefix_results:
                        continue

                    contains_results[target_index] = payload
                    if states[target_index] < STATE_CONTAINS:
                        states[target_index] = STATE_CONTAINS
                    print_hit(console, kind, target, core, user_id, identity_b64, worker_id, total_attempts)
                    jsonl_append(args.jsonl, payload)
                    continue

                is_first_prefix = target_index not in prefix_results
                if is_first_prefix:
                    prefix_results[target_index] = payload

                states[target_index] = STATE_PREFIX
                prefix_report_count += 1
                print_hit(console, kind, target, core, user_id, identity_b64, worker_id, total_attempts)
                jsonl_append(args.jsonl, payload)

            status_text = build_status_text(
                total_attempts,
                started,
                len(contains_results),
                len(prefix_results),
                len(targets),
                continue_after_found,
                prefix_report_count,
            )

            if console.interactive:
                console.set_status(status_text)
            else:
                now = time.monotonic()
                if now - last_progress >= progress_seconds:
                    console.write_lines(status_text)
                    last_progress = now
    except KeyboardInterrupt:
        console.write_lines("Interrupted by user.")
    finally:
        stop_event.set()
        for process in processes:
            process.join(timeout=5)
        for process in processes:
            if process.is_alive():
                process.kill()

    console.finish()
    console.write_lines("", "Summary:")
    console.write_lines(f"  attempts={total_attempts}")
    elapsed = time.monotonic() - started
    console.write_lines(f"  elapsed_seconds={elapsed:.2f}")
    console.write_lines(f"  contains_hits={len(contains_results)}/{len(targets)}")
    console.write_lines(f"  prefix_hits={len(prefix_results)}/{len(targets)}")
    if continue_after_found:
        console.write_lines(f"  reported_prefix_matches={prefix_report_count}")

    for index, target in enumerate(targets):
        contains_status = "yes" if index in contains_results else "no"
        prefix_status = "yes" if index in prefix_results else "no"
        console.write_lines(f"  target='{target.original}' contains={contains_status} prefix={prefix_status}")

    return 0 if len(prefix_results) == len(targets) else 1


def run_derive(args: argparse.Namespace) -> int:
    if bool(args.user_json) == bool(args.identity_base64):
        raise SystemExit("Provide exactly one of --user-json or --identity-base64.")

    if args.user_json:
        payload = json.loads(Path(args.user_json).read_text(encoding="utf-8-sig"))
        identity_base64 = payload.get("identity")
        if not identity_base64:
            raise SystemExit(f"No 'identity' field found in {args.user_json}.")
    else:
        identity_base64 = args.identity_base64

    identity_bytes = base64.b64decode(identity_base64)
    core, user_id = build_user_id_from_identity(identity_bytes)

    if args.json_output:
        output = {
            "core": core,
            "user_id": user_id,
            "identity_base64": identity_base64,
        }
        print(json.dumps(output, indent=2))
    else:
        print(user_id)

    return 0


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(description=__doc__)
    subparsers = parser.add_subparsers(dest="command", required=True)

    search_parser = subparsers.add_parser("search", help="Search for vanity user IDs")
    search_parser.add_argument("words", nargs="*", help="Desired case-insensitive words to search for")
    search_parser.add_argument("--words-file", help="Text file with one desired word per line")
    search_parser.add_argument("--workers", type=int, default=os.cpu_count() or 1, help="Worker process count")
    search_parser.add_argument("--batch-size", type=int, default=128, help="Candidates generated per worker report")
    search_parser.add_argument("--progress-seconds", type=float, default=2.0, help="Progress print interval in seconds")
    search_parser.add_argument("--jsonl", help="Optional JSON Lines file to append hit payloads to")
    search_parser.add_argument("--max-runtime", type=float, help="Optional hard stop in seconds")
    search_parser.add_argument("--prefix-only", action="store_true", help="Skip contains checks and only look for prefix matches")
    search_parser.add_argument(
        "--continue-after-found",
        action="store_true",
        help="Keep reporting additional prefix matches after the first hit; runs until Ctrl+C or --max-runtime",
    )
    search_parser.set_defaults(func=run_search)

    derive_parser = subparsers.add_parser("derive", help="Derive an @anomaly ID from existing identity data")
    derive_parser.add_argument("--user-json", help="Path to Anomaly user.json")
    derive_parser.add_argument("--identity-base64", help="Raw base64 identity blob from user.json")
    derive_parser.add_argument("--json-output", action="store_true", help="Print structured JSON instead of just the user ID")
    derive_parser.set_defaults(func=run_derive)

    return parser


def main() -> int:
    parser = build_parser()
    args = parser.parse_args()
    return args.func(args)


if __name__ == "__main__":
    mp.freeze_support()
    raise SystemExit(main())
