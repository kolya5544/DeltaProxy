using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DeltaProxy
{
    public abstract class ConfigBase<T> where T : ConfigBase<T>, new()
    {
        public static T LoadConfig(string filepath)
        {
            if (File.Exists($"conf/{filepath}"))
            {
                return JsonConvert.DeserializeObject<T>(File.ReadAllText($"conf/{filepath}"));
            }
            else
            {
                T cfg = new T();
                cfg.SaveConfig(filepath);
                return cfg;
            }
        }

        public void SaveConfig(string filepath)
        {
            File.WriteAllText($"conf/{filepath}", JsonConvert.SerializeObject(this, Formatting.Indented));
        }
    }
}
