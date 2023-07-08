using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DeltaProxy
{
    public abstract class DatabaseBase<T> where T : DatabaseBase<T>, new()
    {
        public static T LoadDatabase(string filepath)
        {
            if (File.Exists($"db/{filepath}"))
            {
                return JsonConvert.DeserializeObject<T>(File.ReadAllText($"db/{filepath}"));
            }
            else
            {
                T cfg = new T();
                cfg.SaveDatabase(filepath);
                return cfg;
            }
        }

        public void SaveDatabase(string filepath)
        {
            var contents = JsonConvert.SerializeObject(this, Formatting.Indented);
            if (File.Exists($"db/{filepath}")) { File.Copy($"db/{filepath}", $"db/{filepath}.backup"); }
            File.WriteAllText($"db/{filepath}", contents);
        }
    }
}
