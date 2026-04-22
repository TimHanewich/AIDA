using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Spectre.Console;
using TimHanewich.Foundry;
using TimHanewich.Foundry.OpenAI.Responses;

namespace AIDA
{
    //View a "ConsumptionEvent" as an individual Response that was provided by the API (one ResponseRequest, one Request... so one "unit" of consumption)
    public class ConsumptionEvent
    {
        public long Timestamp {get; set;}          //Unix timestamp, in seconds
        public string? Model {get; set;}           //the name of the model that served it
        public int InputTokens {get; set;}         //Input tokens consumed in this 1 response request
        public int OutputTokens {get; set;}        //Output tokens consumed in this 1 response request
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
            ce.Model = resp.Model;
            ce.Timestamp = resp.CreatedAt.ToUnixTimeSeconds();
            ce.InputTokens = resp.InputTokensConsumed;
            ce.OutputTokens = resp.OutputTokensConsumed;
            AddConsumptionEvent(ce);
        }

        public void PrintReport()
        {
            //Header
            AnsiConsole.MarkupLine("[bold][underline][blue]AIDA STAT REPORT[/][/][/]");

            //Cumulative
            Console.WriteLine();
            AnsiConsole.MarkupLine("[underline]CUMULATIVE CONSUMPTION[/]");
            int CumInput = 0;
            int CumOutput = 0;
            foreach (ConsumptionEvent ce in ConsumptionEvents)
            {
                CumInput = CumInput + ce.InputTokens;
                CumOutput = CumOutput + ce.OutputTokens;
            }
            AnsiConsole.MarkupLine("Input Tokens: " + CumInput.ToString("#,##0"));
            AnsiConsole.MarkupLine("Output Tokens: " + CumOutput.ToString("#,##0"));

            //Prepare a list of last 7 days
            List<DateTime> Last7Days = new List<DateTime>();
            for (int i = 0; i < 7; i++)
            {
                Last7Days.Add(DateTime.UtcNow.AddDays(i * -1));
            }

            //pull data per day
            Console.WriteLine();
            AnsiConsole.MarkupLine("[underline]Consumption Breakdown, Last 7 Days[/]");
            foreach (DateTime day in Last7Days)
            {

                //Tally up tokens
                int InputThisDay = 0;
                int OutputThisDay = 0;
                foreach (ConsumptionEvent ce in ConsumptionEvents)
                {
                    DateTimeOffset consumptionTS = DateTimeOffset.FromUnixTimeSeconds(ce.Timestamp);
                    if (consumptionTS.Year == day.Year && consumptionTS.Month == day.Month && consumptionTS.Day == day.Day)
                    {
                        InputThisDay = InputThisDay + ce.InputTokens;
                        OutputThisDay = OutputThisDay + ce.OutputTokens;
                    }
                }

                //Print
                AnsiConsole.MarkupLine("[bold]" + day.Month.ToString() + "/" + day.Day.ToString() + "/" + day.Year.ToString() + "[/]: " + InputThisDay.ToString("#,##0") + " input tokens, " + OutputThisDay.ToString("#,##0") + " output tokens");
            }

            //Last break
            Console.WriteLine();
        }
    }
}