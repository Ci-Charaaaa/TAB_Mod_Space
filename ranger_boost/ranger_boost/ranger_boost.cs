using AX.ModLoader;
using AX.ModLoader.Config;
using DXVision;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using ZX;
using ZX.Entities;


namespace ranger_boost
{
    public class Boost_Mod : IModEntry 
    {
        private Mod _test_mod;
        public Unit_Total_Config Config;
        public static Boost_Mod Instance;


        public override void OnLoad(Mod mod)
        {
            _test_mod = mod;
            Config = mod.RegisterConfig<Unit_Total_Config>();
            DXLog.Write($"[Test_Mod] 已加载！版本：{mod.Info.Version} gugugaga!");

            Instance = this;

            var harmony = new Harmony("com.charaa.test1");
            harmony.UnpatchAll("com.charaa.test1");
            harmony.PatchAll(Assembly.GetExecutingAssembly()); // 告诉 Harmony 扫描这个项目里的 [HarmonyPatch]

            DXLog.Write($"[Test_Mod] Harmony 补丁已应用！正在等待游戏逻辑初始化...,随时准备进行属性加强模块注入");

            Config.OnConfigChanged += (c) => 
            { _test_mod.SaveConfig(); 
                DXLog.Write("[Test_Mod] 配置已保存");
            };


        }

        // 钩住 ApplyMods
        [HarmonyPatch(typeof(ZXDefaultParams), "ApplyMods")]
        public class InjectionPatch
        {
            public static void Postfix()
            {
                if (Instance == null || Instance.Config == null) return;
                var c = Instance.Config;

                // 游侠
                ApplyUnitStats("Ranger", 60f, 4f, 6f, 8f, 10, 1000,
                    c.Ranger_HealthyMult, c.Ranger_SpeedMult, c.Ranger_RangeMult,
                    c.Ranger_VisionMult, c.Ranger_ArmorAdd, c.Ranger_DamageMult, c.Ranger_DamageSpeedMult);

                // 士兵
                ApplyUnitStats("SoldierRegular", 120f, 2.4f, 5f, 6f, 16, 500,
                    c.Soldier_HealthyMult, c.Soldier_SpeedMult, c.Soldier_RangeMult,
                    c.Soldier_VisionMult, c.Soldier_ArmorAdd, c.Soldier_DamageMult, c.Soldier_DamageSpeedMult);
            }
        }

        // 钩住 Human 的 AddExperience 方法，来实现老兵晋升时的回血效果（这里懒得找具体的晋升通知方法了
        // ，通过检测增加经验值中需要查看是否晋升真值的这个方法，来间接去实现晋升时的生命值恢复）
        [HarmonyPatch(typeof(Human), "AddExperience")]
        public class Patch_VeteranHeal
        {
            private static bool _wasVeteranBefore;


            // 执行前：记录是否已经是老兵，防止重复执行
            public static void Prefix(Human __instance)
            {
                _wasVeteranBefore = __instance.IsVeteran;
            }

            // 执行后：如果状态从 false 变为 true，说明这一刻晋升了
            public static void Postfix(Human __instance)
            {

                if (Instance == null || Instance.Config == null) return;
                var c = Instance.Config;

                if (!c.IsFull_HP)
                {
                    return; // 如果配置里没有开启老兵满血，就直接返回，不执行后续代码
                }

                if (!_wasVeteranBefore && __instance.IsVeteran)
                {
                    try
                    {
                        // 获取最大生命值（从配置参数中获取）
                        // 注意：ep._Life 是数据表中修改后的那个上限
                        var ep = __instance.get_Params();
                        if (ep != null)
                        {
                            // 设置实时生命值为上限值
                            __instance._CLife._Life = ep._Life;

                            DXLog.Write($"[Test_Mod] 单位 {__instance.ID} 晋升老兵，瞬间回满血: {ep._Life}");
                        }
                    }
                    catch (Exception ex)
                    {
                        // 确保不会因为意外崩溃影响游戏主进程
                    }
                }
            }
        }

        // 核心注入方法：根据实体ID扫描并修改属性
        public static void ApplyUnitStats(string entity_ID, float bLife, float bSpeed, float bRange, float bVision, int bDamage, int bTimeAction,
            double mLife, double mSpeed, double mRange, double mVision, double aArmor, double mDamage, double mAtkSpeed)
        {
            var allParams = ZXDefaultParams.get_All();
            if (allParams == null) return;

            foreach (var p in allParams)
            {
                if (p == null || p.ID != entity_ID) continue;

                if (p is ZXEntityDefaultParams ep)
                {
                    try
                    {
                        DXLog.Write($"\n[Test_Mod] >>> 开始注入单位: {entity_ID} (Instance: {ep.GetHashCode()})");

                        // 1. 基础属性注入 (使用已修正类型转换的 _setFieldSafe)
                        _setFieldSafe(ep, "_Life", bLife, mLife / 100.0);
                        _setFieldSafe(ep, "_RunSpeed", bSpeed, mSpeed / 100.0);
                        _setFieldSafe(ep, "_WatchRange", bVision, mVision / 100.0);

                        // 2. 护甲特殊处理 (护甲值与其他属性不太一样，加算而且数值很小)
                        FieldInfo armorField = typeof(ZXEntityDefaultParams).GetField("_Armor", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                        if (armorField != null)
                        {
                            float armorVal = (float)aArmor;
                            armorField.SetValue(ep, armorVal);
                            if (ep.DefaultValues != null) armorField.SetValue(ep.DefaultValues, armorVal);
                            DXLog.Write($"[Test_Mod] [Armor] {entity_ID} -> {armorVal} (DefaultValues已同步)");
                        }

                        // 3. 战斗属性注入
                        // 定义需要处理的攻击命令字段名，前者是新兵攻击，后者为老兵的
                        string[] commandFieldNames = { "_AttackCommandBasic", "_AttackCommandVeteran" };

                        foreach (string fieldName in commandFieldNames)
                        {
                            FieldInfo shellField = typeof(ZXEntityDefaultParams).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                            object shellObj = shellField?.GetValue(ep);

                            if (shellObj != null)
                            {
                                // 修改当前实体的攻击参数 (新兵或老兵是通用的)
                                _processCommandParams(shellObj, $"{fieldName}.Instance", bDamage, mDamage, bRange, mRange, bTimeAction, mAtkSpeed);

                                // 同步 DefaultValues 里的攻击参数
                                if (ep.DefaultValues is ZXEntityDefaultParams dep)
                                {
                                    object dShellObj = shellField?.GetValue(dep);
                                    if (dShellObj != null)
                                    {
                                        DXLog.Write($"[Test_Mod] [Command] 正在同步 {entity_ID} 的 DefaultValues.{fieldName} 攻击参数...");
                                        _processCommandParams(dShellObj, $"{fieldName}.DefaultValues", bDamage, mDamage, bRange, mRange, bTimeAction, mAtkSpeed);
                                    }
                                }
                            }
                        }

                        DXLog.Write($"[Test_Mod] <<< {entity_ID} 注入流程结束\n");
                    }
                    catch (Exception ex)
                    {
                        DXLog.Write($"[Test_Mod] [FATAL] {entity_ID} 注入崩溃: {ex.Message}");
                    }
                }
            }
        }


        // 增加了 string label 参数，用于日志区分，和排查日志，这两个方法是用于params中注入攻击数值的
        private static void _updateIntField(object target, string fieldName, int baseVal, double mult, string label)
        {
            if (target == null) return;

            FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (field != null)
            {
                int final = (int)(baseVal * mult);
                field.SetValue(target, final);
                // 在日志中输出 label (例如 "Instance" 或 "DefaultValues")
                DXLog.Write($"[Test_Mod] [{label}] {fieldName} (Int32) -> {final}");
            }
            else
            {
                DXLog.Write($"[Test_Mod] [ERROR] [{label}] 找不到字段: {fieldName}");
            }
        }

        private static void _updateFloatField(object target, string fieldName, float baseVal, double mult, string label)
        {
            if (target == null) return;

            FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (field != null)
            {
                float final = (float)(baseVal * mult);
                field.SetValue(target, final);
                // 在日志中输出 label
                DXLog.Write($"[Test_Mod] [{label}] {fieldName} (Single) -> {final}");
            }
            else
            {
                DXLog.Write($"[Test_Mod] [ERROR] [{label}] 找不到字段: {fieldName}");
            }
        }

        // 核心：带日志的安全注入工具，它是注入非攻击数值的方法
        private static void _setFieldSafe(ZXEntityDefaultParams target, string fieldName, float baseValue, double multiplier)
        {
            try
            {
                FieldInfo field = typeof(ZXEntityDefaultParams).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                if (field == null) return;

                double rawCalculated = baseValue * multiplier;
                object finalValue;

                
                if (field.FieldType == typeof(int))
                {
                    // 如果是 int 
                    finalValue = (int)Math.Round(rawCalculated);
                }
                else if (field.FieldType == typeof(float))
                {
                    // 如果是 float 
                    finalValue = (float)rawCalculated;
                }
                else
                {
                    finalValue = Convert.ChangeType(rawCalculated, field.FieldType);
                }

                field.SetValue(target, finalValue);

                // 同步修改 DefaultValues（这是对付老兵的关键）
                if (target.DefaultValues != null && target.DefaultValues != target)
                {
                    field.SetValue(target.DefaultValues, finalValue);
                }
            }
            catch (Exception ex)
            {
                DXLog.Write($"[Test_Mod] [ERROR] 注入 {fieldName} 失败: {ex.Message}");
            }
        }

        //这是专门给攻击参数注入的方法，因为攻击的参数比较特殊，不与生命，护甲等其他值放在一起
        //，而是放在一个单独的命令参数对象里，所以需要专门处理，增加了日志输出以便排查问题
        private static void _processCommandParams(object commandObj, string label, int bDmg, double mDmg, float bRng, double mRng, int bTime, double mSpd)
        {
            FieldInfo paramsField = commandObj.GetType().GetField("_Params", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            object realParams = paramsField?.GetValue(commandObj);

            if (realParams != null)
            {
                _updateIntField(realParams, "_Damage", bDmg, mDmg / 100.0, label);
                _updateFloatField(realParams, "_ActionRange", bRng, mRng / 100.0, label);

                // 攻速
                double speedFactor = Math.Max(0.1, mSpd / 100.0);
                int finalInterval = (int)Math.Max(1, bTime / speedFactor);
                FieldInfo timeField = realParams.GetType().GetField("_TimeAction", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                timeField?.SetValue(realParams, finalInterval);

                DXLog.Write($"[Test_Mod] [{label}] _TimeAction -> {finalInterval} (Factor: {speedFactor})");
            }
        }

        /*private void CopyFiles(string file_name,string event_name)
        {
            try
            {
                
                // 寻找目标资源文件
                string sourceDir = Path.Combine(test_mod.ModPath, "ZX",file_name);
                //寻找目标拷贝目录
                string targetDir = Path.Combine(test_mod.ModPath,"ZXRules.dat");

                if (!File.Exists(sourceDir))
                {
                    DXLog.Write($"[Test_Mod] error:源文件夹不存在 {sourceDir}");
                    return;
                }
                else
                {
                    DXLog.Write($"[Test_Mod] 找到源文件 {sourceDir}");
                    File.Copy(sourceDir, targetDir, true);

                    if (!File.Exists(targetDir))
                    {
                        DXLog.Write($"[Test Mod] error:哦，牛批，这能没有的 {targetDir}");
                        return;
                    }

                    DXLog.Write($"[Test_Mod] 已复制文件到 {targetDir}");
                   
                }

                DXLog.Write("[Test_Mod] " + event_name + " 文件部署完成！");
            }
            catch (Exception ex)
            {
                DXLog.Write($"[Test_Mod] 文件拷贝失败: {ex.Message}");
            }
        }*/


        private void OnConfigChanged(ModConfig config)
        {
            //对于配置更改后的保存和更改
            _test_mod.SaveConfig();
            //老兵恢复满血方法不一定如何实现，暂时不做处理，预留一个位置

            DXLog.Write($"[Test_Mod] 配置已更改");
            
        }


        private void Unload()
        {
            Config.OnConfigChanged -= OnConfigChanged;
            
            _test_mod.SaveConfig();
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

        [ConfigOption("游侠-护甲值", ConfigOptionType.Slider, Category = "游侠属性", Order = 6)]
        [Range(0, 0.50, 0.01)]
        public double Ranger_ArmorAdd { get; set; } = 0;

        [ConfigOption("游侠-视野倍率", ConfigOptionType.Slider, Category = "游侠属性", Order = 7)]
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

        [ConfigOption("士兵-护甲值", ConfigOptionType.Slider, Category = "士兵属性", Order = 6)]
        [Range(0, 0.95, 0.01)]
        public double Soldier_ArmorAdd { get; set; } = 0;

        [ConfigOption("士兵-视野倍率", ConfigOptionType.Slider, Category = "士兵属性", Order = 7)]
        [Range(1, 200, 1)]
        public double Soldier_VisionMult { get; set; } = 100;
    }

}
