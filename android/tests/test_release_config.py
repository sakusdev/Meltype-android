# SPDX-License-Identifier: GPL-3.0-or-later

import sys
from pathlib import Path
import unittest

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from release_config import SIGNING_SECRETS, release_config


class ReleaseConfigTests(unittest.TestCase):
    def test_tag_uses_its_version_and_requires_fixed_signing(self):
        config = release_config("refs/tags/android-v1.2.3", "9.9.9", "0.2.0",
                                dict.fromkeys(SIGNING_SECRETS, "configured"))
        self.assertEqual(config["version"], "1.2.3")
        self.assertEqual(config["version_code"], "1002003")
        self.assertEqual(config["release"], "true")
        self.assertEqual(config["signed"], "true")

    def test_unsigned_tag_never_releases_a_debug_signed_apk(self):
        with self.assertRaisesRegex(ValueError, "Actions Secrets"):
            release_config("refs/tags/android-v0.2.0", "", "0.2.0", {})

    def test_fork_pr_without_secrets_still_builds_an_artifact(self):
        config = release_config("refs/pull/1/merge", "", "0.2.0", {})
        self.assertEqual(config["signed"], "false")
        self.assertEqual(config["release"], "false")

    def test_configured_branch_builds_share_the_release_key(self):
        config = release_config("refs/heads/work/android-ime", "", "0.2.0",
                                dict.fromkeys(SIGNING_SECRETS, "configured"))
        self.assertEqual(config["signed"], "true")
        self.assertEqual(config["release"], "false")

    def test_partial_signing_configuration_fails_explicitly(self):
        with self.assertRaisesRegex(ValueError, "ANDROID_KEY_ALIAS"):
            release_config("refs/heads/main", "", "0.2.0", {"ANDROID_KEYSTORE_BASE64": "key"})

    def test_invalid_or_overflowing_version_is_rejected(self):
        for version in ["0.0.0", "01.2.3", "1.1000.0", "1.0.1000", "9999.0.0", "0.2.0;echo bad"]:
            with self.subTest(version=version), self.assertRaises(ValueError):
                release_config("refs/tags/android-v" + version, "", "0.2.0",
                               dict.fromkeys(SIGNING_SECRETS, "configured"))


if __name__ == "__main__":
    unittest.main()
