# SPDX-License-Identifier: GPL-3.0-or-later

import sys
from pathlib import Path
import unittest

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from release_config import SIGNING_SECRETS, release_config


class ReleaseConfigTests(unittest.TestCase):
    def test_configured_tag_uses_its_version_and_release_signing(self):
        config = release_config("refs/tags/android-v1.2.3", "9.9.9", "0.2.0",
                                dict.fromkeys(SIGNING_SECRETS, "configured"))
        self.assertEqual(config["version"], "1.2.3")
        self.assertEqual(config["version_code"], "1002003")
        self.assertEqual(config["release"], "true")
        self.assertEqual(config["release_signed"], "true")
        self.assertEqual(config["signing_mode"], "release")
        self.assertEqual(config["apk_name"], "Meltype-Android-1.2.3-arm64-v8a.apk")

    def test_tag_without_secrets_publishes_a_development_signed_apk(self):
        config = release_config("refs/tags/android-v0.2.0", "", "0.2.0", {})
        self.assertEqual(config["release"], "true")
        self.assertEqual(config["release_signed"], "false")
        self.assertEqual(config["signing_mode"], "development")
        self.assertEqual(config["apk_name"], "Meltype-Android-0.2.0-arm64-v8a-dev-signed.apk")
        self.assertIn("uninstalling", config["signing_notice"])

    def test_fork_pr_without_secrets_still_builds_an_artifact(self):
        config = release_config("refs/pull/1/merge", "", "0.2.0", {})
        self.assertEqual(config["release_signed"], "false")
        self.assertEqual(config["release"], "false")

    def test_configured_branch_builds_share_the_release_key(self):
        config = release_config("refs/heads/work/android-ime", "", "0.2.0",
                                dict.fromkeys(SIGNING_SECRETS, "configured"))
        self.assertEqual(config["release_signed"], "true")
        self.assertEqual(config["release"], "false")

    def test_partial_signing_configuration_fails_explicitly(self):
        with self.assertRaisesRegex(ValueError, "ANDROID_KEY_ALIAS"):
            release_config("refs/heads/main", "", "0.2.0", {"ANDROID_KEYSTORE_BASE64": "key"})

    def test_partial_tag_configuration_does_not_silently_switch_keys(self):
        for missing in SIGNING_SECRETS:
            secrets = dict.fromkeys(SIGNING_SECRETS, "configured")
            del secrets[missing]
            with self.subTest(missing=missing), self.assertRaisesRegex(ValueError, missing):
                release_config("refs/tags/android-v0.2.0", "", "0.2.0", secrets)

    def test_invalid_or_overflowing_version_is_rejected(self):
        for version in ["0.0.0", "01.2.3", "1.1000.0", "1.0.1000", "9999.0.0", "0.2.0;echo bad"]:
            with self.subTest(version=version), self.assertRaises(ValueError):
                release_config("refs/tags/android-v" + version, "", "0.2.0",
                               dict.fromkeys(SIGNING_SECRETS, "configured"))


if __name__ == "__main__":
    unittest.main()
