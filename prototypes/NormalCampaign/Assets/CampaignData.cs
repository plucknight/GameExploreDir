// PROTOTYPE - NOT FOR PRODUCTION
// Question: 能否通过配置双人队伍和阅读战报解决可见威胁？
// Date: 2026-10-23
using System;

namespace NormalCampaignPrototype
{
    [Serializable] public class CampaignData
    {
        public int schema_version;
        public string difficulty;
        public string source_sha256;
        public RegionSpec[] regions;
        public MonsterSpec[] monsters;
        public StageSpec[] stages;
    }
    [Serializable] public class RegionSpec
    {
        public int id;
        public string name;
        public int budget_hours, end_level, end_weapon, end_attack;
    }
    [Serializable] public class MonsterSpec
    {
        public string id, name, kind, mechanic;
        public int region;
        public string[] counterplay;
        public SummonSpec[] summons;
    }
    [Serializable] public class SummonSpec { public string monster_id; public int count; }
    [Serializable] public class EncounterSpec { public string[] enemies; }
    [Serializable] public class StageSpec
    {
        public string id, name, kind, predecessor, reward_focus, decision;
        public int region, index;
        public bool market_eligible;
        public string[] counterplay;
        public EncounterSpec[] encounters;
    }
    [Serializable] public class HeroCatalog { public HeroSpec[] characters; }
    [Serializable] public class HeroSpec
    {
        public string id, name, title, kind, unlock_stage, passive_intent;
        public bool initial;
        public SkillSpec[] skills;
    }
    [Serializable] public class SkillSpec
    {
        public string id, name, effect, unlock_stage;
        public float prototype_cooldown_seconds, prototype_power;
    }
}
