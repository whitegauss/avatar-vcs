#!/usr/bin/env python3
"""Fails when anything in the shipped package is missing its .meta file.

Unity identifies an asset by the GUID inside its .meta, and generates one
locally for any asset that arrives without it. So a package shipped with no
.meta files gets *different* GUIDs on every install: updating the package
re-generates them, and every reference a user's scene holds to our marker
components breaks at once -- including AvatarVcsRoot.avatarGuid, which is the
key their entire commit history is stored under. That shipped in every
release up to 0.8.0-poc (see LESSONS.md).

Guarding it here rather than trusting the next person to notice: a .meta is
invisible in a normal diff, and nothing else in this repo reads them.
"""
import subprocess
import sys

PACKAGE = "Packages/dev.avatarvcs.avatar-vcs"


def tracked(pathspec):
    out = subprocess.run(["git", "ls-files", "-z", pathspec],
                         check=True, capture_output=True, text=True).stdout
    return [p for p in out.split("\0") if p]


def main():
    files = tracked(PACKAGE)
    if not files:
        print(f"no tracked files under {PACKAGE}; is the path still right?")
        return 1

    assets = {p for p in files if not p.endswith(".meta")}
    metas = {p for p in files if p.endswith(".meta")}

    # Every directory below the package root is an asset in its own right and
    # carries its own .meta. Walk up from each file so a directory holding
    # only other directories is covered too.
    for path in list(assets):
        parent = path.rsplit("/", 1)[0]
        while parent != PACKAGE:
            assets.add(parent)
            parent = parent.rsplit("/", 1)[0]

    problems = []
    for asset in sorted(assets):
        if asset + ".meta" not in metas:
            problems.append(f"::error file={asset}::missing {asset}.meta")

    # A .meta with nothing beside it is what a delete, or a rename that moved
    # the file without its .meta, leaves behind.
    for meta in sorted(metas):
        if meta[: -len(".meta")] not in assets:
            problems.append(f"::error file={meta}::orphaned; nothing named {meta[:-len('.meta')]}")

    guids = {}
    for meta in sorted(metas):
        with open(meta, encoding="utf-8") as f:
            guid = next((line.split(":", 1)[1].strip()
                         for line in f if line.startswith("guid:")), None)
        if guid is None:
            problems.append(f"::error file={meta}::no guid line")
            continue
        # Two assets sharing a GUID is the same breakage as having none:
        # Unity resolves references to whichever it imported last.
        if guid in guids:
            problems.append(f"::error file={meta}::guid {guid} is also used by {guids[guid]}")
        guids[guid] = meta

    for problem in problems:
        print(problem)

    if problems:
        print(f"\n{len(problems)} problem(s). Every file and folder under "
              f"{PACKAGE} must be committed together with its .meta.")
        return 1

    print(f"{len(assets)} assets, {len(metas)} .meta files, all accounted for.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
