# SPDX-License-Identifier: GPL-3.0-or-later

import io
from pathlib import Path
import subprocess
import sys
import tarfile
import tempfile
import unittest
from unittest.mock import patch

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from distribution import HtmlText, add_git_source, maven_source, native_directories, repository_source


class DistributionTests(unittest.TestCase):
    def test_repository_sources_use_an_immutable_commit(self):
        sha = "a" * 40
        self.assertEqual(repository_source("https://github.com/dotnet/android-libraries.git", sha),
                         f"https://codeload.github.com/dotnet/android-libraries/tar.gz/{sha}")
        for url, revision in [("https://github.com/dotnet/android", "main"),
                              ("https://example.com/dotnet/android", sha)]:
            with self.assertRaises(ValueError):
                repository_source(url, revision)

    def test_maven_sources_select_the_correct_upstream_repository(self):
        self.assertEqual(maven_source("com.google.android.material:material:1.14.0"),
                         "https://dl.google.com/dl/android/maven2/com/google/android/material/material/1.14.0/material-1.14.0-sources.jar")
        self.assertEqual(maven_source("org.jetbrains.kotlin:kotlin-stdlib:2.2.0"),
                         "https://repo.maven.apache.org/maven2/org/jetbrains/kotlin/kotlin-stdlib/2.2.0/kotlin-stdlib-2.2.0-sources.jar")
        with self.assertRaises(ValueError):
            maven_source("com.example:../../private:1.0")

    def test_notice_text_preserves_preformatted_legal_terms(self):
        parser = HtmlText()
        parser.feed("<style>hidden</style><h3>Terms</h3><pre>Copyright &amp; notice\nNo warranty.</pre>")
        text = "".join(parser.parts)
        self.assertIn("Copyright & notice\nNo warranty.", text)
        self.assertNotIn("hidden", text)

    def test_native_source_collection_handles_bazel_canonical_names(self):
        with tempfile.TemporaryDirectory() as temporary:
            base = Path(temporary)
            external = base / "external"
            names = ["abseil-cpp+", "protobuf+", "+_repo_rules+ja_usage_dict",
                     "+_repo_rules+zip_code_jigyosyo", "+_repo_rules+zip_code_ken_all", "androidndk"]
            for name in names:
                (external / name).mkdir(parents=True)
            with patch("distribution.subprocess.check_output", return_value=str(base) + "\n"):
                result = native_directories(base, "bazel")
            self.assertEqual(len(result), 5)
            self.assertNotIn(external / "androidndk", result)
            (external / "protobuf+").rmdir()
            with patch("distribution.subprocess.check_output", return_value=str(base) + "\n"):
                with self.assertRaisesRegex(RuntimeError, "protobuf"):
                    native_directories(base, "bazel")

    def test_project_source_archive_excludes_untracked_keys_and_build_outputs(self):
        with tempfile.TemporaryDirectory() as temporary:
            source = Path(temporary)
            (source / "LICENSE").write_text("GPL notice")
            (source / "app.cs").write_text("source")
            (source / "release.jks").write_text("private")
            subprocess.run(["git", "init", "-q", str(source)], check=True)
            subprocess.run(["git", "add", "LICENSE", "app.cs"], cwd=source, check=True)
            subprocess.run(["git", "-c", "user.name=Distribution Test", "-c", "user.email=test@example.com",
                            "commit", "-qm", "fixture"], cwd=source, check=True)
            (source / "app.apk").write_text("binary")
            buffer = io.BytesIO()
            with tarfile.open(fileobj=buffer, mode="w") as archive:
                add_git_source(archive, source, "Meltype")
            buffer.seek(0)
            with tarfile.open(fileobj=buffer) as archive:
                self.assertEqual(set(archive.getnames()), {"Meltype/LICENSE", "Meltype/app.cs"})


if __name__ == "__main__":
    unittest.main()
