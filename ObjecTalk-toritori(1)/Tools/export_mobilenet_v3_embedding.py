#!/usr/bin/env python3
"""Reproducibly export the ObjecTalk MobileNetV3-Small embedding model.

Upstream model: https://docs.pytorch.org/vision/0.20/models/generated/torchvision.models.mobilenet_v3_small.html
Weights: https://download.pytorch.org/models/mobilenet_v3_small-047dcff4.pth
TorchVision license: https://github.com/pytorch/vision/blob/v0.20.1/LICENSE (BSD-3-Clause)

The caller supplies RGB float32 data rescaled to [0,1] and normalized with
ImageNet mean [0.485,0.456,0.406] and standard deviation [0.229,0.224,0.225].
The fixed [1,3,224,224] input produces one L2-normalized 576-value embedding.
"""

from __future__ import annotations

import argparse
import hashlib
import importlib.metadata
import os
import sys
import urllib.request
from pathlib import Path


MODEL_VERSION = "mobilenetv3-small-imagenet-fp16-v1"
DEFAULT_OUTPUT = Path("Assets/Models/mobilenetv3-small-embedding-fp16.onnx")
DEFAULT_CHECKSUM = Path("Tools/mobilenetv3-small-embedding-fp16.onnx.sha256")
WEIGHTS_URL = "https://download.pytorch.org/models/mobilenet_v3_small-047dcff4.pth"
WEIGHTS_SHA256 = "047dcff4addef86ea5bc2eff13c9614dc11f47ab1160d0a71a25e7db994f4e1f"
LICENSE_URL = "https://github.com/pytorch/vision/blob/v0.20.1/LICENSE"
EXPECTED_VERSIONS = {
    "numpy": "1.26.4",
    "onnx": "1.17.0",
    "onnxruntime": "1.19.2",
    "torch": "2.5.1",
    "torchvision": "0.20.1",
}


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def require_pinned_environment() -> None:
    mismatches = []
    for package, expected in EXPECTED_VERSIONS.items():
        try:
            actual = importlib.metadata.version(package)
        except importlib.metadata.PackageNotFoundError:
            actual = "not installed"
        if actual != expected:
            mismatches.append(f"{package}=={expected} (found {actual})")
    if mismatches:
        joined = ", ".join(mismatches)
        raise RuntimeError(
            "Exporter dependency mismatch: "
            f"{joined}. Install Tools/requirements-mobilenetv3-export.txt."
        )


def acquire_weights(path: Path) -> Path:
    path.parent.mkdir(parents=True, exist_ok=True)
    if not path.exists():
        partial_path = path.with_suffix(path.suffix + ".part")
        urllib.request.urlretrieve(WEIGHTS_URL, partial_path)
        os.replace(partial_path, path)
    actual = sha256(path)
    if actual != WEIGHTS_SHA256:
        raise RuntimeError(
            f"Weights SHA-256 mismatch for {path}: expected {WEIGHTS_SHA256}, got {actual}"
        )
    return path


def export_model(output: Path, checksum_path: Path, weights_path: Path) -> str:
    import onnx
    import torch
    import torch.nn.functional as functional
    from torchvision.models import mobilenet_v3_small

    class EmbeddingModel(torch.nn.Module):
        def __init__(self) -> None:
            super().__init__()
            backbone = mobilenet_v3_small(weights=None)
            state_dict = torch.load(weights_path, map_location="cpu", weights_only=True)
            backbone.load_state_dict(state_dict, strict=True)
            self.features = backbone.features
            self.average_pool = backbone.avgpool
            self.half()
            self.eval()

        def forward(self, image: torch.Tensor) -> torch.Tensor:
            features = self.features(image.to(dtype=torch.float16))
            pooled = self.average_pool(features)
            flattened = torch.flatten(pooled, 1).to(dtype=torch.float32)
            return functional.normalize(flattened, p=2.0, dim=1, eps=1e-12)

    torch.manual_seed(0)
    torch.use_deterministic_algorithms(True)
    torch.set_num_threads(1)
    model = EmbeddingModel()
    example = torch.zeros((1, 3, 224, 224), dtype=torch.float32)

    output.parent.mkdir(parents=True, exist_ok=True)
    torch.onnx.export(
        model,
        example,
        output,
        export_params=True,
        opset_version=17,
        do_constant_folding=True,
        input_names=["image"],
        output_names=["embedding"],
        dynamic_axes=None,
        training=torch.onnx.TrainingMode.EVAL,
        dynamo=False,
    )

    onnx_model = onnx.load(output)
    onnx_model.producer_name = "ObjecTalk reproducible TorchVision exporter"
    onnx_model.producer_version = MODEL_VERSION
    onnx_model.doc_string = (
        "TorchVision MobileNetV3-Small ImageNet feature trunk; classifier removed; "
        "global average pooling; 576-dimensional L2-normalized embedding."
    )
    metadata = {
        "architecture": "torchvision.models.mobilenet_v3_small",
        "embedding_dimension": "576",
        "input_color_space": "RGB",
        "input_mean": "[0.485,0.456,0.406]",
        "input_range": "[0,1]",
        "input_std": "[0.229,0.224,0.225]",
        "license_url": LICENSE_URL,
        "model_version": MODEL_VERSION,
        "weights": "MobileNet_V3_Small_Weights.IMAGENET1K_V1",
        "weights_sha256": WEIGHTS_SHA256,
        "weights_url": WEIGHTS_URL,
    }
    del onnx_model.metadata_props[:]
    for key, value in sorted(metadata.items()):
        entry = onnx_model.metadata_props.add()
        entry.key = key
        entry.value = value
    onnx.checker.check_model(onnx_model, full_check=True)
    output.write_bytes(onnx_model.SerializeToString(deterministic=True))

    model_sha256 = sha256(output)
    checksum_path.parent.mkdir(parents=True, exist_ok=True)
    with checksum_path.open("w", encoding="ascii", newline="\n") as checksum_stream:
        checksum_stream.write(f"{model_sha256}  {output.name}\n")
    return model_sha256


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", type=Path, default=DEFAULT_OUTPUT)
    parser.add_argument("--checksum", type=Path, default=DEFAULT_CHECKSUM)
    parser.add_argument(
        "--weights",
        type=Path,
        default=Path.home() / ".cache" / "torch" / "hub" / "checkpoints" / Path(WEIGHTS_URL).name,
    )
    arguments = parser.parse_args()
    try:
        require_pinned_environment()
        weights_path = acquire_weights(arguments.weights)
        model_sha256 = export_model(arguments.output, arguments.checksum, weights_path)
    except (ImportError, OSError, RuntimeError, ValueError) as error:
        print(f"ERROR: {error}", file=sys.stderr)
        return 1
    print(f"Exported {arguments.output} (SHA-256 {model_sha256})")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
