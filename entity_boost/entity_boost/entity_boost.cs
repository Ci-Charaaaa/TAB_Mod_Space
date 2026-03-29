using AX.ModLoader;
using AX.ModLoader.Config;
using DXVision;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Reflection;
using ZX;
using ZX.Entities;

namespace entity_boost
{
    public class Boost_Mod : IModEntry
    {
        public Unit_Total_Config Config;
        public static Boost_Mod Instance;
        private Mod entity_boost_mod;

        //锁死原始数值字典
        private static readonly Dictionary<string, float> _originalValues = new Dictionary<string, float>();

        public override void OnLoad(Mod mod)
        {
            entity_boost_mod = mod;
            Config = mod.RegisterConfig<Unit_Total_Config>();
            Instance = this;

            var harmony = new Harmony("entity_boost");
            harmony.UnpatchAll("entity_boost");
            harmony.PatchAll(Assembly.GetExecutingAssembly());

            DXLog.Write($"[Entity_Boost] 加载成功！");

            Config.OnConfigChanged += (c) => { entity_boost_mod.SaveConfig(); };
        }

        [HarmonyPatch(typeof(ZXDefaultParams), "ApplyMods")]
        public class InjectionPatch
        {
            public static void Postfix()
            {
                if (Instance == null || Instance.Config == null) return;
                var c = Instance.Config;

                // 统一调用修改
                ApplyUnitStats("Ranger", c.Ranger_HealthyMult, c.Ranger_SpeedMult, c.Ranger_RangeMult,
                    c.Ranger_VisionMult, c.Ranger_ArmorAdd, c.Ranger_FireResistArmor, c.Ranger_PoisonResistArmor,
                    c.Ranger_DamageMult, c.Ranger_DamageSpeedMult);

                ApplyUnitStats("SoldierRegular", c.Soldier_HealthyMult, c.Soldier_SpeedMult, c.Soldier_RangeMult,
                    c.Soldier_VisionMult, c.Soldier_ArmorAdd, c.Soldier_FireResistArmor, c.Soldier_PoisonResistArmor,
                    c.Soldier_DamageMult, c.Soldier_DamageSpeedMult);

                ApplyUnitStats("Sniper", c.Sniper_HealthyMult, c.Sniper_SpeedMult, c.Sniper_RangeMult,
                    c.Sniper_VisionMult, c.Sniper_ArmorAdd, c.Sniper_FireResistArmor, c.Sniper_PoisonResistArmor,
                    c.Sniper_DamageMult, c.Sniper_DamageSpeedMult);
            }
        }

        [HarmonyPatch(typeof(Human), "AddExperience")]
        public class Patch_VeteranHeal
        {
            private static bool _wasVeteranBefore;
            public static void Prefix(Human __instance) => _wasVeteranBefore = __instance.IsVeteran;

            public static void Postfix(Human __instance)
            {
                if (Instance == null || Instance.Config == null || !Instance.Config.IsFull_HP) return;

                // 当单位从新兵变老兵的那一刻
                if (!_wasVeteranBefore && __instance.IsVeteran)
                {
                    try
                    {
                        var ep = __instance.get_Params();
                        if (ep != null && __instance._CLife != null)
                        {
                            
                            float veteranMax = (float)(ep._Life * 1.2f);

                            // 用 _setField 强行注入当前单位实例
                            _setField(__instance._CLife, "_Life", veteranMax);

                            DXLog.Write($"[Entity_Boost] 老兵晋升成功: {__instance.ID} 血量同步为 {veteranMax}");
                        }
                    }
                    catch { }
                }
            }
        }

        public static void ApplyUnitStats(string entityID, double mLife, double mSpeed, double mRange, double mVision,
            double aArmor, double aFire, double aPoison, double mDmg, double mSpd)
        {
            var allParams = ZXDefaultParams.get_All();
            if (allParams == null) return;

            foreach (var p in allParams)
            {
                if (p == null || p.ID != entityID) continue;
                if (p is ZXEntityDefaultParams ep)
                {
                    // 1. 基础属性 (使用字典锁定原始值，防止叠乘)
                    _setSafeValue(ep, entityID, "_Life", mLife / 100.0, true);
                    _setSafeValue(ep, entityID, "_RunSpeed", mSpeed / 100.0, true);
                    _setSafeValue(ep, entityID, "_WatchRange", mVision / 100.0, true);

                    // 2. 护甲与抗性
                    _setSafeValue(ep, entityID, "_Armor", aArmor, false);
                    _setSafeValue(ep, entityID, "FireDamageFactor", -aFire, false);
                    _setSafeValue(ep, entityID, "VenomDamageFactor", -aPoison, false);

                    // 3. 攻击指令 (新兵 & 老兵)
                    _processCommand(ep, entityID, "_AttackCommandBasic", mDmg, mRange, mSpd);
                    _processCommand(ep, entityID, "_AttackCommandVeteran", mDmg, mRange, mSpd);
                }
            }
        }

        private static void _processCommand(ZXEntityDefaultParams ep, string entityID, string cmdName, double mDmg, double mRng, double mSpd)
        {
            object cmdObj = _getField(ep, cmdName);
            if (cmdObj == null) return;

            object realParams = _getField(cmdObj, "_Params");
            if (realParams == null) return;

            string keyPrefix = $"{entityID}_{cmdName}";
            double speedFactor = Math.Max(0.1, mSpd / 100.0);

            // 注入伤害与射程
            _setSafeValue(realParams, keyPrefix, "_Damage", mDmg / 100.0, true);
            _setSafeValue(realParams, keyPrefix, "_ActionRange", mRng / 100.0, true);

            // 注入攻速与前摇 (TimePreAction)
            // 修正后的逻辑：使用正确的字段名和 int 转换
            _setSafeValue(realParams, keyPrefix, "_TimeAction", 1.0 / speedFactor, true);
            _setSafeValue(realParams, keyPrefix, "_TimePreAction", 1.0 / speedFactor, true);
        }

        // --- 核心数值保护函数：确保永远基于“出厂设置”进行乘法 ---
        private static void _setSafeValue(object target, string ownerKey, string fieldName, double value, bool isMult)
        {
            string fullKey = $"{ownerKey}_{fieldName}";

            // 如果字典里没有，说明是游戏启动后的第一次加载，记录原始值
            if (!_originalValues.ContainsKey(fullKey))
            {
                object raw = _getField(target, fieldName);
                if (raw == null) return;
                _originalValues[fullKey] = Convert.ToSingle(raw);
            }

            float baseVal = _originalValues[fullKey];
            float finalVal = isMult ? (float)(baseVal * value) : (float)(baseVal + value);

            // 范围限制 (抗性与护甲)
            if (fieldName.Contains("Factor") || fieldName == "_Armor")
                finalVal = Math.Max(0f, Math.Min(1.0f, finalVal));

            _setField(target, fieldName, finalVal);
        }

        // 辅助方法：反射读写
        private static object _getField(object target, string name)
        {
            return target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)?.GetValue(target);
        }

        private static void _setField(object target, string fieldName, object value)
        {
            FieldInfo f = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (f != null)
            {
                // 自动处理 int 和 float 的转换
                f.SetValue(target, Convert.ChangeType(value, f.FieldType));
            }
        }
    

        private void OnConfigChanged(ModConfig config)
        {
            //对于配置更改后的保存和更改
            entity_boost_mod.SaveConfig();
            DXLog.Write($"[Test_Mod] 配置已更改");
            
        }


        private void Unload()
        {
            Config.OnConfigChanged -= OnConfigChanged;
            
            entity_boost_mod.SaveConfig();
        }


    }

    public class Unit_Total_Config : ModConfig
    {
        // ================= 全局设置 =================
        [ConfigOption("老兵恢复满血", ConfigOptionType.Checkbox,
            Description = "单位晋升老兵时，是否恢复满血",
            Category = "全局设置", Order = 1)]
        public bool IsFull_HP { get; set; } = true;

        // ================= 游侠属性 (Ranger) =================
        [ConfigOption("游侠-生命倍率", ConfigOptionType.Slider, Category = "游侠属性", Order = 1)]
        [Range(1, 500, 1)]
        public double Ranger_HealthyMult { get; set; } = 100;

        [ConfigOption("游侠-攻击倍率", ConfigOptionType.Slider, Category = "游侠属性", Order = 2)]
        [Range(1, 300, 1)]
        public double Ranger_DamageMult { get; set; } = 100;

        [ConfigOption("游侠-射程倍率", ConfigOptionType.Slider, Category = "游侠属性", Order = 3)]
        [Range(1, 200, 1)]
        public double Ranger_RangeMult { get; set; } = 100;

        [ConfigOption("游侠-攻速倍率", ConfigOptionType.Slider, Category = "游侠属性", Order = 4)]
        [Range(1, 300, 1)]
        public double Ranger_DamageSpeedMult { get; set; } = 100;

        [ConfigOption("游侠-移速倍率", ConfigOptionType.Slider, Category = "游侠属性", Order = 5)]
        [Range(1, 200, 1)]
        public double Ranger_SpeedMult { get; set; } = 100;

        [ConfigOption("游侠-增加额外护甲值", ConfigOptionType.Slider, Category = "游侠属性", Order = 6)]
        [Range(0, 0.90, 0.01)]
        public double Ranger_ArmorAdd { get; set; } = 0;

        [ConfigOption("游侠-增加额外毒抗", ConfigOptionType.Slider, Category = "游侠属性", Order = 7)]
        [Range(0, 1.0, 0.01)]
        public double Ranger_PoisonResistArmor { get; set; } = 0;

        [ConfigOption("游侠-增加额外火抗", ConfigOptionType.Slider, Category = "游侠属性", Order = 8)]
        [Range(0, 1.0, 0.01)]
        public double Ranger_FireResistArmor { get; set; } = 0;

        [ConfigOption("游侠-视野倍率", ConfigOptionType.Slider, Category = "游侠属性", Order = 9)]
        [Range(1, 200, 1)]
        public double Ranger_VisionMult { get; set; } = 100;

        // ================= 士兵属性 (Soldier) =================
        [ConfigOption("士兵-生命倍率", ConfigOptionType.Slider, Category = "士兵属性", Order = 1)]
        [Range(1, 500, 1)]
        public double Soldier_HealthyMult { get; set; } = 100;

        [ConfigOption("士兵-攻击倍率", ConfigOptionType.Slider, Category = "士兵属性", Order = 2)]
        [Range(1, 300, 1)]
        public double Soldier_DamageMult { get; set; } = 100;

        [ConfigOption("士兵-射程倍率", ConfigOptionType.Slider, Category = "士兵属性", Order = 3)]
        [Range(1, 200, 1)]
        public double Soldier_RangeMult { get; set; } = 100;

        [ConfigOption("士兵-攻速倍率", ConfigOptionType.Slider, Category = "士兵属性", Order = 4)]
        [Range(1, 300, 1)]
        public double Soldier_DamageSpeedMult { get; set; } = 100;

        [ConfigOption("士兵-移速倍率", ConfigOptionType.Slider, Category = "士兵属性", Order = 5)]
        [Range(1, 200, 1)]
        public double Soldier_SpeedMult { get; set; } = 100;

        [ConfigOption("士兵-增加额外护甲值", ConfigOptionType.Slider, Category = "士兵属性", Order = 6)]
        [Range(0, 0.55, 0.01)]
        public double Soldier_ArmorAdd { get; set; } = 0;

        [ConfigOption("士兵-增加额外毒抗", ConfigOptionType.Slider, Category = "士兵属性", Order = 7)]
        [Range(0, 0.50, 0.01)]
        public double Soldier_PoisonResistArmor { get; set; } = 0;

        [ConfigOption("士兵-增加额外火炕", ConfigOptionType.Slider, Category = "士兵属性", Order = 8)]
        [Range(0, 0.50, 0.01)]
        public double Soldier_FireResistArmor { get; set; } = 0;

        [ConfigOption("士兵-视野倍率", ConfigOptionType.Slider, Category = "士兵属性", Order = 9)]
        [Range(1, 200, 1)]
        public double Soldier_VisionMult { get; set; } = 100;

        // ================= 狙击手属性 (Sniper) =================
        [ConfigOption("狙击手-生命倍率", ConfigOptionType.Slider, Category = "狙击手属性", Order = 1)]
        [Range(1, 500, 1)]
        public double Sniper_HealthyMult { get; set; } = 100;

        [ConfigOption("狙击手-攻击倍率", ConfigOptionType.Slider, Category = "狙击手属性", Order = 2)]
        [Range(1, 500, 1)] // 我是狙击手猎鹰，枪枪爆头，好运连连
        public double Sniper_DamageMult { get; set; } = 100;

        [ConfigOption("狙击手-射程倍率", ConfigOptionType.Slider, Category = "狙击手属性", Order = 3)]
        [Range(1, 200, 1)]
        public double Sniper_RangeMult { get; set; } = 100;

        [ConfigOption("狙击手-攻速倍率", ConfigOptionType.Slider, Category = "狙击手属性", Order = 4)]
        [Range(1, 500, 1)] // 基础攻速极慢，给更高倍率
        public double Sniper_DamageSpeedMult { get; set; } = 100;

        [ConfigOption("狙击手-移速倍率", ConfigOptionType.Slider, Category = "狙击手属性", Order = 5)]
        [Range(1, 200, 1)]
        public double Sniper_SpeedMult { get; set; } = 100;

        [ConfigOption("狙击手-增加额外护甲值", ConfigOptionType.Slider, Category = "狙击手属性", Order = 6)]
        [Range(0, 0.90, 0.01)]
        public double Sniper_ArmorAdd { get; set; } = 0;

        [ConfigOption("狙击手-增加额外火炕", ConfigOptionType.Slider, Category = "狙击手属性", Order = 7)]
        [Range(0, 1.0, 0.01)]
        public double Sniper_PoisonResistArmor { get; set; } = 0;

        [ConfigOption("狙击手-增加额外火炕", ConfigOptionType.Slider, Category = "狙击手属性", Order = 8)]
        [Range(0, 1.0, 0.01)]
        public double Sniper_FireResistArmor { get; set; } = 0;

        [ConfigOption("狙击手-视野倍率", ConfigOptionType.Slider, Category = "狙击手属性", Order = 9)]
        [Range(1, 200, 1)]
        public double Sniper_VisionMult { get; set; } = 100;
    }

}
