#!/usr/bin/env python3
"""Download the research datasets used by the local SFT pipeline.

Only stdlib is required. Files are written atomically and a SHA-256 manifest is
created. NaturalConv is guarded by an explicit license flag because accessing
the dataset constitutes acceptance of Tencent's non-commercial terms.
"""

from __future__ import annotations

import argparse
import gzip
import hashlib
import json
import shutil
import sys
import time
import urllib.error
import urllib.request
from pathlib import Path
from typing import BinaryIO


USER_AGENT = "ChatApp-research-dataset-preparer/1.0"


def request(url: str) -> BinaryIO:
    last_error: Exception | None = None
    for attempt in range(5):
        try:
            return urllib.request.urlopen(
                urllib.request.Request(url, headers={"User-Agent": USER_AGENT}),
                timeout=120,
            )
        except (urllib.error.URLError, TimeoutError, ConnectionError) as error:
            last_error = error
            wait_seconds = 2 ** attempt
            print(
                f"[retry {attempt + 1}/5] {url}: {error}; waiting {wait_seconds}s",
                file=sys.stderr,
            )
            time.sleep(wait_seconds)
    assert last_error is not None
    raise last_error


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as handle:
        for block in iter(lambda: handle.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def download(url: str, destination: Path) -> None:
    if destination.exists() and destination.stat().st_size > 0:
        print(f"[skip] {destination}")
        return
    destination.parent.mkdir(parents=True, exist_ok=True)
    partial = destination.with_suffix(destination.suffix + ".part")
    print(f"[download] {url}\n       -> {destination}")
    with request(url) as source, partial.open("wb") as target:
        shutil.copyfileobj(source, target, length=1024 * 1024)
    partial.replace(destination)


def download_lccc_sample(url: str, destination: Path, max_records: int) -> None:
    if destination.exists() and sum(1 for _ in destination.open("rb")) >= max_records:
        print(f"[skip] {destination}")
        return
    destination.parent.mkdir(parents=True, exist_ok=True)
    partial = destination.with_suffix(destination.suffix + ".part")
    print(f"[stream] LCCC-base first {max_records:,} records")
    with request(url) as response, gzip.GzipFile(fileobj=response) as source, partial.open(
        "wb"
    ) as target:
        for index, line in enumerate(source):
            if index >= max_records:
                break
            target.write(line)
    partial.replace(destination)


def add_file(manifest: dict, dataset: str, path: Path, url: str) -> None:
    manifest.setdefault("datasets", {}).setdefault(dataset, []).append(
        {
            "path": str(path),
            "url": url,
            "bytes": path.stat().st_size,
            "sha256": sha256(path),
        }
    )


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--raw-dir", type=Path, required=True)
    parser.add_argument("--lccc-records", type=int, default=20_000)
    parser.add_argument("--accept-naturalconv-license", action="store_true")
    args = parser.parse_args()

    raw = args.raw_dir.resolve()
    raw.mkdir(parents=True, exist_ok=True)
    manifest: dict = {
        "purpose": "non-commercial research and evaluation",
        "datasets": {},
    }

    cped_base = "https://raw.githubusercontent.com/scutcyr/CPED/main"
    for name in ("train_split.csv", "valid_split.csv", "test_split.csv"):
        url = f"{cped_base}/data/CPED/{name}"
        path = raw / "cped" / name
        download(url, path)
        add_file(manifest, "cped", path, url)
    url = f"{cped_base}/LICENSE"
    path = raw / "cped" / "LICENSE"
    download(url, path)
    add_file(manifest, "cped", path, url)

    kd_base = "https://raw.githubusercontent.com/thu-coai/KdConv/master"
    for domain in ("film", "music", "travel"):
        for split in ("train", "dev", "test"):
            name = f"data/{domain}/{split}.json"
            url = f"{kd_base}/{name}"
            path = raw / "kdconv" / domain / f"{split}.json"
            download(url, path)
            add_file(manifest, "kdconv", path, url)
    url = f"{kd_base}/LICENSE"
    path = raw / "kdconv" / "LICENSE"
    download(url, path)
    add_file(manifest, "kdconv", path, url)

    if not args.accept_naturalconv_license:
        print(
            "NaturalConv was not downloaded. Re-run with "
            "--accept-naturalconv-license after reviewing its terms.",
            file=sys.stderr,
        )
        return 2
    natural_base = (
        "https://huggingface.co/datasets/xywang1/NaturalConv/resolve/main"
    )
    for name in (
        "dialog_release.json",
        "train.txt",
        "dev.txt",
        "test.txt",
        "LICENSE",
        "README.md",
    ):
        url = f"{natural_base}/{name}?download=true"
        path = raw / "naturalconv" / name
        download(url, path)
        add_file(manifest, "naturalconv", path, url)

    lccc_dir = raw / "lccc"
    lccc_url = (
        "https://huggingface.co/datasets/silver/lccc/resolve/main/"
        "lccc_base_train.jsonl.gz"
    )
    lccc_path = lccc_dir / "lccc_base_train.sample.jsonl"
    download_lccc_sample(lccc_url, lccc_path, args.lccc_records)
    add_file(manifest, "lccc", lccc_path, lccc_url)
    readme_url = "https://raw.githubusercontent.com/thu-coai/CDial-GPT/master/README.md"
    readme_path = lccc_dir / "README.md"
    download(readme_url, readme_path)
    add_file(manifest, "lccc", readme_path, readme_url)

    manifest_path = raw / "manifest.json"
    manifest_path.write_text(
        json.dumps(manifest, ensure_ascii=False, indent=2), encoding="utf-8"
    )
    print(f"[done] manifest: {manifest_path}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
