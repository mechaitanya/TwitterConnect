#!/usr/bin/env python3
"""Checks a packed XConnect NuGet package the way a reviewer would, instead of trusting that `dotnet pack` succeeded.

Usage: python3 tools/verify_package.py <directory containing the .nupkg and .snupkg> [--allow-dirty-commit]

Fails (exit 1) on: missing metadata, a deprecated license URL, relative README links, unexpected dependencies, missing XML docs, a missing or
Source Link-less symbol package. The Source Link commit is also compared with the working tree: if the sources have uncommitted changes the
embedded commit does not contain them, which is reported as an error unless --allow-dirty-commit is given (CI checks out a clean tree anyway).
"""
import glob, os, re, subprocess, sys, zipfile
import xml.etree.ElementTree as ET

def main():
    args = [a for a in sys.argv[1:] if not a.startswith("--")]
    allow_dirty = "--allow-dirty-commit" in sys.argv
    folder = args[0] if args else "."
    pk = sorted(glob.glob(os.path.join(folder, "*.nupkg")))
    sk = sorted(glob.glob(os.path.join(folder, "*.snupkg")))
    errors, notes = [], []
    if len(pk) != 1:
        sys.exit(f"expected exactly one .nupkg in {folder}, found {pk}")
    if len(sk) != 1:
        errors.append(f"expected exactly one .snupkg, found {sk}")

    z = zipfile.ZipFile(pk[0])
    names = z.namelist()
    nuspec_name = next(n for n in names if n.endswith(".nuspec"))
    root = ET.fromstring(z.read(nuspec_name).decode("utf-8-sig"))
    ns = {"n": root.tag.split("}")[0].strip("{")}
    meta = root.find("n:metadata", ns)
    def g(tag):
        e = meta.find(f"n:{tag}", ns)
        return None if e is None else (e.text or "")
    def need(tag):
        v = g(tag)
        if not v: errors.append(f"nuspec has no <{tag}>")
        return v

    pid, ver = need("id"), need("version")
    for t in ("authors", "description", "copyright", "projectUrl", "readme", "tags"):
        need(t)
    lic = meta.find("n:license", ns)
    if lic is None or lic.get("type") != "expression" or lic.text != "MIT":
        errors.append("license must be the SPDX expression MIT")
    if (g("licenseUrl") or "").startswith("https://aka.ms/deprecateLicenseUrl"):
        errors.append("deprecated licenseUrl present")
    repo = meta.find("n:repository", ns)
    if repo is None or repo.get("type") != "git" or not repo.get("url", "").startswith("https://"):
        errors.append("repository type/url missing")
    if g("authors") and g("copyright") and g("authors") not in g("copyright"):
        errors.append(f"author '{g('authors')}' does not appear in copyright '{g('copyright')}'")
    deps = [(d.get("id"), d.get("version")) for d in meta.iter(f"{{{ns['n']}}}dependency")]
    if sorted({d[0] for d in deps}) != ["Microsoft.Extensions.Http"]:
        errors.append(f"unexpected dependencies: {deps}")

    for required in ("README.md", "LICENSE.txt"):
        if required not in names: errors.append(f"{required} missing from package")
    dlls = [n for n in names if n.startswith("lib/") and n.endswith(".dll")]
    xmls = [n for n in names if n.startswith("lib/") and n.endswith(".xml")]
    if len(dlls) != 1 or len(xmls) != 1: errors.append(f"expected one dll and one xml doc, found {dlls} {xmls}")
    extra = [n for n in names if re.search(r"\.(pdb|json|cs|csproj|env|pfx|snk|key)$", n, re.I) and not n.startswith("package/")]
    if extra: errors.append(f"unexpected files in package: {extra}")

    # The assembly must carry the package version and the commit (InformationalVersion "<version>+<commit>"). Building without the release
    # version and then packing with it (dotnet pack --no-build -p:Version=...) produces a DLL that disagrees with its own package.
    dll_bytes = z.read(dlls[0]) if len(dlls) == 1 else b""
    if dll_bytes and ver and repo is not None and repo.get("commit"):
        expected_info = f"{ver}+{repo.get('commit')}".encode("utf-16-le")
        if expected_info not in dll_bytes:
            found = re.findall(rb"(?:[0-9]\x00\.\x00)+[0-9]\x00(?:-\x00[0-9A-Za-z.\x00-]*?)?\+\x00(?:[0-9a-f]\x00){7,40}", dll_bytes)
            actual = found[0].decode("utf-16-le") if found else "no informational version"
            errors.append(f"assembly InformationalVersion is '{actual}' but the package is '{ver}+{repo.get('commit')}': build and pack with the same -p:Version")
        else:
            notes.append(f"assembly InformationalVersion {ver}+{repo.get('commit')[:7]}")

    readme = z.read("README.md").decode("utf-8") if "README.md" in names else ""
    links = re.findall(r"\]\(([^)\s]+)\)", readme)
    rel = [l for l in links if not l.startswith(("http://", "https://", "#", "mailto:"))]
    if rel: errors.append(f"README has relative links (broken on nuget.org): {rel}")
    if re.search(r"(?i)(secret|token)\s*[:=]\s*\"[A-Za-z0-9_\-]{20,}\"", readme): errors.append("README seems to contain a credential")

    if xmls:
        doc = ET.fromstring(z.read(xmls[0]))
        members = doc.findall("./members/member")
        if len(members) < 500: errors.append(f"XML docs look incomplete ({len(members)} members)")
        notes.append(f"XML docs: {len(members)} members")

    if sk:
        s = zipfile.ZipFile(sk[0])
        pdbs = [n for n in s.namelist() if n.endswith(".pdb")]
        if not pdbs: errors.append("symbol package has no .pdb")
        else:
            pdb_bytes = s.read(pdbs[0])
            rsds = dll_bytes.find(b"RSDS")
            if rsds < 0:
                errors.append("dll has no CodeView debug record, so the symbol package cannot be matched to it")
            elif dll_bytes[rsds + 4:rsds + 20] not in pdb_bytes:
                errors.append("symbol package does not match the dll: the CodeView debug id of the dll is not in the .pdb")
            else:
                notes.append("pdb matches the dll")
            blob = pdb_bytes.decode("latin-1")
            m = re.search(r'\{"documents":\{[^}]*\}\}', blob)
            if not m: errors.append("pdb has no Source Link document map")
            else:
                notes.append("Source Link: " + m.group(0))
                slug = re.search(r"raw\.githubusercontent\.com/([^/]+/[^/]+)/[0-9a-f]{40}/", m.group(0))
                repo_slug = re.search(r"github\.com/([^/]+/[^/.]+?)(?:\.git)?/?$", repo.get("url", "")) if repo is not None else None
                if slug and repo_slug and slug.group(1).lower() != repo_slug.group(1).lower():
                    errors.append(f"Source Link points at {slug.group(1)} but the nuspec repository is {repo_slug.group(1)}: the local git remote 'origin' is stale (or the repository was renamed); fix the remote or build in CI")
                commit = re.search(r"/([0-9a-f]{40})/\*", m.group(0))
                if repo is not None and commit and commit.group(1) != repo.get("commit"): errors.append("Source Link commit differs from the nuspec commit")
                try:
                    git = subprocess.run(["git", "status", "--porcelain", "--", "src"], capture_output=True, text=True)
                    dirty = git.stdout.strip() if git.returncode == 0 else ""
                    if git.returncode != 0: notes.append("working tree state unknown (not a git checkout)")
                except FileNotFoundError:
                    dirty = ""
                    notes.append("working tree state unknown (git not installed)")
                if dirty and not allow_dirty:
                    errors.append("src/ has uncommitted changes, so the commit embedded for Source Link does not contain the packed sources; commit, then rebuild the package")

    print(f"{pid} {ver}: {len(names)} entries; " + "; ".join(notes))
    for e in errors: print("ERROR:", e)
    sys.exit(1 if errors else 0)

main()
