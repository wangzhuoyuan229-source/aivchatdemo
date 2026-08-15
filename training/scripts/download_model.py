#!/usr/bin/env python3
"""Download the official Qwen snapshot with parallel range requests.

ModelScope hosts the Qwen organization's official mirror. The large safetensors
file is verified against the linked SHA-256 exposed by the official artifact.
"""

from __future__ import annotations

import argparse
import concurrent.futures
import hashlib
import math
import os
from pathlib import Path

import httpx


BASE_URL = "https://www.modelscope.cn/models/Qwen/Qwen2.5-1.5B-Instruct/resolve/master"
MODEL_NAME = "model.safetensors"
MODEL_SHA256 = "dd924a11b4c220f385b51ffa522daea7c9f3d850e31b162bb5661df483c6d3ee"
SMALL_FILES = (
    "config.json",
    "generation_config.json",
    "tokenizer_config.json",
    "tokenizer.json",
    "merges.txt",
    "vocab.json",
    "LICENSE",
    "README.md",
)


def file_sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as handle:
        for block in iter(lambda: handle.read(8 * 1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def remote_size(url: str) -> int:
    with httpx.Client(follow_redirects=True, timeout=60) as client:
        response = client.get(url, headers={"Range": "bytes=0-0"})
        response.raise_for_status()
        content_range = response.headers.get("content-range", "")
        if "/" not in content_range:
            raise RuntimeError(f"server did not return a byte range: {content_range}")
        return int(content_range.rsplit("/", 1)[1])


def download_small(url: str, destination: Path) -> None:
    if destination.exists() and destination.stat().st_size:
        return
    partial = destination.with_suffix(destination.suffix + ".part")
    with httpx.stream("GET", url, follow_redirects=True, timeout=None) as response:
        response.raise_for_status()
        with partial.open("wb") as handle:
            for block in response.iter_bytes(1024 * 1024):
                handle.write(block)
    partial.replace(destination)


def download_range(url: str, path: Path, start: int, end: int) -> Path:
    expected = end - start + 1
    if path.exists() and path.stat().st_size == expected:
        print(f"[skip part] {path.name}")
        return path
    partial = path.with_suffix(path.suffix + ".partial")
    headers = {"Range": f"bytes={start}-{end}"}
    print(f"[part] {path.name}: {start:,}-{end:,}")
    with httpx.stream(
        "GET", url, headers=headers, follow_redirects=True, timeout=None
    ) as response:
        response.raise_for_status()
        if response.status_code != 206:
            raise RuntimeError(f"range request returned HTTP {response.status_code}")
        with partial.open("wb") as handle:
            for block in response.iter_bytes(4 * 1024 * 1024):
                handle.write(block)
    if partial.stat().st_size != expected:
        raise RuntimeError(
            f"{path.name} has {partial.stat().st_size:,} bytes; expected {expected:,}"
        )
    partial.replace(path)
    return path


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--output-dir", type=Path, required=True)
    parser.add_argument("--parts", type=int, default=8)
    args = parser.parse_args()

    output = args.output_dir.resolve()
    output.mkdir(parents=True, exist_ok=True)
    destination = output / MODEL_NAME
    if destination.exists() and file_sha256(destination) == MODEL_SHA256:
        print(f"[skip verified] {destination}")
    else:
        url = f"{BASE_URL}/{MODEL_NAME}"
        size = remote_size(url)
        chunk = math.ceil(size / args.parts)
        ranges = []
        for index in range(args.parts):
            start = index * chunk
            end = min(size - 1, start + chunk - 1)
            if start <= end:
                ranges.append((index, start, end))
        part_dir = output / ".model-parts"
        part_dir.mkdir(exist_ok=True)
        with concurrent.futures.ThreadPoolExecutor(max_workers=args.parts) as pool:
            futures = [
                pool.submit(
                    download_range,
                    url,
                    part_dir / f"{MODEL_NAME}.{index:02d}",
                    start,
                    end,
                )
                for index, start, end in ranges
            ]
            parts = [future.result() for future in futures]
        partial_model = destination.with_suffix(destination.suffix + ".part")
        print(f"[merge] {len(parts)} parts -> {destination}")
        with partial_model.open("wb") as target:
            for path in sorted(parts):
                with path.open("rb") as source:
                    while block := source.read(8 * 1024 * 1024):
                        target.write(block)
        if partial_model.stat().st_size != size:
            raise RuntimeError("merged model size does not match the server artifact")
        actual = file_sha256(partial_model)
        if actual != MODEL_SHA256:
            raise RuntimeError(f"model SHA-256 mismatch: {actual}")
        partial_model.replace(destination)
        for path in parts:
            path.unlink()
        try:
            part_dir.rmdir()
        except OSError:
            pass
        print(f"[verified] sha256={actual}")

    for name in SMALL_FILES:
        download_small(f"{BASE_URL}/{name}", output / name)
    print(f"[done] official model snapshot: {output}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
