﻿using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Net;
using Spectre.Console;
using System.Web;
using System.Collections.Specialized;
using HtmlAgilityPack;
using System.Reflection;
using TimHanewich.AgentFramework;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;
using System.IO.Compression;
using SecuritiesExchangeCommission.Edgar;
using SecuritiesExchangeCommission.Edgar.Data;
using AIDA.Finance;

namespace AIDA
{
    public class Program
    {
        #region "GLOBAL VARIABLES"

        public static Agent AGENT { get; set; }
        public static AIDASettings SETTINGS { get; set; }

        public static string ConfigDirectory
        {
            get
            {
                return System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AIDA");
            }
        }

        #endregion


        public static void Main(string[] args)
        {
            RunAsync().Wait();
        }

        public static async Task RunAsync()
        {

            #region "Setup"

            //Ensure ConsoleOutput is in UTF-8 so it can show bullet points
            //I noticed when you publish and run the exe, it defaults to System.Text.OSEncoding as the ConsoleEncoding
            //When it goes to OSEncoding, the bullet points do not print
            Console.OutputEncoding = System.Text.Encoding.UTF8;

            //Does config directory exist? if not, make it
            if (System.IO.Directory.Exists(ConfigDirectory) == false)
            {
                System.IO.Directory.CreateDirectory(ConfigDirectory);
            }

            //Retrieve settings
            SETTINGS = AIDASettings.Open();

            //If settings has no credentials, show warning message
            if (SETTINGS.ActiveModelConnection == null)
            {
                AnsiConsole.MarkupLine("[red]:warning: Warning - no active model connection specified! Use command '[bold]settings[/]' to update your model info before proceeding.[/]");
            }

            //Plug in SEC info in case it is used later
            IdentificationManager.Instance.AppName = "AIDA";
            IdentificationManager.Instance.AppVersion = "1.0";
            IdentificationManager.Instance.Email = "admin@gmail.com";

            #endregion

            //Create the agent
            AGENT = new Agent();

            //Add system message
            List<string> SystemMessage = new List<string>();
            SystemMessage.Add("You are AIDA, Artificial Intelligence Desktop Assistant. Your role is to be a friendly and helpful assistant. Speak in a playful, lighthearted, and fun manner.");
            SystemMessage.Add("Do not use emojis.");
            string sysmsg = "";
            foreach (string s in SystemMessage)
            {
                sysmsg = sysmsg + s + "\n\n";
            }
            sysmsg = sysmsg.Substring(0, sysmsg.Length - 2);
            AGENT.Messages.Add(new Message(Role.system, sysmsg));

            //Add welcoming message
            string opening_msg = "Hi, I'm AIDA, and I'm here to help! What can I do for you?";
            AGENT.Messages.Add(new Message(Role.assistant, opening_msg));
            AnsiConsole.MarkupLine("[bold][" + SETTINGS.AssistantMessageColor + "]" + opening_msg + "[/][/]");

            //Add link to project
            AnsiConsole.MarkupLine("[gray][italic]github.com/TimHanewich/AIDA[/][/]");

            //Version just below
            Assembly ass = Assembly.GetExecutingAssembly();
            Version? v = ass.GetName().Version;
            if (v != null)
            {
                AnsiConsole.MarkupLine("[gray][italic]AIDA version " + v.ToString().Substring(0, v.ToString().Length - 2) + "[/][/]");
            }

            //Infinite chat
            Console.WriteLine();
            while (true)
            {
            //Collect input
            Input:

                //Collect the raw input
                string? input = null;
                while (input == null)
                {
                    Console.Write("> ");
                    input = Console.ReadLine();
                    Console.WriteLine();
                }

                //Handle special inputs
                if (input.ToLower() == "help")
                {
                    AnsiConsole.MarkupLine("Here are the commands you can use:");
                    Console.WriteLine();
                    AnsiConsole.MarkupLine("[bold]clear[/] - clear the chat history.");
                    AnsiConsole.MarkupLine("[bold]tokens[/] - check token consumption for this session.");
                    AnsiConsole.MarkupLine("[bold]settings[/] - Open AIDA's settings menu");
                    AnsiConsole.MarkupLine("[bold]tools[/] - list all tools AIDA has available to it.");
                    AnsiConsole.MarkupLine("[bold]save[/] - Save chat history to a local file.");
                    AnsiConsole.MarkupLine("[bold]load[/] - Save chat history to a local file.");
                    Console.WriteLine();
                    goto Input;
                }
                if (input.ToLower() == "tokens")
                {

                    //Print tokens
                    AnsiConsole.MarkupLine("[blue][underline]Cumulative Tokens so Far[/][/]");
                    AnsiConsole.MarkupLine("[blue]Prompt tokens: [bold]" + AGENT.CumulativePromptTokens.ToString("#,##0") + "[/][/]");
                    AnsiConsole.MarkupLine("[blue]Completion tokens: [bold]" + AGENT.CumulativeCompletionTokens.ToString("#,##0") + "[/][/]");
                    Console.WriteLine();

                    //Print costs
                    float input_cost_per_1M = 2.00f; //in US dollars
                    float output_cost_per_1M = 8.00f; //in US dollars
                    float input_costs = (input_cost_per_1M / 1000000f) * AGENT.CumulativePromptTokens;
                    float output_costs = (output_cost_per_1M / 1000000f) * AGENT.CumulativeCompletionTokens;
                    AnsiConsole.MarkupLine("[blue][underline]Token Cost Estimates[/][/]");
                    AnsiConsole.MarkupLine("[blue]Input token costs: [bold]$" + input_costs.ToString("#,##0.00") + "[/][/]");
                    AnsiConsole.MarkupLine("[blue]Output token costs: [bold]$" + output_costs.ToString("#,##0.00") + "[/][/]");
                    Console.WriteLine();

                    //print the Cost Assumptions
                    AnsiConsole.MarkupLine("[gray][underline]Cost Assumptions Used[/][/]");
                    AnsiConsole.MarkupLine("[gray]Input = $" + input_cost_per_1M.ToString("#,##0.00") + " per 1M tokens[/]");
                    AnsiConsole.MarkupLine("[gray]Output = $" + output_cost_per_1M.ToString("#,##0.00") + " per 1M tokens[/]");

                    Console.WriteLine();
                    goto Input;
                }
                else if (input.ToLower() == "settings") //Where the config files are
                {

                    //Present settings menu and allow them to change things
                    SettingsMenu();

                    //Now that it has changed, refresh settings
                    SETTINGS = AIDASettings.Open();

                    Console.WriteLine();
                    goto Input;
                }
                else if (input.ToLower() == "tools")
                {
                    AnsiConsole.MarkupLine("[underline]AIDA's Available Tools[/]");
                    foreach (Tool t in DetermineAvailableTools())
                    {
                        AnsiConsole.MarkupLine("[bold][blue]" + t.Name + "[/][/] - [gray]" + t.Description + "[/]");
                    }
                    Console.WriteLine();
                    goto Input;
                }
                else if (input.ToLower() == "clear")
                {
                    AGENT.Messages.Clear(); //clear the message history
                    AGENT.Messages.Add(new Message(Role.system, sysmsg)); //but add the system message back (need that!)
                    AnsiConsole.MarkupLine("[blue][bold]Chat history cleared.[/][/]");
                    Console.WriteLine();
                    goto Input;
                }
                else if (input.ToLower().Trim() == "save")
                {
                    string FileName = "chat-" + Guid.NewGuid().ToString().Replace("-", "") + ".json";
                    string DownloadsPath = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
                    string SavePath = Path.Combine(DownloadsPath, FileName);

                    //Save
                    AnsiConsole.Markup("[gray]saving...[/] ");
                    System.IO.File.WriteAllText(SavePath, JsonConvert.SerializeObject(AGENT.Messages, Formatting.Indented));
                    AnsiConsole.MarkupLine("[green]success![/]");

                    //Say it was successful
                    Console.WriteLine();
                    AnsiConsole.MarkupLine("[green]Chat history saved to:[/]");
                    AnsiConsole.MarkupLine("[green]" + SavePath + "[/]");

                    Console.WriteLine();
                    goto Input;
                }
                else if (input.ToLower().Trim() == "load")
                {
                    string FilePath = AnsiConsole.Ask<string>("What is the path to the chat file?");

                    //Clean the file path
                    FilePath = FilePath.Replace("\"", "");

                    //Is it legit?
                    if (System.IO.File.Exists(FilePath) == false)
                    {
                        AnsiConsole.MarkupLine("[red]That is not a valid file![/]");
                        goto Input;
                    }

                    //Is it a JSON file?
                    if (System.IO.Path.GetExtension(FilePath).ToLower() != ".json")
                    {
                        AnsiConsole.MarkupLine("[red]That is not a JSON file![/]");
                        goto Input;
                    }

                    //Open it
                    string FileContent = System.IO.File.ReadAllText(FilePath);
                    Message[]? messages;
                    try
                    {
                        messages = JsonConvert.DeserializeObject<Message[]>(FileContent);
                    }
                    catch (Exception ex)
                    {
                        AnsiConsole.MarkupLine("[red]Error while deserializing file content! Message: " + Markup.Escape(ex.Message) + "[/]");
                        goto Input;
                    }

                    //If messages are empty
                    if (messages == null)
                    {
                        AnsiConsole.MarkupLine("[red]Messages did not deserialize properly for an unknown reason.[/]");
                        goto Input;
                    }

                    //Load it up!
                    AGENT.Messages = messages.ToList();
                    AnsiConsole.MarkupLine("[green][bold]" + messages.Length.ToString("#,##0") + " messages loaded from " + Markup.Escape(System.IO.Path.GetFileName(FilePath)) + "![/][/]");
                    Console.WriteLine();

                    goto Input;
                }

                //It did not trigger a special command, so add it to the history, it will be passed to the AI!
                AGENT.Messages.Add(new Message(Role.user, input));

            //Prompt
            Prompt:

                //Plug in the correct agent
                if (SETTINGS.ActiveModelConnection != null)
                {
                    if (SETTINGS.ActiveModelConnection.AzureOpenAIConnection != null)
                    {
                        AGENT.Model = SETTINGS.ActiveModelConnection.AzureOpenAIConnection;
                    }
                    else if (SETTINGS.ActiveModelConnection.OllamaModelConnection != null)
                    {
                        AGENT.Model = SETTINGS.ActiveModelConnection.OllamaModelConnection;
                    }
                }
                else
                {
                    AnsiConsole.MarkupLine("[red]:warning: Warning - no active model connection specified! Use command '[bold]settings[/]' to update your model info before proceeding.[/]");
                }

                #region "Plug in tools"

                //CLEAR TOOLS (so we don't re-add them)
                //The clearing and re-adding process will happen each time so they can update the tools available on the fly
                AGENT.Tools.Clear();

                //Add them back
                AGENT.Tools = DetermineAvailableTools().ToList();

                #endregion

                //Prompt the model
                AnsiConsole.Markup("[gray][italic]thinking... [/][/]");
                Message response;
                try
                {
                    response = await AGENT.PromptAsync(9999);
                }
                catch (Exception ex)
                {
                    AnsiConsole.MarkupLine("[red]Uh oh! There was an issue when prompting the underlying model. Message: " + Markup.Escape(ex.Message) + "[/]");
                    Console.WriteLine();
                    Console.WriteLine();
                    AnsiConsole.Markup("[italic][gray]Press enter to try another input... [/][/]");
                    Console.ReadLine();
                    AGENT.Messages.Remove(AGENT.Messages.Last()); //Remove the last one (it failed)
                    goto Input;
                }

                //Add it
                AGENT.Messages.Add(response); //Add response to message array
                Console.WriteLine();

                //Write content if there is some
                if (response.Content != null)
                {
                    if (response.Content != "")
                    {
                        PrintAIMessage(response.Content, SETTINGS.AssistantMessageColor);
                        Console.WriteLine();
                    }
                }

                //Handle tool calls
                if (response.ToolCalls.Length > 0)
                {
                    foreach (ToolCall tc in response.ToolCalls)
                    {
                        AnsiConsole.Markup("[gray][italic]calling tool '" + tc.ToolName + "'... [/][/]");
                        string tool_call_response_payload = "";

                        //Call to the tool and save the response from that tool
                        if (tc.ToolName == "check_weather")
                        {

                            //Get latitude
                            float? latitude = null;
                            JProperty? prop_latitude = tc.Arguments.Property("latitude");
                            if (prop_latitude != null)
                            {
                                latitude = Convert.ToSingle(prop_latitude.Value.ToString());
                            }

                            //Get longitude
                            float? longitude = null;
                            JProperty? prop_longitude = tc.Arguments.Property("longitude");
                            if (prop_longitude != null)
                            {
                                longitude = Convert.ToSingle(prop_longitude.Value.ToString());
                            }

                            //Are either not there?
                            if (latitude == null || longitude == null)
                            {
                                tool_call_response_payload = "You did not provide the latitude and longtiude correctly! You must provide the lat & long of the location you want to check the weather for."; //mesasge for the AI (I hope the AI will know what the lat and long is)
                            }
                            else
                            {
                                AnsiConsole.Markup("[gray][italic]Checking weather for " + latitude.Value.ToString() + ", " + longitude.Value.ToString() + "... [/][/]");
                                tool_call_response_payload = await CheckWeather(latitude.Value, longitude.Value);
                            }
                        }
                        else if (tc.ToolName == "save_txt_file")
                        {
                            //Get file name
                            string file_name = "dummy.txt";
                            JProperty? prop_file_name = tc.Arguments.Property("file_name");
                            if (prop_file_name != null)
                            {
                                file_name = prop_file_name.Value.ToString() + ".txt";
                            }

                            //Get file content
                            string file_content = "(dummy content)";
                            JProperty? prop_file_content = tc.Arguments.Property("file_content");
                            if (prop_file_content != null)
                            {
                                file_content = prop_file_content.Value.ToString();
                            }

                            //Save file
                            tool_call_response_payload = SaveFile(file_name, file_content);
                        }
                        else if (tc.ToolName == "read_file")
                        {
                            //Get file path
                            string file_path = "?";
                            JProperty? prop_file_path = tc.Arguments.Property("file_path");
                            if (prop_file_path != null)
                            {
                                file_path = prop_file_path.Value.ToString();
                            }

                            tool_call_response_payload = ReadFile(file_path);
                        }
                        else if (tc.ToolName == "check_current_time")
                        {
                            tool_call_response_payload = "The current date and time is " + DateTime.Now.ToString();
                        }
                        else if (tc.ToolName == "read_webpage")
                        {
                            //Get URL
                            string? url = null;
                            JProperty? prop_url = tc.Arguments.Property("url");
                            if (prop_url != null)
                            {
                                url = prop_url.Value.ToString();
                            }

                            //Open page
                            if (url != null)
                            {
                                tool_call_response_payload = await ReadWebpage(url);
                            }
                            else
                            {
                                tool_call_response_payload = "Unable to read webpage because the 'url' parameter was not successfully provided by the AI.";
                            }
                        }
                        else if (tc.ToolName == "open_folder")
                        {
                            //Get the folder_path variable
                            JProperty? prop_folder_path = tc.Arguments.Property("folder_path");
                            if (prop_folder_path == null)
                            {
                                tool_call_response_payload = "You did not provide the folder path correctly! Provide it as the 'folder_path' property."; //message back to the AI.
                            }
                            else
                            {
                                string folder_path = prop_folder_path.Value.ToString();
                                tool_call_response_payload = OpenFolder(folder_path);
                            }
                        }
                        else if (tc.ToolName == "get_cik")
                        {

                            //Get symbol
                            string? symbol = null;
                            JProperty? prop_symbol = tc.Arguments.Property("symbol");
                            if (prop_symbol != null)
                            {
                                symbol = prop_symbol.Value.ToString();
                            }

                            //Handle
                            if (symbol != null)
                            {
                                //Print status
                                AnsiConsole.Markup("[gray][italic]getting CIK for '" + symbol.Trim().ToUpper() + "'... [/][/]");

                                try
                                {
                                    string cik = await SecToolkit.GetCompanyCikFromTradingSymbolAsync(symbol);
                                    tool_call_response_payload = cik;
                                }
                                catch (Exception ex)
                                {
                                    tool_call_response_payload = "Attempt to find CIK from trading symbol failed. Exception message: " + ex.Message;
                                }
                            }
                            else
                            {
                                tool_call_response_payload = "To use the this tool you must provide the symbol parameter!";
                            }

                        }
                        else if (tc.ToolName == "search_financial_data")
                        {
                            //Get CIK
                            int? CIK = null;
                            JProperty? prop_CIK = tc.Arguments.Property("CIK");
                            if (prop_CIK != null)
                            {
                                CIK = Convert.ToInt32(prop_CIK.Value.ToString());
                            }

                            //Get search term
                            string? search_term = null;
                            JProperty? prop_search_term = tc.Arguments.Property("search_term");
                            if (prop_search_term != null)
                            {
                                search_term = prop_search_term.Value.ToString();
                            }

                            //Perform the query
                            if (CIK != null && search_term != null)
                            {

                                //Print status
                                AnsiConsole.Markup("[gray][italic] searching '" + CIK.Value.ToString() + "' for '" + search_term + "' facts... [/][/]");

                                //Perform query (get the company's facts)
                                CompanyFactsQuery? cfq = null;
                                try
                                {
                                    cfq = await SECBandwidthManager.CompanyFactsQueryAsync(CIK.Value);
                                }
                                catch (Exception ex)
                                {
                                    tool_call_response_payload = "Unable to perform Company Facts Query via SEC API: " + ex.Message;
                                }

                                //If we successfully got data
                                if (cfq != null)
                                {
                                    //Pull facts via keyword search
                                    List<Fact> SearchResultFacts = new List<Fact>();
                                    foreach (Fact f in cfq.Facts)
                                    {
                                        if (f.Tag.ToLower().Contains(search_term.ToLower()) || f.Label.ToLower().Contains(search_term.ToLower()) || f.Description.ToLower().Contains(search_term.ToLower())) //If it matches the search results
                                        {
                                            SearchResultFacts.Add(f);
                                        }
                                    }

                                    //Prepare string
                                    string ToGive = "Financial facts available for " + CIK.Value.ToString() + " that match your search: ";
                                    foreach (Fact f in SearchResultFacts)
                                    {
                                        //Figure out last reported date
                                        DateTime MostRecentReported = DateTime.Now.AddYears(-999);
                                        foreach (FactDataPoint fdp in f.DataPoints)
                                        {
                                            if (fdp.End > MostRecentReported)
                                            {
                                                MostRecentReported = fdp.End;
                                            }
                                        }

                                        ToGive = ToGive + "\n" + "- " + f.Tag + ": " + f.Description + " (last reported " + MostRecentReported.ToShortDateString() + ")";
                                    }

                                    tool_call_response_payload = ToGive;
                                }
                            }
                            else
                            {
                                tool_call_response_payload = "To use this tool you must provide the CIK and search term parameter!";
                            }

                        }
                        else if (tc.ToolName == "get_financial_data")
                        {
                            //Get parameter: CIK
                            int? CIK = null;
                            JProperty? prop_CIK = tc.Arguments.Property("CIK");
                            if (prop_CIK != null)
                            {
                                CIK = Convert.ToInt32(prop_CIK.Value.ToString());
                            }

                            //Get fact
                            string? fact = null;
                            JProperty? prop_fact = tc.Arguments.Property("fact");
                            if (prop_fact != null)
                            {
                                fact = prop_fact.Value.ToString();
                            }

                            //Query
                            if (CIK.HasValue && fact != null)
                            {
                                //Print more info
                                AnsiConsole.Markup("[gray][italic]retrieving '" + fact + "' data for '" + CIK.Value.ToString() + "'... [/][/]");

                                //Query the data
                                CompanyFactsQuery? cfq = null;
                                try
                                {
                                    cfq = await SECBandwidthManager.CompanyFactsQueryAsync(CIK.Value);
                                }
                                catch (Exception ex)
                                {
                                    tool_call_response_payload = "Unable to perform Company Facts Query via SEC API: " + ex.Message;
                                }

                                //If we got it, continue
                                if (cfq != null)
                                {
                                    //Find the fact
                                    Fact? DesiredFact = null;
                                    foreach (Fact f in cfq.Facts)
                                    {
                                        if (f.Tag == fact)
                                        {
                                            DesiredFact = f;
                                        }
                                    }

                                    //If we found it, provide it
                                    if (DesiredFact != null)
                                    {

                                        //Build a list of ones we will collect
                                        List<FactDataPoint> AllFDPs = new List<FactDataPoint>();
                                        AllFDPs.AddRange(DesiredFact.DataPoints);

                                        //Sort from NEWEST to OLDEST
                                        //We do this because, if there are multiple reports of the same time period, the most recent one is correct as it is probably the amdneded/updated value
                                        List<FactDataPoint> AllFDPsNewestToOldest = new List<FactDataPoint>();
                                        while (AllFDPs.Count > 0)
                                        {
                                            FactDataPoint winner = AllFDPs[0];
                                            foreach (FactDataPoint fdp in AllFDPs)
                                            {
                                                if (fdp.End > winner.End)
                                                {
                                                    winner = fdp;
                                                }
                                            }
                                            AllFDPsNewestToOldest.Add(winner);
                                            AllFDPs.Remove(winner);
                                        }

                                        //Build a list of UNIQUE data points (don't allow on same day)
                                        List<FactDataPoint> UniqueFDPs = new List<FactDataPoint>();
                                        foreach (FactDataPoint fdp in AllFDPsNewestToOldest)
                                        {

                                            //Check if unique list already has this...
                                            bool AlreadyHasIt = false;
                                            foreach (FactDataPoint havealready in UniqueFDPs)
                                            {
                                                if (havealready.End == fdp.End)
                                                {
                                                    AlreadyHasIt = true;
                                                }
                                            }

                                            //If we do not already have this day, add it
                                            if (AlreadyHasIt == false)
                                            {
                                                UniqueFDPs.Add(fdp);
                                            }
                                        }

                                        //Stitch together what we will give to the AI
                                        string ToGive = "Historical data for fact '" + DesiredFact.Tag + "' for company '" + cfq.EntityName + "' (CIK " + cfq.CIK.ToString() + "):";
                                        foreach (FactDataPoint fdp in UniqueFDPs)
                                        {

                                            //Determine how to express period
                                            string PeriodPart = "";
                                            if (fdp.Start.HasValue) //if it is for a period
                                            {
                                                //Determine Year/Quarter End
                                                string YearQuarterEnd = "";
                                                if (fdp.Period == FiscalPeriod.FiscalYear)
                                                {
                                                    YearQuarterEnd = "Year End";
                                                }
                                                else
                                                {
                                                    YearQuarterEnd = "Quarter End";
                                                }

                                                //Figure out date part
                                                string DatePart = fdp.End.ToShortDateString();

                                                PeriodPart = YearQuarterEnd + " " + DatePart;
                                            }
                                            else //If it is just a snap in time, just do the date
                                            {
                                                PeriodPart = fdp.End.ToShortDateString();
                                            }

                                            //The value
                                            string ValuePart = fdp.Value.ToString("#,##0");

                                            //Add it
                                            ToGive = ToGive + "\n" + "- " + PeriodPart + ": " + ValuePart;
                                        }

                                        //Give it to the AI
                                        tool_call_response_payload = ToGive;
                                    }
                                    else
                                    {
                                        tool_call_response_payload = "Fact '" + fact + "' not found for company '" + cfq.EntityName + "' (CIK " + cfq.CIK.ToString() + ")";
                                    }
                                }
                            }
                            else
                            {
                                tool_call_response_payload = "To use this tool you must provide the CIK and fact parameter!";
                            }

                        }
                        else
                        {
                            AnsiConsole.MarkupLine("[red]Model called tool '" + tc.ToolName + "' but AIDA is not properly configured to handle that! Oops, sorry about that! We dropped the ball (not the AI). Please contact support.[/]");
                            tool_call_response_payload = "Tool '" + tc.ToolName + "' is not working right now. Sorry.";
                        }

                        //Append tool response to messages
                        Message ToolResponseMessage = new Message();
                        ToolResponseMessage.Role = Role.tool;
                        ToolResponseMessage.ToolCallID = tc.ID;
                        ToolResponseMessage.Content = tool_call_response_payload;
                        AGENT.Messages.Add(ToolResponseMessage);

                        //Confirm completion of tool call
                        AnsiConsole.MarkupLine("[gray][italic]complete[/][/]");
                    }

                    //Prompt right away (do not ask for user for input yet)
                    goto Prompt;
                }



            } //END INFINITE CHAT



        }



        #region "TOOLS FOR THE AI"

        public static async Task<string> CheckWeather(float latitude, float longitude)
        {
            string url = "https://api.open-meteo.com/v1/forecast?latitude=" + latitude.ToString() + "&longitude=" + longitude.ToString() + "&current=temperature_2m,relative_humidity_2m,precipitation,rain,apparent_temperature,is_day,showers,snowfall,weather_code,cloud_cover,pressure_msl,surface_pressure,wind_speed_10m,wind_direction_10m,wind_gusts_10m&temperature_unit=fahrenheit";
            HttpClient hc = new HttpClient();
            HttpResponseMessage resp = await hc.GetAsync(url);
            string content = await resp.Content.ReadAsStringAsync();
            if (resp.StatusCode != HttpStatusCode.OK)
            {
                throw new Exception("Request to open-meteo.com returned code '" + resp.StatusCode.ToString() + "'. Msg: " + content);
            }
            return content; //Just return the entire body
        }

        public static string SaveFile(string file_name, string file_content)
        {
            string DestinationDirectory = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            string DestinationPath = System.IO.Path.Combine(DestinationDirectory, file_name);
            System.IO.File.WriteAllText(DestinationPath, file_content);
            return "File successfully saved to '" + DestinationPath + "'. Explicitly tell the user where the file was saved in confirming it was saved (tell the full file path).";
        }

        public static async Task<string> ReadWebpage(string url)
        {
            HttpClient hc = new HttpClient();
            HttpRequestMessage req = new HttpRequestMessage();
            req.Method = HttpMethod.Get;
            req.RequestUri = new Uri(url);
            req.Headers.Add("User-Agent", "AIDA/1.0.0");
            hc.Timeout = new TimeSpan(0, 1, 0); // 1 minute timeout
            AnsiConsole.Markup("[gray][italic]reading '" + url + "'... [/][/]");
            HttpResponseMessage resp = await hc.SendAsync(req);
            if (resp.StatusCode != HttpStatusCode.OK)
            {
                return "Attempt to read the web page came back with status code '" + resp.StatusCode.ToString() + "', so unfortunately it cannot be read (wasn't 200 OK)";
            }
            string content = await resp.Content.ReadAsStringAsync();

            //Convert raw HTML to text
            HtmlDocument doc = new HtmlDocument();
            doc.LoadHtml(content);
            string PlainText = doc.DocumentNode.InnerText;

            return PlainText;
        }

        public static string ReadFile(string path)
        {
            //Print what file we are reading
            string FileName = System.IO.Path.GetFileName(path);
            string FullPath = System.IO.Path.GetFullPath(path);
            AnsiConsole.Markup("[gray][italic]reading file '" + Markup.Escape(FullPath) + "'... [/][/]");

            //Does file exist?
            if (System.IO.File.Exists(path) == false)
            {
                return "File with path '" + path + "' does not exist!";
            }

            //Handle based on what type of file it is
            if (path.ToLower().EndsWith(".pdf"))
            {
                string FullTxt = "";
                PdfDocument doc = PdfDocument.Open(path);
                foreach (UglyToad.PdfPig.Content.Page p in doc.GetPages())
                {
                    string txt = ContentOrderTextExtractor.GetText(p);
                    FullTxt = FullTxt + txt + "\n\n";
                }
                if (FullTxt.Length > 0)
                {
                    FullTxt = FullTxt.Substring(0, FullTxt.Length - 2); //Strip out trailing two new lines
                }
                return FullTxt;
            }
            else if (path.ToLower().EndsWith(".zip"))
            {
                return "Cannot read the raw content of a .zip folder!";
            }
            else if (path.ToLower().EndsWith(".docx") || path.ToLower().EndsWith(".doc"))
            {
                return ReadWordDocument(path);
            }
            else if (path.ToLower().EndsWith(".xlsx") || path.ToLower().EndsWith(".xls"))
            {
                return "Cannot read an excel document";
            }
            else if (path.ToLower().EndsWith(".pptx") || path.ToLower().EndsWith(".ppt"))
            {
                return "Cannot read a PowerPoint deck.";
            }
            else //every other file
            {
                return System.IO.File.ReadAllText(path);
            }
        }

        public static string OpenFolder(string path)
        {
            //Print message
            string FolderName = System.IO.Path.GetFileName(path);
            string FullPath = System.IO.Path.GetFullPath(path);
            AnsiConsole.Markup("[gray][italic]opening folder '" + Markup.Escape(FullPath) + "'... [/][/]");

            //Is it a real directory?
            if (System.IO.Directory.Exists(path) == false)
            {
                return "'" + path + "' is not a directory!";
            }

            //Get the stuff in it
            string[] folders = System.IO.Directory.GetDirectories(path);
            string[] files = System.IO.Directory.GetFiles(path);

            //Put them in a variable
            string ToReturn = "The directory '" + path + "' contains the following:" + "\n";

            //Files
            ToReturn = ToReturn + "\n" + "Files:" + "\n";
            foreach (string file in files)
            {
                string? name = System.IO.Path.GetFileName(file);
                if (name != null)
                {
                    ToReturn = ToReturn + "\"" + name + "\"" + "\n";
                }
            }

            //Folders
            ToReturn = ToReturn + "\n" + "Child directories (folders):" + "\n";
            foreach (string folder in folders)
            {
                string? name = System.IO.Path.GetFileName(folder);
                if (name != null)
                {
                    ToReturn = ToReturn + "\"" + name + "\"" + "\n";
                }
            }

            return ToReturn;
        }

        #endregion

        #region "UTILITIES"

        public static string ReadWordDocument(string path)
        {
            //Get the RAW content (the document.xml file)
            string RawXmlContent = "";
            try
            {
                FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read);
                MemoryStream ms = new MemoryStream();
                fs.CopyTo(ms);
                ZipArchive za = new ZipArchive(ms, ZipArchiveMode.Read);
                foreach (ZipArchiveEntry zae in za.Entries)
                {
                    if (zae.FullName == "word/document.xml") //the file that contains the document content itsef
                    {
                        Stream EntryStream = zae.Open();
                        StreamReader sr = new StreamReader(EntryStream);
                        string RawText = sr.ReadToEnd();
                        RawXmlContent = RawText;
                    }
                }
            }
            catch (Exception ex)
            {
                return "There was an error while trying to open word document '" + path + "'. Exception message: " + ex.Message;
            }

            //Now that we found something, pick it apart (if we did find something that is)
            string ToReturn = "";
            if (RawXmlContent == "")
            {
                ToReturn = "Unable to read Word document content.";
            }
            else
            {
                //Extract content, line by line
                string[] parts = RawXmlContent.Split("<w:t>", StringSplitOptions.None);
                for (int t = 1; t < parts.Length; t++)
                {
                    string ThisPart = parts[t];
                    int ClosingTagLocation = ThisPart.IndexOf("</w:t>");
                    if (ClosingTagLocation > -1)
                    {
                        string TextContent = ThisPart.Substring(0, ClosingTagLocation);
                        ToReturn = ToReturn + TextContent + "\n";
                    }
                }

                //Trim ending newline
                ToReturn = ToReturn.TrimEnd('\n');
            }

            return ToReturn;
        }

        public static string MarkdownToSpectre(string markdown)
        {

            string ToReturn = markdown;

            //First, look for bolds (**)
            int OnIndex = 0;
            while (true)
            {
                int DoubleStarLocation1 = ToReturn.IndexOf("**", OnIndex);
                if (DoubleStarLocation1 != -1)
                {
                    int DoubleStarLocation2 = ToReturn.IndexOf("**", DoubleStarLocation1 + 2);
                    if (DoubleStarLocation2 != -1)
                    {
                        //Replace first "**" with "[bold]"
                        string PartBefore = ToReturn.Substring(0, DoubleStarLocation1);
                        string PartAfter = ToReturn.Substring(DoubleStarLocation1 + 2);
                        ToReturn = PartBefore + "[bold]" + PartAfter;

                        //Find second "**" again
                        DoubleStarLocation2 = ToReturn.IndexOf("**", DoubleStarLocation1);
                        PartBefore = ToReturn.Substring(0, DoubleStarLocation2);
                        PartAfter = ToReturn.Substring(DoubleStarLocation2 + 2);
                        ToReturn = PartBefore + "[/]" + PartAfter;

                        //Update OnIndex
                        OnIndex = DoubleStarLocation2;
                    }
                    else
                    {
                        break;
                    }
                }
                else
                {
                    break;
                }
            }

            //Look for italics "*"
            OnIndex = 0;
            while (true)
            {
                int StarLocation1 = ToReturn.IndexOf("*", OnIndex);
                if (StarLocation1 != -1)
                {
                    int StarLocation2 = ToReturn.IndexOf("*", StarLocation1 + 1);
                    if (StarLocation2 != -1)
                    {
                        //Replace first star with "[italic]"
                        string PartBefore = ToReturn.Substring(0, StarLocation1);
                        string PartAfter = ToReturn.Substring(StarLocation1 + 1);
                        ToReturn = PartBefore + "[italic]" + PartAfter;

                        //Find second "*" again and replace with "[/]"
                        StarLocation2 = ToReturn.IndexOf("*", StarLocation1);
                        PartBefore = ToReturn.Substring(0, StarLocation2);
                        PartAfter = ToReturn.Substring(StarLocation2 + 1);
                        ToReturn = PartBefore + "[/]" + PartAfter;


                        //Update OnIndex
                        OnIndex = StarLocation2;
                    }
                    else
                    {
                        break;
                    }
                }
                else
                {
                    break;
                }
            }

            //Headings
            string[] lines = ToReturn.Split("\n", StringSplitOptions.None);
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                if (line.StartsWith("# ") || line.StartsWith("## ") || line.StartsWith("### ") || line.StartsWith("#### "))
                {
                    int SpaceLocation = line.IndexOf(" ");
                    if (SpaceLocation != -1)
                    {
                        string ReplacementLine = "[underline]" + line.Substring(SpaceLocation + 1) + "[/]";
                        lines[i] = ReplacementLine;
                    }
                }
            }
            //Now re-stitch together
            ToReturn = "";
            foreach (string line in lines)
            {
                ToReturn = ToReturn + line + "\n";
            }
            if (ToReturn.Length > 0)
            {
                ToReturn = ToReturn.Substring(0, ToReturn.Length - 1); //remove last one we added
            }

            //bullet points
            lines = ToReturn.Split("\n", StringSplitOptions.None);
            for (int i = 0; i < lines.Length; i++)
            {
                if (lines[i].StartsWith("- "))
                {
                    lines[i] = "• " + lines[i].Substring(2);
                }
                else if (lines[i].StartsWith("  - ")) //sub bullet
                {
                    lines[i] = "  ‣ " + lines[i].Substring(4);
                }
            }
            //Now re-stitch together
            ToReturn = "";
            foreach (string line in lines)
            {
                ToReturn = ToReturn + line + "\n";
            }
            if (ToReturn.Length > 0)
            {
                ToReturn = ToReturn.Substring(0, ToReturn.Length - 1); //remove last one we added
            }


            return ToReturn;
        }

        public static void PrintAIMessage(string message, string color = "navyblue")
        {
            //Convert the markdown it gave to spectre and AnsiConsole it out
            string ToDisplay = message;
            ToDisplay = Markup.Escape(ToDisplay); //Make it save to have []

            //Print
            try
            {
                string SpectreFormat = MarkdownToSpectre(ToDisplay);
                Console.WriteLine();
                AnsiConsole.MarkupLine("[" + color + "]" + SpectreFormat + "[/]");
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine("[yellow]There was an error while displaying the response with formatting. Displaying it below normally instead.[/]");
                Console.WriteLine();
                AnsiConsole.MarkupLine("[yellow]Error message: " + Markup.Escape(ex.Message) + "[/]");
                Console.WriteLine();
                Console.WriteLine(message);
            }
        }

        public static void SettingsMenu()
        {

            //Loop until selected out
            while (true)
            {

                //Print header
                Console.WriteLine();
                AnsiConsole.MarkupLine("[bold][underline]AIDA SETTINGS MENU[/][/]");

                //AIDA version
                Assembly ass = Assembly.GetExecutingAssembly();
                Version? v = ass.GetName().Version;
                if (v != null)
                {
                    AnsiConsole.MarkupLine("AIDA version [bold]" + v.ToString().Substring(0, v.ToString().Length - 2) + "[/]");
                }

                //Config directory
                AnsiConsole.MarkupLine("Config directory: [bold]" + ConfigDirectory + "[/]");

                //Model info
                if (SETTINGS.ActiveModelConnection == null)
                {
                    AnsiConsole.MarkupLine("Active Model: not specified!");
                }
                else
                {
                    AnsiConsole.MarkupLine("Active Model: " + SETTINGS.ActiveModelConnection.ToString());
                }

                //Assistant color
                AnsiConsole.MarkupLine("AI Assistant Msg Color: [bold]" + SETTINGS.AssistantMessageColor + "[/] ([" + SETTINGS.AssistantMessageColor + "]looks like this[/])");

                //Ask what to do
                Console.WriteLine();
                SelectionPrompt<string> SettingToDo = new SelectionPrompt<string>();
                SettingToDo.Title("What do you want to do?");
                SettingToDo.AddChoice("Add/Change/Delete a model connection");
                SettingToDo.AddChoice("Change Assistant Message Color");
                SettingToDo.AddChoice("Enable/Disable a tool package");
                SettingToDo.AddChoice("Save & Continue");
                string SettingToDoAnswer = AnsiConsole.Prompt(SettingToDo);

                //Handle what to do
                if (SettingToDoAnswer == "Add/Change/Delete a model connection")
                {

                    //Build model connection table
                    Table ModelTable = new Table();
                    ModelTable.Border(TableBorder.Rounded);
                    ModelTable.AddColumn("Name");
                    ModelTable.AddColumn("Type");
                    ModelTable.AddColumn("Active");
                    foreach (ModelConnection mc in SETTINGS.ModelConnections)
                    {
                        //prepare vars
                        string vName = "";
                        string dType = "";
                        string dActive = "";

                        //name
                        vName = mc.Name;

                        //Plug in vars
                        if (mc.AzureOpenAIConnection != null)
                        {
                            dType = "Azure OpenAI";
                        }
                        else if (mc.OllamaModelConnection != null)
                        {
                            dType = "Ollama";
                        }

                        //Plug in active?
                        if (mc.Active)
                        {
                            dActive = "ACTIVE";
                        }
                        else
                        {
                            dActive = "";
                        }

                        //Add row
                        ModelTable.AddRow(vName, dType, dActive);
                    }

                    //Print the table
                    AnsiConsole.MarkupLine("[underline][bold]Stored Model Connections[/][/]");
                    AnsiConsole.Write(ModelTable);

                    //Ask what to do
                    SelectionPrompt<string> ModelActionQuestion = new SelectionPrompt<string>();
                    ModelActionQuestion.Title("What do you want to do?");
                    ModelActionQuestion.AddChoice("Add model connection");
                    ModelActionQuestion.AddChoice("Change active model connection");
                    ModelActionQuestion.AddChoice("Delete model connection");
                    ModelActionQuestion.AddChoice("[gray]Nothing - continue[/]");
                    string ModelActionQuestionAnswer = AnsiConsole.Prompt(ModelActionQuestion);

                    //Handle
                    if (ModelActionQuestionAnswer == "Add model connection")
                    {
                        //Ask what type of model to add
                        SelectionPrompt<string> WhatToAdd = new SelectionPrompt<string>();
                        WhatToAdd.Title("What type of model connection do you want to add?");
                        WhatToAdd.AddChoice("Azure OpenAI");
                        WhatToAdd.AddChoice("Ollama");
                        string WhatToAddAnswer = AnsiConsole.Prompt(WhatToAdd);

                        if (WhatToAddAnswer == "Azure OpenAI")
                        {
                            AnsiConsole.MarkupLine("Ok, let's add your Azure OpenAI connection.");
                            string URL = AnsiConsole.Ask<string>("URL endpoint to your model?");
                            string KEY = AnsiConsole.Ask<string>("API Key?");
                            string NAME = AnsiConsole.Ask<string>("What do you want to call this connection (a custom name)?");
                            ModelConnection newmc = new ModelConnection();
                            newmc.Name = NAME;
                            newmc.Active = false;
                            newmc.AzureOpenAIConnection = new AzureOpenAICredentials(URL, KEY);
                            SETTINGS.ModelConnections.Add(newmc);
                            AnsiConsole.Markup("[green]Connection added![/] [italic][gray]enter to continue[/][/]"); Console.ReadLine();
                        }
                        else if (WhatToAddAnswer == "Ollama")
                        {
                            AnsiConsole.MarkupLine("Ok, let's add your Ollama connection.");
                            string ModelIdentifier = AnsiConsole.Ask<string>("What is your model identifier (i.e. \"qwen3:0.6b\")?");
                            string NAME = AnsiConsole.Ask<string>("What do you want to call this connection (a custom name)?");
                            ModelConnection newmc = new ModelConnection();
                            newmc.Name = NAME;
                            newmc.Active = false;
                            newmc.OllamaModelConnection = new OllamaModel(ModelIdentifier);
                            SETTINGS.ModelConnections.Add(newmc);
                            AnsiConsole.Markup("[green]Connection added! [italic][gray]enter to continue[/][/][/]"); Console.ReadLine();
                        }
                        else
                        {
                            AnsiConsole.MarkupLine("[red]I am sorry, I cannot handle that yet.[/]");
                        }
                    }
                    else if (ModelActionQuestionAnswer == "Change active model connection")
                    {

                        //Ask which one to make active?
                        if (SETTINGS.ModelConnections.Count > 0)
                        {
                            SelectionPrompt<string> ModelToMakeActive = new SelectionPrompt<string>();
                            ModelToMakeActive.Title("Which connection do you want to make active?");
                            foreach (ModelConnection mc in SETTINGS.ModelConnections)
                            {
                                ModelToMakeActive.AddChoice(mc.ToString());
                            }
                            string ModelToMakeActiveAnswer = AnsiConsole.Prompt(ModelToMakeActive);

                            //Make it active
                            foreach (ModelConnection mc in SETTINGS.ModelConnections)
                            {
                                if (mc.ToString() == ModelToMakeActiveAnswer)
                                {
                                    mc.Active = true;
                                }
                                else
                                {
                                    mc.Active = false;
                                }
                            }

                            //Print what is now active
                            if (SETTINGS.ActiveModelConnection != null)
                            {
                                AnsiConsole.Markup("[green]Active model connection updated![/] [italic][gray]enter to continue...[/][/]");
                                Console.ReadLine();
                            }
                        }
                        else
                        {
                            AnsiConsole.Markup("[red]No model connections added! Add some first.[/]");
                            Console.ReadLine();
                        }
                    }
                    else if (ModelActionQuestionAnswer == "Delete model connection")
                    {
                        if (SETTINGS.ModelConnections.Count > 0)
                        {
                            //Build question
                            SelectionPrompt<string> ToDeleteQuestion = new SelectionPrompt<string>();
                            ToDeleteQuestion.Title("Which connection do you want to delete?");
                            foreach (ModelConnection mc in SETTINGS.ModelConnections)
                            {
                                ToDeleteQuestion.AddChoice(mc.ToString());
                            }

                            //Ask
                            string ToDeleteAnswer = AnsiConsole.Prompt(ToDeleteQuestion);

                            //Select
                            ModelConnection? ToDelete = null;
                            foreach (ModelConnection mc in SETTINGS.ModelConnections)
                            {
                                if (mc.ToString() == ToDeleteAnswer)
                                {
                                    ToDelete = mc;
                                }
                            }

                            if (ToDelete != null)
                            {
                                //Print some info about it
                                AnsiConsole.MarkupLine("[bold]Delete a Model[/]");
                                AnsiConsole.MarkupLine("[bold]Name:[/] " + ToDelete.Name);
                                if (ToDelete.AzureOpenAIConnection != null)
                                {
                                    AnsiConsole.MarkupLine("[bold]Type: [/]Azure OpenAI");
                                    AnsiConsole.MarkupLine("[bold]URL:[/] " + ToDelete.AzureOpenAIConnection.URL);
                                    AnsiConsole.MarkupLine("[bold]API Key:[/] " + ToDelete.AzureOpenAIConnection.ApiKey);
                                }
                                else if (ToDelete.OllamaModelConnection != null)
                                {
                                    AnsiConsole.MarkupLine("[bold]Type: [/]Ollama");
                                    AnsiConsole.MarkupLine("[bold]Identifier: [/] " + ToDelete.OllamaModelConnection.ModelIdentifier);
                                }

                                //Confirm
                                SelectionPrompt<string> DeleteConfirmation = new SelectionPrompt<string>();
                                DeleteConfirmation.Title("Are you sure you want to delete this connection?");
                                DeleteConfirmation.AddChoice("Yes");
                                DeleteConfirmation.AddChoice("No");
                                Console.WriteLine();
                                string DeleteConfirmationAnswer = AnsiConsole.Prompt(DeleteConfirmation);
                                if (DeleteConfirmationAnswer == "Yes")
                                {
                                    SETTINGS.ModelConnections.Remove(ToDelete);
                                    AnsiConsole.Markup("[green]Model deleted![/] [italic][gray]enter to continue...[/][/]");
                                    Console.ReadLine();
                                }
                            }
                        }
                        else
                        {
                            AnsiConsole.Markup("[red]No model connections added yet.[/]");
                            Console.ReadLine();
                        }
                    }
                }

                else if (SettingToDoAnswer == "Change Assistant Message Color")
                {
                    AnsiConsole.MarkupLine("Visit here to see the available colors: [bold]https://spectreconsole.net/appendix/colors[/]");
                    string NewColor = AnsiConsole.Ask<string>("New color?");
                    try
                    {
                        AnsiConsole.MarkupLine("Future AI messages will be in [" + NewColor + "]this color[/]");
                        SETTINGS.AssistantMessageColor = NewColor;
                    }
                    catch
                    {
                        AnsiConsole.MarkupLine("[red]That didn't work! Make sure it is a valid color and try again.[/]");
                    }
                    AnsiConsole.Markup("[gray][italic]enter to continue... [/][/]");
                    Console.ReadLine();
                }
                else if (SettingToDoAnswer == "Enable/Disable a tool package")
                {

                    //Prepare packages question (multi selection prompt)
                    MultiSelectionPrompt<string> PackagesQuestion = new MultiSelectionPrompt<string>();
                    PackagesQuestion.Title("What tool packages do you want enabled?");
                    PackagesQuestion.NotRequired(); //selecting none is fine!
                    PackagesQuestion.AddChoice("Weather");
                    PackagesQuestion.AddChoice("Finance");

                    //Defaults
                    if (SETTINGS.FinancePackageEnabled)
                    {
                        PackagesQuestion.Select("Finance");
                    }
                    if (SETTINGS.WeatherPackageEnabled)
                    {
                        PackagesQuestion.Select("Weather");
                    }

                    //Ask
                    List<string> PackagesToEnable = AnsiConsole.Prompt(PackagesQuestion);

                    //Enable/Disable: Finance
                    if (PackagesToEnable.Contains("Finance"))
                    {
                        SETTINGS.FinancePackageEnabled = true;
                    }
                    else
                    {
                        SETTINGS.FinancePackageEnabled = false;
                    }

                    //Enable/Disable: Weather
                    if (PackagesToEnable.Contains("Weather"))
                    {
                        SETTINGS.WeatherPackageEnabled = true;
                    }
                    else
                    {
                        SETTINGS.WeatherPackageEnabled = false;
                    }

                    //Confirm
                    AnsiConsole.MarkupLine("[green][bold]" + PackagesToEnable.Count.ToString() + " packages enabled[/][/]");
                }
                else if (SettingToDoAnswer == "Save & Continue")
                {
                    AnsiConsole.Markup("[gray]Saving settings... [/]");
                    SETTINGS.Save();
                    AnsiConsole.MarkupLine("[green]saved![/]");
                    return; //break out of while loop!
                }
                else
                {
                    AnsiConsole.MarkupLine("[red]Sorry, I can't handle that yet![/]");
                    Console.ReadLine();
                }
            }
        }

        public static Tool[] DetermineAvailableTools()
        {
            List<Tool> ToReturn = new List<Tool>();

            //Add tool: save text file
            Tool tool_savetxtfile = new Tool("save_txt_file", "Save a text file to the user's computer.");
            tool_savetxtfile.Parameters.Add(new ToolInputParameter("file_name", "The name of the file, WITHOUT the '.txt' file extension at the end."));
            tool_savetxtfile.Parameters.Add(new ToolInputParameter("file_content", "The content of the .txt file (raw text)."));
            ToReturn.Add(tool_savetxtfile);

            //Add tool: read file
            Tool tool_readfile = new Tool("read_file", "Read the contents of a file of any type (txt, pdf, word document, etc.) from the user's computer");
            tool_readfile.Parameters.Add(new ToolInputParameter("file_path", "The path to the file on the computer, for example 'C:\\Users\\timh\\Downloads\\notes.txt' or '.\\notes.txt' or 'notes.txt'"));
            ToReturn.Add(tool_readfile);

            //Add tool: check current time
            Tool tool_checkcurrenttime = new Tool("check_current_time", "Check the current date and time right now.");
            ToReturn.Add(tool_checkcurrenttime);

            //Add tool: open web page
            Tool tool_readwebpage = new Tool("read_webpage", "Read the contents of a particular web page.");
            tool_readwebpage.Parameters.Add(new ToolInputParameter("url", "The specific URL of the webpage to read."));
            ToReturn.Add(tool_readwebpage);

            //Add tool: Open Folder
            Tool tool_OpenFolder = new Tool("open_folder", "Open a folder (directory) to see its contents (files and child folders).");
            tool_OpenFolder.Parameters.Add(new ToolInputParameter("folder_path", "Path of the folder, i.e. 'C:\\Users\\timh\\Downloads\\MyFolder' or '/home/tim/Downloads/MyFolder/'"));
            ToReturn.Add(tool_OpenFolder);

            //Add finance package?
            if (SETTINGS.FinancePackageEnabled)
            {
                //Symbol to CIK
                Tool tool_SymbolToCik = new Tool("get_cik", "Get the CIK (Central Index Key) for a company based on its stock symbol.");
                tool_SymbolToCik.Parameters.Add(new ToolInputParameter("symbol", "Stock symbol, i.e. 'MSFT'."));
                ToReturn.Add(tool_SymbolToCik);

                //Search available financial data
                Tool tool_search_financial_data = new Tool("search_financial_data", "Search for available XBRL facts the company has reported before (i.e. 'AssetsCurrent', 'Liabilities', etc).");
                tool_search_financial_data.Parameters.Add(new ToolInputParameter("CIK", "The company's central index key (CIK), i.e. '1655210'", "number"));
                tool_search_financial_data.Parameters.Add(new ToolInputParameter("search_term", "The term to search for, i.e. 'revenue' or 'assets' or 'advertising'."));
                ToReturn.Add(tool_search_financial_data);

                //Get financial data
                Tool tool_get_financial_data = new Tool("get_financial_data", "Gather current and historical financial data for a particular company for a particular financial XBRL fact (i.e. 'Assets' or 'CurrentLiabilities').");
                tool_get_financial_data.Parameters.Add(new ToolInputParameter("CIK", "The company's central index key (CIK), i.e. '1655210'", "number"));
                tool_get_financial_data.Parameters.Add(new ToolInputParameter("fact", "The name (tag) of the specific XBRL fact you are requesting historical financial data for (i.e. 'Assets' or 'CurrentLiabilities' or 'RevenueNet')"));
                ToReturn.Add(tool_get_financial_data);
            }

            //Weather package?
            if (SETTINGS.WeatherPackageEnabled)
            {
                //Add tool: check weather
                Tool tool_weather = new Tool("check_weather", "Check the weather for the current location.");
                tool_weather.Parameters.Add(new ToolInputParameter("latitude", "Latitude of the location you want to check location of, as a floating point number.", "number"));
                tool_weather.Parameters.Add(new ToolInputParameter("longitude", "Longitude of the location you want to check location of, as a floating point number.", "number"));
                ToReturn.Add(tool_weather);
            }

            return ToReturn.ToArray();
        }

        #endregion

    }
}