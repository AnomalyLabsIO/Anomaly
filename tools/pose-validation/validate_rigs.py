"""Validate the compiled pose catalog against local recovered stock assets without launching Unity.

Export the compiled catalog using PowerShell:
  Add-Type -Path Anomaly.Shared/bin/Debug/Anomaly.Shared.dll
  [Anomaly.Shared.Poses.PoseRigCatalog]::All | ConvertTo-Json -Depth 8 | Set-Content artifacts/pose-catalog.json
Then pass that JSON and the recovered Unity project to this script. No game assets are copied.
"""
import argparse
import hashlib
import json
from pathlib import Path
import re

parser = argparse.ArgumentParser()
parser.add_argument("--catalog", type=Path, required=True)
parser.add_argument("--source", type=Path, required=True)
parser.add_argument("--renderer", type=Path, default=Path(__file__).resolve().parents[2] / "Anomaly.Client.Api/Internal/Poses/PoseRigBinding.cs")
args = parser.parse_args()
catalog = json.loads(args.catalog.read_text(encoding="utf-8-sig"))
guid_paths = {}
for folder in ("Avatar", "GameObject"):
    for meta in (args.source / "Assets" / folder).glob("*.meta"):
        match = re.search(r"^guid: (\w+)$", meta.read_text(encoding="utf-8-sig"), re.M)
        if match:
            guid_paths[match[1]] = meta.with_suffix("")


def prefab_data(path):
    text = path.read_text(encoding="utf-8-sig")
    blocks = re.findall(r"^--- !u!(\d+) &(-?\d+)[^\n]*\n(.*?)(?=^--- !u!|\Z)", text, re.S | re.M)
    names = {i: match[1] for kind, i, block in blocks if kind == "1" and (match := re.search(r"^  m_Name: (.*)$", block, re.M))}
    transforms = {i: (go[1], parent[1]) for kind, i, block in blocks if kind == "4" and (go := re.search(r"m_GameObject: \{fileID: (-?\d+)", block)) and (parent := re.search(r"m_Father: \{fileID: (-?\d+)", block))}

    def hierarchy(ident):
        go, parent = transforms[ident]
        return (hierarchy(parent) + "/" if parent in transforms else "") + names[go]

    paths = {hierarchy(i).split("/", 1)[-1] for i in transforms if transforms[i][0] in names}
    return text, names, paths


assert len(catalog) == 11, "Expected eleven authored model families"
count = 0
for rig in catalog:
    path = args.source / "Assets" / "GameObject" / (rig["ModelName"] + ".prefab")
    text, names, paths = prefab_data(path)
    ids = [bone["Id"] for bone in rig["Bones"]]
    assert len(ids) == len(set(ids)) and all(0 <= i < 128 for i in ids)
    humanoid = [bone for bone in rig["Bones"] if bone["HumanoidBone"] >= 0]
    avatar = None
    if humanoid:
        match = re.search(r"m_Avatar: \{fileID: \d+, guid: (\w+)", text)
        assert match, f"{rig['Id']}: missing animator avatar"
        avatar = guid_paths[match[1]]
        avatar_text = avatar.read_text(encoding="utf-8-sig")
        mapping = {human.replace(" ", ""): bone for bone, human in re.findall(r"m_BoneName: (.+)\n\s+m_HumanName: (.+)", avatar_text)}
        for bone in humanoid:
            assert bone["SemanticName"] in mapping, (rig["Id"], bone["SemanticName"])
    for bone in rig["Bones"]:
        if bone["Path"] is not None:
            assert bone["Path"] in paths, (rig["Id"], bone["Path"])
        count += 1
    print(json.dumps({"rig": rig["Id"], "bones": len(ids), "prefabSha256": hashlib.sha256(path.read_bytes()).hexdigest(), "avatarSha256": hashlib.sha256(avatar.read_bytes()).hexdigest() if avatar else None}))
print(f"POSE_RIG_CATALOG_OK families={len(catalog)} bones={count}; authored paths and avatar semantic mappings verified; no engine runtime qualification")

# This is an explicit source ownership audit, not an engine-rendering test. The actual
# native collider-on-bone layouts make absence of native transform writes significant.
renderer = args.renderer.read_text(encoding="utf-8-sig")
for native in ("bone.Transform", "node.Original", "Model.transform", "Player.Transform", "rigid.Original.transform", "skin.Renderer.transform"):
    assert not re.search(re.escape(native) + r"\.(?:localPosition|localRotation|localScale|position|rotation)\s*=", renderer), native
    assert not re.search(re.escape(native) + r"\.(?:SetPositionAndRotation|SetParent)\(", renderer), native
assert "Instantiate(" not in renderer, "Never clone gameplay GameObjects/components"
assert "skin.Renderer.bones = skin.OriginalBones;" in renderer
assert "skin.Renderer.rootBone = skin.OriginalRoot;" in renderer
assert "skin.Renderer.localBounds = skin.Bounds;" in renderer
assert "rigid.Original.forceRenderingOff = rigid.WasHidden;" in renderer
capture = renderer[renderer.index("internal bool TryCapture"):renderer.index("internal void Apply")]
assert "EnsureClone(" not in capture and "new GameObject(" not in capture
print("POSE_VISUAL_OWNERSHIP_SOURCE_OK native transform writes absent; renderer palette/bounds/visibility restoration present; capture does not create visual clones")
