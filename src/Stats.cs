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

        public ConsumptionEvent()
        {
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        }

        public ConsumptionEvent(string model, int input_tokens, int output_tokens)
        {
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            Model = model;
            InputTokens = input_tokens;
            OutputTokens = output_tokens;
        }
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

            //Per-model breakdown
            Dictionary<string, (int Input, int Output)> perModel = new Dictionary<string, (int, int)>();
            foreach (ConsumptionEvent ce in ConsumptionEvents)
            {
                string model = ce.Model ?? "unknown";
                if (perModel.ContainsKey(model))
                {
                    var existing = perModel[model];
                    perModel[model] = (existing.Input + ce.InputTokens, existing.Output + ce.OutputTokens);
                }
                else
                {
                    perModel[model] = (ce.InputTokens, ce.OutputTokens);
                }
            }
            if (perModel.Count > 0)
            {
                Console.WriteLine();
                AnsiConsole.MarkupLine("[underline]CUMULATIVE CONSUMPTION BY MODEL[/]");
                foreach (var kvp in perModel)
                {
                    AnsiConsole.MarkupLine("[bold]" + Markup.Escape(kvp.Key) + "[/]: " + kvp.Value.Input.ToString("#,##0") + " input tokens, " + kvp.Value.Output.ToString("#,##0") + " output tokens");
                }
            }

            //Ask which model to see daily breakdown for (loop until they choose to go back)
            List<string> modelChoices = perModel.Keys.Where(k => k != "unknown").ToList();
            while (modelChoices.Count > 0)
            {
                Console.WriteLine();
                SelectionPrompt<string> modelPrompt = new SelectionPrompt<string>();
                modelPrompt.Title("Which model would you like to see further consumption details on?");
                modelPrompt.AddChoices(modelChoices);
                modelPrompt.AddChoice("Back");
                string selectedModel = AnsiConsole.Prompt(modelPrompt);

                if (selectedModel == "Back") break;

                //Build a table of daily consumption for the selected model over the past 10 days
                Table table = new Table();
                table.Title("[bold][underline]" + Markup.Escape(selectedModel) + " - Daily Token Consumption (Last 10 Days)[/][/]");
                table.AddColumn("Date");
                table.AddColumn("Input Tokens");
                table.AddColumn("Output Tokens");

                for (int i = 9; i >= 0; i--)
                {
                    DateTime day = DateTime.UtcNow.AddDays(i * -1);
                    int inputTokens = 0;
                    int outputTokens = 0;
                    foreach (ConsumptionEvent ce in ConsumptionEvents)
                    {
                        if ((ce.Model ?? "unknown") != selectedModel) continue;
                        DateTimeOffset consumptionTS = DateTimeOffset.FromUnixTimeSeconds(ce.Timestamp);
                        if (consumptionTS.Year == day.Year && consumptionTS.Month == day.Month && consumptionTS.Day == day.Day)
                        {
                            inputTokens += ce.InputTokens;
                            outputTokens += ce.OutputTokens;
                        }
                    }
                    table.AddRow(day.ToString("MMMM d, yyyy"), inputTokens.ToString("#,##0"), outputTokens.ToString("#,##0"));
                }

                Console.WriteLine();
                AnsiConsole.Write(table);
            }

            //Last break
            Console.WriteLine();
        }
    }
}