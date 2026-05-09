using System;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json;
using System.Collections.Generic;
using TimHanewich.Foundry;
using TimHanewich.Foundry.OpenAI.Responses;

namespace AIDA
{
    public class AIDASettings
    {
        public ModelConnectionInfo? TextModel {get; set;} //The model we have for text generation (i.e. GPT-5.4)
        public ModelConnectionInfo? ImageModel {get; set;} //The model we have for image generation (i.e. gpt-image-2)

        //Text generation settings
        public Verbosity? VerbosityLevel {get; set;} //the amount of verbosity to use
        public ReasoningEffortLevel? ReasoningEffortLevel {get; set;} //the amount of reasoning effort to use

        //Formatting settings
        public string AssistantMessageColor { get; set; } //the spectre color all AI responses are in (https://spectreconsole.net/appendix/colors)

        //Tools enabled/disabled
        public bool WebSearchEnabled {get; set;} //the built-in web search
        public bool ShellEnabled {get; set;} //are shell commands (terminal) enabled

        public AIDASettings()
        {
            //Default
            TextModel = null;
            ImageModel = null;
            
            //Default settings
            VerbosityLevel = null;
            ReasoningEffortLevel = null;
            AssistantMessageColor = "navyblue";
            WebSearchEnabled = false;
            ShellEnabled = false;
        }

        

        private static string SavePath
        {
            get
            {
                return Path.Combine(Tools.ConfigDirectoryPath, "settings.json");
            }
        }

        public void Save()
        {
            System.IO.File.WriteAllText(SavePath, JsonConvert.SerializeObject(this, Formatting.Indented));
        }

        public static AIDASettings Load()
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