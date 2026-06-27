#!/usr/bin/env python3
"""FireflyMC Launcher 打包脚本：publish → manifest → zip → sign。

流程：
  1. dotnet publish Launcher（self-contained 目录，玩家免装运行时）
  2. dotnet publish Updater（single-file self-contained，独立进程不依赖 Launcher 运行时）
  3. 合并 Updater.exe 到 Launcher 发布目录
  4. 生成 package-manifest.json（Updater 解压后逐文件 SHA-256 校验用，见 Program.cs:107）
  5. 打 ZIP（含 manifest）
  6. 调 sign_release.py 签名

用法：
  python tools/package_release.py [--version 1.0.0]
  python tools/package_release.py --skip-publish            # 用已有 staging/launcher
  python tools/package_release.py --no-sign                  # 跳过签名
"""
import os
import sys
import json
import shutil
import argparse
import subprocess
import zipfile
import hashlib
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
DEFAULT_STAGING = ROOT / "artifacts" / "publish"
DEFAULT_OUTPUT = ROOT / "artifacts" / "dist"
DEFAULT_KEY = os.path.expanduser("~/.fireflymc/keys/fireflymc-ed25519-private.pem")
LAUNCHER_CSPROJ = ROOT / "src" / "FireflyMC.Launcher" / "FireflyMC.Launcher.csproj"
UPDATER_CSPROJ = ROOT / "src" / "FireflyMC.Updater" / "FireflyMC.Updater.csproj"


def run(cmd):
    print(f"$ {' '.join(str(c) for c in cmd)}")
    subprocess.run([str(c) for c in cmd], check=True)


def publish(staging: Path) -> Path:
    launcher_out = staging / "launcher"
    updater_out = staging / "updater"
    for d in (launcher_out, updater_out):
        if d.exists():
            shutil.rmtree(d)
        d.mkdir(parents=True)

    # Launcher：self-contained 目录（含运行时）
    run(["dotnet", "publish", LAUNCHER_CSPROJ,
         "-c", "Release", "-r", "win-x64", "--self-contained", "true",
         "-o", launcher_out])
    # Updater：single-file self-contained（独立 exe，自更新时不受 Launcher 退出影响）
    run(["dotnet", "publish", UPDATER_CSPROJ,
         "-c", "Release", "-r", "win-x64", "--self-contained", "true",
         "-p:PublishSingleFile=true", "-o", updater_out])

    updater_exe = updater_out / "FireflyMC.Updater.exe"
    if not updater_exe.exists():
        raise FileNotFoundError(f"Updater.exe 未生成于 {updater_out}")
    shutil.copy2(updater_exe, launcher_out / "FireflyMC.Updater.exe")
    return launcher_out


def generate_manifest(package_dir: Path):
    """生成 package-manifest.json 的 files 列表（path + sha256 小写 hex）。"""
    files = []
    for p in sorted(package_dir.rglob("*")):
        if not p.is_file():
            continue
        rel = p.relative_to(package_dir).as_posix()
        if rel == "package-manifest.json":
            continue
        sha = hashlib.sha256(p.read_bytes()).hexdigest()
        files.append({"path": rel, "sha256": sha})
    return files


def make_zip(package_dir: Path, manifest_files, zip_path: Path) -> int:
    (package_dir / "package-manifest.json").write_text(
        json.dumps({"files": manifest_files}, indent=2), encoding="utf-8")
    if zip_path.exists():
        zip_path.unlink()
    count = 0
    with zipfile.ZipFile(zip_path, "w", zipfile.ZIP_DEFLATED) as zf:
        for p in package_dir.rglob("*"):
            if p.is_file():
                zf.write(p, p.relative_to(package_dir).as_posix())
                count += 1
    return count


def main() -> int:
    ap = argparse.ArgumentParser(description="打包 FireflyMC Launcher 发布包")
    ap.add_argument("--version", default="1.0.0", help="版本号（zip 命名）")
    ap.add_argument("--output", default=str(DEFAULT_OUTPUT), help="zip 输出目录")
    ap.add_argument("--skip-publish", action="store_true", help="跳过 dotnet publish，用已有 staging/launcher")
    ap.add_argument("--staging", default=str(DEFAULT_STAGING), help="发布暂存目录")
    ap.add_argument("--key", default=os.environ.get("FIREFLYMC_ED25519_KEY", DEFAULT_KEY),
                    help="Ed25519 私钥路径")
    ap.add_argument("--no-sign", action="store_true", help="跳过签名")
    args = ap.parse_args()

    staging = Path(args.staging)
    output = Path(args.output)
    output.mkdir(parents=True, exist_ok=True)

    if args.skip_publish:
        package_dir = staging / "launcher"
        if not package_dir.exists():
            print(f"错误：{package_dir} 不存在（需先 publish 或去掉 --skip-publish）", file=sys.stderr)
            return 1
    else:
        print("=== dotnet publish ===")
        package_dir = publish(staging)

    zip_path = output / f"FireflyMC-Launcher-{args.version}-win-x64.zip"

    print("\n=== 生成 package-manifest.json ===")
    manifest_files = generate_manifest(package_dir)
    print(f"  {len(manifest_files)} 个文件")

    print(f"\n=== 打包 {zip_path.name} ===")
    count = make_zip(package_dir, manifest_files, zip_path)
    size_mb = zip_path.stat().st_size // 1024 // 1024
    print(f"  {count} 个条目，约 {size_mb} MB")

    if not args.no_sign:
        key = Path(args.key)
        if not key.exists():
            print(f"\n警告：私钥不存在 {key}，跳过签名", file=sys.stderr)
        else:
            print("\n=== 签名 ===")
            run([sys.executable, str(ROOT / "tools" / "sign_release.py"),
                 str(zip_path), "--key", str(key)])

    print(f"\n完成：{zip_path}")
    if zip_path.with_suffix(".zip.sig").exists():
        print(f"签名：{zip_path}.sig")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
