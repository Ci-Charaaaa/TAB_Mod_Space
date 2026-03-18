using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using DXVision;
using AX.ModLoader;
using AX.ModLoader.Config;
using System.Runtime.CompilerServices;


namespace MyModTest1
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
                //
            }

            test_config.OnConfigChanged += OnConfigChanged;

                throw new NotImplementedException();
        }

        private void OnConfigChanged(ModConfig config)
        {
            DXLog.Write($"[Test_Mod] 配置已更改！启用功能 更强的游侠: {test_config.EnableFeature1}");
            //
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
