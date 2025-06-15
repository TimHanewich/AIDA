using System;
using TimHanewich.AgentFramework;

namespace AIDA
{
    public class AIDASettings
    {
        public AzureOpenAICredentials Credentials { get; set; }
        public string AssistantMessageColor { get; set; }

        public AIDASettings()
        {
            Credentials = new AzureOpenAICredentials();
            AssistantMessageColor = "navyblue";
        }
    }
}