#!/usr/bin/env python3
"""Independent ONNX contract and inference validation for the embedding model."""

from __future__ import annotations

import argparse
import hashlib
import sys
from pathlib import Path


MODEL_PATH = Path("Assets/Models/mobilenetv3-small-embedding-fp16.onnx")
CHECKSUM_PATH = Path("Tools/mobilenetv3-small-embedding-fp16.onnx.sha256")
MODEL_VERSION = "mobilenetv3-small-imagenet-fp16-v1"
INPUT_NAME = "image"
INPUT_SHAPE = [1, 3, 224, 224]
OUTPUT_NAME = "embedding"
OUTPUT_SHAPE = [1, 576]
MAX_MODEL_BYTES = 15 * 1024 * 1024
EXPECTED_METADATA = {
    "architecture": "torchvision.models.mobilenet_v3_small",
    "embedding_dimension": "576",
    "input_color_space": "RGB",
    "input_range": "[0,1]",
    "input_mean": "[0.485,0.456,0.406]",
    "input_std": "[0.229,0.224,0.225]",
    "license_url": "https://github.com/pytorch/vision/blob/v0.20.1/LICENSE",
    "model_version": MODEL_VERSION,
    "weights": "MobileNet_V3_Small_Weights.IMAGENET1K_V1",
    "weights_url": "https://download.pytorch.org/models/mobilenet_v3_small-047dcff4.pth",
}


def _fixed_shape(value_info: object) -> list[int]:
    dimensions = value_info.type.tensor_type.shape.dim
    if any(not dimension.HasField("dim_value") for dimension in dimensions):
        raise AssertionError(f"{value_info.name} must have a fully fixed shape")
    return [dimension.dim_value for dimension in dimensions]


def _sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def validate(model_path: Path, checksum_path: Path) -> str:
    if not model_path.is_file():
        raise AssertionError(f"model asset is missing: {model_path}")
    if not checksum_path.is_file():
        raise AssertionError(f"checksum record is missing: {checksum_path}")
    if model_path.stat().st_size >= MAX_MODEL_BYTES:
        raise AssertionError(
            f"model is {model_path.stat().st_size} bytes; expected less than {MAX_MODEL_BYTES}"
        )

    import numpy as np
    import onnx
    import onnxruntime as ort

    model = onnx.load(model_path)
    onnx.checker.check_model(model, full_check=True)

    opsets = {entry.domain: entry.version for entry in model.opset_import}
    if opsets.get("") != 17:
        raise AssertionError(f"default ONNX opset must be 17, got {opsets.get('')}")
    if len(model.graph.input) != 1 or model.graph.input[0].name != INPUT_NAME:
        raise AssertionError(f"expected sole input {INPUT_NAME!r}")
    if _fixed_shape(model.graph.input[0]) != INPUT_SHAPE:
        raise AssertionError(f"input shape must be {INPUT_SHAPE}")
    if len(model.graph.output) != 1 or model.graph.output[0].name != OUTPUT_NAME:
        raise AssertionError(f"expected sole output {OUTPUT_NAME!r}")
    if _fixed_shape(model.graph.output[0]) != OUTPUT_SHAPE:
        raise AssertionError(f"output shape must be {OUTPUT_SHAPE}")

    metadata = {entry.key: entry.value for entry in model.metadata_props}
    for key, expected in EXPECTED_METADATA.items():
        if metadata.get(key) != expected:
            raise AssertionError(
                f"metadata {key!r} must be {expected!r}, got {metadata.get(key)!r}"
            )
    if len(metadata.get("weights_sha256", "")) != 64:
        raise AssertionError("metadata 'weights_sha256' must be a full SHA-256 digest")

    if not model.graph.initializer:
        raise AssertionError("model must contain parameter initializers")
    non_fp16 = [item.name for item in model.graph.initializer if item.data_type != onnx.TensorProto.FLOAT16]
    if non_fp16:
        raise AssertionError(f"all parameter initializers must be FP16; non-FP16: {non_fp16[:5]}")

    actual_sha256 = _sha256(model_path)
    checksum_fields = checksum_path.read_text(encoding="ascii").strip().split()
    if len(checksum_fields) != 2 or checksum_fields[1] != model_path.name:
        raise AssertionError("checksum record must use '<sha256>  <filename>' format")
    if checksum_fields[0] != actual_sha256:
        raise AssertionError(
            f"checksum mismatch: recorded {checksum_fields[0]}, actual {actual_sha256}"
        )

    rgb = np.full(INPUT_SHAPE, 0.5, dtype=np.float32)
    mean = np.asarray([0.485, 0.456, 0.406], dtype=np.float32).reshape(1, 3, 1, 1)
    std = np.asarray([0.229, 0.224, 0.225], dtype=np.float32).reshape(1, 3, 1, 1)
    normalized = (rgb - mean) / std
    session = ort.InferenceSession(model_path.as_posix(), providers=["CPUExecutionProvider"])
    output = session.run([OUTPUT_NAME], {INPUT_NAME: normalized})[0]
    if list(output.shape) != OUTPUT_SHAPE:
        raise AssertionError(f"runtime output shape must be {OUTPUT_SHAPE}, got {list(output.shape)}")
    if not np.isfinite(output).all():
        raise AssertionError("runtime output must contain only finite values")
    norm = float(np.linalg.norm(output[0]))
    if abs(norm - 1.0) > 1e-4:
        raise AssertionError(f"runtime output must have unit L2 norm, got {norm:.9f}")

    return (
        f"PASS: {model_path} | bytes={model_path.stat().st_size} | "
        f"sha256={actual_sha256} | output_shape={list(output.shape)} | l2={norm:.9f}"
    )


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--model", type=Path, default=MODEL_PATH)
    parser.add_argument("--checksum", type=Path, default=CHECKSUM_PATH)
    arguments = parser.parse_args()
    try:
        print(validate(arguments.model, arguments.checksum))
    except (AssertionError, ImportError, OSError, ValueError) as error:
        print(f"FAIL: {error}", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
