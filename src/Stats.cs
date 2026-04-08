using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using TimHanewich.Foundry;
using TimHanewich.Foundry.OpenAI.Responses;

namespace AIDA
{
    public class ConsumptionEvent
    {
        public long Timestamp {get; set;}
        public int InputTokens {get; set;}
        public int OutputTokens {get; set;}
    }

    public class Stats
    {
        public List<ConsumptionEvent> ConsumptionEvents {get; set;}

        public Stats()
        {
            ConsumptionEvents = new List<ConsumptionEvent>();
        }

        private static string SavePath
        {
            get
            {
                string ConfigDir = Tools.ConfigDirectoryPath;
                string FullPath = Path.Combine(ConfigDir, "stats.json");
                return FullPath;
            }
        }

        public static Stats Load()
        {
            string path = SavePath;
            if (System.IO.File.Exists(path) == false)
            {
                return new Stats();
            }
            string content = System.IO.File.ReadAllText(path);
            Stats? ToReturn = null;
            try
            {
                ToReturn = JsonConvert.DeserializeObject<Stats>(content);
            }
            catch (Exception ex)
            {
                throw new Exception("Parsing of the contents of " + path + " failed! Msg: " + ex.Message);
            }
            if (ToReturn == null)
            {
                throw new Exception("stats.json did not parse for some reason.");
            }
            return ToReturn;
        }

        public void Save()
        {
            string path = SavePath;
            System.IO.File.WriteAllText(path, JsonConvert.SerializeObject(this, Formatting.Indented));
        }
    
        public void AddConsumptionEvent(ConsumptionEvent ce)
        {
            ConsumptionEvents.Add(ce);
        }

        public void AddConsumptionEvent(Response resp)
        {
            ConsumptionEvent ce = new ConsumptionEvent();
            ce.Timestamp = resp.CreatedAt.ToUnixTimeSeconds();
            ce.InputTokens = resp.InputTokensConsumed;
            ce.OutputTokens = resp.OutputTokensConsumed;
            AddConsumptionEvent(ce);
        }
    }
}