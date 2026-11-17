#!/usr/bin/env python3
"""Run immediately before publishing: confirms the package ID and version are not already on nuget.org.

Usage: python3 tools/check_package_id.py XConnect.Sdk 2.1.0-preview.1

Exit 0: the version is free (and the ID is either unused or already yours; this script cannot tell ownership, so if the ID exists it prints
the existing authors and versions and exits 2 for a human to decide). Exit 1: that exact version already exists. Exit 3: nuget.org was unreachable.
Reads only public data; sends no credentials and nothing about the local project.
"""
import json, sys, urllib.error, urllib.request

def main():
    if len(sys.argv) != 3:
        sys.exit("usage: check_package_id.py <id> <version>")
    pid, version = sys.argv[1].lower(), sys.argv[2].lower()
    url = f"https://api.nuget.org/v3-flatcontainer/{pid}/index.json"
    try:
        with urllib.request.urlopen(url, timeout=30) as r:
            versions = [v.lower() for v in json.load(r)["versions"]]
    except urllib.error.HTTPError as e:
        if e.code == 404:
            print(f"OK: '{sys.argv[1]}' has never been published on nuget.org; {sys.argv[2]} is free.")
            return 0
        print(f"nuget.org answered HTTP {e.code}")
        return 3
    except (urllib.error.URLError, TimeoutError) as e:
        print(f"could not reach nuget.org: {e}")
        return 3
    if version in versions:
        print(f"BLOCKED: {sys.argv[1]} {sys.argv[2]} already exists on nuget.org.")
        return 1
    try:
        with urllib.request.urlopen(f"https://api.nuget.org/v3-flatcontainer/{pid}/{versions[-1]}/{pid}.nuspec", timeout=30) as r:
            nuspec = r.read().decode("utf-8-sig")
        authors = nuspec.split("<authors>")[1].split("</authors>")[0] if "<authors>" in nuspec else "?"
    except Exception:
        authors = "?"
    print(f"ATTENTION: '{sys.argv[1]}' already exists (versions: {', '.join(versions)}; latest authors: {authors}). Publish only if you own it.")
    return 2

sys.exit(main())
