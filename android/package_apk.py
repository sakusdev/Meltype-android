#!/usr/bin/env python3
# SPDX-License-Identifier: GPL-3.0-or-later
"""Select the signed APK, verify it and the release certificate, then package it."""

import hashlib
import os
from pathlib import Path
import re
import shutil
import subprocess
import tempfile


def verify_release_certificate(verified, expected):
    # SDK versions print either "Signer #1" or "V3.0 Signer:". Multiple
    # schemes can repeat the same certificate; source stamps are not APK signers.
    certificates = set(re.findall(
        r"^(?:Signer(?: #\d+| \(.*\))?|V\d+(?:\.\d+)? Signer(?: #\d+)?(?: \(.*\))?):? "
        r"certificate SHA-256 digest: ([0-9a-fA-F]{64})$", verified, re.MULTILINE))
    if {digest.lower() for digest in certificates} != {expected.lower()}:
        raise RuntimeError("APK certificate does not match the configured release keystore")


def main():
    publish = Path("src/Meltype.Android/bin/Release/net10.0-android/android-arm64/publish")
    apks = list(publish.glob("*-Signed.apk"))
    if len(apks) != 1:
        raise RuntimeError(f"Expected exactly one signed APK in {publish}; found {len(apks)}")
    sdk = Path(os.environ.get("ANDROID_HOME") or os.environ["ANDROID_SDK_ROOT"])
    signers = list((sdk / "build-tools").glob("*/apksigner"))
    if not signers:
        raise RuntimeError("Android SDK apksigner was not found")
    signer = max(signers, key=lambda p: tuple(int(n) for n in p.parent.name.split(".") if n.isdigit()))
    verified = subprocess.check_output([str(signer), "verify", "--verbose", "--print-certs", str(apks[0])], text=True)
    print(verified)

    if os.environ["RELEASE_SIGNED"] == "true":
        with tempfile.TemporaryDirectory(dir=os.environ["RUNNER_TEMP"]) as temporary:
            certificate = Path(temporary) / "signer.der"
            subprocess.run(["keytool", "-exportcert", "-keystore", os.environ["SIGNING_KEYSTORE"],
                            "-alias", os.environ["ANDROID_KEY_ALIAS"],
                            "-storepass:env", "ANDROID_KEYSTORE_PASSWORD", "-file", str(certificate)], check=True)
            expected = hashlib.sha256(certificate.read_bytes()).hexdigest()
        verify_release_certificate(verified, expected)

    destination = Path("dist/android")
    destination.mkdir(parents=True, exist_ok=True)
    apk = destination / os.environ["APK_NAME"]
    shutil.copyfile(apks[0], apk)
    digest = hashlib.sha256(apk.read_bytes()).hexdigest()
    apk.with_suffix(".apk.sha256").write_text(f"{digest}  {apk.name}\n", encoding="utf-8")
    print(f"Packaged {apk.name}")


if __name__ == "__main__":
    main()
