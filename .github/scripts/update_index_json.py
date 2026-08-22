#!/usr/bin/env python3
"""Inserts/overwrites one package version's entry in the VPM repo index.json,
copying every field from that version's package.json and adding the
release-specific "url"/"zipSHA256" the package.json itself doesn't carry.
Used by .github/workflows/release.yml right after a release zip is built.
"""
import json
import sys


def main():
    package_json_path, index_json_path, url, sha256 = sys.argv[1:5]

    with open(package_json_path, encoding="utf-8") as f:
        package = json.load(f)

    with open(index_json_path, encoding="utf-8") as f:
        index = json.load(f)

    entry = dict(package)
    entry["url"] = url
    entry["zipSHA256"] = sha256

    package_id = package["name"]
    version = package["version"]

    versions = index.setdefault("packages", {}).setdefault(package_id, {}).setdefault("versions", {})
    versions[version] = entry

    with open(index_json_path, "w", encoding="utf-8") as f:
        json.dump(index, f, indent=2, ensure_ascii=False)
        f.write("\n")


if __name__ == "__main__":
    main()
