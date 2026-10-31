"""Package an already-tested Actions artifact; never rebuild the release locally."""
from pathlib import Path
import hashlib
import io
import json
import shutil
import re
import subprocess
import sys
import zipfile
from xml.etree import ElementTree as ET


def prepare(source: Path, output: Path, sha: str, run_id: str, version: str) -> None:
    if not re.fullmatch(r"[a-f0-9]{40}", sha) or not re.fullmatch(r"[0-9]+", run_id) or not re.fullmatch(r"v[0-9]+\.[0-9]+\.[0-9]+", version):
        raise ValueError("Invalid release identity")
    output.mkdir(parents=True, exist_ok=True)
    desktop = source / "artifacts/GuideDeck-windows-x64.zip"
    entries = (source / "artifacts/SHA256SUMS.txt").read_text().splitlines()
    matches = [line.split()[0] for line in entries if re.fullmatch(r"[a-fA-F0-9]{64}\s+\*?GuideDeck-windows-x64\.zip", line)]
    data = desktop.read_bytes()
    if len(matches) != 1 or hashlib.sha256(data).hexdigest() != matches[0].lower():
        raise ValueError("Tested desktop archive checksum mismatch")
    (output / desktop.name).write_bytes(data)
    # Native installer and updater feed come only from this same verified build.
    installer = source / "artifacts/installer"
    setup = list(installer.glob("*Setup.exe"))
    feed_path = installer / "releases.win-x64.json"
    if len(setup) != 1 or not feed_path.is_file():
        raise ValueError("Expected one tested installer and its win-x64 update feed")
    if setup[0].read_bytes()[:2] != b"MZ":
        raise ValueError("Invalid installer")
    shutil.copyfile(setup[0], output / "GuideDeck-Setup.exe")
    feed = json.loads(feed_path.read_text(encoding="utf-8-sig"))
    assets = feed.get("Assets", [])
    if not assets:
        raise ValueError("Empty updater feed")
    update_names = ["GuideDeck-Setup.exe", feed_path.name]
    for asset in assets:
        name = asset["FileName"]
        if Path(name).name != name or not re.fullmatch(r"GuideDeck\.Desktop-.*-full\.nupkg", name):
            raise ValueError("Unexpected update package path")
        if asset["Version"] != version[1:] or asset["PackageId"] != "GuideDeck.Desktop":
            raise ValueError("Update feed identity/version mismatch")
        payload = (installer / name).read_bytes()
        if len(payload) != asset["Size"] or hashlib.sha256(payload).hexdigest().lower() != asset["SHA256"].lower():
            raise ValueError("Tested update package checksum mismatch")
        (output / name).write_bytes(payload)
        update_names.append(name)
    shutil.copyfile(feed_path, output / feed_path.name)
    packages = [p for p in source.rglob("*") if p.suffix.lower() in (".msix", ".appx") and "dependencies" not in str(p).lower()]
    if len(packages) != 1:
        raise ValueError("Expected one Game Bar package")
    package = packages[0]
    with zipfile.ZipFile(package) as archive:
        manifest = ET.fromstring(archive.read('AppxManifest.xml'))
        identity = manifest.find('{http://schemas.microsoft.com/appx/manifest/foundation/windows10}Identity')
        if identity is None or '.'.join(identity.attrib['Version'].split('.')[:3]) != version[1:]:
            raise ValueError('Release version does not match the tested package')
        executable = archive.read("Companion/GuideDeck.exe")
        if executable[:2] != b"MZ":
            raise ValueError("Invalid companion executable")
        (output / "GuideDeck.exe").write_bytes(executable)
    with zipfile.ZipFile(output / "GuideDeck-GameBar-development.zip", "w", zipfile.ZIP_DEFLATED) as archive:
        archive.write(package, package.name)
        for dependency in (package.parent / "Dependencies/x64").glob("*.appx"):
            archive.write(dependency, "Dependencies/x64/" + dependency.name)
        archive.writestr("README.txt", "Unsigned Game Bar development MSIX, not a normal end-user installer.\nUse Visual Studio developer deployment or your own approved signing process.\nNo certificate or private key is included. Hardware/game behavior is unverified.\n")
    with zipfile.ZipFile(output / "GuideDeck-icons.zip", "w", zipfile.ZIP_DEFLATED) as archive:
        for source_name, target_name in (
            ("assets/branding/GuideDeck.svg", "GuideDeck.svg"),
            ("assets/branding/GuideDeck.png", "GuideDeck.png"),
            ("assets/branding/BRANDING.txt", "BRANDING.txt"),
            ("src/GuideDeck.Desktop/Assets/GuideDeck.ico", "GuideDeck.ico"),
            ("LICENSE", "LICENSE"),
        ):
            content = subprocess.run(["git", "show", f"{sha}:{source_name}"], check=True, capture_output=True).stdout
            archive.writestr(target_name, content)
    notices = []
    with zipfile.ZipFile(io.BytesIO(data)) as archive:
        for name in sorted(archive.namelist()):
            if "license" in name.lower() or "notice" in name.lower():
                notices.append(name + "\n" + archive.read(name).decode("utf-8-sig", errors="replace"))
    (output / "GuideDeck-LICENSES.txt").write_text("\n\n".join(notices))
    for script in ("Install-GuideDeck.ps1", "Uninstall-GuideDeck.ps1"):
        # Scripts must come from the tested commit, not the controller workflow commit.
        content = subprocess.run(["git", "show", f"{sha}:scripts/{script}"], check=True, capture_output=True).stdout
        (output / script).write_bytes(content)
    changes = subprocess.run(['git', 'show', f'{sha}:docs/releases/{version}.txt'], capture_output=True)
    features = changes.stdout.decode('utf-8') if changes.returncode == 0 else 'mapping 局部備份／還原、設定漂移檢查、驗收歷史與匯出、Game Bar 同套件管理器入口與狀態摘要。'
    names = ("GuideDeck.exe", "GuideDeck-windows-x64.zip", "GuideDeck-GameBar-development.zip", "Install-GuideDeck.ps1", "Uninstall-GuideDeck.ps1", "GuideDeck-LICENSES.txt", "GuideDeck-icons.zip")
    (output / "SHA256SUMS.txt").write_text("\n".join(hashlib.sha256((output / name).read_bytes()).hexdigest() + "  " + name for name in (*names, *update_names)) + "\n")
    (output / "NOTES.txt").write_text(f"""GuideDeck {version} 實驗版

保留 Windows 原生 Xbox 按鈕 → Game Bar，研究移除 Steam 同時響應 Guide 的行為。

下載：
- **GuideDeck-Setup.exe：建議下載，正式安裝流程、Windows 設定解除安裝、app 內檢查／下載／重啟更新。**\n- GuideDeck.exe：選用單檔免安裝版，沒有安裝版更新能力。
- GuideDeck-windows-x64.zip：較小的免安裝壓縮包，內含 .NET runtime。
- Install-GuideDeck.ps1／Uninstall-GuideDeck.ps1：目前使用者的安裝／移除腳本。
- SHA256SUMS.txt／GuideDeck-LICENSES.txt：校驗與授權聲明。
- GuideDeck-icons.zip：手把圖標的 SVG／PNG／ICO 原始素材。
- GuideDeck-GameBar-development.zip：未簽章開發 MSIX 與 x64 相依套件，不是一般使用者安裝包。

建議直接執行 GuideDeck-Setup.exe。舊版首次需手動安裝一次，之後在 app「更新與安裝」操作。\n舊腳本仍可安裝免安裝 ZIP：.\\Install-GuideDeck.ps1 -Version {version}

{features}
不修改 Game Bar、Steam Input 或整體 Overlay 的啟用開關。

驗證：核心回歸測試、Windows 桌面與 UWP 建置、一般／單檔版啟動與單一實例、安裝／更新／校驗拒絕／移除。
產物來自已成功的 CI：https://github.com/shenlee377/xbox-buttom-changer/actions/runs/{run_id}
測試來源 commit：{sha}

解除安裝會清除安裝版程式、更新套件、捷徑及本機 GuideDeck 資料（含備份／觀察）。請先還原 Steam 修改或匯出備份。\n自行匯出、舊免安裝副本、另行部署的 widget 與 Windows 系統記錄另行管理。\n\n未簽章的開發測試版；Game Bar widget 需開發部署。尚未驗證實體手把、遊戲或反作弊相容性。
這是 Steam Guide 設定管理與進階 mapping 工具，沒有全系統攔截驅動，效果仍需實機驗收。
""")


if __name__ == "__main__":
    prepare(Path(sys.argv[1]), Path(sys.argv[2]), sys.argv[3], sys.argv[4], sys.argv[5])
