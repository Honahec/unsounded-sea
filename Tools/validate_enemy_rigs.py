#!/usr/bin/env python3
"""Validate structural, skinning, and animation evidence in binary glTF enemy rigs."""

import argparse
import json
import math
import re
import struct
import sys
from pathlib import Path

COMPONENTS = {
    5120: ("b", 1), 5121: ("B", 1), 5122: ("h", 2), 5123: ("H", 2),
    5125: ("I", 4), 5126: ("f", 4),
}
SHAPES = {"SCALAR": 1, "VEC2": 2, "VEC3": 3, "VEC4": 4, "MAT2": 4, "MAT3": 9, "MAT4": 16}
DEFAULT_JOINT_PATTERNS = {
    "left_upper_arm": [r"(^|[^a-z])(left|l)[^a-z]*(upper)?[^a-z]*arm", r"upper[_ .-]*arm[_ .-]*l"],
    "right_upper_arm": [r"(^|[^a-z])(right|r)[^a-z]*(upper)?[^a-z]*arm", r"upper[_ .-]*arm[_ .-]*r"],
    "left_lower_arm": [r"(^|[^a-z])(left|l)[^a-z]*(lower|fore)[^a-z]*arm", r"(lower[_ .-]*arm|forearm)[_ .-]*l"],
    "right_lower_arm": [r"(^|[^a-z])(right|r)[^a-z]*(lower|fore)[^a-z]*arm", r"(lower[_ .-]*arm|forearm)[_ .-]*r"],
    "left_upper_leg": [r"(^|[^a-z])(left|l)[^a-z]*(upper)?[^a-z]*(leg|thigh)", r"(upper[_ .-]*leg|thigh)[_ .-]*l"],
    "right_upper_leg": [r"(^|[^a-z])(right|r)[^a-z]*(upper)?[^a-z]*(leg|thigh)", r"(upper[_ .-]*leg|thigh)[_ .-]*r"],
    "left_lower_leg": [r"(^|[^a-z])(left|l)[^a-z]*(lower|calf|shin)[^a-z]*leg", r"(lower[_ .-]*leg|calf|shin)[_ .-]*l"],
    "right_lower_leg": [r"(^|[^a-z])(right|r)[^a-z]*(lower|calf|shin)[^a-z]*leg", r"(lower[_ .-]*leg|calf|shin)[_ .-]*r"],
    "torso": [r"spine", r"chest", r"torso"],
}


class ValidationError(ValueError):
    pass


def require(condition, message):
    if not condition:
        raise ValidationError(message)


def load_glb(path):
    data = path.read_bytes()
    require(len(data) >= 12, "file is shorter than the GLB header")
    magic, version, total = struct.unpack_from("<4sII", data)
    require(magic == b"glTF", "invalid GLB magic")
    require(version == 2, f"unsupported GLB version {version}")
    require(total == len(data), f"declared length {total} differs from file length {len(data)}")
    chunks, offset = [], 12
    while offset < len(data):
        require(offset + 8 <= len(data), "truncated chunk header")
        length, kind = struct.unpack_from("<I4s", data, offset)
        offset += 8
        require(offset + length <= len(data), "truncated chunk payload")
        chunks.append((kind, data[offset:offset + length]))
        offset += length
    require(chunks and chunks[0][0] == b"JSON", "first GLB chunk must be JSON")
    require(len(chunks) == 2 and chunks[1][0] == b"BIN\x00", "GLB must contain exactly one embedded BIN chunk")
    try:
        document = json.loads(chunks[0][1].decode("utf-8").rstrip("\x00 \t\r\n"))
    except (UnicodeDecodeError, json.JSONDecodeError) as exc:
        raise ValidationError(f"invalid JSON chunk: {exc}") from exc
    return document, chunks[1][1]


def accessor_values(doc, blob, index):
    accessors = doc.get("accessors", [])
    require(0 <= index < len(accessors), f"accessor index {index} is out of range")
    acc = accessors[index]
    require("sparse" not in acc, f"accessor {index}: sparse accessors are unsupported")
    require("bufferView" in acc, f"accessor {index}: missing bufferView")
    component = acc.get("componentType")
    shape = acc.get("type")
    require(component in COMPONENTS, f"accessor {index}: unsupported component type {component}")
    require(shape in SHAPES, f"accessor {index}: unsupported shape {shape}")
    count = acc.get("count")
    require(isinstance(count, int) and count >= 0, f"accessor {index}: invalid count")
    views = doc.get("bufferViews", [])
    view_index = acc["bufferView"]
    require(isinstance(view_index, int) and 0 <= view_index < len(views), f"accessor {index}: invalid bufferView")
    view = views[view_index]
    require(view.get("buffer", 0) == 0, f"accessor {index}: bufferView is not in embedded buffer 0")
    fmt, size = COMPONENTS[component]
    width = SHAPES[shape]
    packed = size * width
    stride = view.get("byteStride", packed)
    require(isinstance(stride, int) and stride >= packed and stride % size == 0,
            f"accessor {index}: invalid byteStride {stride} for {packed}-byte element")
    start = view.get("byteOffset", 0) + acc.get("byteOffset", 0)
    require(start % size == 0, f"accessor {index}: byte offset is not aligned to component size")
    view_end = view.get("byteOffset", 0) + view.get("byteLength", 0)
    end = start if count == 0 else start + (count - 1) * stride + packed
    require(start >= view.get("byteOffset", 0) and end <= view_end and end <= len(blob),
            f"accessor {index}: data exceeds its bufferView")
    unpack = struct.Struct("<" + fmt * width).unpack_from
    values = [unpack(blob, start + row * stride) for row in range(count)]
    for value in values:
        require(all(math.isfinite(float(item)) for item in value), f"accessor {index}: non-finite value")
    return values


def normalized(values, component, enabled):
    if not enabled or component == 5126:
        return [[float(v) for v in row] for row in values]
    require(component != 5125, "UNSIGNED_INT accessors cannot be normalized")
    if component in (5121, 5123):
        divisor = 255.0 if component == 5121 else 65535.0
        return [[v / divisor for v in row] for row in values]
    divisor = 127.0 if component == 5120 else 32767.0
    return [[max(v / divisor, -1.0) for v in row] for row in values]


def node_paths(nodes):
    parents = {}
    for parent, node in enumerate(nodes):
        for child in node.get("children", []):
            require(isinstance(child, int) and 0 <= child < len(nodes), f"node {parent}: invalid child {child}")
            require(child not in parents, f"node {child}: multiple parents")
            parents[child] = parent
    paths = []
    for index in range(len(nodes)):
        seen, chain, current = set(), [], index
        while current is not None:
            require(current not in seen, f"node hierarchy cycle at node {current}")
            seen.add(current)
            chain.append(nodes[current].get("name") or f"node_{current}")
            current = parents.get(current)
        paths.append("/".join(reversed(chain)))
    return parents, paths


def load_patterns(path):
    patterns = DEFAULT_JOINT_PATTERNS
    if path:
        supplied = json.loads(path.read_text(encoding="utf-8"))
        require(isinstance(supplied, dict), "joint map must be a JSON object")
        patterns = dict(DEFAULT_JOINT_PATTERNS)
        for semantic, aliases in supplied.items():
            if isinstance(aliases, str):
                aliases = [aliases]
            require(isinstance(aliases, list) and all(isinstance(v, str) for v in aliases),
                    f"joint map entry {semantic!r} must be a string or string list")
            patterns[semantic] = [rf"^{re.escape(alias)}$" for alias in aliases]
    return patterns


def semantic_nodes(nodes, patterns):
    result = {}
    for semantic, expressions in patterns.items():
        matches = [i for i, node in enumerate(nodes) if any(re.search(expr, node.get("name", ""), re.I) for expr in expressions)]
        require(matches, f"no joint matches required semantic {semantic!r}")
        result[semantic] = matches
    return result


def validate(path, patterns):
    doc, blob = load_glb(path)
    require(doc.get("asset", {}).get("version") == "2.0", "asset.version must be 2.0")
    buffers = doc.get("buffers", [])
    require(len(buffers) == 1 and "uri" not in buffers[0], "all buffer data must be embedded in one GLB buffer")
    require(buffers[0].get("byteLength", -1) <= len(blob), "embedded buffer is shorter than declared byteLength")
    views = doc.get("bufferViews", [])
    for i, image in enumerate(doc.get("images", [])):
        view_index = image.get("bufferView")
        require("uri" not in image and isinstance(view_index, int) and 0 <= view_index < len(views),
                f"image {i} is not embedded in a valid bufferView")
        require(image.get("mimeType") in ("image/png", "image/jpeg", "image/webp"), f"image {i}: unsupported or missing mimeType")
        view = views[view_index]
        require(view.get("buffer", 0) == 0 and view.get("byteOffset", 0) + view.get("byteLength", 0) <= len(blob),
                f"image {i}: data exceeds embedded buffer")
    for i, texture in enumerate(doc.get("textures", [])):
        require(isinstance(texture.get("source"), int) and 0 <= texture["source"] < len(doc.get("images", [])),
                f"texture {i}: invalid embedded image source")
    for i in range(len(doc.get("accessors", []))):
        accessor_values(doc, blob, i)

    nodes = doc.get("nodes", [])
    require(nodes, "no nodes")
    parents, paths = node_paths(nodes)
    semantics = semantic_nodes(nodes, patterns)
    meshes, skins = doc.get("meshes", []), doc.get("skins", [])
    require(meshes and skins, "model must contain meshes and skins")
    mesh_uses = {}
    for node_index, node in enumerate(nodes):
        if "mesh" in node:
            require("skin" in node, f"mesh node {node_index} is not skinned")
            mesh_uses.setdefault(node["mesh"], []).append((node_index, node["skin"]))
    require(mesh_uses, "no skinned mesh nodes")

    skin_stats, total_triangles = [], 0
    influenced_semantics = set()
    for skin_index, skin in enumerate(skins):
        joints = skin.get("joints", [])
        require(joints and len(set(joints)) == len(joints), f"skin {skin_index}: missing or duplicate joints")
        require(all(isinstance(j, int) and 0 <= j < len(nodes) for j in joints), f"skin {skin_index}: invalid joint node")
        ibm = skin.get("inverseBindMatrices")
        require(isinstance(ibm, int), f"skin {skin_index}: missing inverseBindMatrices")
        require(doc["accessors"][ibm].get("type") == "MAT4", f"skin {skin_index}: inverseBindMatrices must be MAT4")
        require(len(accessor_values(doc, blob, ibm)) == len(joints), f"skin {skin_index}: inverse bind count differs from joint count")
        skin_stats.append({"index": skin_index, "bone_count": len(joints), "bones": [nodes[j].get("name") or f"node_{j}" for j in joints],
                           "hierarchy": [paths[j] for j in joints]})

    for mesh_index, uses in mesh_uses.items():
        require(isinstance(mesh_index, int) and 0 <= mesh_index < len(meshes), f"invalid mesh index {mesh_index}")
        for primitive_index, primitive in enumerate(meshes[mesh_index].get("primitives", [])):
            require(primitive.get("mode", 4) == 4, f"mesh {mesh_index} primitive {primitive_index}: mode is not TRIANGLES")
            attrs = primitive.get("attributes", {})
            require(all(name in attrs for name in ("POSITION", "JOINTS_0", "WEIGHTS_0")),
                    f"mesh {mesh_index} primitive {primitive_index}: missing POSITION/JOINTS_0/WEIGHTS_0")
            positions = accessor_values(doc, blob, attrs["POSITION"])
            joint_acc = doc["accessors"][attrs["JOINTS_0"]]
            weight_acc = doc["accessors"][attrs["WEIGHTS_0"]]
            require(joint_acc.get("type") == "VEC4" and joint_acc.get("componentType") in (5121, 5123),
                    f"mesh {mesh_index} primitive {primitive_index}: JOINTS_0 must be unsigned VEC4")
            require(weight_acc.get("type") == "VEC4" and weight_acc.get("componentType") in (5121, 5123, 5126),
                    f"mesh {mesh_index} primitive {primitive_index}: WEIGHTS_0 must be float or normalized unsigned VEC4")
            require(weight_acc["componentType"] == 5126 or weight_acc.get("normalized") is True,
                    f"mesh {mesh_index} primitive {primitive_index}: integer WEIGHTS_0 must be normalized")
            joint_values = accessor_values(doc, blob, attrs["JOINTS_0"])
            weights = normalized(accessor_values(doc, blob, attrs["WEIGHTS_0"]), weight_acc["componentType"], weight_acc.get("normalized", False))
            require(len(positions) == len(joint_values) == len(weights), f"mesh {mesh_index} primitive {primitive_index}: vertex attribute counts differ")
            if "indices" in primitive:
                indices = accessor_values(doc, blob, primitive["indices"])
                index_acc = doc["accessors"][primitive["indices"]]
                require(index_acc["componentType"] in (5121, 5123, 5125) and index_acc["type"] == "SCALAR", "indices must be unsigned scalar")
                flat_indices = [row[0] for row in indices]
                require(all(index < len(positions) for index in flat_indices), "primitive index exceeds vertex count")
                count = len(flat_indices)
            else:
                count = len(positions)
            require(count % 3 == 0, f"mesh {mesh_index} primitive {primitive_index}: triangle element count is not divisible by three")
            total_triangles += count // 3
            for node_index, skin_index in uses:
                require(0 <= skin_index < len(skins), f"node {node_index}: invalid skin index")
                skin_joints = skins[skin_index]["joints"]
                for vertex, (joint_row, weight_row) in enumerate(zip(joint_values, weights)):
                    require(all(float(j).is_integer() and 0 <= int(j) < len(skin_joints) for j in joint_row),
                            f"mesh {mesh_index} vertex {vertex}: joint index outside skin")
                    require(all(math.isfinite(w) and w >= 0 for w in weight_row), f"mesh {mesh_index} vertex {vertex}: invalid weight")
                    require(sum(weight_row) > 0, f"mesh {mesh_index} vertex {vertex}: unweighted")
                    require(math.isclose(sum(weight_row), 1.0, rel_tol=0, abs_tol=2e-3),
                            f"mesh {mesh_index} vertex {vertex}: weights sum to {sum(weight_row):.6g}")
                    for joint, weight in zip(joint_row, weight_row):
                        if weight > 0:
                            target = skin_joints[int(joint)]
                            influenced_semantics.update(key for key, matches in semantics.items() if target in matches)
    limb_keys = set(DEFAULT_JOINT_PATTERNS) - {"torso"}
    require(limb_keys <= influenced_semantics, "missing nonzero vertex influence for: " + ", ".join(sorted(limb_keys - influenced_semantics)))
    require(len(influenced_semantics) > 1, "all vertices appear weighted to a single root joint")

    animation_stats, animated_semantics, nonconstant_rotations, moving_semantics = [], set(), set(), set()
    animations = doc.get("animations", [])
    require(animations, "no animations")
    for animation_index, animation in enumerate(animations):
        require(animation.get("name"), f"animation {animation_index}: missing name")
        channels = animation.get("channels", [])
        require(channels, f"animation {animation_index}: no channels")
        samplers = animation.get("samplers", [])
        times_seen = []
        for channel_index, channel in enumerate(channels):
            sampler_index = channel.get("sampler")
            require(isinstance(sampler_index, int) and 0 <= sampler_index < len(samplers), f"animation {animation_index} channel {channel_index}: invalid sampler")
            sampler = samplers[sampler_index]
            input_acc = doc["accessors"][sampler["input"]]
            require(input_acc.get("componentType") == 5126 and input_acc.get("type") == "SCALAR",
                    f"animation {animation_index} channel {channel_index}: key times must be float SCALAR")
            times = [row[0] for row in accessor_values(doc, blob, sampler["input"])]
            require(times and all(b > a for a, b in zip(times, times[1:])), f"animation {animation_index} channel {channel_index}: key times are not strictly increasing")
            output = accessor_values(doc, blob, sampler["output"])
            interpolation = sampler.get("interpolation", "LINEAR")
            multiplier = 3 if interpolation == "CUBICSPLINE" else 1
            require(interpolation in ("LINEAR", "STEP", "CUBICSPLINE"),
                    f"animation {animation_index} channel {channel_index}: unsupported interpolation")
            target = channel.get("target", {})
            target_node, target_path = target.get("node"), target.get("path")
            require(isinstance(target_node, int) and 0 <= target_node < len(nodes), f"animation {animation_index}: invalid target node")
            require(target_path in ("translation", "rotation", "scale", "weights"), f"animation {animation_index}: invalid target path")
            if target_path != "weights":
                expected_type = "VEC4" if target_path == "rotation" else "VEC3"
                require(doc["accessors"][sampler["output"]].get("type") == expected_type,
                        f"animation {animation_index} channel {channel_index}: {target_path} output must be {expected_type}")
                require(len(output) == len(times) * multiplier,
                        f"animation {animation_index} channel {channel_index}: sampler output count mismatch")
            animated_semantics.update(key for key, matches in semantics.items() if target_node in matches)
            values = output[1::3] if interpolation == "CUBICSPLINE" else output
            if target_path == "rotation" and any(any(abs(a - b) > 1e-6 for a, b in zip(values[0], value)) for value in values[1:]):
                nonconstant_rotations.add(target_node)
                moving_semantics.update(key for key, matches in semantics.items() if target_node in matches)
            times_seen.extend(times)
        animation_stats.append({"name": animation["name"], "duration": max(times_seen) - min(times_seen), "channels": len(channels),
                                "key_time_min": min(times_seen), "key_time_max": max(times_seen)})
    required_clips = {"Idle", "Action", "Hit", "Death", "RigValidation"}
    require(required_clips <= {a["name"] for a in animation_stats}, "required Idle/Action/Hit/Death/RigValidation animation is missing")
    require("torso" in animated_semantics, "animation has no torso channel")
    require(any(k.endswith("arm") for k in animated_semantics) and any(k.endswith("leg") for k in animated_semantics), "animation must channel both limbs and torso")
    require("torso" in moving_semantics and any(k.endswith("arm") for k in moving_semantics) and any(k.endswith("leg") for k in moving_semantics),
            "animation needs nonconstant rotation on arm, leg, and torso joints")

    materials = [{"index": i, "name": item.get("name"), "alpha_mode": item.get("alphaMode", "OPAQUE"), "double_sided": item.get("doubleSided", False)}
                 for i, item in enumerate(doc.get("materials", []))]
    images = [{"index": i, "name": item.get("name"), "mime_type": item.get("mimeType"), "byte_length": views[item["bufferView"]].get("byteLength")}
              for i, item in enumerate(doc.get("images", []))]
    return {"path": str(path), "valid": True, "triangles": total_triangles, "skins": skin_stats, "materials": materials,
            "images": images, "animations": animation_stats, "influenced_semantics": sorted(influenced_semantics),
            "animated_semantics": sorted(animated_semantics), "moving_rotation_semantics": sorted(moving_semantics),
            "nonconstant_rotation_nodes": [nodes[i].get("name") or f"node_{i}" for i in sorted(nonconstant_rotations)]}


def self_check():
    doc = {"bufferViews": [{"buffer": 0, "byteOffset": 0, "byteLength": 32, "byteStride": 16}],
           "accessors": [{"bufferView": 0, "componentType": 5126, "count": 2, "type": "VEC3"}]}
    blob = struct.pack("<3fI3fI", 1, 2, 3, 0, 4, 5, 6, 0)
    assert accessor_values(doc, blob, 0) == [(1.0, 2.0, 3.0), (4.0, 5.0, 6.0)]
    doc["bufferViews"][0]["byteStride"] = 10
    try:
        accessor_values(doc, blob, 0)
    except ValidationError:
        pass
    else:
        raise AssertionError("misaligned accessor stride was accepted")
    try:
        normalized([(1,)], 5125, True)
    except ValidationError:
        pass
    else:
        raise AssertionError("normalized UNSIGNED_INT was accepted")


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("glb", nargs="*", type=Path, help="GLB files to validate")
    parser.add_argument("--json", type=Path, dest="json_path", help="write complete JSON results to this path")
    parser.add_argument("--joint-map", type=Path, help="JSON semantic-to-exact-bone-name map")
    parser.add_argument("--self-check", action="store_true", help="run deterministic parser rejection checks")
    args = parser.parse_args(argv)
    if args.self_check:
        self_check()
    if not args.glb:
        require(args.self_check, "provide at least one GLB path")
        return 0
    patterns = load_patterns(args.joint_map)
    results, failed = [], False
    for path in args.glb:
        try:
            results.append(validate(path, patterns))
        except (OSError, KeyError, IndexError, TypeError, struct.error, ValidationError, json.JSONDecodeError) as exc:
            failed = True
            results.append({"path": str(path), "valid": False, "error": str(exc)})
    output = {"validator": "structural evidence only; visual validity is not assessed", "results": results}
    rendered = json.dumps(output, ensure_ascii=False, indent=2)
    if args.json_path:
        args.json_path.write_text(rendered + "\n", encoding="utf-8")
    print(rendered)
    return 1 if failed else 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except ValidationError as exc:
        print(f"error: {exc}", file=sys.stderr)
        raise SystemExit(2)
