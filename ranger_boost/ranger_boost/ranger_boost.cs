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
    public class Test_Mod : IModEntry 
    {
        private Mod test_mod;
        private Test_Mod_config test_config;
 

        public override void OnLoad(Mod mod)
        {
            test_mod = mod;

            DXLog.Write($"[Test_Mod] 已加载！版本：{mod.Info.Version} gugugaga!");

            test_config = mod.RegisterConfig<Test_Mod_config>();

            if (test_config.EnableFeature1)
            {
                DXLog.Write($"[Test_Mod] 游侠增强功能 已启用！");
                //目前功能就是把ZXRules.dat放到mod目录下覆盖原文件，后续可以增加更多功能
                CopyFiles("youxia.dat","游侠增强");
            }
            else
            {
                DXLog.Write($"[Test_Mod] 游侠增强功能 已禁用！");
                CopyFiles("original.dat","恢复原版");
                //DeleteFiles("ZXRules.dat","游侠增强");
            }

            var harmony = new Harmony("com.charaa.test1");
            harmony.PatchAll(Assembly.GetExecutingAssembly()); // 告诉 Harmony 扫描这个项目里的 [HarmonyPatch]
            test_config.OnConfigChanged += OnConfigChanged;
            Console.WriteLine("[StatModifierMod] Mod已初始化，等待数据加载...");

        }

        // 钩住 ApplyMods
        [HarmonyPatch(typeof(ZXDefaultParams), "ApplyMods")]
        public class InjectionPatch
        {
            // 使用 Postfix，确保在游戏逻辑跑完后，执行强行覆盖
            public static void Postfix()
            {
                DXLog.Write("[StatModifierMod] 检测到关卡数据初始化，开始执行强行注入...");
                ApplyMemoryPatches();
            }
        }

        public static void ApplyMemoryPatches()
        {
            var allParams = ZXDefaultParams.get_All();
            if (allParams == null) return;

            foreach (var p in allParams)
            {
                // 确保 ID 匹配
                if (p.ID == "Ranger")
                {
                    if (p is ZXEntityDefaultParams entityParams)
                    {
                        try
                        {
                            // 获取生命值字段
                            FieldInfo lifeField = entityParams.GetType().GetField("_Life", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

                            if (lifeField != null)
                            {
                                // --- 关键修正：去掉 f，改用整数 100 ---
                                // 既然日志显示原数值是 80 (Int32)，我们必须给它 Int32
                                int newLifeValue = 100;
                                lifeField.SetValue(entityParams, newLifeValue);

                                // 同步修改 DefaultValues 里的字段
                                if (entityParams.DefaultValues != null)
                                {
                                    FieldInfo defLifeField = entityParams.DefaultValues.GetType().GetField("_Life", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                                    if (defLifeField != null)
                                    {
                                        defLifeField.SetValue(entityParams.DefaultValues, newLifeValue);
                                    }
                                }

                                DXLog.Write($"[StatModifierMod] 成功！Ranger 生命已强行锁定为: {newLifeValue}");
                            }
                        }
                        catch (Exception ex)
                        {
                            DXLog.Write($"[StatModifierMod] 注入失败: {ex.Message}");
                        }
                    }
                    break;
                }
            }

        }



        private void CopyFiles(string file_name,string event_name)
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
        }

        /*private void DeleteFiles(string file_name,string event_name)
        {
            try
            {
                string targetDir = Path.Combine(test_mod.ModPath, file_name);
                if (File.Exists(targetDir))
                {
                    File.Delete(targetDir);
                    DXLog.Write($"[Test_Mod] 已删除文件 {targetDir}");
                }
                else
                {
                    DXLog.Write($"[Test_Mod] 文件不存在，无需删除 {targetDir}");
                }
                DXLog.Write("[Test_Mod] " + event_name + " 文件清理完成！");
            }
            catch (Exception ex)
            {
                DXLog.Write($"[Test_Mod] 文件删除失败: {ex.Message}");
            }
        }*/

        private void OnConfigChanged(ModConfig config)
        {
            //对于配置更改后的判断
            if (test_config.EnableFeature1)
            {
                CopyFiles("youxia.dat", "游侠增强");
            }
            else
            {
                CopyFiles("original.dat", "恢复原版");
            }

            DXLog.Write($"[Test_Mod] 配置已更改！启用功能 更强的游侠: {test_config.EnableFeature1}");
            
        }

        private void Unload()
        {
            test_config.OnConfigChanged -= OnConfigChanged;
            test_mod.SaveConfig();
        }


    }
    
    public class Test_Mod_config : ModConfig
    {

       [ConfigOption("启用功能 更强的游侠", ConfigOptionType.Checkbox,
       Description = "启用 游侠增强 功能",
       Category = "常规",
       Order = 1)]
        public bool EnableFeature1 { get; set; } = true;

    }

}
