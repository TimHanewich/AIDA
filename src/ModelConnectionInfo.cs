using System;
using TimHanewich.Foundry;

namespace AIDA
{
    public class ModelConnectionInfo
    {
        public string FoundryUrl {get; set;}
        public string ModelName {get; set;}

        //Auth: API key method
        public string? ApiKey {get; set;}

        //Auth: Service Principal method
        public string? TenantID {get; set;}
        public string? ClientID {get; set;}
        public string? ClientSecret {get; set;}
        public TokenCredential? AuthenticatedTokenCredentials {get; set;} //If already authenticated, store them here for use later

        public ModelConnectionInfo()
        {
            FoundryUrl = string.Empty;
            ModelName = string.Empty;
        }

        public ModelConnectionInfo(string foundry_url, string model_name)
        {
            FoundryUrl = foundry_url;
            ModelName = model_name;
        }

    }
}