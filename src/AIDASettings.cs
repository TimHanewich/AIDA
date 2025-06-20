using System;
using TimHanewich.AgentFramework;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json;
using System.Collections.Generic;

namespace AIDA
{
    public class AIDASettings
    {
        public List<ModelConnection> ModelConnections { get; set; }
        public string AssistantMessageColor { get; set; } //the spectre color all AI responses are in (https://spectreconsole.net/appendix/colors)

        public AIDASettings()
        {
            AssistantMessageColor = "navyblue";
            ModelConnections = new List<ModelConnection>();
        }

        private static string SavePath
        {
            get
            {
                return Path.Combine(Program.ConfigDirectory, "settings.json");
            }
        }

        public void Save()
        {
            System.IO.File.WriteAllText(SavePath, JsonConvert.SerializeObject(this, Formatting.Indented));
        }

        public static AIDASettings Open()
        {
            if (System.IO.File.Exists(SavePath) == false)
            {
                return new AIDASettings();
            }
            else
            {
                string content = System.IO.File.ReadAllText(SavePath);
                if (content == "")
                {
                    return new AIDASettings();
                }
                AIDASettings? ToReturn = JsonConvert.DeserializeObject<AIDASettings>(content);
                if (ToReturn == null)
                {
                    return new AIDASettings();
                }
                return ToReturn;
            }
        }

        public ModelConnection? ActiveModelConnection
        {
            get
            {
                foreach (ModelConnection mc in ModelConnections)
                {
                    if (mc.Active)
                    {
                        return mc;
                    }
                }
                return null;
            }
        }

    }
}