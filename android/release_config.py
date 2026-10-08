#!/usr/bin/env python3
# SPDX-License-Identifier: GPL-3.0-or-later
"""Validate Android release versions and signing configuration before building."""

import os
import re
import sys
import xml.etree.ElementTree as ET

SIGNING_SECRETS = (
    "ANDROID_KEYSTORE_BASE64",
    "ANDROID_KEYSTORE_PASSWORD",
    "ANDROID_KEY_ALIAS",
    "ANDROID_KEY_PASSWORD",
)


def release_config(ref, requested_version, project_version, secrets):
    prefix = "refs/tags/android-v"
    release = ref.startswith(prefix)
    version = ref[len(prefix):] if release else requested_version or project_version
    if not re.fullmatch(r"(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)", version):
        raise ValueError("Android version must be major.minor.patch, for example android-v0.2.0")
    major, minor, patch = map(int, version.split("."))
    version_code = major * 1_000_000 + minor * 1_000 + patch
    if minor > 999 or patch > 999 or not 0 < version_code <= 2_100_000_000:
        raise ValueError("Version cannot be represented by an Android versionCode")

    missing = [name for name in SIGNING_SECRETS if not secrets.get(name)]
    signed = not missing
    if (release or len(missing) != len(SIGNING_SECRETS)) and missing:
        raise ValueError("Configure these Actions Secrets before signing: " + ", ".join(missing))
    return {
        "version": version,
        "version_code": str(version_code),
        "release": str(release).lower(),
        "signed": str(signed).lower(),
        "tag": "android-v" + version,
        "artifact_name": "meltype-android-arm64-apk",
        "apk_name": f"Meltype-Android-{version}-arm64-v8a.apk",
    }


def main():
    project = ET.parse("src/Meltype.Android/Meltype.Android.csproj")
    project_version = project.findtext(".//ApplicationDisplayVersion")
    try:
        config = release_config(os.environ.get("GITHUB_REF", ""),
                                os.environ.get("REQUESTED_VERSION", ""), project_version, os.environ)
    except ValueError as error:
        print(f"::error::{error}", file=sys.stderr)
        return 1
    with open(os.environ["GITHUB_OUTPUT"], "a", encoding="utf-8") as output:
        for name, value in config.items():
            output.write(f"{name}={value}\n")
    print(f"Android {config['version']} (versionCode {config['version_code']}), "
          f"release signing={config['signed']}, publish release={config['release']}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
