# SPDX-License-Identifier: GPL-3.0-or-later

import sys
from pathlib import Path
import unittest

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from package_apk import verify_release_certificate


class ReleaseCertificateTests(unittest.TestCase):
    # Actual build-tools output from the Android CI build.
    digest = "3e88dd782a945d781875704dcc98d9867a6b10a7e32677bc71aafe9a6f26333a"

    def test_modern_apksigner_output(self):
        output = ("Verified using v2 scheme (APK Signature Scheme v2): true\n"
                  "Verified using v3 scheme (APK Signature Scheme v3): true\n"
                  f"V3.0 Signer: certificate SHA-256 digest: {self.digest}\n")
        verify_release_certificate(output, self.digest)

    def test_legacy_apksigner_output(self):
        verify_release_certificate(f"Signer #1 certificate SHA-256 digest: {self.digest.upper()}\n",
                                   self.digest)

    def test_same_key_across_schemes_and_source_stamp(self):
        output = (f"V2 Signer: certificate SHA-256 digest: {self.digest}\n"
                  f"V3.0 Signer: certificate SHA-256 digest: {self.digest}\n"
                  f"Source Stamp Signer certificate SHA-256 digest: {'a' * 64}\n")
        verify_release_certificate(output, self.digest)

    def test_wrong_release_key_is_rejected(self):
        with self.assertRaisesRegex(RuntimeError, "release keystore"):
            verify_release_certificate(f"V3.0 Signer: certificate SHA-256 digest: {self.digest}\n",
                                       "a" * 64)

    def test_additional_signing_key_is_rejected(self):
        output = (f"Signer #1 certificate SHA-256 digest: {self.digest}\n"
                  f"Signer #2 certificate SHA-256 digest: {'a' * 64}\n")
        with self.assertRaisesRegex(RuntimeError, "release keystore"):
            verify_release_certificate(output, self.digest)

    def test_missing_or_source_stamp_only_certificate_is_rejected(self):
        for output in ["Verified\n", f"Source Stamp Signer certificate SHA-256 digest: {self.digest}\n"]:
            with self.subTest(output=output), self.assertRaisesRegex(RuntimeError, "release keystore"):
                verify_release_certificate(output, self.digest)


if __name__ == "__main__":
    unittest.main()
