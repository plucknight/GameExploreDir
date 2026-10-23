"""Independent bad-data and arithmetic checks; no game-runtime claims."""

import importlib.util
import json
from pathlib import Path
import unittest


ROOT = Path(__file__).resolve().parents[1]
SPEC = importlib.util.spec_from_file_location("check_design", ROOT / "tools" / "check_design.py")
CHECK = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(CHECK)


def fixture():
    with (ROOT / "data" / "balance-model.json").open(encoding="utf-8") as stream:
        balance = json.load(stream)
    campaign = {"schema_version": 1, "difficulty": "normal", "regions": [], "monsters": [], "stages": []}
    market = {"schema_version": 1, "difficulty": "normal", "items": []}
    for r in range(1, 7):
        campaign["regions"].append({"id": r, "name": f"Region {r}", "budget_hours": [5, 7, 9, 10, 12, 13][r-1],
                                    "end_level": [4, 7, 10, 13, 16, 20][r-1], "end_weapon": [12, 16, 20, 24, 29, 34][r-1],
                                    "end_attack": [38, 48, 58, 68, 79, 92][r-1]})
        for kind in ("normal", "elite", "boss"):
            campaign["monsters"].append({"id": f"r{r}_{kind}", "region": r, "kind": kind,
                                         "name": kind, "mechanic": "bounded mechanic", "counterplay": ["adapt loadout", "change target priority"]})
        for i in range(1, 10):
            sid = f"{r}-{i}"
            kind = "elite" if i == 5 else "boss" if i == 9 else "normal"
            predecessor = None if sid == "1-1" else f"{r}-{i-1}" if i > 1 else f"{r-1}-9"
            campaign["stages"].append({"id": sid, "region": r, "index": i, "name": sid, "kind": kind,
                                       "predecessor": predecessor, "market_eligible": i in (5, 7, 9),
                                       "reward_focus": "materials", "decision": "choose loadout", "counterplay": ["use guard", "burst down source"],
                                       "encounters": [[f"r{r}_normal"], [f"r{r}_normal"], [f"r{r}_{kind}"]]})
        for slot in ("weapon", "offhand", "accessory", "none"):
            catalyst = slot == "none"
            market["items"].append({"id": f"normal_r{r}_{slot}", "region": r, "name": slot,
                                    "kind": "catalyst" if catalyst else "equipment", "slot": slot,
                                    "required_region": r, "trade_before_use": True, "bind_on_confirmed_use": not catalyst,
                                    "weight": 1, "effect": "fixed effect", "steam_itemdefid": None})
    return campaign, balance, market


class DesignValidationTests(unittest.TestCase):
    def setUp(self):
        self.campaign, self.balance, self.market = fixture()

    def errors(self):
        return CHECK.validate_design(self.campaign, self.balance, self.market)

    def test_valid_fixture(self):
        self.assertEqual(self.errors(), [])

    def test_counterplay_requires_two_nonempty_strings(self):
        for collection in ("monsters", "stages"):
            original = self.campaign[collection][0]["counterplay"]
            for invalid in ("use guard", [], ["one route"], ["valid route", " "], ["valid route", 2]):
                with self.subTest(collection=collection, invalid=invalid):
                    self.campaign[collection][0]["counterplay"] = invalid
                    self.assertTrue(any("counterplay" in e for e in self.errors()))
            self.campaign[collection][0]["counterplay"] = original
        self.assertEqual(self.errors(), [])

    def test_predecessor_jump_rejected(self):
        self.campaign["stages"][18]["predecessor"] = "1-9"
        self.assertTrue(any("predecessor" in e for e in self.errors()))

    def test_predecessor_cycle_rejected(self):
        self.campaign["stages"][0]["predecessor"] = "6-9"
        self.assertTrue(any("predecessor" in e for e in self.errors()))

    def test_four_initial_enemies_rejected(self):
        self.campaign["stages"][0]["encounters"][0] = ["r1_normal"] * 4
        self.assertTrue(any("living enemies" in e for e in self.errors()))

    def test_three_summons_rejected(self):
        self.campaign["monsters"][2]["summons"] = [{"monster_id": "r1_normal", "count": 3}]
        self.assertTrue(any("summon" in e for e in self.errors()))

    def test_deferred_summons_do_not_add_to_initial_count(self):
        self.campaign["monsters"][2]["summons"] = [{"monster_id": "r1_normal", "count": 2}]
        self.campaign["stages"][8]["encounters"][2] = ["r1_boss", "r1_normal", "r1_normal"]
        self.assertEqual(self.errors(), [])

    def test_recursive_summons_rejected(self):
        self.campaign["monsters"][0]["summons"] = [{"monster_id": "r1_normal", "count": 1}]
        self.assertTrue(any("recursive" in e for e in self.errors()))

    def test_market_eligibility_wrong_stage_rejected(self):
        self.campaign["stages"][0]["market_eligible"] = True
        self.assertTrue(any("market eligibility" in e for e in self.errors()))

    def test_wrong_budget_rejected(self):
        self.campaign["regions"][5]["budget_hours"] = 14
        self.assertTrue(any("budget" in e for e in self.errors()))

    def test_wrong_growth_rejected(self):
        self.campaign["regions"][5]["end_attack"] = 100
        self.assertTrue(any("growth" in e or "attack formula" in e for e in self.errors()))

    def test_boss_in_first_encounter_rejected(self):
        self.campaign["stages"][8]["encounters"][0] = ["r1_boss"]
        self.assertTrue(any("final encounter" in e for e in self.errors()))

    def test_recursive_bonus_events_rejected(self):
        self.balance["combat_constraints"]["bonus_events_can_trigger_bonus_events"] = True
        self.assertTrue(any("bonus_events" in e for e in self.errors()))

    def test_recovery_budget_beyond_cap_rejected(self):
        self.balance["combat_constraints"]["effective_healing_and_new_shield_fraction_per_window"] = 1.6
        self.assertTrue(any("healing" in e for e in self.errors()))

    def test_high_region_equipment_cannot_use_region_one_gate(self):
        self.market["items"][-2]["required_region"] = 1
        self.assertTrue(any("source region" in e for e in self.errors()))

    def test_binding_removed_rejected(self):
        self.market["items"][0]["bind_on_confirmed_use"] = False
        self.assertTrue(any("bind on confirmed use" in e for e in self.errors()))

    def test_pool_weight_changed_rejected(self):
        self.market["items"][0]["weight"] = 2
        self.assertTrue(any("weights" in e for e in self.errors()))

    def test_pacing_cannot_be_claimed_validated(self):
        self.balance["pacing"]["status"] = "PASS"
        self.assertTrue(any("cannot be marked validated" in e for e in self.errors()))

    def test_report_keeps_playtime_unvalidated(self):
        report = CHECK.build_report(self.campaign, self.balance, self.market)
        self.assertEqual(report["structural_status"], "PASS")
        self.assertEqual(report["pacing"]["status"], "NOT_VALIDATED")
        self.assertEqual(report["counts"]["encounters"], 162)


class ArithmeticTests(unittest.TestCase):
    def setUp(self):
        self.balance = fixture()[1]

    def test_truncated_geometric_matches_survival_formula(self):
        expected = (1 - .9 ** 20) / .1
        self.assertAlmostEqual(CHECK.truncated_geometric_mean(.1, 20), expected, places=12)
        self.assertEqual(CHECK.truncated_geometric_mean(1, 20), 1)
        self.assertEqual(CHECK.truncated_geometric_mean(.1, 1), 1)
        with self.assertRaises(ValueError):
            CHECK.truncated_geometric_mean(0, 20)

    def test_named_item_mean_and_tail(self):
        stats = CHECK.named_item_distribution()
        self.assertAlmostEqual(stats["mean_attempts"], (1 - .9 ** 20) / .1 / .25, places=9)
        self.assertEqual(stats["quantile_attempts"]["0.95"], 101)

    def test_crafting_reduces_target_tail(self):
        stats = CHECK.named_item_distribution(craft=True)
        self.assertAlmostEqual(stats["mean_attempts"] * .25, 6.730512206725008, places=8)
        self.assertEqual(stats["quantile_attempts"]["0.95"], 62)

    def test_gate_wait_preserves_income(self):
        route = CHECK.recruitment_route(self.balance)
        self.assertAlmostEqual(route["third_team_hour"], 5)
        self.assertAlmostEqual(route["all_base_characters_hour"], 9 + 50/60)
        self.assertAlmostEqual(route["timeline"][4]["hour"], 5 + 50/60)

    def test_dlc_counts_without_advancing_base_costs(self):
        one = CHECK.recruitment_route(self.balance, 1)
        two = CHECK.recruitment_route(self.balance, 2)
        self.assertAlmostEqual(one["all_base_characters_hour"], 10 + 20/60)
        self.assertAlmostEqual(two["all_base_characters_hour"], 11)
        self.assertAlmostEqual(one["second_team_hour"], 2.5)
        self.assertAlmostEqual(two["second_team_hour"], 3)

    def test_tutorial_income_delay_is_not_free(self):
        expected = {0.25: 10, .5: 10.25, 1: 10.75}
        for delay, hours in expected.items():
            with self.subTest(delay=delay):
                self.assertAlmostEqual(CHECK.recruitment_route(self.balance, income_delay_hours=delay)["all_base_characters_hour"], hours)


if __name__ == "__main__":
    unittest.main()
