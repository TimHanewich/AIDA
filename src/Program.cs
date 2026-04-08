using System;
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
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;
using System.IO.Compression;
using SecuritiesExchangeCommission.Edgar;
using SecuritiesExchangeCommission.Edgar.Data;
using AIDA.Finance;
using TimHanewich.Foundry;
using TimHanewich.Foundry.OpenAI.Responses;

namespace AIDA
{
    public class Program
    {
        #region "GLOBAL VARIABLES"

        public static AIDASettings SETTINGS { get; set; }
        public static Agent AGENT {get; set;}

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
            if (System.IO.Directory.Exists(Tools.ConfigDirectoryPath) == false)
            {
                System.IO.Directory.CreateDirectory(Tools.ConfigDirectoryPath);
            }

            //Retrieve settings
            SETTINGS = AIDASettings.Open();

            //Plug in SEC info in case it is used later
            IdentificationManager.Instance.AppName = "AIDA";
            IdentificationManager.Instance.AppVersion = "1.0";
            IdentificationManager.Instance.Email = "admin@gmail.com";

            //Set up main AGENT
            AGENT = new Agent();

            #endregion

            //Add system message
            AGENT.Inputs.Add(new Message(Role.developer, Tools.GetSystemPrompt(SETTINGS)));

            //Add welcoming message
            string opening_msg = "Hi, I'm AIDA, and I'm here to help! What can I do for you?";
            AGENT.Inputs.Add(new Message(Role.assistant, opening_msg));
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
                    Console.WriteLine();
                    goto Input;
                }
                if (input.ToLower() == "tokens")
                {

                    //Print tokens
                    AnsiConsole.MarkupLine("[blue][underline]Cumulative Tokens so Far[/][/]");
                    AnsiConsole.MarkupLine("[blue]Prompt tokens: [bold]" + AGENT.CumulativeInputTokens.ToString("#,##0") + "[/][/]");
                    AnsiConsole.MarkupLine("[blue]Completion tokens: [bold]" + AGENT.CumulativeOutputTokens.ToString("#,##0") + "[/][/]");
                    Console.WriteLine();

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
                    foreach (Function f in DetermineAvailableFunctions())
                    {
                        AnsiConsole.MarkupLine("[bold][blue]" + f.Name + "[/][/] - [gray]" + f.Description + "[/]");
                    }
                    Console.WriteLine();
                    goto Input;
                }
                else if (input.ToLower() == "clear")
                {
                    AGENT.ClearHistory();
                    AGENT.Inputs.Add(new Message(Role.user, Tools.GetSystemPrompt(SETTINGS))); //add the system message back (need that!)
                    AnsiConsole.MarkupLine("[blue][bold]Chat history cleared. Latest prompt.md injected.[/][/]");
                    Console.WriteLine();
                    goto Input;
                }

                //It did not trigger a special command, so add it to the history, it will be passed to the AI!
                AGENT.Inputs.Add(new Message(Role.user, input));

            //Prompt
            Prompt:

                //Configure the agent's foundry connection
                if (SETTINGS.FoundryUrl != null)
                {
                    AGENT.FoundryConnection = new FoundryResource(SETTINGS.FoundryUrl);
                    
                    //If we are using API key auth, plug that in
                    if (SETTINGS.ApiKey != null)
                    {
                        AGENT.FoundryConnection.ApiKey = SETTINGS.ApiKey;
                    }
                    else if (SETTINGS.TenantID != null && SETTINGS.ClientID != null && SETTINGS.ClientSecret != null) // if instead we are using entra ID auth
                    {
                        //Do we have a non-expired token right now?
                        bool NeedNewToken = true;
                        if (SETTINGS.AuthenticatedTokenCredentials != null)
                        {
                            if (SETTINGS.AuthenticatedTokenCredentials.Expires >= DateTime.UtcNow) // if it is NOT expired yet
                            {
                                AGENT.FoundryConnection.AccessToken = SETTINGS.AuthenticatedTokenCredentials.AccessToken;
                                NeedNewToken = false;
                            }
                        }

                        //If we need a new token, get it
                        if (NeedNewToken)
                        {
                            //Authenticate now
                            AnsiConsole.Markup("Requesting new access token... ");
                            EntraAuthenticationHandler auth = new EntraAuthenticationHandler();
                            auth.TenantID = SETTINGS.TenantID;
                            auth.ClientID = SETTINGS.ClientID;
                            auth.ClientSecret = SETTINGS.ClientSecret;
                            try
                            {
                                SETTINGS.AuthenticatedTokenCredentials = await auth.AuthenticateAsync();
                                AnsiConsole.MarkupLine("[green]success![/]");
                            }
                            catch (Exception ex)
                            {
                               AnsiConsole.MarkupLine("[red]Authentication failed! Msg: " + Markup.Escape(ex.Message) + "[/]");
                            }

                            //If it was successful
                            if (SETTINGS.AuthenticatedTokenCredentials != null)
                            {
                                TimeSpan UntilExpiration = SETTINGS.AuthenticatedTokenCredentials.Expires - DateTime.UtcNow;
                                AnsiConsole.MarkupLine("[gray][italic]expires: " + SETTINGS.AuthenticatedTokenCredentials.Expires.ToLocalTime().ToString() + " (in " + UntilExpiration.TotalHours.ToString("#,##0.0") + " hours)[/][/]");
                                AGENT.FoundryConnection.AccessToken = SETTINGS.AuthenticatedTokenCredentials.AccessToken; //Plug it in
                                SETTINGS.Save(); //save it to settings so it is hard saved
                            }

                            //Line break
                            Console.WriteLine();
                        }
                    }
                }

                //Configure the agent's model
                if (SETTINGS.ModelName != null)
                {
                    AGENT.ModelName = SETTINGS.ModelName;
                }

                #region "Plug in tools & functions"

                //CLEAR TOOLS (so we don't re-add them)
                //The clearing and re-adding process will happen each time so they can update the tools available on the fly
                AGENT.Functions.Clear();

                //Add them back
                AGENT.Functions = DetermineAvailableFunctions().ToList();

                //Built-in tools: web search
                AGENT.WebSearchEnabled = SETTINGS.WebSearchEnabled;

                #endregion

                //Prompt the model
                AnsiConsole.Markup("[gray][italic]thinking... [/][/]");
                Exchange[] outputs;
                try
                {
                    outputs = await AGENT.PromptAsync();
                    Console.WriteLine();
                }
                catch (Exception ex)
                {
                    AnsiConsole.MarkupLine("[red]Uh oh! There was an issue when prompting the underlying model. Message: " + Markup.Escape(ex.Message) + "[/]");
                    Console.WriteLine();
                    Console.WriteLine();
                    AnsiConsole.Markup("[italic][gray]Press enter to try another input... [/][/]");
                    Console.ReadLine();
                    goto Input;
                }

                //Handle outputs
                foreach (Exchange ex in outputs)
                {
                    //If it is a message (text)
                    if (ex is Message msg)
                    {
                        if (msg.Text != null)
                        {
                            PrintAIMessage(msg.Text, SETTINGS.AssistantMessageColor);
                            Console.WriteLine();
                        }
                    }
                    else if (ex is FunctionCall fc) // if it is a function call
                    {
                        AnsiConsole.Markup("[gray][italic]calling tool '" + fc.FunctionName + "'... [/][/]");
                        string tool_call_response_payload = "";

                        //Call to the tool and save the response from that tool
                        if (fc.FunctionName == "check_weather")
                        {

                            //Get latitude
                            float? latitude = null;
                            JProperty? prop_latitude = fc.Arguments.Property("latitude");
                            if (prop_latitude != null)
                            {
                                latitude = Convert.ToSingle(prop_latitude.Value.ToString());
                            }

                            //Get longitude
                            float? longitude = null;
                            JProperty? prop_longitude = fc.Arguments.Property("longitude");
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
                        else if (fc.FunctionName == "save_file")
                        {
                            //Get file name
                            string file_name = "dummy.txt";
                            JProperty? prop_file_name = fc.Arguments.Property("file_name");
                            if (prop_file_name != null)
                            {
                                file_name = prop_file_name.Value.ToString();
                            }

                            //Get file content
                            string file_content = "(dummy content)";
                            JProperty? prop_file_content = fc.Arguments.Property("file_content");
                            if (prop_file_content != null)
                            {
                                file_content = prop_file_content.Value.ToString();
                            }

                            //Save file
                            tool_call_response_payload = SaveFile(file_name, file_content);
                        }
                        else if (fc.FunctionName == "read_file")
                        {
                            //Get file path
                            string file_path = "?";
                            JProperty? prop_file_path = fc.Arguments.Property("file_path");
                            if (prop_file_path != null)
                            {
                                file_path = prop_file_path.Value.ToString();
                            }

                            tool_call_response_payload = ReadFile(file_path);
                        }
                        else if (fc.FunctionName == "check_current_time")
                        {
                            tool_call_response_payload = "The current date and time is " + DateTime.Now.ToString();
                        }
                        else if (fc.FunctionName == "read_webpage")
                        {
                            //Get URL
                            string? url = null;
                            JProperty? prop_url = fc.Arguments.Property("url");
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
                        else if (fc.FunctionName == "rename_file")
                        {
                            //Define input params the AI must provide
                            string? path = null;
                            string? new_name = null;

                            //Get the 'path' parameter
                            JProperty? prop_path = fc.Arguments.Property("path");
                            if (prop_path != null)
                            {
                                path = prop_path.Value.ToString();
                            }

                            //Get the 'new_name' parameter
                            JProperty? prop_new_name = fc.Arguments.Property("new_name");
                            if (prop_new_name != null)
                            {
                                new_name = prop_new_name.Value.ToString();
                            }

                            //Check
                            if (path == null && new_name == null)
                            {
                                tool_call_response_payload = "Renaming of file unsuccessful. You must provide both the 'path' and 'new_name' parameters.";
                            }
                            else if (path == null)
                            {
                                tool_call_response_payload = "Renaming of file unsuccessful. You did not provide the 'path' parameter.";
                            }
                            else if (new_name == null)
                            {
                                tool_call_response_payload = "Renaming of file unsuccessful. You did not provide the 'new_name' parameter.";
                            }
                            else //AI provided both
                            {

                                try
                                {
                                    //Get directory path
                                    string? dir_path = Path.GetDirectoryName(path);
                                    if (dir_path == null)
                                    {
                                        tool_call_response_payload = "Internal failure while renaming: unable to determine directory path";
                                    }
                                    else
                                    {
                                        //Get extension
                                        string extension = Path.GetExtension(path);

                                        //Get new abs path (target)
                                        string NewAbsPath = Path.Combine(dir_path, new_name + extension);

                                        //Perform rename
                                        File.Move(path, NewAbsPath);

                                        //Mention it being successful
                                        tool_call_response_payload = "Renaming successful! File '" + path + "' renamed to '" + NewAbsPath + "'";
                                    }
                                }
                                catch (Exception ex2)
                                {
                                    tool_call_response_payload = "Renaming of file failed. Exception message: " + ex2.Message;
                                }
                            }   
                        }
                        else if (fc.FunctionName == "get_cik")
                        {

                            //Get symbol
                            string? symbol = null;
                            JProperty? prop_symbol = fc.Arguments.Property("symbol");
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
                                catch (Exception bex)
                                {
                                    tool_call_response_payload = "Attempt to find CIK from trading symbol failed. Exception message: " + bex.Message;
                                }
                            }
                            else
                            {
                                tool_call_response_payload = "To use the this tool you must provide the symbol parameter!";
                            }

                        }
                        else if (fc.FunctionName == "search_financial_data")
                        {
                            //Get CIK
                            int? CIK = null;
                            JProperty? prop_CIK = fc.Arguments.Property("CIK");
                            if (prop_CIK != null)
                            {
                                CIK = Convert.ToInt32(prop_CIK.Value.ToString());
                            }

                            //Get search term
                            string? search_term = null;
                            JProperty? prop_search_term = fc.Arguments.Property("search_term");
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
                                catch (Exception bex)
                                {
                                    tool_call_response_payload = "Unable to perform Company Facts Query via SEC API: " + bex.Message;
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
                        else if (fc.FunctionName == "get_financial_data")
                        {
                            //Get parameter: CIK
                            int? CIK = null;
                            JProperty? prop_CIK = fc.Arguments.Property("CIK");
                            if (prop_CIK != null)
                            {
                                CIK = Convert.ToInt32(prop_CIK.Value.ToString());
                            }

                            //Get fact
                            string? fact = null;
                            JProperty? prop_fact = fc.Arguments.Property("fact");
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
                                catch (Exception bex)
                                {
                                    tool_call_response_payload = "Unable to perform Company Facts Query via SEC API: " + bex.Message;
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
                        else if (fc.FunctionName == "msx_search_users")
                        {
                            if (SETTINGS.msx_cookie != null)
                            {
                                MSXInterface msxi = new MSXInterface(SETTINGS.msx_cookie);
                                JProperty? prop_fullname = fc.Arguments.Property("fullname");
                                if (prop_fullname != null)
                                {
                                    string fullname = prop_fullname.Value.ToString();
                                    AnsiConsole.Markup("[gray][italic]searching for user '" + fullname + "'... [/][/]");
                                    try
                                    {
                                        JArray results = await msxi.SearchUsersAsync(fullname);
                                        tool_call_response_payload = results.ToString();
                                    }
                                    catch (Exception ex2)
                                    {
                                        tool_call_response_payload = ex2.Message;
                                    }
                                }
                                else
                                {
                                    tool_call_response_payload = "You must provide property 'fullname'!";
                                }
                            }
                            else
                            {
                                string errmsg = "MSX Cookie not specifified - please update in settings before using MSX tools. ";
                                AnsiConsole.Markup("[red]" + errmsg + "[/]");
                                tool_call_response_payload = errmsg;
                            }
                        }
                        else if (fc.FunctionName == "msx_search_accounts")
                        {
                            if (SETTINGS.msx_cookie != null)
                            {
                                MSXInterface msxi = new MSXInterface(SETTINGS.msx_cookie);
                                JProperty? prop_name = fc.Arguments.Property("name");
                                if (prop_name != null)
                                {
                                    string name = prop_name.Value.ToString();
                                    AnsiConsole.Markup("[gray][italic]searching '" + name + "'... [/][/]");
                                    try
                                    {
                                        JArray accounts = await msxi.SearchAccountsAsync(name);
                                        AnsiConsole.Markup("[gray][italic]" + accounts.Count.ToString() + " found [/][/]");
                                        tool_call_response_payload = accounts.ToString(Formatting.None);
                                    }
                                    catch (Exception ex2)
                                    {
                                        tool_call_response_payload = ex2.Message;
                                    }
                                }
                            }
                            else
                            {
                                string errmsg = "MSX Cookie not specifified - please update in settings before using MSX tools. ";
                                AnsiConsole.Markup("[red]" + errmsg + "[/]");
                                tool_call_response_payload = errmsg;
                            }
                        }
                        else if (fc.FunctionName == "msx_search_opportunities")
                        {
                            if (SETTINGS.msx_cookie != null)
                            {
                                MSXInterface msxi = new MSXInterface(SETTINGS.msx_cookie);
                                JProperty? prop_accountid = fc.Arguments.Property("accountid");
                                JProperty? prop_name = fc.Arguments.Property("name");
                                if (prop_accountid != null && prop_name != null)
                                {
                                    string accountid = prop_accountid.Value.ToString();
                                    string name = prop_name.Value.ToString();

                                    AnsiConsole.Markup("[gray][italic]" + "Searching account '" + accountid + "' for '" + name + "'... [/][/]");
                                    try
                                    {
                                        JArray opps = await msxi.SearchOpportunitiesAsync(accountid, name);
                                        AnsiConsole.Markup("[gray][italic]" + opps.Count.ToString() + " found [/][/]");
                                        tool_call_response_payload = opps.ToString(Formatting.None);
                                    }
                                    catch (Exception ex2)
                                    {
                                        tool_call_response_payload = ex2.Message;
                                    }
                                }
                            }
                            else
                            {
                                string errmsg = "MSX Cookie not specifified - please update in settings before using MSX tools. ";
                                AnsiConsole.Markup("[red]" + errmsg + "[/]");
                                tool_call_response_payload = errmsg;
                            }
                        }
                        else if (fc.FunctionName == "msx_log_task")
                        {
                            if (SETTINGS.msx_cookie != null)
                            {
                                MSXInterface msxi = new MSXInterface(SETTINGS.msx_cookie);
                                JProperty? prop_title = fc.Arguments.Property("title");
                                JProperty? prop_description = fc.Arguments.Property("description");
                                JProperty? prop_timestamp = fc.Arguments.Property("timestamp");
                                JProperty? prop_accountid = fc.Arguments.Property("accountid");
                                JProperty? prop_opportunityid = fc.Arguments.Property("opportunityid");
                                if (prop_title != null && prop_description != null && prop_timestamp != null)
                                {
                                    string title = prop_title.Value.ToString();
                                    string description = prop_description.Value.ToString();
                                    DateTime timestamp = DateTime.Parse(prop_timestamp.Value.ToString());

                                    //Console.WriteLine(fc.Arguments.ToString());
                                    //Console.ReadLine();

                                    //Check if opportunityid is added first because THAT is priority.
                                    if (prop_opportunityid != null && prop_opportunityid.Value.ToString() != "")
                                    {
                                        try
                                        {
                                            await msxi.CreateTaskAsync(title, description, timestamp, tiedto_opportunity: prop_opportunityid.Value.ToString());
                                            tool_call_response_payload = "Created task tied to opportunity '" + prop_opportunityid.Value.ToString() + "' successfully.";
                                        }
                                        catch (Exception ex2)
                                        {
                                            tool_call_response_payload = ex2.Message;
                                        }
                                    }
                                    else if (prop_accountid != null && prop_accountid.Value.ToString() != "")
                                    {
                                        try
                                        {
                                            await msxi.CreateTaskAsync(title, description, timestamp, tiedto_account: prop_accountid.Value.ToString());
                                            tool_call_response_payload = "Created task tied to account '" + prop_accountid.Value.ToString() + "' successfully.";
                                        }
                                        catch (Exception ex2)
                                        {
                                            tool_call_response_payload = ex2.Message;
                                        }
                                    }
                                }
                            }
                            else
                            {
                                string errmsg = "MSX Cookie not specifified - please update in settings before using MSX tools. ";
                                AnsiConsole.Markup("[red]" + errmsg + "[/]");
                                tool_call_response_payload = errmsg;
                            }
                        }
                        else if (fc.FunctionName == "msx_my_recent_tasks")
                        {
                            if (SETTINGS.msx_cookie != null)
                            {
                                MSXInterface msxi = new MSXInterface(SETTINGS.msx_cookie);
                                try
                                {
                                    JArray RecentTaskData = await msxi.GetMyRecentTasksAsync();
                                    tool_call_response_payload = RecentTaskData.ToString();
                                }
                                catch (Exception ex2)
                                {
                                    tool_call_response_payload = ex2.Message;
                                }
                            }
                            else
                            {
                                string errmsg = "MSX Cookie not specifified - please update in settings before using MSX tools. ";
                                AnsiConsole.Markup("[red]" + errmsg + "[/]");
                                tool_call_response_payload = errmsg;
                            }
                        }
                        else if (fc.FunctionName == "msx_get_user_opportunities")
                        {
                            if (SETTINGS.msx_cookie != null)
                            {
                                MSXInterface msxi = new MSXInterface(SETTINGS.msx_cookie);
                                JProperty? prop_systemuserid = fc.Arguments.Property("systemuserid");
                                if (prop_systemuserid != null)
                                {
                                    string systemuserid = prop_systemuserid.Value.ToString();
                                    AnsiConsole.Markup("[gray][italic]getting opportunities of user '" + systemuserid + "'... [/][/]");
                                    try
                                    {
                                        JArray opps = await msxi.GetAssociatedOpportunitiesAsync(systemuserid);
                                        tool_call_response_payload = opps.ToString();
                                    }
                                    catch (Exception ex2)
                                    {
                                        tool_call_response_payload = ex2.Message;
                                    }
                                }
                                else
                                {
                                    tool_call_response_payload = "You must provide systemuserid parameter.";
                                }
                            }
                            else
                            {
                                string errmsg = "MSX Cookie not specifified - please update in settings before using MSX tools. ";
                                AnsiConsole.Markup("[red]" + errmsg + "[/]");
                                tool_call_response_payload = errmsg;
                            }
                        }
                        else if (fc.FunctionName == "msx_run_query")
                        {
                            if (SETTINGS.msx_cookie != null)
                            {
                                MSXInterface msxi = new MSXInterface(SETTINGS.msx_cookie);
                                JProperty? prop_query = fc.Arguments.Property("query");
                                if (prop_query != null)
                                {
                                    string query = prop_query.Value.ToString();
                                    query = query.Replace("\n", ""); //strip out new lines for sake of printing
                                    AnsiConsole.Markup("[gray][italic]running query '" + query + "'... [/][/]");
                                    try
                                    {
                                        JArray results = await msxi.RunQueryAsync(query);
                                        tool_call_response_payload = results.ToString();
                                    }
                                    catch (Exception ex2)
                                    {
                                        tool_call_response_payload = "That query failed! Msg: " + ex2.Message;
                                    }
                                }
                                else
                                {
                                    tool_call_response_payload = "You must provide a query to run with the `query` parameter.";
                                }
                            }
                            else
                            {
                                string errmsg = "MSX Cookie not specifified - please update in settings before using MSX tools. ";
                                AnsiConsole.Markup("[red]" + errmsg + "[/]");
                                tool_call_response_payload = errmsg;
                            }
                        }
                        else if (fc.FunctionName == "shell")
                        {
                            //Get command
                            string? cmd = null;
                            JProperty? prop_command = fc.Arguments.Property("command");
                            if (prop_command != null)
                            {
                                cmd = prop_command.Value.ToString();
                            }

                            //Handle
                            if (cmd != null)
                            {
                                AnsiConsole.Markup("[gray][italic]running shell '" + cmd + "'... [/][/]");
                                string response = await Tools.ExecuteShellAsync(cmd);
                                AnsiConsole.MarkupLine("[gray][italic]done[/][/]");
                                tool_call_response_payload = response;
                            }
                            else
                            {
                                tool_call_response_payload = "You must provide the 'command' parameter! It wasn't provided.";
                            }
                        }
                        else
                        {
                            AnsiConsole.MarkupLine("[red]Model called tool '" + fc.FunctionName + "' but AIDA is not properly configured to handle that! Oops, sorry about that! We dropped the ball (not the AI). Please contact support.[/]");
                            tool_call_response_payload = "Tool '" + fc.FunctionName + "' is not working right now. Sorry.";
                        }

                        //Append function call result
                        FunctionCallOutput fco = new FunctionCallOutput();
                        fco.CallId = fc.CallId;
                        fco.Output = tool_call_response_payload;
                        AGENT.Inputs.Add(fco);
                        //Confirm completion of tool call
                        AnsiConsole.MarkupLine("[gray][italic]complete[/][/]");
                    }
                    else if (ex is WebSearchCallQuery wscq)
                    {
                        AnsiConsole.MarkupLine("[gray][italic]searched '" + wscq.Query + "'[/][/]");
                    }
                    else if (ex is WebSearchCallOpenPage)
                    {
                        AnsiConsole.MarkupLine("[gray][italic]opened web page[/][/]");
                    }
                }

                //If there was at least one function call we just fulfilled, do not go back to user imput yet
                //Now that we got the result of the function call, we need to go re-prompt it with the results
                foreach (Exchange ex in outputs)
                {
                    if (ex is FunctionCall)
                    {
                        goto Prompt;
                    }
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
            string DestinationDirectory = Directory.GetCurrentDirectory();
            string DestinationPath = System.IO.Path.Combine(DestinationDirectory, file_name);
            System.IO.File.WriteAllText(DestinationPath, file_content);
            return "File successfully saved to '" + DestinationPath + "'.";
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
                AnsiConsole.MarkupLine("Config directory: [bold]" + Tools.ConfigDirectoryPath + "[/]");

                //Custom prompt path
                AnsiConsole.MarkupLine("Custom prompt file: [bold]" + Tools.CustomPromptPath + "[/]");

                //Foundry URL
                if (SETTINGS.FoundryUrl != null)
                {
                    AnsiConsole.MarkupLine("Foundry Resource: " + SETTINGS.FoundryUrl);
                }
                else
                {
                    AnsiConsole.MarkupLine("Foundry Resource: [italic]none[/]");
                }

                //Foundry Auth
                if (SETTINGS.ApiKey != null)
                {
                    AnsiConsole.MarkupLine("Auth Type: API Key");
                }
                else if (SETTINGS.TenantID != null && SETTINGS.ClientID != null && SETTINGS.ClientSecret != null)
                {
                    AnsiConsole.MarkupLine("Auth Type: Access Token");
                }
                else
                {
                    AnsiConsole.MarkupLine("Auth Type: (unknown)");
                }

                //Model name
                if (SETTINGS.ModelName != null)
                {
                    AnsiConsole.MarkupLine("Model: " + SETTINGS.ModelName);
                }
                else
                {
                    AnsiConsole.MarkupLine("Model: (none)");
                }

                //Assistant color
                AnsiConsole.MarkupLine("AI Assistant Msg Color: [bold]" + SETTINGS.AssistantMessageColor + "[/] ([" + SETTINGS.AssistantMessageColor + "]looks like this[/])");

                //Ask what to do
                Console.WriteLine();
                SelectionPrompt<string> SettingToDo = new SelectionPrompt<string>();
                SettingToDo.Title("What do you want to do?");
                SettingToDo.AddChoice("Update Foundry Connection Info");
                SettingToDo.AddChoice("Update Model");
                SettingToDo.AddChoice("Update MSX Cookie");
                SettingToDo.AddChoice("Change Assistant Message Color");
                SettingToDo.AddChoice("Enable/Disable Tools");
                SettingToDo.AddChoice("Save & Continue");
                string SettingToDoAnswer = AnsiConsole.Prompt(SettingToDo);

                //Handle what to do
                if (SettingToDoAnswer == "Update Foundry Connection Info")
                {
                    //Get foundry URL
                    SETTINGS.FoundryUrl = AnsiConsole.Ask<string>("Foundry URL (i.e. https://myfoundry-resource.services.ai.azure.com)?");
    
                    //Ask how they want to authenticate
                    SelectionPrompt<string> FoundryAuthOptions = new SelectionPrompt<string>();
                    FoundryAuthOptions.Title("How do you want to authenticate?");
                    FoundryAuthOptions.AddChoice("API Key");
                    FoundryAuthOptions.AddChoice("Entra ID");
                    string FoundryAuthSelection = AnsiConsole.Prompt(FoundryAuthOptions);

                    //Handle auth
                    if (FoundryAuthSelection == "API Key")
                    {
                        SETTINGS.ApiKey = AnsiConsole.Ask<string>("What is the API key?");

                        //Clear out any existing entra ID tokens
                        SETTINGS.AuthenticatedTokenCredentials = null;

                        //Clear out the Entra ID info becuase now we wil use API key
                        SETTINGS.TenantID = null;
                        SETTINGS.ClientID = null;
                        SETTINGS.ClientSecret = null;
                    }
                    else if (FoundryAuthSelection == "Entra ID")
                    {
                        SETTINGS.TenantID = AnsiConsole.Ask<string>("Tenant ID?");
                        SETTINGS.ClientID = AnsiConsole.Ask<string>("Client ID?");
                        SETTINGS.ClientSecret = AnsiConsole.Ask<string>("Client Secret?");
                        
                        //Clear out any existing entra ID tokens
                        SETTINGS.AuthenticatedTokenCredentials = null;

                        //Clear out any API key because now we will use Entra ID
                        SETTINGS.ApiKey = null;
                    }
                }
                else if (SettingToDoAnswer == "Update Model")
                {
                    string model_name = AnsiConsole.Ask<string>("Model name?");
                    SETTINGS.ModelName = model_name;
                    AnsiConsole.MarkupLine("Model updated to '" + model_name + "'");
                }
                else if (SettingToDoAnswer == "Update MSX Cookie")
                {
                    string newcookie = AnsiConsole.Ask<string>("New cookie? > ");
                    SETTINGS.msx_cookie = newcookie;
                    AnsiConsole.MarkupLine("MSX Cookie updated.");
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
                else if (SettingToDoAnswer == "Enable/Disable Tools")
                {

                    //Prepare packages question (multi selection prompt)
                    MultiSelectionPrompt<string> PackagesQuestion = new MultiSelectionPrompt<string>();
                    PackagesQuestion.Title("What tools do you want enabled?");
                    PackagesQuestion.NotRequired(); //selecting none is fine!
                    PackagesQuestion.AddChoice("Web Search (built in)");
                    PackagesQuestion.AddChoice("Weather");
                    PackagesQuestion.AddChoice("Finance");
                    PackagesQuestion.AddChoice("MSX");
                    PackagesQuestion.AddChoice("Shell");

                    //Defaults
                    if (SETTINGS.WebSearchEnabled)
                    {
                        PackagesQuestion.Select("Web Search (built in)");
                    }
                    if (SETTINGS.FinancePackageEnabled)
                    {
                        PackagesQuestion.Select("Finance");
                    }
                    if (SETTINGS.WeatherPackageEnabled)
                    {
                        PackagesQuestion.Select("Weather");
                    }
                    if (SETTINGS.MsxPackageEnabled)
                    {
                        PackagesQuestion.Select("MSX");
                    }
                    if (SETTINGS.ShellEnabled)
                    {
                        PackagesQuestion.Select("Shell");
                    }

                    //Ask
                    List<string> PackagesToEnable = AnsiConsole.Prompt(PackagesQuestion);

                    //Enable/Disable: Web Search
                    if (PackagesToEnable.Contains("Web Search (built in)"))
                    {
                        SETTINGS.WebSearchEnabled = true;
                    }
                    else
                    {
                        SETTINGS.WebSearchEnabled = false;
                    }

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

                    //Enable/Disable: MSX
                    if (PackagesToEnable.Contains("MSX"))
                    {
                        SETTINGS.MsxPackageEnabled = true;
                    }
                    else
                    {
                        SETTINGS.MsxPackageEnabled = false;
                    }

                    //Enable/Disable: Shell
                    SETTINGS.ShellEnabled = PackagesToEnable.Contains("Shell");

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

        public static Function[] DetermineAvailableFunctions()
        {
            List<Function> ToReturn = new List<Function>();

            //Add tool: save file
            Function tool_savetxtfile = new Function("save_file", "Save a file to the user's computer in the current directory.");
            tool_savetxtfile.Parameters.Add(new FunctionInputParameter("file_name", "The name of the file, like `myfile.txt` or `report.md`."));
            tool_savetxtfile.Parameters.Add(new FunctionInputParameter("file_content", "The content of the file."));
            ToReturn.Add(tool_savetxtfile);

            //Add tool: read file
            Function tool_readfile = new Function("read_file", "Read the contents of a file of any type (txt, pdf, word document, etc.) from the user's computer");
            tool_readfile.Parameters.Add(new FunctionInputParameter("file_path", "The path to the file on the computer, for example 'C:\\Users\\timh\\Downloads\\notes.txt' or '.\\notes.txt' or 'notes.txt'"));
            ToReturn.Add(tool_readfile);

            //Add tool: check current time
            Function tool_checkcurrenttime = new Function("check_current_time", "Check the current date and time right now.");
            ToReturn.Add(tool_checkcurrenttime);

            //Add tool: open web page
            Function tool_readwebpage = new Function("read_webpage", "Read the contents of a particular web page.");
            tool_readwebpage.Parameters.Add(new FunctionInputParameter("url", "The specific URL of the webpage to read."));
            ToReturn.Add(tool_readwebpage);

            //Add tool: rename file
            Function tool_RenameFile = new Function("rename_file", "Rename a specific file on the user's drive.");
            tool_RenameFile.Parameters.Add(new FunctionInputParameter("path", "The current absolute path of the file."));
            tool_RenameFile.Parameters.Add(new FunctionInputParameter("new_name", "The new name of the file, NOT including the extension."));
            ToReturn.Add(tool_RenameFile);

            

            //Add finance package?
            if (SETTINGS.FinancePackageEnabled)
            {
                //Symbol to CIK
                Function tool_SymbolToCik = new Function("get_cik", "Get the CIK (Central Index Key) for a company based on its stock symbol.");
                tool_SymbolToCik.Parameters.Add(new FunctionInputParameter("symbol", "Stock symbol, i.e. 'MSFT'."));
                ToReturn.Add(tool_SymbolToCik);

                //Search available financial data
                Function tool_search_financial_data = new Function("search_financial_data", "Search for available XBRL facts the company has reported before (i.e. 'AssetsCurrent', 'Liabilities', etc).");
                tool_search_financial_data.Parameters.Add(new FunctionInputParameter("CIK", "The company's central index key (CIK), i.e. '1655210'", "number"));
                tool_search_financial_data.Parameters.Add(new FunctionInputParameter("search_term", "The term to search for, i.e. 'revenue' or 'assets' or 'advertising'."));
                ToReturn.Add(tool_search_financial_data);

                //Get financial data
                Function tool_get_financial_data = new Function("get_financial_data", "Gather current and historical financial data for a particular company for a particular financial XBRL fact (i.e. 'Assets' or 'CurrentLiabilities').");
                tool_get_financial_data.Parameters.Add(new FunctionInputParameter("CIK", "The company's central index key (CIK), i.e. '1655210'", "number"));
                tool_get_financial_data.Parameters.Add(new FunctionInputParameter("fact", "The name (tag) of the specific XBRL fact you are requesting historical financial data for (i.e. 'Assets' or 'CurrentLiabilities' or 'RevenueNet')"));
                ToReturn.Add(tool_get_financial_data);
            }

            //Weather package?
            if (SETTINGS.WeatherPackageEnabled)
            {
                //Add tool: check weather
                Function tool_weather = new Function("check_weather", "Check the weather for the current location.");
                tool_weather.Parameters.Add(new FunctionInputParameter("latitude", "Latitude of the location you want to check location of, as a floating point number.", "number"));
                tool_weather.Parameters.Add(new FunctionInputParameter("longitude", "Longitude of the location you want to check location of, as a floating point number.", "number"));
                ToReturn.Add(tool_weather);
            }

            //if MSX package is enabled
            if (SETTINGS.MsxPackageEnabled)
            {
                //MSX: Search Users
                Function tool_MsxSearchUsers = new Function("msx_search_users", "Search through the users (systemuser) in MSX.");
                tool_MsxSearchUsers.Parameters.Add(new FunctionInputParameter("fullname", "The full name (first and last) of the user to search for."));
                ToReturn.Add(tool_MsxSearchUsers);

                //MSX: Search Accounts
                Function tool_MsxSearchAccounts = new Function("msx_search_accounts", "Search through the managed accounts in MSX.");
                tool_MsxSearchAccounts.Parameters.Add(new FunctionInputParameter("name", "The account name to search for."));
                ToReturn.Add(tool_MsxSearchAccounts);

                //MSX: Search opportunities
                Function tool_MsxSearchOpportunities = new Function("msx_search_opportunities", "Search through the open opportunities in MSX for a particular account.");
                tool_MsxSearchOpportunities.Parameters.Add(new FunctionInputParameter("accountid", "The unique ID of the account to search opportunities for."));
                tool_MsxSearchOpportunities.Parameters.Add(new FunctionInputParameter("name", "The opportunity name (title) to search for."));
                ToReturn.Add(tool_MsxSearchOpportunities);

                //MSX: log task
                Function tool_MsxLogTask = new Function("msx_log_task", "Log a completed task, tied to a particular account or opportunity in MSX.");
                tool_MsxLogTask.Parameters.Add(new FunctionInputParameter("title", "The title of the task"));
                tool_MsxLogTask.Parameters.Add(new FunctionInputParameter("description", "The description of what was done and accomplished as part of this effort."));
                tool_MsxLogTask.Parameters.Add(new FunctionInputParameter("timestamp", "The date and time the task was completed, in ISO 8601 format, like '2026-03-06T15:10:28Z'"));
                FunctionInputParameter fic_accountid = new FunctionInputParameter("accountid", "If tying this task to an account in MSX, the unique account ID");
                FunctionInputParameter fic_opportunityid = new FunctionInputParameter("opportunityid", "If tying this task to an opportunity in MSX, the unique opportunity ID");
                fic_accountid.Required = false;
                fic_opportunityid.Required = false;
                tool_MsxLogTask.Parameters.Add(fic_accountid);
                tool_MsxLogTask.Parameters.Add(fic_opportunityid);
                ToReturn.Add(tool_MsxLogTask);

                //MSX: My Recent Tasks
                Function tool_MsxMyRecentTasks = new Function("msx_my_recent_tasks", "Get a list of the user's recent tasks logged in MSX and what account/opportunities they were logged to.");
                ToReturn.Add(tool_MsxMyRecentTasks);

                //MSX: Get someone's opportunities
                Function tool_MsxGetSystemUsersOpportunities = new Function("msx_get_user_opportunities", "Get a list of all the opportunities a systemuser is part of the deal team for in MSX.");
                tool_MsxGetSystemUsersOpportunities.Parameters.Add(new FunctionInputParameter("systemuserid", "The unique ID of the systemuser."));
                ToReturn.Add(tool_MsxGetSystemUsersOpportunities);

                //MSX: Run OData query (any query!)
                Function tool_MsxRunQuery = new Function("msx_run_query", "Run any OData query on MSX, a D365 Sales system.");
                tool_MsxRunQuery.Parameters.Add(new FunctionInputParameter("query", "The OData query to run, for example 'accounts?$top=5&$select=name'"));
                //ToReturn.Add(tool_MsxRunQuery); //DISABLING THIS TOOL!!!!
            }

            //Is shell enabled?
            if (SETTINGS.ShellEnabled)
            {
                Function tool_shell = new Function("shell", "Executes a single shell command on the host machine (Windows/cmd.exe or Linux/bash) and returns the combined standard output and standard error. Use this for file system operations, running compilers, or checking system status.");
                tool_shell.Parameters.Add(new FunctionInputParameter("command", "The shell command to execute."));
                ToReturn.Add(tool_shell);
            }

            return ToReturn.ToArray();
        }

        #endregion

    }
}