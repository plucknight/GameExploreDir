"""Cross-document/data contracts and deployable prototype input, not playtest evidence."""
import copy
import importlib.util
import json
from pathlib import Path
import re
import unittest
from urllib.parse import unquote

ROOT = Path(__file__).resolve().parents[1]


def module(name, path):
    spec = importlib.util.spec_from_file_location(name, path)
    result = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(result)
    return result


CHECK = module("repository_check", ROOT / "tools/check_design.py")
PREPARE = module("repository_prepare", ROOT / "tools/prepare_prototype.py")


def read(name):
    return json.loads((ROOT / "data" / name).read_text(encoding="utf-8-sig"))


class RepositoryContracts(unittest.TestCase):
    def setUp(self):
        self.campaign = read("normal-campaign.json")
        self.balance = read("balance-model.json")
        self.market = read("market-items.json")

    def test_real_data_passes_the_validator(self):
        self.assertEqual(CHECK.validate_design(self.campaign, self.balance, self.market), [])

    def test_character_qualification_matches_progression_source(self):
        heroes = read("characters.json")["characters"]
        self.assertEqual(len({h["id"] for h in heroes}), 12)
        self.assertEqual([h["id"] for h in heroes if h["initial"]], ["C01", "C02"])
        self.assertEqual(sum(h["kind"] == "base" for h in heroes), 10)
        unlocks = self.balance["unlocks"]
        for hero in heroes:
            if hero["id"] in unlocks["characters"]:
                self.assertEqual(hero["unlock_stage"], unlocks["characters"][hero["id"]])
            self.assertEqual(len(hero["skills"]), 3)
            self.assertEqual(hero["skills"][2]["unlock_stage"], unlocks["third_active_skill_choice"])

    def test_unity_resources_are_exactly_derived(self):
        for name, expected in PREPARE.outputs().items():
            actual = json.loads((PREPARE.DEST / name).read_text(encoding="utf-8"))
            self.assertEqual(actual, expected, "Run tools/prepare_prototype.py after source edits")

    def test_partial_or_failed_run_cannot_be_declared_qualified(self):
        for key, bad in (("qualified_time_commit", "first_encounter_win"),
                         ("failed_or_abandoned_run_qualified_seconds", 60),
                         ("rare_target_change_boundary", "any_time")):
            altered = copy.deepcopy(self.balance)
            altered["rare_model"][key] = bad
            self.assertTrue(CHECK.validate_design(self.campaign, altered, self.market))

    def test_exchange_does_not_bypass_region_or_bind_rules(self):
        for key, bad in (("craft_requires_same_region_inputs_catalyst_and_output", False),
                         ("unbound_collection_exchange_requires_region_access", True),
                         ("equipment_use_and_personal_refinement_require_region_access", False)):
            altered = copy.deepcopy(self.balance)
            altered["rare_model"][key] = bad
            self.assertTrue(CHECK.validate_design(self.campaign, altered, self.market))

    def test_document_links_exist(self):
        files = list((ROOT / "design/normal").glob("*.md"))
        files += [ROOT / "README.md", ROOT / "prototypes/NormalCampaign/README.md"]
        for path in files:
            for target in re.findall(r"\]\(([^)]+)\)", path.read_text(encoding="utf-8")):
                if "://" in target or target.startswith("#"):
                    continue
                target = unquote(target.split("#", 1)[0].strip("<>"))
                with self.subTest(document=str(path.relative_to(ROOT)), target=target):
                    self.assertTrue((path.parent / target).exists())


if __name__ == "__main__":
    unittest.main()
