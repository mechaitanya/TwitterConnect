#!/usr/bin/env python3
"""Proves tools/verify_package.py actually fails on bad packages (a checker that never fails is worthless).

Usage: python3 tools/test_verify_package.py <directory holding a good .nupkg and .snupkg>
Each case copies the good package, breaks one thing, and expects verify_package.py to exit 1 with the matching message.
"""
import glob, os, re, shutil, subprocess, sys, tempfile, unittest, zipfile

HERE = os.path.dirname(os.path.abspath(__file__))
GOOD = os.path.abspath(sys.argv.pop(1)) if len(sys.argv) > 1 else sys.exit(__doc__)

def rewrite(src, dst, edit):
    """Copies a zip, passing (name, bytes) through edit; edit returns new bytes, or None to drop the entry."""
    with zipfile.ZipFile(src) as zin, zipfile.ZipFile(dst, "w", zipfile.ZIP_DEFLATED) as zout:
        for item in zin.infolist():
            data = edit(item.filename, zin.read(item.filename))
            if data is not None:
                zout.writestr(item.filename, data)

class VerifyPackage(unittest.TestCase):
    def run_case(self, nupkg_edit=None, snupkg_edit=None, drop_snupkg=False):
        with tempfile.TemporaryDirectory() as tmp:
            nupkg = glob.glob(os.path.join(GOOD, "*.nupkg"))[0]
            snupkg = glob.glob(os.path.join(GOOD, "*.snupkg"))[0]
            rewrite(nupkg, os.path.join(tmp, os.path.basename(nupkg)), nupkg_edit or (lambda n, d: d))
            if not drop_snupkg:
                rewrite(snupkg, os.path.join(tmp, os.path.basename(snupkg)), snupkg_edit or (lambda n, d: d))
            p = subprocess.run([sys.executable, os.path.join(HERE, "verify_package.py"), tmp, "--allow-dirty-commit"], capture_output=True, text=True)
            return p.returncode, p.stdout

    def nuspec(self, fn):
        return lambda n, d: fn(d.decode("utf-8-sig")).encode("utf-8") if n.endswith(".nuspec") else d

    def assertFails(self, result, message):
        code, out = result
        self.assertEqual(code, 1, out)
        self.assertIn(message, out)

    def test_the_good_package_passes(self):
        code, out = self.run_case()
        self.assertEqual(code, 0, out)

    def test_relative_readme_link(self):
        self.assertFails(self.run_case(lambda n, d: d + b"\n[x](docs/a.md)\n" if n == "README.md" else d), "relative links")

    def test_missing_readme(self):
        self.assertFails(self.run_case(lambda n, d: None if n == "README.md" else d), "README.md missing")

    def test_missing_license_file(self):
        self.assertFails(self.run_case(lambda n, d: None if n == "LICENSE.txt" else d), "LICENSE.txt missing")

    def test_extra_dependency(self):
        edit = self.nuspec(lambda s: s.replace("</dependencies>", '<group targetFramework="net11.0"><dependency id="Newtonsoft.Json" version="13.0.3" /></group></dependencies>'))
        self.assertFails(self.run_case(edit), "unexpected dependencies")

    def test_deprecated_license_url(self):
        edit = self.nuspec(lambda s: s.replace("</metadata>", "<licenseUrl>https://aka.ms/deprecateLicenseUrl</licenseUrl></metadata>") if "<licenseUrl>" not in s
                           else re.sub(r"<licenseUrl>.*?</licenseUrl>", "<licenseUrl>https://aka.ms/deprecateLicenseUrl</licenseUrl>", s))
        self.assertFails(self.run_case(edit), "deprecated licenseUrl")

    def test_license_not_mit_expression(self):
        self.assertFails(self.run_case(self.nuspec(lambda s: s.replace(">MIT<", ">Apache-2.0<"))), "license must be")

    def test_author_not_in_copyright(self):
        self.assertFails(self.run_case(self.nuspec(lambda s: re.sub(r"<authors>.*?</authors>", "<authors>Someone Else</authors>", s))), "does not appear in copyright")

    def test_missing_repository(self):
        self.assertFails(self.run_case(self.nuspec(lambda s: re.sub(r"<repository [^>]*/>", "", s))), "repository type/url missing")

    def test_missing_project_url(self):
        self.assertFails(self.run_case(self.nuspec(lambda s: re.sub(r"<projectUrl>.*?</projectUrl>", "", s))), "no <projectUrl>")

    def test_missing_xml_docs(self):
        self.assertFails(self.run_case(lambda n, d: None if n.endswith(".xml") and n.startswith("lib/") else d), "expected one dll and one xml doc")

    def test_stray_file(self):
        def edit(n, d): return d
        with tempfile.TemporaryDirectory() as tmp:
            nupkg = glob.glob(os.path.join(GOOD, "*.nupkg"))[0]
            dst = os.path.join(tmp, os.path.basename(nupkg))
            rewrite(nupkg, dst, edit)
            with zipfile.ZipFile(dst, "a") as z:
                z.writestr("appsettings.secrets.json", "{}")
            shutil.copy(glob.glob(os.path.join(GOOD, "*.snupkg"))[0], tmp)
            p = subprocess.run([sys.executable, os.path.join(HERE, "verify_package.py"), tmp, "--allow-dirty-commit"], capture_output=True, text=True)
        self.assertEqual(p.returncode, 1, p.stdout)
        self.assertIn("unexpected files", p.stdout)

    def test_missing_symbol_package(self):
        self.assertFails(self.run_case(drop_snupkg=True), "expected exactly one .snupkg")

    def test_symbols_without_source_link(self):
        self.assertFails(self.run_case(snupkg_edit=lambda n, d: d.replace(b'{"documents":', b'{"docXments":') if n.endswith(".pdb") else d), "Source Link")

    def test_source_link_commit_must_match_the_nuspec(self):
        def edit(n, d):
            return re.sub(rb"/[0-9a-f]{40}/\*", b"/" + b"a" * 40 + b"/*", d) if n.endswith(".pdb") else d
        self.assertFails(self.run_case(snupkg_edit=edit), "commit differs")

    def test_source_link_repository_must_match_the_nuspec_repository(self):
        def edit(n, d):
            return re.sub(rb"raw\.githubusercontent\.com/[^/]+/[^/]+/", b"raw.githubusercontent.com/someone/else/", d) if n.endswith(".pdb") else d
        self.assertFails(self.run_case(snupkg_edit=edit), "Source Link points at")

    def test_credential_in_readme(self):
        self.assertFails(self.run_case(lambda n, d: d + b'\nsecret = "abcdefghijklmnopqrstuvwxyz123456"\n' if n == "README.md" else d), "credential")

if __name__ == "__main__":
    unittest.main(argv=[sys.argv[0], "-v"])
