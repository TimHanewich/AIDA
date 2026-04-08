using System;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json;
using System.Collections.Generic;
using TimHanewich.Foundry;

namespace AIDA
{
    public class AIDASettings
    {
        //Foundry URL
        public string? FoundryUrl {get; set;}

        //Foundry credentials - API Key (if they choose to use this)
        public string? ApiKey {get; set;}

        //Foundry Credentials - Entra ID Auth (if they choose to use this)
        public string? TenantID {get; set;}
        public string? ClientID {get; set;}
        public string? ClientSecret {get; set;}
        public TokenCredential? AuthenticatedTokenCredentials {get; set;} //previous authentication info (may be expired)

        //AI Model we are using within foundry
        public string? ModelName {get; set;} //the name of the model or deployment to be used, i.e. "gpt-5.2"

        //Formatting settings
        public string AssistantMessageColor { get; set; } //the spectre color all AI responses are in (https://spectreconsole.net/appendix/colors)

        //MSX Cookie
        public string? msx_cookie {get; set;}

        //Tools enabled/disabled
        public bool WebSearchEnabled {get; set;} //the built-in web search
        public bool FinancePackageEnabled { get; set; } //enables SEC.EDGAR
        public bool WeatherPackageEnabled { get; set; } //enabled check current weather
        public bool MsxPackageEnabled {get; set;} //interfacing with MSX
        public bool ShellEnabled {get; set;} //are shell commands (terminal) enabled

        public AIDASettings()
        {
            FoundryUrl = null;
            ApiKey = null;
            TenantID = null;
            ClientID = null;
            ClientSecret = null;
            ModelName = null;
            AssistantMessageColor = "navyblue";
            WebSearchEnabled = false;
            FinancePackageEnabled = false;
            WeatherPackageEnabled = false;
            MsxPackageEnabled = false;
            msx_cookie = null;
        }

        public FoundryResource PrepareFoundryResource()
        {
            if (FoundryUrl == null)
            {
                throw new Exception("Unable to prepare foundry resource: the URL was null!");
            }

            FoundryResource ToReturn = new FoundryResource(FoundryUrl);
            
            //if they did API Key method
            if (ApiKey != null)
            {
                ToReturn.ApiKey = ApiKey;
            }
            else //assume entra ID auth insteads
            {
                if (AuthenticatedTokenCredentials != null)
                {
                    ToReturn.AccessToken = AuthenticatedTokenCredentials.AccessToken;
                }
            }

            return ToReturn;
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