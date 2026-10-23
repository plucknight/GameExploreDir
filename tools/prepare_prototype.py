"""将权威设计数据转换为 Unity JsonUtility 可读形状；不改设计源数据。"""
from pathlib import Path
import argparse
import copy
import hashlib
import json

ROOT = Path(__file__).resolve().parents[1]
DEST = ROOT / "prototypes" / "NormalCampaign" / "Assets" / "Resources"


def outputs():
    source = (ROOT / "data" / "normal-campaign.json").read_bytes()
    campaign = copy.deepcopy(json.loads(source.decode("utf-8-sig")))
    campaign["source_sha256"] = hashlib.sha256(source).hexdigest()
    # JsonUtility 不使用 jagged array；领域数据仍保留直观的三组敌人数组。
    for stage in campaign["stages"]:
        stage["encounters"] = [{"enemies": wave} for wave in stage["encounters"]]
    heroes = json.loads((ROOT / "data" / "characters.json").read_text(encoding="utf-8-sig"))
    return {"campaign.json": campaign, "heroes.json": heroes}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--check", action="store_true", help="只检查衍生数据是否最新")
    args = parser.parse_args()
    for name, payload in outputs().items():
        path = DEST / name
        expected = json.dumps(payload, ensure_ascii=False, indent=2) + "\n"
        if args.check:
            if not path.exists() or path.read_text(encoding="utf-8") != expected:
                raise SystemExit(f"STALE: {path.relative_to(ROOT)}; run py -3 tools/prepare_prototype.py")
        else:
            DEST.mkdir(parents=True, exist_ok=True)
            path.write_text(expected, encoding="utf-8")
        print(f"{'CHECK' if args.check else 'WRITE'} {path.relative_to(ROOT)}")


if __name__ == "__main__":
    main()
