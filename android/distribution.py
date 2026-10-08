#!/usr/bin/env python3
# SPDX-License-Identifier: GPL-3.0-or-later
"""Prepare offline notices and offer the source used for each Android APK."""

import argparse
import hashlib
from html.parser import HTMLParser
import io
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import tarfile
import tempfile
import urllib.request
import xml.etree.ElementTree as ET

PROJECT = Path(__file__).resolve().parents[1]
ASSETS = PROJECT / "src/Meltype.Android/Assets/licenses"
NATIVE_REPOSITORIES = ("abseil-cpp", "protobuf", "re2", "utf8_range", "ja_usage_dict",
                       "zip_code_jigyosyo", "zip_code_ken_all")


class HtmlText(HTMLParser):
    def __init__(self):
        super().__init__(convert_charrefs=True)
        self.parts = []
        self.hidden = 0

    def handle_starttag(self, tag, attrs):
        if tag in ("style", "script"):
            self.hidden += 1
        if tag in ("p", "div", "h1", "h2", "h3", "pre", "br"):
            self.parts.append("\n")

    def handle_endtag(self, tag):
        if tag in ("style", "script"):
            self.hidden -= 1
        if tag in ("p", "div", "h1", "h2", "h3", "pre"):
            self.parts.append("\n")

    def handle_data(self, data):
        if not self.hidden:
            self.parts.append(data)


def repository_source(url, commit):
    match = re.fullmatch(r"https://github.com/([^/]+)/([^/]+?)(?:\.git)?/?", url)
    if not match or not re.fullmatch(r"[0-9a-fA-F]{40}", commit):
        raise ValueError(f"An immutable GitHub source revision is required: {url} {commit}")
    return f"https://codeload.github.com/{match[1]}/{match[2]}/tar.gz/{commit}"


def maven_source(coordinate):
    group, artifact, version = coordinate.split(":")
    if not all(re.fullmatch(r"[A-Za-z0-9_.-]+", part) for part in (group, artifact, version)):
        raise ValueError(f"Invalid Maven coordinate: {coordinate}")
    server = ("https://dl.google.com/dl/android/maven2" if group.startswith("androidx.")
              or group == "com.google.android.material" else "https://repo.maven.apache.org/maven2")
    return f"{server}/{group.replace('.', '/')}/{artifact}/{version}/{artifact}-{version}-sources.jar"


def notice_files(directory):
    return sorted(path for path in directory.rglob("*") if path.is_file()
                  and re.match(r"(?:license|licence|notice|third[-_.]?party[-_.]?notices|copying)(?:[._-]|$)",
                               path.name, re.IGNORECASE)
                  and path.suffix.lower() not in (".dll", ".so", ".jar", ".zip"))


def workload_pack(dotnet_root, identity, major):
    packs = [path for path in (dotnet_root / "packs" / identity).glob("*")
             if path.name.split(".")[0] == str(major)]
    if not packs:
        raise RuntimeError(f"Required workload pack is missing: {identity} {major}")
    return max(packs, key=lambda path: tuple(int(part) for part in path.name.split(".")))


def native_directories(mozc, bazel):
    base = Path(subprocess.check_output([bazel, "info", "output_base"], cwd=mozc / "src", text=True).strip())
    external = base / "external"
    def matches(path, name):
        return re.search(r"(?:^|[+~])" + re.escape(name) + r"(?:[+~]|$)", path.name) is not None
    directories = [path for path in external.iterdir() if path.is_dir()
                   and any(matches(path, name) for name in NATIVE_REPOSITORIES)]
    for required in ("abseil-cpp", "protobuf", "ja_usage_dict", "zip_code_jigyosyo", "zip_code_ken_all"):
        if not any(matches(path, required) for path in directories):
            raise RuntimeError(f"Missing native dependency source: {required}")
    return sorted(directories)


def prepare(mozc, bazel):
    ASSETS.mkdir(parents=True, exist_ok=True)
    sources = []
    notices = ["Meltype for Android\nCopyright (C) 2026 雪代 / Yukishiro and contributors\n"
               "Android modifications: sakusdev/Meltype-android, 2026-10-08.\n"
               "GNU GPL version 3 or later; no warranty. You may modify and redistribute under that license.\n"
               "Source and build instructions: https://github.com/sakusdev/Meltype-android\n"
               "Each Android release includes the matching source archive and dependency manifest.\n"]
    for name in ("LICENSE", "THIRD-PARTY-NOTICES.md", "android/LICENSE-COMPLIANCE.md"):
        path = PROJECT / name
        target = ASSETS / path.name
        shutil.copyfile(path, target)
        notices.append(f"\n\n=== {name} ===\n" + path.read_text(encoding="utf-8"))
    for path in sorted((PROJECT / "android/licenses").glob("*.txt")):
        shutil.copyfile(path, ASSETS / path.name)
        notices.append(f"\n\n=== {path.name} ===\n" + path.read_text(encoding="utf-8"))
    for source, name in ((mozc / "LICENSE", "MOZC-LICENSE.txt"),
                         (mozc / "src/data/installer/credits_en.html", "MOZC-CREDITS.html")):
        shutil.copyfile(source, ASSETS / name)
        content = source.read_text(encoding="utf-8")
        if source.suffix == ".html":
            parser = HtmlText()
            parser.feed(content)
            content = "".join(parser.parts)
        notices.append(f"\n\n=== {name} (upstream notices, including unused desktop components) ===\n" + content)

    assets = json.loads((PROJECT / "src/Meltype.Android/obj/project.assets.json").read_text())
    packages = {name: info["path"] for name, info in assets["libraries"].items() if info["type"] == "package"}
    # Runtime packs are PackageDownload items rather than ordinary references.
    for framework in assets["project"]["frameworks"].values():
        for dependency in framework.get("downloadDependencies", []):
            versions = [part.strip() for part in dependency["version"].strip("[]").split(",")]
            if len(set(versions)) != 1:
                raise RuntimeError(f"Runtime pack version must be exact: {dependency}")
            version = versions[0]
            packages[f"{dependency['name']}/{version}"] = f"{dependency['name'].lower()}/{version.lower()}"
    for identity, relative in sorted(packages.items()):
        directory = next((Path(folder) / relative for folder in assets["packageFolders"]
                          if (Path(folder) / relative).exists()), None)
        if directory is None:
            raise RuntimeError(f"Restored package is missing: {identity}")
        nuspec = next(directory.glob("*.nuspec"))
        metadata = ET.parse(nuspec).getroot()
        def element(name):
            return metadata.find(".//{*}" + name)
        license_info, repository = element("license"), element("repository")
        if license_info is None or repository is None:
            raise RuntimeError(f"License or source metadata is missing: {identity}")
        expression = license_info.text or ""
        if license_info.get("type") == "expression":
            identifiers = set(re.findall(r"[A-Za-z0-9.+-]+", expression)) - {"AND", "OR", "WITH"}
            if not identifiers <= {"MIT", "Apache-2.0", "BSD-2-Clause", "BSD-3-Clause", "Unicode-3.0"}:
                raise RuntimeError(f"Review the license before distributing {identity}: {expression}")
        files = notice_files(directory)
        if not files:
            raise RuntimeError(f"No license text found in {identity}")
        notices.append(f"\n\n=== NuGet {identity}: {expression} ===\n")
        for file in files:
            notices.append(f"\n--- {file.relative_to(directory)} ---\n" + file.read_text(encoding="utf-8-sig"))
        source = repository_source(repository.get("url", ""), repository.get("commit", ""))
        record = {"package": identity, "license": expression, "repository": repository.attrib,
                  "source": source}
        tags = element("tags")
        coordinate = re.search(r"(?:^|\s)artifact_versioned=([^\s]+)", tags.text or "") if tags is not None else None
        if coordinate:
            record["maven"] = coordinate[1]
            record["maven_source"] = maven_source(coordinate[1])
        sources.append(record)
        notices.append("\nSource: " + source + "\n")
        if "maven_source" in record:
            notices.append("Java/Kotlin source: " + record["maven_source"] + "\n")

    # The Android SDK adds Java/native runtime glue to the APK. Preserve its
    # exact NuGet repository revision as well, even though it is a workload pack.
    dotnet_root = Path(os.environ.get("DOTNET_ROOT") or Path(shutil.which("dotnet")).resolve().parent)
    framework = next(iter(assets["project"]["frameworks"]))
    target = re.match(r"net(\d+)\.\d+-android", framework)
    if target is None:
        raise RuntimeError(f"Unsupported Android framework: {framework}")
    # assets.json keeps the alias (net10.0-android) without its inferred API.
    # Ask the same MSBuild SDK resolver used by publish for that API version.
    platform_version = subprocess.check_output(
        ["dotnet", "msbuild", str(PROJECT / "src/Meltype.Android/Meltype.Android.csproj"),
         "-getProperty:TargetPlatformVersion"], text=True).strip()
    sdk_version = workload_pack(dotnet_root, "Microsoft.Android.Sdk.Linux", platform_version.split(".")[0]).name
    sdk_id = "microsoft.android.sdk.linux"
    url = f"https://api.nuget.org/v3-flatcontainer/{sdk_id}/{sdk_version}/{sdk_id}.nuspec"
    with urllib.request.urlopen(url, timeout=30) as response:
        sdk_metadata = ET.fromstring(response.read())
    repository = sdk_metadata.find(".//{*}repository")
    if repository is None:
        raise RuntimeError("Android SDK source revision is missing")
    sources.append({"package": f"{sdk_id}/{sdk_version}", "license": "MIT",
                    "repository": repository.attrib,
                    "source": repository_source(repository.get("url", ""), repository.get("commit", ""))})
    base = repository.get("url", "").removesuffix(".git").replace("https://github.com/", "https://raw.githubusercontent.com/")
    for name in ("LICENSE.TXT", "THIRD-PARTY-NOTICES.TXT"):
        with urllib.request.urlopen(f"{base}/{repository.get('commit')}/{name}", timeout=30) as response:
            notices.append(f"\n\n=== .NET Android SDK {name} ===\n" + response.read().decode("utf-8-sig"))

    # The self-contained Mono runtime can be supplied by workload packs without
    # appearing in project.assets.json's ordinary package references.
    runtime = workload_pack(dotnet_root, "Microsoft.NETCore.App.Runtime.Mono.android-arm64", target[1])
    runtime_id = "microsoft.netcore.app.runtime.mono.android-arm64"
    with urllib.request.urlopen(f"https://api.nuget.org/v3-flatcontainer/{runtime_id}/{runtime.name}/{runtime_id}.nuspec", timeout=30) as response:
        runtime_metadata = ET.fromstring(response.read())
    repository = runtime_metadata.find(".//{*}repository")
    if repository is None:
        raise RuntimeError("Mono runtime source revision is missing")
    sources.append({"package": f"{runtime_id}/{runtime.name}", "license": "MIT",
                    "repository": repository.attrib,
                    "source": repository_source(repository.get("url", ""), repository.get("commit", ""))})
    runtime_notices = notice_files(runtime)
    for file in runtime_notices:
        notices.append(f"\n\n=== Mono runtime {file.name} ===\n" + file.read_text(encoding="utf-8-sig"))
    if not runtime_notices:
        base = repository.get("url", "").removesuffix(".git").replace("https://github.com/", "https://raw.githubusercontent.com/")
        third_party = "THIRD-PARTY-NOTICES.txt" if repository.get("url", "").removesuffix(".git").endswith("/dotnet/dotnet") else "THIRD-PARTY-NOTICES.TXT"
        for name in ("LICENSE.TXT", third_party):
            with urllib.request.urlopen(f"{base}/{repository.get('commit')}/{name}", timeout=30) as response:
                notices.append(f"\n\n=== Mono runtime {name} ===\n" + response.read().decode("utf-8-sig"))

    for directory in native_directories(mozc, bazel):
        for file in notice_files(directory):
            notices.append(f"\n\n=== Native {directory.name}/{file.relative_to(directory)} ===\n"
                           + file.read_text(encoding="utf-8-sig"))
    # Compiler/runtime support from the NDK also retains its upstream notices.
    ndk_notice = mozc / "src/third_party/ndk/android-ndk-r29/NOTICE"
    if not ndk_notice.exists():
        raise RuntimeError("NDK notice file is missing")
    notices.append("\n\n=== Android NDK notices ===\n" + ndk_notice.read_text(encoding="utf-8"))
    (ASSETS / "NOTICE.txt").write_text("\n".join(notices), encoding="utf-8")
    (ASSETS / "DEPENDENCIES.json").write_text(json.dumps(sources, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(f"Prepared offline notices for {len(packages)} NuGet packages and native dependencies")


def add_git_source(archive, directory, prefix):
    data = subprocess.check_output(["git", "archive", "--format=tar", "HEAD"], cwd=directory)
    with tarfile.open(fileobj=io.BytesIO(data)) as git_tar:
        for member in git_tar:
            member.name = f"{prefix}/{member.name}"
            archive.addfile(member, git_tar.extractfile(member) if member.isfile() else None)


def package_source(mozc, bazel, version):
    destination = PROJECT / "dist/android"
    destination.mkdir(parents=True, exist_ok=True)
    output = destination / f"Meltype-Android-{version}-source.tar.gz"
    manifest = json.loads((ASSETS / "DEPENDENCIES.json").read_text())
    with tarfile.open(output, "w:gz") as archive:
        add_git_source(archive, PROJECT, "Meltype")
        add_git_source(archive, mozc, "third-party/mozc")
        # Include the actual modified bridge and Bazel files used in this build.
        for relative in ("src/converter/meltype_mozc_android.cc", "src/converter/BUILD.bazel", "src/android/jni/BUILD.bazel"):
            archive.add(mozc / relative, arcname="third-party/mozc/" + relative)
        for directory in native_directories(mozc, bazel):
            archive.add(directory.resolve(), arcname="third-party/native/" + directory.name)
        archive.add(ASSETS, arcname="distribution/licenses")
        urls = sorted({record[key] for record in manifest for key in ("source", "maven_source") if key in record})
        with tempfile.TemporaryDirectory() as temporary:
            for index, url in enumerate(urls):
                downloaded = Path(temporary) / "source"
                with urllib.request.urlopen(url, timeout=60) as response, downloaded.open("wb") as file:
                    shutil.copyfileobj(response, file)
                extension = "jar" if url.endswith(".jar") else "tar.gz"
                name = f"third-party/managed/{index:03d}-{hashlib.sha256(url.encode()).hexdigest()[:12]}.{extension}"
                archive.add(downloaded, arcname=name)
                record = json.dumps({"url": url, "archive": name}, indent=2).encode()
                info = tarfile.TarInfo(name + ".json")
                info.size = len(record)
                archive.addfile(info, io.BytesIO(record))
                print(f"Archived dependency source {index + 1}/{len(urls)}", flush=True)
    digest = hashlib.sha256(output.read_bytes()).hexdigest()
    output.with_suffix(output.suffix + ".sha256").write_text(f"{digest}  {output.name}\n", encoding="utf-8")
    shutil.copyfile(ASSETS / "DEPENDENCIES.json", destination / "DEPENDENCIES.json")
    shutil.copyfile(ASSETS / "NOTICE.txt", destination / "NOTICE.txt")
    print(f"Packaged {output.name}")


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("mode", choices=("prepare", "source"))
    parser.add_argument("--mozc", type=Path, required=True)
    parser.add_argument("--bazel", required=True)
    parser.add_argument("--version", default="")
    args = parser.parse_args()
    if args.mode == "prepare":
        prepare(args.mozc, args.bazel)
    else:
        if not re.fullmatch(r"\d+\.\d+\.\d+", args.version):
            parser.error("--version must be major.minor.patch")
        package_source(args.mozc, args.bazel, args.version)
