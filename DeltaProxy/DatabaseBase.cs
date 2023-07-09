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
        private string filename;

        public static T LoadDatabase(string filepath)
        {
            if (File.Exists($"db/{filepath}"))
            {
                var tmp = JsonConvert.DeserializeObject<T>(File.ReadAllText($"db/{filepath}"));
                tmp.filename = filepath;
                return tmp;
            }
            else
            {
                T cfg = new T();
                cfg.filename = filepath;
                cfg.SaveDatabase();
                return cfg;
            }
        }

        public void SaveDatabase()
        {
            lock (this)
            {
                var contents = JsonConvert.SerializeObject(this, Formatting.Indented);
                if (File.Exists($"db/{filename}")) { File.Copy($"db/{filename}", $"db/{filename}.backup", true); }
                File.WriteAllText($"db/{filename}", contents);
            }
        }
    }
}
