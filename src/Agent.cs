using HtmlAgilityPack;
using TimHanewich.Foundry;
using TimHanewich.Foundry.OpenAI.Responses;

namespace AIDA
{
    public class Agent
    {
        //Private vars
        private string? PreviousResponseID;
        private int _CumulativeInputTokens;
        private int _CumulativeOutputTokens;

        //Microsoft Foundry connection info
        //This will come "pre-baked" with a URL and API key/token
        public FoundryResource? FoundryConnection {get; set;}

        //Call configuration
        public string ModelName {get; set;}
        public List<Function> Functions {get; set;}
        public List<Exchange> Inputs;
        public int CumulativeInputTokens
        {
            get
            {
                return _CumulativeInputTokens;
            }
        }
        public int CumulativeOutputTokens
        {
            get
            {
                return _CumulativeOutputTokens;
            }
        }

        public Agent()
        {
            FoundryConnection = null;
            PreviousResponseID = null;
            _CumulativeInputTokens = 0;
            _CumulativeOutputTokens = 0;
            ModelName = string.Empty;
            Functions = new List<Function>();
            Inputs = new List<Exchange>();
        }

        public async Task<Exchange[]> PromptAsync()
        {
            if (FoundryConnection == null)
            {
                throw new Exception("Foundry resource not provided - unable to prompt!");
            }

            //Construct response request
            ResponseRequest rr = new ResponseRequest();
            rr.Model = ModelName;
            rr.PreviousResponseID = PreviousResponseID; //null is fine too!
            rr.Tools.AddRange(Functions); //Add in all the custom functions
            rr.Inputs.AddRange(Inputs);

            //Call!
            Response r;
            try
            {
                r = await FoundryConnection.CreateResponseAsync(rr);
                
                
            }
            catch (Exception ex)
            {
                throw new Exception("Requesting response from Foundry failed! Msg: " + ex.Message);
            }

            //Handle that response
            PreviousResponseID = r.Id;
            _CumulativeInputTokens = _CumulativeInputTokens + r.InputTokensConsumed;
            _CumulativeOutputTokens = _CumulativeOutputTokens + r.OutputTokensConsumed;

            //"flush" the inputs we had before
            Inputs.Clear();

            //Return
            return r.Outputs;
        }

        public void ClearHistory()
        {
            PreviousResponseID = null;
        }

    }
}