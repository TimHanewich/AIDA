using System;
using TimHanewich.AgentFramework;

namespace AIDA
{
    public class ModelConnection
    {
        public AzureOpenAICredentials? AzureOpenAIConnection { get; set; }
        public OllamaModel? OllamaModelConnection { get; set; }
        public bool Active { get; set; }

        public ModelConnection()
        {
            AzureOpenAIConnection = null;
            OllamaModelConnection = null;
        }

        public override string ToString()
        {
            if (AzureOpenAIConnection != null)
            {
                return AzureOpenAIConnection.URL + " (Azure OpenAI)";
            }
            else if (OllamaModelConnection != null)
            {
                return OllamaModelConnection.ModelIdentifier + " (Ollama)";
            }
            else
            {
                return "UNKNOWN MODEL CONNECTION TYPE";
            }
        }
    }
}