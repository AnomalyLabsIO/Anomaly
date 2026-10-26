"""Regenerate the reviewable patch against hash-verified, local pinned source."""
import argparse
import difflib
import hashlib
import json
from pathlib import Path

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("upstream_source", type=Path, help="Pinned checkout src/MoonSharp.Interpreter")
args = parser.parse_args()
inputs = Path(__file__).resolve().parent
fork = inputs.parent.parent / "third_party" / "MoonSharp"
manifest = json.loads((inputs / "upstream-source-manifest.json").read_text(encoding="utf-8-sig"))
original = {entry["Path"].replace("\\", "/"): entry for entry in manifest}
for name, entry in original.items():
    actual = hashlib.sha256((args.upstream_source / name).read_bytes()).hexdigest().upper()
    if actual != entry["SHA256"]:
        raise ValueError(f"Pinned upstream source hash mismatch: {name}")

chunks = []
changed = 0
for local in sorted((fork / "src").rglob("*.cs")):
    name = local.relative_to(fork / "src").as_posix()
    # Preserve BOM and final-newline changes; normalize only CRLF for unified diff.
    before = (args.upstream_source / name).read_text(encoding="utf-8").splitlines(keepends=True) if name in original else []
    after = local.read_text(encoding="utf-8").splitlines(keepends=True)
    if before == after:
        continue
    changed += 1
    chunks.append(f"diff --git a/src/{name} b/src/{name}\n")
    if not before:
        chunks.append("new file mode 100644\n")
    for line in difflib.unified_diff(before, after, fromfile=f"a/src/{name}" if before else "/dev/null", tofile=f"b/src/{name}"):
        chunks.append(line if line.endswith("\n") else line + "\n\\ No newline at end of file\n")

patch = "".join(chunks)
for path in (inputs / "anomaly-hardening.patch", fork / "anomaly-hardening.patch"):
    path.write_text(patch, encoding="utf-8", newline="\n")
print(json.dumps({"upstreamFiles": len(original), "changedFiles": changed, "patchBytes": len(patch.encode("utf-8"))}))
