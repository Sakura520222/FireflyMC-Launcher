#!/usr/bin/env python3
"""FireflyMC Launcher 发布包 Ed25519 签名工具。

用法：
  python tools/sign_release.py <zip路径> [--key <私钥.pem>]

环境变量：
  FIREFLYMC_ED25519_KEY  私钥路径（覆盖 --key 默认值）

输出：
  <zip>.sig（原始签名字节），并打印 base64 签名（可贴入 Release 说明）。
"""
import os
import sys
import base64
import argparse

from cryptography.hazmat.primitives import serialization

DEFAULT_KEY = os.path.expanduser("~/.fireflymc/keys/fireflymc-ed25519-private.pem")


def sign(zip_path: str, key_path: str) -> bytes:
    with open(key_path, "rb") as f:
        priv = serialization.load_pem_private_key(f.read(), password=None)
    with open(zip_path, "rb") as f:
        data = f.read()
    sig = priv.sign(data)

    # 自检：用对应公钥验证一次，确保密钥/签名/数据一致
    priv.public_key().verify(sig, data)

    sig_path = zip_path + ".sig"
    with open(sig_path, "wb") as f:
        f.write(sig)
    return sig


def main() -> int:
    p = argparse.ArgumentParser(description="Ed25519 签名 FireflyMC 发布包")
    p.add_argument("zip", help="发布包 ZIP 路径")
    p.add_argument(
        "--key",
        default=os.environ.get("FIREFLYMC_ED25519_KEY", DEFAULT_KEY),
        help=f"私钥 PEM 路径（默认 {DEFAULT_KEY}，或环境变量 FIREFLYMC_ED25519_KEY）",
    )
    args = p.parse_args()

    if not os.path.isfile(args.key):
        print(f"错误：私钥不存在: {args.key}", file=sys.stderr)
        print("提示：私钥应离线保管于仓库外，如 ~/.fireflymc/keys/", file=sys.stderr)
        return 1
    if not os.path.isfile(args.zip):
        print(f"错误：发布包不存在: {args.zip}", file=sys.stderr)
        return 1

    sig = sign(args.zip, args.key)
    print(f"已签名: {args.zip}.sig")
    print(f"签名 base64（用于 Release 校验）:\n{base64.b64encode(sig).decode()}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
