using System;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json;
using System.Collections.Generic;
using TimHanewich.Foundry;

namespace AIDA
{
    public class AIDASettings
    {
        //AI Model relevant stuff
        public FoundryResource? FoundryConnection {get; set;} //only a single foundry connection allowed
        public string? ModelName {get; set;} //the name of the model or deployment to be used, i.e. "gpt-5.2"

        //Formatting settings
        public string AssistantMessageColor { get; set; } //the spectre color all AI responses are in (https://spectreconsole.net/appendix/colors)

        //Packages enabled or disabled
        public bool FinancePackageEnabled { get; set; } //enables SEC.EDGAR
        public bool WeatherPackageEnabled { get; set; } //enabled check current weather

        public AIDASettings()
        {
            FoundryConnection = null;
            ModelName = null;
            AssistantMessageColor = "navyblue";
            FinancePackageEnabled = false;
            WeatherPackageEnabled = false;
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
    }
}