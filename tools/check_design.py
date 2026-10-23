#!/usr/bin/env python3
"""Validate normal-campaign design data and calculate bounded reference models.

Standard library only. This is not the game combat/loot implementation and does
not validate the campaign's intended playtime or the real Steam integration.
"""

from __future__ import annotations

import argparse
import json
import math
from pathlib import Path
import sys


ROOT = Path(__file__).resolve().parents[1]


def _number(value):
    return isinstance(value, (int, float)) and not isinstance(value, bool) and math.isfinite(value)


def validate_design(campaign, balance, market):
    """Return actionable errors; empty means structural checks only passed."""
    errors = []

    def require(condition, message):
        if not condition:
            errors.append(message)

    for name, data in (("campaign", campaign), ("balance", balance), ("market", market)):
        if not isinstance(data, dict):
            errors.append(f"{name}: root must be an object")
            return errors
        require(data.get("schema_version") == 1, f"{name}: schema_version must be 1")
        require(data.get("difficulty") == "normal", f"{name}: difficulty must be normal")

    regions = campaign.get("regions", [])
    monsters = campaign.get("monsters", [])
    stages = campaign.get("stages", [])
    items = market.get("items", [])
    for name, values in (("regions", regions), ("monsters", monsters), ("stages", stages), ("items", items)):
        if not isinstance(values, list) or not all(isinstance(v, dict) for v in values):
            errors.append(f"{name}: must be a list of objects")
            return errors

    scope = balance.get("scope", {})
    expected_scope = {"base_characters": 10, "dlc_characters": 2, "starting_characters": 2,
                      "characters_per_team": 2, "maximum_teams": 3, "regions": 6,
                      "stages_per_region": 9, "encounters_per_stage": 3,
                      "maximum_living_enemies": 3, "maximum_total_summons_per_encounter": 2,
                      "elite_stage_index": 5, "boss_stage_index": 9,
                      "market_stage_indices": [5, 7, 9]}
    for key, value in expected_scope.items():
        require(scope.get(key) == value, f"scope.{key}: expected {value}")
    require(len(regions) == 6, "campaign: expected 6 regions")
    require([r.get("id") for r in regions] == list(range(1, 7)), "regions: IDs must be ordered integers 1..6")
    require(len(stages) == 54, "campaign: expected 54 stages")

    pacing = balance.get("pacing", {})
    growth = balance.get("growth", {})
    budgets = [r.get("budget_hours") for r in regions]
    expected_budgets = [5, 7, 9, 10, 12, 13]
    require(budgets == expected_budgets, "regions: budget_hours must be [5,7,9,10,12,13]")
    require(pacing.get("region_budget_hours") == expected_budgets, "pacing: regional budgets disagree")
    require(pacing.get("target_hours") == 56, "pacing: target budget must total 56 hours")
    if all(_number(v) for v in budgets):
        require(math.isclose(sum(budgets), 56), "regions: budget total must equal 56")
    require(pacing.get("status") == "NOT_VALIDATED", "pacing: budget cannot be marked validated by this checker")
    require(growth.get("normal_level_cap") == 20, "growth: normal level cap must be 20")
    require(growth.get("normal_weapon_cap") == 34, "growth: normal weapon cap must be 34")
    require(growth.get("reserved_total_level_cap") == 30, "growth: reserved level cap must be 30")
    require(growth.get("reserved_total_weapon_cap") == 48, "growth: reserved weapon cap must be 48")
    require(growth.get("attack_base") == 20 and growth.get("attack_per_level_after_first") == 2,
            "growth: attack formula must be 20+2*(L-1)+W")
    for r in regions:
        rid = r.get("id")
        if isinstance(rid, int) and 1 <= rid <= 6:
            j = rid - 1
            expected = ([4, 7, 10, 13, 16, 20][j], [12, 16, 20, 24, 29, 34][j], [38, 48, 58, 68, 79, 92][j])
            actual = (r.get("end_level"), r.get("end_weapon"), r.get("end_attack"))
            require(actual == expected, f"region {rid}: end growth tuple must be {expected}")
            if all(_number(v) for v in actual):
                require(actual[2] == 20 + 2 * (actual[0] - 1) + actual[1], f"region {rid}: attack formula mismatch")
    for key, values in (("region_end_levels", [4, 7, 10, 13, 16, 20]),
                        ("region_end_weapons", [12, 16, 20, 24, 29, 34]),
                        ("region_end_attacks", [38, 48, 58, 68, 79, 92])):
        require(growth.get(key) == values, f"growth.{key}: regional table mismatch")

    monster_ids = [m.get("id") for m in monsters]
    require(all(isinstance(mid, str) and mid for mid in monster_ids), "monsters: nonempty string IDs required")
    if not all(isinstance(mid, str) for mid in monster_ids):
        return errors
    require(len(set(monster_ids)) == len(monster_ids), "monsters: duplicate ID")
    monster_by_id = {m["id"]: m for m in monsters}
    for monster in monsters:
        mid = monster["id"]
        require(monster.get("region") in range(1, 7), f"monster {mid}: invalid region")
        require(monster.get("kind") in ("normal", "elite", "boss"), f"monster {mid}: invalid kind")
        for field in ("name", "mechanic"):
            require(isinstance(monster.get(field), str) and bool(monster[field].strip()), f"monster {mid}: missing {field}")
        counterplay = monster.get("counterplay")
        require(isinstance(counterplay, list) and len(counterplay) >= 2 and
                all(isinstance(item, str) and bool(item.strip()) for item in counterplay),
                f"monster {mid}: counterplay must contain at least two nonempty strings")
        summons = monster.get("summons", [])
        if not isinstance(summons, list):
            errors.append(f"monster {mid}: summons must be a list")
            continue
        quota = 0
        for summon in summons:
            if not isinstance(summon, dict):
                errors.append(f"monster {mid}: malformed summon")
                continue
            count = summon.get("count")
            valid_count = isinstance(count, int) and not isinstance(count, bool) and 1 <= count <= 2
            require(valid_count, f"monster {mid}: summon count must be 1 or 2")
            quota += count if valid_count else 0
            target = monster_by_id.get(summon.get("monster_id"))
            require(target is not None, f"monster {mid}: unknown summon target")
            if target:
                require(target.get("region") == monster.get("region") and target.get("kind") == "normal",
                        f"monster {mid}: summon target must be same-region normal")
                require(not target.get("summons"), f"monster {mid}: recursive summons are forbidden")
        require(quota <= 2, f"monster {mid}: total summon quota exceeds 2")

    expected_ids = [f"{r}-{i}" for r in range(1, 7) for i in range(1, 10)]
    stage_ids = [s.get("id") for s in stages]
    require(stage_ids == expected_ids, "stages: IDs must be ordered 1-1 through 6-9 without duplicates or omissions")
    for ordinal, stage in enumerate(stages):
        sid = stage.get("id", "?")
        region, index = ordinal // 9 + 1, ordinal % 9 + 1
        require(stage.get("region") == region and stage.get("index") == index, f"stage {sid}: region/index mismatch")
        predecessor = None if ordinal == 0 else expected_ids[ordinal - 1] if ordinal < 54 else None
        require(stage.get("predecessor") == predecessor, f"stage {sid}: predecessor must be {predecessor}; skips and cycles forbidden")
        kind = "elite" if index == 5 else "boss" if index == 9 else "normal"
        require(stage.get("kind") == kind, f"stage {sid}: kind must be {kind}")
        require(stage.get("market_eligible") is (index in (5, 7, 9)), f"stage {sid}: market eligibility only at 5/7/9")
        for field in ("name", "reward_focus", "decision"):
            require(isinstance(stage.get(field), str) and bool(stage[field].strip()), f"stage {sid}: missing {field}")
        counterplay = stage.get("counterplay")
        require(isinstance(counterplay, list) and len(counterplay) >= 2 and
                all(isinstance(item, str) and bool(item.strip()) for item in counterplay),
                f"stage {sid}: counterplay must contain at least two nonempty strings")
        encounters = stage.get("encounters")
        if not isinstance(encounters, list) or len(encounters) != 3:
            errors.append(f"stage {sid}: expected exactly 3 encounters")
            continue
        for wave, encounter in enumerate(encounters):
            if not isinstance(encounter, list) or not 1 <= len(encounter) <= 3:
                errors.append(f"stage {sid} encounter {wave + 1}: must contain 1..3 living enemies")
                continue
            members = []
            for mid in encounter:
                monster = monster_by_id.get(mid) if isinstance(mid, str) else None
                require(monster is not None, f"stage {sid}: unknown monster {mid}")
                if monster:
                    members.append(monster)
                    require(monster.get("region") == region, f"stage {sid}: cross-region monster {mid}")
            special = [m for m in members if m.get("kind") != "normal"]
            if wave < 2 or kind == "normal":
                require(not special, f"stage {sid}: elite/boss may appear only in its designated final encounter")
            else:
                require(len(special) == 1 and special[0].get("kind") == kind,
                        f"stage {sid}: final encounter needs exactly one {kind}")
            quota = sum(s.get("count", 0) for m in members for s in m.get("summons", [])
                        if isinstance(s, dict) and isinstance(s.get("count"), int))
            require(quota <= 2, f"stage {sid} encounter {wave + 1}: summon quota exceeds 2")

    combat = balance.get("combat_constraints", {})
    caps = {"general_damage_bonus_cap": .5, "target_vulnerability_cap": .2,
            "attack_speed_bonus_cap": .4, "critical_chance_cap": .35,
            "permanent_damage_reduction_cap": .35, "temporary_damage_reduction_cap": .6,
            "shield_capacity_fraction": .35, "effective_healing_and_new_shield_fraction_per_window": .6}
    for key, cap in caps.items():
        value = combat.get(key)
        require(_number(value) and 0 <= value <= cap, f"combat.{key}: must be within 0..{cap}")
    require(combat.get("recovery_window_seconds") == 20, "combat: recovery window must be 20 seconds")
    require(combat.get("critical_multiplier") == 1.75, "combat: critical multiplier must be 1.75")
    require(combat.get("maximum_bonus_event_depth") == 1, "combat: bonus event depth must be 1")
    for key in ("bonus_events_can_trigger_bonus_events", "damage_over_time_can_crit",
                "damage_over_time_applies_damage_bonus_twice", "summon_quota_refills_after_summon_death"):
        require(combat.get(key) is False, f"combat.{key}: must be false")
    require(combat.get("summons_defer_when_living_enemy_cap_reached") is True,
            "combat: summons must defer at the living-enemy cap; runtime verification remains required")

    unlocks = balance.get("unlocks", {})
    expected_characters = {"C03": "1-2", "C04": "1-3", "C05": "1-5", "C06": "1-5",
                           "C07": "1-9", "C08": "1-9", "C09": "2-3", "C10": "2-3"}
    require(unlocks.get("characters") == expected_characters, "unlocks: base recruitment qualifications mismatch")
    require(unlocks.get("third_active_skill_choice") == "2-7", "unlocks: third skill choice must be 2-7")
    require(unlocks.get("second_charm_slot") == "3-9", "unlocks: second charm slot must be 3-9")
    require(unlocks.get("charm_evolutions") == {"trauma": "2-9", "purification": "3-3", "guard": "3-5",
                                               "hunt_mark": "4-3", "charge": "4-5", "ember": "4-9"},
            "unlocks: charm evolution stages mismatch")
    require(unlocks.get("dlc_counts_toward_team_roster") is True, "unlocks: DLC must count toward recruited roster")
    require(unlocks.get("teams") == [{"teams": 2, "minimum_total_recruited": 4, "stage": "1-5", "reference_hour": 2.5},
                                     {"teams": 3, "minimum_total_recruited": 6, "stage": "1-9", "reference_hour": 5.0}],
            "unlocks: team size/progress gates mismatch")
    recruit = balance.get("recruitment_model", {})
    require(recruit.get("base_recruitment_costs") == [42, 63, 84, 105, 126, 147, 168, 189], "recruitment: base cost steps mismatch")
    require(recruit.get("dlc_recruitment_cost_each") == 63 and recruit.get("dlc_changes_base_cost_index") is False,
            "recruitment: DLC must cost 63 independently of base steps")
    require(recruit.get("material_income_starts_at_stage") == "1-2", "recruitment: material income begins at 1-2")
    for key in ("material_work_per_team_hour", "fraction_allocated_to_recruitment"):
        require(_number(recruit.get(key)) and recruit[key] > 0, f"recruitment.{key}: positive finite value required")
    require(_number(recruit.get("fraction_allocated_to_recruitment")) and recruit["fraction_allocated_to_recruitment"] <= 1,
            "recruitment: allocation fraction cannot exceed 1")

    rare = balance.get("rare_model", {})
    require(rare.get("base_event_probability") == .1 and rare.get("hard_pity_attempt") == 20,
            "rare: reference probability/pity must be 0.1/20")
    require(rare.get("qualified_minutes_per_account_attempt") == 15, "rare: qualified interval must be 15 minutes")
    require(rare.get("maximum_simultaneous_qualified_time_streams") == 1 and rare.get("offline_time_qualifies") is False,
            "rare: account time cannot multiply by teams or include offline time")
    require(rare.get("pity_scope") == "difficulty_shared_between_regions", "rare: pity must not transfer across difficulty")
    require(rare.get("normal_template_count") == 24 and len(items) == 24, "market: normal requires 24 fixed templates")
    require(rare.get("mixed_binding_equipment_inputs_allowed") is False and rare.get("bound_equipment_can_return_to_market") is False,
            "market: mixed crafting or bound-item return is forbidden")
    require(rare.get("craft_requires_same_region_inputs_catalyst_and_output") is True,
            "market: crafting must keep inputs catalyst and output in the same region")
    require(rare.get("unbound_collection_exchange_requires_region_access") is False
            and rare.get("equipment_use_and_personal_refinement_require_region_access") is True,
            "market: collection exchange is permitted early but use and refinement require region access")
    require(rare.get("qualified_time_commit") == "complete_three_encounter_run_success"
            and rare.get("failed_or_abandoned_run_qualified_seconds") == 0,
            "rare: qualified time requires the complete successful run, not partial/failed encounters")
    require(rare.get("rare_target_selection") == "one_explicit_team"
            and rare.get("rare_target_change_boundary") == "run_complete_or_abandon_previous_run",
            "rare: target changes require a complete or abandoned run boundary")
    ids = [item.get("id") for item in items]
    require(all(isinstance(i, str) and i for i in ids), "market: nonempty string IDs required")
    if all(isinstance(i, str) for i in ids):
        require(len(set(ids)) == len(ids), "market: duplicate template ID")
    for region in range(1, 7):
        pool = [item for item in items if item.get("region") == region]
        require(len(pool) == 4 and sum(i.get("kind") == "equipment" for i in pool) == 3
                and sum(i.get("kind") == "catalyst" for i in pool) == 1,
                f"market region {region}: expected three equipment and one catalyst")
        require(all(item.get("weight") == 1 for item in pool), f"market region {region}: all four weights must be 1")
    for item in items:
        label = item.get("id", "?")
        require(item.get("region") in range(1, 7) and item.get("required_region") == item.get("region"),
                f"market {label}: use must require its source region")
        require(item.get("trade_before_use") is True, f"market {label}: must be tradable before use in the design")
        if item.get("kind") == "equipment":
            require(item.get("bind_on_confirmed_use") is True, f"market {label}: equipment must bind on confirmed use")
            require(item.get("slot") in ("weapon", "offhand", "armor", "accessory"), f"market {label}: invalid equipment slot")
        elif item.get("kind") == "catalyst":
            require(item.get("slot") == "none" and item.get("bind_on_confirmed_use") is False,
                    f"market {label}: catalyst is consumed, not equipped/bound")
        else:
            errors.append(f"market {label}: invalid kind")
        require(item.get("steam_itemdefid") is None, f"market {label}: real Steam ItemDefID has not been assigned")
    return errors


def truncated_geometric_mean(probability, hard_pity):
    if not 0 < probability <= 1 or not isinstance(hard_pity, int) or hard_pity < 1:
        raise ValueError("probability must be in (0,1] and hard_pity a positive integer")
    return sum((1 - probability) ** k for k in range(hard_pity))


def named_item_distribution(probability=.1, hard_pity=20, craft=False):
    """Exact finite-state survival calculation; pool weights are 1/1/1/1.

    States: pity misses, spare non-target equipment (cap 3), catalyst (cap 1).
    Returned time is in qualified attempts, starting with no spares or pity.
    """
    states = {(0, 0, 0): 1.0}
    expected = 0.0
    quantiles = {}
    for tick in range(10000):
        survival = sum(states.values())
        expected += survival
        for q in (.5, .9, .95, .99):
            if str(q) not in quantiles and survival <= 1 - q:
                quantiles[str(q)] = tick
        if survival < 1e-13:
            return {"mean_attempts": expected, "quantile_attempts": quantiles}
        following = {}

        def add(key, value):
            following[key] = following.get(key, 0.0) + value

        for (misses, gear, catalyst), mass in states.items():
            event = 1.0 if misses == hard_pity - 1 else probability
            if misses < hard_pity - 1:
                add((misses + 1, gear, catalyst), mass * (1 - event))
            if not craft:
                add((0, 0, 0), mass * event * .75)
            else:
                new_gear = min(3, gear + 1)
                if not (new_gear == 3 and catalyst == 1):
                    add((0, new_gear, catalyst), mass * event * .5)
                if gear < 3:
                    add((0, gear, 1), mass * event * .25)
            # The named equipment's 25% ends the sequence immediately.
        states = following
    raise ArithmeticError("rare distribution did not converge")


def recruitment_route(balance, dlc_first=0, income_delay_hours=0.0):
    """Piecewise material-budget model, NOT a measured playthrough.

    Gate waiting retains income; DLC counts toward roster but not base-cost index.
    Early qualifications must be open by affordability as documented in config.
    """
    if dlc_first not in (0, 1, 2) or not _number(income_delay_hours) or income_delay_hours < 0:
        raise ValueError("invalid DLC count or income delay")
    model = balance["recruitment_model"]
    gates = balance["unlocks"]["teams"]
    qualifications = balance["unlocks"]["characters"]
    reference_times = model["reference_stage_clear_hours"]
    actions = [("DLC", model["dlc_recruitment_cost_each"], None)] * dlc_first
    actions += [(f"C{i + 3:02}", cost, qualifications[f"C{i + 3:02}"])
                for i, cost in enumerate(model["base_recruitment_costs"])]
    time, stock = float(income_delay_hours), 0.0
    base, dlc, teams = 2, 0, 1
    team_times, timeline = {}, []
    allocated_rate = model["material_work_per_team_hour"] * model["fraction_allocated_to_recruitment"]

    def refresh():
        nonlocal teams
        for gate in gates:
            if teams < gate["teams"] and base + dlc >= gate["minimum_total_recruited"] and time + 1e-9 >= gate["reference_hour"]:
                teams = gate["teams"]
                team_times[str(teams)] = time

    for character, cost, stage in actions:
        ready = reference_times.get(stage, income_delay_hours)
        while stock + 1e-8 < cost or time + 1e-9 < ready:
            refresh()
            affordable = time + max(0.0, cost - stock) / (allocated_rate * teams)
            purchase = max(affordable, ready)
            next_gate = min((g["reference_hour"] for g in gates if teams < g["teams"]
                             and base + dlc >= g["minimum_total_recruited"] and g["reference_hour"] > time + 1e-9),
                            default=math.inf)
            event = min(purchase, next_gate)
            stock += (event - time) * allocated_rate * teams
            time = event
            refresh()
        stock -= cost
        if character == "DLC":
            dlc += 1
        else:
            base += 1
        refresh()
        timeline.append({"character": character, "hour": time, "base_owned": base, "dlc_owned": dlc,
                         "teams": teams, "unspent_recruitment_mu": max(0.0, stock)})
    return {"dlc_first": dlc_first, "income_start_delay_hours": income_delay_hours,
            "second_team_hour": team_times.get("2"), "third_team_hour": team_times.get("3"),
            "all_base_characters_hour": time, "timeline": timeline}


def build_report(campaign, balance, market):
    errors = validate_design(campaign, balance, market)
    result = {"structural_status": "FAIL" if errors else "PASS", "errors": errors,
              "pacing": {"status": "NOT_VALIDATED", "target_hours": 56,
                         "reason": "Missing measured combat, recipes and resource yields for the two-main-character route; no playtime simulation exists."},
              "runtime_combat_status": "NOT_ASSESSED_BY_STATIC_CHECKER", "steam_integration_status": "NOT_VERIFIED"}
    if errors:
        return result
    rare = balance["rare_model"]
    mean = truncated_geometric_mean(rare["base_event_probability"], rare["hard_pity_attempt"])
    interval_hours = rare["qualified_minutes_per_account_attempt"] / 60
    direct = named_item_distribution()
    crafted = named_item_distribution(craft=True)
    for stats in (direct, crafted):
        stats["mean_qualified_hours"] = stats["mean_attempts"] * interval_hours
        stats["quantile_qualified_hours"] = {q: n * interval_hours for q, n in stats["quantile_attempts"].items()}
    result["counts"] = {"regions": len(campaign["regions"]), "stages": len(campaign["stages"]),
                        "encounters": sum(len(s["encounters"]) for s in campaign["stages"]), "market_templates": len(market["items"])}
    result["rare_reference"] = {"clock": "qualified_exploration_only", "mean_event_attempts": mean,
                                "mean_event_qualified_hours": mean * interval_hours,
                                "named_direct": direct, "named_direct_or_crafting": crafted,
                                "wall_clock_status": "NOT_VALIDATED_QUALIFIED_TIME_SHARE_UNKNOWN",
                                "market_price_status": "NOT_VALIDATED"}
    result["recruitment_reference"] = {"status": "ILLUSTRATIVE_NOT_MEASURED",
                                       "routes": [recruitment_route(balance, n, delay)
                                                  for delay in balance["recruitment_model"]["illustrative_income_start_delays_hours"]
                                                  for n in range(3)]}
    return result


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--json", action="store_true", help="emit JSON to standard output; writes no file")
    parser.add_argument("--data-dir", type=Path, default=ROOT / "data", help="design data directory")
    args = parser.parse_args(argv)
    filenames = ("normal-campaign.json", "balance-model.json", "market-items.json")
    loaded = []
    errors = []
    for filename in filenames:
        try:
            with (args.data_dir / filename).open(encoding="utf-8-sig") as stream:
                loaded.append(json.load(stream))
        except (OSError, ValueError):
            errors.append(f"data/{filename}: missing, unreadable or invalid JSON")
    report = {"structural_status": "FAIL", "errors": errors, "pacing": {"status": "NOT_VALIDATED"}} if errors else build_report(*loaded)
    if args.json:
        print(json.dumps(report, ensure_ascii=False, indent=2, allow_nan=False))
    else:
        print(f"Structure: {report['structural_status']}; pacing: NOT_VALIDATED")
        for error in report["errors"]:
            print(f"- {error}")
        if not report["errors"]:
            print("Arithmetic reference models completed. No combat, playtime or Steam-market validation is claimed.")
    return 1 if report["errors"] else 0


if __name__ == "__main__":
    sys.exit(main())
