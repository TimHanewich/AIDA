using System;
using TimHanewich.Foundry;

namespace AIDA
{
    public class ModelConnection
    {
        
        //Configuration of the foundry resource and model name
        public string FoundryUrl {get; set;} //i.e. https://myfoundry.services.ai.azure.com
        public string ModelName {get; set;} //i.e. gpt-5.2

        //If they use an API key
        public string? ApiKey {get; set;} // if they choose to use an API key

        //if they use Entra Authentication
        public string? TenantID {get; set;}
        public string? ClientID {get; set;} 
        public string? ClientSecret {get; set;}

        

        public ModelConnection()
        {
            FoundryUrl = string.Empty;
            ModelName = string.Empty;
            ApiKey = null;
            TenantID = null;
            ClientID = null;
            ClientSecret = null;
        }

        public override string ToString()
        {
            if (ApiKey != null)
            {
                return ModelName + " (API key)";
            }
            else if (TenantID != null && ClientID != null && ClientSecret != null)
            {
                return ModelName = " (Entra ID Auth)";
            }
            else
            {
                return ModelName;
            }
        }
    }
}