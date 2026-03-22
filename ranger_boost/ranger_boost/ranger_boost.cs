using AX.ModLoader;
using AX.ModLoader.Config;
using DXVision;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using ZX;
using HarmonyLib;


namespace ranger_boost
{
    public class Boost_Mod : IModEntry 
    {
        private Mod test_mod;

        public Unit_Mod_config ranger_config;
        public Unit_Mod_config soldier_config;

        public static Boost_Mod Instance;


        public override void OnLoad(Mod mod)
        {
            test_mod = mod;

            DXLog.Write($"[Test_Mod] 已加载！版本：{mod.Info.Version} gugugaga!");

            ranger_config = mod.RegisterConfig<Ranger_Mod_config>();
            soldier_config = mod.RegisterConfig<Soldier_Mod_config>();

            Instance = this;

            var harmony = new Harmony("com.charaa.test1");
            harmony.UnpatchAll("com.charaa.test1");
            harmony.PatchAll(Assembly.GetExecutingAssembly()); // 告诉 Harmony 扫描这个项目里的 [HarmonyPatch]

            DXLog.Write($"[Test_Mod] Harmony 补丁已应用！正在等待游戏逻辑初始化...,随时准备进行属性加强模块注入");

            ranger_config.OnConfigChanged += OnConfigChanged;
            soldier_config.OnConfigChanged += OnConfigChanged;

        }

        // 钩住 ApplyMods
        [HarmonyPatch(typeof(ZXDefaultParams), "ApplyMods")]
        public class InjectionPatch
        {
            public static void Postfix()
            {
                if (Instance == null) return;

                DXLog.Write("[Test_Mod] 开始执行多单位属性强行注入...");

                // 调用通用方法：传入不同的 ID、对应的 Config 实例、以及该兵种的原始数值
                ApplyUnitStats("Ranger", Instance.ranger_config, 60f, 4f, 6f, 8f);
                ApplyUnitStats("Soldier", Instance.soldier_config, 120f, 2.4f, 5f, 6f);
            }
        }

        //核心注入方法：根据实体ID扫描并修改属性
        // 核心注入方法：传入 ID、配置对象以及基础属性参考值
        public static void ApplyUnitStats(string entity_ID, Unit_Mod_config config, float baseLife, float baseSpeed, float baseRange, float baseVision)
        {
            var allParams = ZXDefaultParams.get_All();
            if (allParams == null) return;

            foreach (var p in allParams)
            {
                // 匹配 ID 并且确保是实体参数类
                if (p.ID == entity_ID && p is ZXEntityDefaultParams ep)
                {
                    DXLog.Write($"[Test_Mod] >>> 正在注入实体: {p.ID}");

                    try
                    {
                        // 1. 基础属性注入 (Life, Speed, Vision, Armor)
                        SetFieldSafe(ep, "_Life", baseLife, config.HealthyMult / 100.0);
                        SetFieldSafe(ep, "_RunSpeed", baseSpeed, config.SpeedMult / 100.0);
                        SetFieldSafe(ep, "_WatchRange", baseVision, config.VisionMult / 100.0);
                        SetFieldSafe(ep, "_Armor", (float)config.ArmorAdd, 1.0); // 护甲通常是直接加法

                        // 2. 战斗属性注入 (Damage, Range, Speed)
                        FieldInfo shellField = typeof(ZXEntityDefaultParams).GetField("_AttackCommandBasic", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                        object shellObj = shellField?.GetValue(ep);

                        if (shellObj != null)
                        {
                            FieldInfo paramsField = shellObj.GetType().GetField("_Params", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                            object realParams = paramsField?.GetValue(shellObj);

                            if (realParams != null)
                            {
                                // 伤害
                                UpdateIntField(realParams, "_Damage", (int)10, config.DamageMult / 100.0);
                                // 射程
                                UpdateFloatField(realParams, "_ActionRange", baseRange, config.RangeMult / 100.0);
                                // 攻击速度
                                UpdateIntField(realParams, "_TimeAction", 1, config.DamageSpeedMult / 1.0);

                                DXLog.Write($"[Test_Mod] [SUCCESS] {p.ID} 战斗属性注入完成！");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        DXLog.Write($"[Test_Mod] [ERROR] {entity_ID} 注入过程中崩溃: {ex.Message}");
                    }
                    // 找到目标后就不再继续扫这个实体的循环了
                    break;
                }
            }
        }

        //处理整数和浮点数字段的通用方法，带日志输出
        private static void UpdateIntField(object target, string fieldName, int baseVal, double mult)
        {
            FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (field != null)
            {
                int final = (int)(baseVal * mult);
                field.SetValue(target, final);
                DXLog.Write($"[Test_Mod] [OK] {fieldName} (Int32) -> {final}");
            }
        }
        private static void UpdateFloatField(object target, string fieldName, float baseVal, double mult)
        {
            FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (field != null)
            {
                float final = (float)(baseVal * mult);
                field.SetValue(target, final);
                DXLog.Write($"[Test_Mod] [OK] {fieldName} (Single) -> {final}");
            }
        }

        // 核心：带日志的安全注入工具
        private static void SetFieldSafe(ZXEntityDefaultParams target, string fieldName, float baseValue, double multiplier)
        {
            try
            {
                // 1. 获取字段信息
                FieldInfo field = typeof(ZXEntityDefaultParams).GetField(fieldName,
                    BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

                if (field == null)
                {
                    DXLog.Write($"[Test_Mod] [ERROR] 找不到字段: {fieldName}，请检查游戏版本是否变更了字段名。");
                    return;
                }

                // 2. 计算并转换
                double rawCalculated = baseValue * multiplier;

                // 根据字段的实际类型进行转换
                object finalValue;
                if (field.FieldType == typeof(float))
                {
                    finalValue = (float)rawCalculated;
                }
                else
                {
                    finalValue = Convert.ToInt32(rawCalculated);
                }

                // 3. 执行注入
                field.SetValue(target, finalValue);

                // 4. 同步修改 DefaultValues 
                if (target.DefaultValues != null)
                {
                    field.SetValue(target.DefaultValues, finalValue);
                }

                DXLog.Write($"[Test_Mod] [SUCCESS] 字段 {fieldName} 修改成功: {baseValue} -> {finalValue} (Type: {field.FieldType.Name})");
            }
            catch (Exception ex)
            {
                DXLog.Write($"[Test_Mod] [ERROR] 修改字段 {fieldName} 时发生异常: {ex.GetType().Name} - {ex.Message}");
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
            //对于配置更改后的加载
            test_mod.SaveConfig();

            DXLog.Write($"[Test_Mod] 配置已更改！启用功能 更强的游侠: {ranger_config.HealthyMult}");
            
        }


        private void Unload()
        {
            ranger_config.OnConfigChanged -= OnConfigChanged;
            soldier_config.OnConfigChanged -= OnConfigChanged;
            test_mod.SaveConfig();
        }


    }

    public abstract class Unit_Mod_config : ModConfig
    {
        public abstract double HealthyMult { get; set; }
        public abstract double DamageMult { get; set; }
        public abstract double RangeMult { get; set; }
        public abstract double DamageSpeedMult { get; set; }
        public abstract double SpeedMult { get; set; }
        public abstract double ArmorAdd { get; set; }
        public abstract double VisionMult { get; set; }

    }
    public class Ranger_Mod_config : Unit_Mod_config

    {
        //游侠属性调整
        [ConfigOption("游侠生命", ConfigOptionType.Slider,
        Description = "调整游侠生命值,不能小于0，最大500%",
        Category = "游戏性",
        Order = 1)]
        [Range(1, 500, 1)]
        public override double HealthyMult { get; set; } = 100;

        [ConfigOption("攻击倍率", ConfigOptionType.Slider, 
            Description = "调整游侠攻击", 
            Category = "游侠属性", 
            Order = 2)]
        [Range(1, 300, 1)]
        public override double DamageMult { get; set; } = 100;

        [ConfigOption("攻击范围", ConfigOptionType.Slider,
            Description = "调整游侠攻击范围",
            Category = "游侠属性",
            Order = 3)]
        [Range(1, 200, 1)]
        public override double RangeMult { get; set; } = 100;

        [ConfigOption("攻击速度", ConfigOptionType.Slider,
            Description = "调整游侠攻击速度",
            Category = "游侠属性",
            Order = 4)]
        [Range(1, 300, 1)]
        public override double DamageSpeedMult { get; set; } = 100;

        [ConfigOption("移动速度", ConfigOptionType.Slider, 
            Description = "调整游侠移动速度", 
            Category = "游侠属性", 
            Order = 5)]
        [Range(1, 200, 1)]
        public override double SpeedMult { get; set; } = 100;

        [ConfigOption("护甲值", ConfigOptionType.Slider, 
            Description = "调整游侠护甲", 
            Category = "游侠属性", 
            Order = 6)]
        [Range(0, 50, 1)]
        public override double ArmorAdd { get; set; } = 0;

        [ConfigOption("视野倍率", ConfigOptionType.Slider, 
            Description = "调整游侠视野", 
            Category = "游侠属性", 
            Order = 7)]
        [Range(1, 200, 1)]
        public override double VisionMult { get; set; } = 100;

    }

    public class Soldier_Mod_config : Unit_Mod_config
    {
        //士兵属性调整
        [ConfigOption("士兵生命", ConfigOptionType.Slider,
        Description = "调整士兵生命值,不能小于0，最大500%",
        Category = "游戏性",
        Order = 1)]
        [Range(1, 500, 1)]
        public override double HealthyMult { get; set; } = 100;
        [ConfigOption("攻击倍率", ConfigOptionType.Slider,
            Description = "调整士兵攻击",
            Category = "士兵属性",
            Order = 2)]
        [Range(1, 300, 1)]
        public override double DamageMult { get; set; } = 100;
        [ConfigOption("攻击范围", ConfigOptionType.Slider,
            Description = "调整士兵攻击范围",
            Category = "士兵属性",
            Order = 3)]
        [Range(1, 200, 1)]
        public override double RangeMult { get; set; } = 100;
        [ConfigOption("攻击速度", ConfigOptionType.Slider,
            Description = "调整士兵攻击速度",
            Category = "士兵属性",
            Order = 4)]
        [Range(1, 300, 1)]
        public override double DamageSpeedMult { get; set; } = 100;
        [ConfigOption("移动速度", ConfigOptionType.Slider,
            Description = "调整士兵移动速度",
            Category = "士兵属性",
            Order = 5)]
        [Range(1, 200, 1)]
        public override double SpeedMult { get; set; } = 100;

        [ConfigOption("护甲值", ConfigOptionType.Slider,
        Description = "调整士兵护甲",
        Category = "士兵属性",
        Order = 6)]
        [Range(0, 95, 1)]
        public override double ArmorAdd { get; set; } = 0;

        [ConfigOption("视野倍率", ConfigOptionType.Slider,
        Description = "调整士兵视野",
        Category = "士兵属性",
        Order = 7)]
        [Range(1, 200, 1)]
        public override double VisionMult { get; set; } = 100;
    }

}
