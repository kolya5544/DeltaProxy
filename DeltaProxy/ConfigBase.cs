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
        private string filename;

        public static T LoadConfig(string filepath)
        {
            if (File.Exists($"conf/{filepath}"))
            {
                var tmp = JsonConvert.DeserializeObject<T>(File.ReadAllText($"conf/{filepath}"));
                tmp.filename = filepath;
                return tmp;
            }
            else
            {
                T cfg = new T();
                cfg.filename = filepath;
                cfg.SaveConfig();
                return cfg;
            }
        }

        public void SaveConfig()
        {
            lock (this)
            {
                File.WriteAllText($"conf/{filename}", JsonConvert.SerializeObject(this, Formatting.Indented));
            }
        }
    }
}
