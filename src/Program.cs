using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Net;
using Spectre.Console;
using TimHanewich.MicrosoftGraphHelper;
using TimHanewich.MicrosoftGraphHelper.Outlook;
using System.Web;
using System.Collections.Specialized;
using TimHanewich.Bing;
using TimHanewich.Bing.Search;
using HtmlAgilityPack;
using System.Reflection;
using TimHanewich.AgentFramework;
using Yahoo.Finance;

namespace AIDA
{
    public class Program
    {
        public static void Main(string[] args)
        {
            string content = ReadFile(@"C:\Users\timh\OneDrive - Microsoft\Territory Alignment\Territories\Georgia\Georgia Technology Authority\GTA-EPMO - Power Platform POC USE CASES.pdf");
            Console.WriteLine(content);
            //RunAsync().Wait();
        }

        //GLOBAL VARIABLES
        public static Agent FinanceGuru {get; set;} = new Agent();

        public static async Task RunAsync()
        {

            #region "credentials"

            //Check Config directory for config files
            string ConfigDirectory = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AIDA");
            if (System.IO.Directory.Exists(ConfigDirectory) == false)
            {
                System.IO.Directory.CreateDirectory(ConfigDirectory);
            }

            //Get AzureOpenAICredentials.json
            AzureOpenAICredentials azoai;
            string AzureOpenAICredentialsPath = System.IO.Path.Combine(ConfigDirectory, "AzureOpenAICredentials.json");
            if (System.IO.File.Exists(AzureOpenAICredentialsPath) == false)
            {
                //Write the file
                System.IO.File.WriteAllText(AzureOpenAICredentialsPath, JsonConvert.SerializeObject(new AzureOpenAICredentials(), Formatting.Indented));
                
                Console.WriteLine("Your Azure OpenAI secrets were not provided! Please enter your Azure OpenAI details here: " + AzureOpenAICredentialsPath);
                return;
            }
            else
            {
                string content = System.IO.File.ReadAllText(AzureOpenAICredentialsPath);
                AzureOpenAICredentials? azoaicreds = JsonConvert.DeserializeObject<AzureOpenAICredentials>(content);
                if (azoaicreds == null)
                {
                    Console.WriteLine("Was unable to parse valid Azure OpenAI credentials out of file '" + AzureOpenAICredentialsPath + "'. Please fix the errors and try again.");
                    return;
                }
                else
                {
                    if (azoaicreds.URL == "" || azoaicreds.ApiKey == "")
                    {
                        Console.WriteLine("The Azure OpenAI credentials in '" + AzureOpenAICredentialsPath + "' were not populated. Please add your Azure OpenAI details and try again.");
                        return;
                    }
                    else
                    {
                        azoai = azoaicreds;
                    }
                }
            }

            //Try to retrieve bing search api key (an azure service)
            BingSearchService? bss = null;
            string BingSearchApiKeyPath = System.IO.Path.Combine(ConfigDirectory, "BingSearchApiKey.txt");
            if (System.IO.File.Exists(BingSearchApiKeyPath))
            {
                string bingkey = System.IO.File.ReadAllText(BingSearchApiKeyPath);
                if (bingkey != "")
                {
                    bss = new BingSearchService(bingkey);
                }
                else
                {
                    AnsiConsole.MarkupLine("[yellow]A Bing Search API key was not found in file '" + BingSearchApiKeyPath + "'. Please put an API key in there and re-start AIDA if you wish to enable web search.[/]");
                }
            }
            else
            {
                System.IO.File.WriteAllText(BingSearchApiKeyPath, ""); //Create the file
                AnsiConsole.MarkupLine("[yellow]Unable to find your Bing Search API key! If you wish to enable web search, please place it in here: " + BingSearchApiKeyPath + "[/]");
            }

            //Try to revive stored MicrosoftGraphHelper
            MicrosoftGraphHelper? mgh = null;
            string MicrosoftGraphHelperPath = System.IO.Path.Combine(ConfigDirectory, "MicrosoftGraphHelper.json");
            if (System.IO.File.Exists(MicrosoftGraphHelperPath))
            {
                mgh = JsonConvert.DeserializeObject<MicrosoftGraphHelper>(System.IO.File.ReadAllText(MicrosoftGraphHelperPath));
            }

            //If we were unable to get the microsoft graph credentials from file, ask if they want to sign in
            if (mgh == null)
            {
                SelectionPrompt<string> WantToSignInToGraph = new SelectionPrompt<string>();
                WantToSignInToGraph.Title("I was not able to find access to your Microsoft Graph credentials. Do you want to sign in to Microsoft Outlook so I can access your calendar?");
                WantToSignInToGraph.AddChoices("Yes", "No");
                string WantToSignInToGraphAnswer = AnsiConsole.Prompt(WantToSignInToGraph);
                if (WantToSignInToGraphAnswer == "Yes")
                {
                    //Assemble the authroization URL
                    mgh = new MicrosoftGraphHelper();
                    mgh.Tenant = "consumers";
                    mgh.ClientId = Guid.Parse("e32b77a3-67df-411b-927b-f05cc6fe8d5d");
                    mgh.RedirectUrl = "https://www.google.com/";
                    mgh.Scope.Add("User.Read");
                    mgh.Scope.Add("Calendars.ReadWrite");
                    mgh.Scope.Add("Mail.Read");
                    string url = mgh.AssembleAuthorizationUrl();

                    //Ask them to sign in
                    AnsiConsole.MarkupLine("Great! Please go to the following URL and sign in, granting the necessary permissions:");
                    AnsiConsole.MarkupLine("[gray][italic]" + url + "[/][/]");
                    Console.WriteLine();
                    AnsiConsole.MarkupLine("After you do, it will redirect you to a URL. Copy + Paste that URL back to me.");
                    
                    //Collect code by stripping it out of the redirect URL
                    string? GraphAuthCode = null;
                    while (GraphAuthCode == null)
                    {

                        //Ask for full URL it redirected them to
                        Console.Write("Full URL it redirects you to (copy + paste): ");
                        string? full_url = Console.ReadLine();
                        while (full_url == null)
                        {
                            full_url = Console.ReadLine();
                        }

                        //Clip out the 'code' parameter
                        Uri AuthRedirect = new Uri(full_url);
                        NameValueCollection nvc = HttpUtility.ParseQueryString(AuthRedirect.Query); //Parse the query portion (where the parameters are in the URL) into a name value collection, splitting each param up
                        GraphAuthCode = nvc["code"];
                        if (GraphAuthCode == null)
                        {
                            AnsiConsole.MarkupLine("I'm sorry, I couldn't find the necessary 'code' parameter in that URL. Are you sure you are copying + pasting the full URL? Or perhaps it isn't working. Please try again.");
                        }
                    }

                    //Now that we have the graph auth code, redeem it for a bearer token
                    AnsiConsole.Markup("Authenticating with Microsoft Graph... ");
                    bool GraphAuthenticationSuccessful = false;
                    try
                    {
                        await mgh.GetAccessTokenAsync(GraphAuthCode);
                        AnsiConsole.MarkupLine("[green]success![/]");
                        GraphAuthenticationSuccessful = true;
                    }
                    catch (Exception ex)
                    {
                        AnsiConsole.MarkupLine("[red]" + "redeeming code for bearer token failed! Msg: " + ex.Message + "[/]");
                    }

                    //Write to file if successful, but clear out if not
                    if (GraphAuthenticationSuccessful)
                    {
                        AnsiConsole.Markup("Storing Graph credentials for future use... ");
                        System.IO.File.WriteAllText(MicrosoftGraphHelperPath, JsonConvert.SerializeObject(mgh, Formatting.Indented));
                        AnsiConsole.MarkupLine("[green]saved[/]!");
                    }
                    else
                    {
                        AnsiConsole.MarkupLine("Authenticating with the Microsoft Graph was unsuccesful. Proceeding with AIDA without Microsoft Graph capabilities for now. Outlook-related tools will not work.");
                        mgh = null;
                    }
                }
            }


            #endregion
            
            //Create the agent
            Agent a = new Agent();
            a.Model = azoai;

            //Add system message
            List<string> SystemMessage = new List<string>();
            SystemMessage.Add("You are AIDA, Artificial Intelligence Desktop Assistant. Your role is to be a friendly and helpful assistant. Speak in a playful, lighthearted, and fun manner.");
            SystemMessage.Add("Do not use emojis.");
            SystemMessage.Add("Only use the 'search_web' tool if the user explicitly tells you to search the web or check online or do online research.");
            SystemMessage.Add("If the user asks you to set a reminder for today or a certain amount of time from now, make sure you first check what time that reminder should be by checking the current date and time via the 'check_current_time' tool.");
            string sysmsg = "";
            foreach (string s in SystemMessage)
            {
                sysmsg = sysmsg + s + "\n\n";
            }
            sysmsg = sysmsg.Substring(0, sysmsg.Length - 2);
            a.Messages.Add(new Message(Role.system, sysmsg));

            //Add tool: check weather
            Tool tool = new Tool("check_temperature", "Check the temperature for a given location.");
            a.Tools.Add(tool);

            //Add tool: save text file
            Tool tool_savetxtfile = new Tool("save_txt_file", "Save a text file to the user's computer.");
            tool_savetxtfile.Parameters.Add(new ToolInputParameter("file_name", "The name of the file, WITHOUT the '.txt' file extension at the end."));
            tool_savetxtfile.Parameters.Add(new ToolInputParameter("file_content", "The content of the .txt file (raw text)."));
            a.Tools.Add(tool_savetxtfile);

            //Add tool: read text file
            Tool tool_readtxtfile = new Tool("read_file", "Read the contents of a .txt or .pdf file from the user's computer");
            tool_readtxtfile.Parameters.Add(new ToolInputParameter("file_path", "The path to the file on the computer, for example 'C:\\Users\\timh\\Downloads\\notes.txt' or '.\\notes.txt' or 'notes.txt'"));
            a.Tools.Add(tool_readtxtfile);

            //Add tool: schedule reminder (via outlook)
            Tool tool_schedulereminder = new Tool("schedule_reminder", "Schedule a reminder on the user's Outlook Calendar.");
            tool_schedulereminder.Parameters.Add(new ToolInputParameter("name", "The name of the reminder (what the user will be reminded of)."));
            tool_schedulereminder.Parameters.Add(new ToolInputParameter("datetime", "The date and time of the reminder, in the format of the following example: 3/2/2025 12:02:38 PM"));
            a.Tools.Add(tool_schedulereminder);

            //Add tool: check current time
            Tool tool_checkcurrenttime = new Tool("check_current_time", "Check the current date and time right now.");
            a.Tools.Add(tool_checkcurrenttime);

            //Add tool: send email
            Tool tool_sendemail = new Tool("send_email", "Send an email.");
            tool_sendemail.Parameters.Add(new ToolInputParameter("to", "The email the email is to be sent to. i.e. 'kris.henson@gmail.com'"));
            tool_sendemail.Parameters.Add(new ToolInputParameter("subject", "The subject line of the email."));
            tool_sendemail.Parameters.Add(new ToolInputParameter("body", "The body (content) of the email."));
            a.Tools.Add(tool_sendemail);
            
            //Add tool: search web
            Tool tool_searchweb = new Tool("search_web", "Perform a web search and get back information about a particular topic.");
            tool_searchweb.Parameters.Add(new ToolInputParameter("search_phrase", "The phrase to search for."));
            a.Tools.Add(tool_searchweb);

            //Add tool: open web page
            Tool tool_readwebpage = new Tool("read_webpage", "Read the contents of a particular web page.");
            tool_readwebpage.Parameters.Add(new ToolInputParameter("url", "The specific URL of the webpage to read."));
            a.Tools.Add(tool_readwebpage);

            //Set up agent: FinanceGuru
            FinanceGuru.Model = azoai; //add the model it should use
            FinanceGuru.Messages.Add(new Message(Role.system, "You are FinanceGuru. Your role is to find and provide various financial data to the user."));
            Tool tool_checkstockprice = new Tool("check_stock_price", "Check the price for any stock.");
            tool_checkstockprice.Parameters.Add(new ToolInputParameter("stock_symbol", "The stock symbol, i.e. 'MSFT'."));
            FinanceGuru.Tools.Add(tool_checkstockprice);

            //Add tool: FinanceGuru
            Tool tool_financeguru = new Tool("ask_finance_guru", "Ask finance guru, an expert in finding financial data such as stock prices.");
            tool_financeguru.Parameters.Add(new ToolInputParameter("request", "The request to finance guru (ask in plain english), for example 'what is the stock price of $MSFT?'"));
            a.Tools.Add(tool_financeguru);

            //Add welcoming message
            string opening_msg = "Hi, I'm AIDA, and I'm here to help! What can I do for you?";
            a.Messages.Add(new Message(Role.assistant, opening_msg));
            Console.WriteLine(opening_msg);

            //Version just below
            Assembly ass = Assembly.GetExecutingAssembly();
            Version? v = ass.GetName().Version;
            if (v != null)
            {
                AnsiConsole.MarkupLine("[gray][italic]AIDA version " + v.ToString().Substring(0, v.ToString().Length-2) + "[/][/]");
            }
            
            //Infinite chat
            while (true)
            {
                //Collect input
                Input:
                Console.WriteLine();
                string? input = null;
                while (input == null)
                {
                    Console.Write("> ");
                    input = Console.ReadLine();
                    Console.WriteLine();
                }
                a.Messages.Add(new Message(Role.user, input));

                //Handle special inputs
                if (input.ToLower() == "tokens")
                {
                    AnsiConsole.MarkupLine("[blue][underline]Cumulative Tokens so Far[/][/]");
                    AnsiConsole.MarkupLine("[blue]Prompt tokens: [bold]" + a.CumulativePromptTokens.ToString("#,##0") + "[/][/]");
                    AnsiConsole.MarkupLine("[blue]Completion tokens: [bold]" + a.CumulativeCompletionTokens.ToString("#,##0") + "[/][/]");

                    //Model costs (this is for GPT-4o-mini)
                    float input_cost_per_1M = 0.15f;
                    float output_cost_per_1M = 0.60f;

                    //Calculate costs
                    float input_costs = (input_cost_per_1M / 1000000f) * a.CumulativePromptTokens;
                    float output_costs = (output_cost_per_1M / 1000000f) * a.CumulativeCompletionTokens;

                    Console.WriteLine();
                    AnsiConsole.MarkupLine("[blue][underline]Token Cost Estimates[/][/]");
                    AnsiConsole.MarkupLine("[blue]Input token costs: [bold]$" + input_costs.ToString("#,##0.00") + "[/][/]");
                    AnsiConsole.MarkupLine("[blue]Output token costs: [bold]$" + output_costs.ToString("#,##0.00") + "[/][/]");

                    goto Input;
                }
                else if (input.ToLower() == "config") //Where the config files are
                {
                    Console.WriteLine(ConfigDirectory);
                    goto Input;
                }

                //Prompt
                Prompt:
                AnsiConsole.Markup("[gray][italic]thinking... [/][/]");
                Message response = await a.PromptAsync();
                a.Messages.Add(response); //Add response to message array
                Console.WriteLine();

                //Write content if there is some
                if (response.Content != null)
                {
                    if (response.Content != "")
                    {
                        Console.WriteLine(response.Content);
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
                        if (tc.ToolName == "check_temperature")
                        {
                            tool_call_response_payload = await CheckTemperature(27.17f, -82.46f);
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
                        else if (tc.ToolName == "schedule_reminder")
                        {

                            if (mgh != null) //if we are logged in to Microsoft Graph, we can do it!
                            {
                                //Get name of the reminder
                                string? ReminderName = null;
                                JProperty? prop_name = tc.Arguments.Property("name");
                                if (prop_name != null)
                                {
                                    ReminderName = prop_name.Value.ToString();
                                }

                                //Get the datetime
                                DateTime? ReminderDateTime = null;
                                JProperty? prop_datetime = tc.Arguments.Property("datetime");
                                if (prop_datetime != null)
                                {
                                    string ai_provided_datetime = prop_datetime.Value.ToString();
                                    try
                                    {
                                        ReminderDateTime = DateTime.Parse(ai_provided_datetime);
                                    }
                                    catch (Exception ex)
                                    {
                                        tool_call_response_payload = "Unable to parse '" + ai_provided_datetime + " datetime provided by AI into a valid DateTime. Msg: " + ex.Message;
                                    }
                                }


                                //Call to function
                                if (ReminderName != null && ReminderDateTime != null)
                                {
                                    //Console.WriteLine("About to call to schedule a reminder.");
                                    //Console.WriteLine("Reminder name: " + ReminderName);
                                    //Console.WriteLine("ReminderDateTime: " + ReminderDateTime.ToString());
                                    tool_call_response_payload = await ScheduleReminder(mgh, ReminderName, ReminderDateTime.Value, MicrosoftGraphHelperPath);
                                }
                                else
                                {
                                    tool_call_response_payload = "Due to being unable to collect and parse at least one of the necessary parameters to make a new calendar reminder, cancelling attempt to set reminder.";
                                }
                            }
                            else
                            {
                                tool_call_response_payload = "Unable to schedule reminder because we are not logged in to Microsoft Outlook!";
                            }                            
                        }
                        else if (tc.ToolName == "check_current_time")
                        {
                            tool_call_response_payload = "The current date and time is " + DateTime.Now.ToString();
                        }
                        else if (tc.ToolName == "send_email")
                        {

                            if (mgh != null)
                            {
                                //Get to line
                                string? to = null;
                                JProperty? prop_to = tc.Arguments.Property("to");
                                if (prop_to != null)
                                {
                                    to = prop_to.Value.ToString();
                                }

                                //Get subject
                                string? subject = null;
                                JProperty? prop_subject = tc.Arguments.Property("subject");
                                if (prop_subject != null)
                                {
                                    subject = prop_subject.Value.ToString();
                                }

                                //Get body
                                string? body = null;
                                JProperty? prop_body = tc.Arguments.Property("body");
                                if (prop_body != null)
                                {
                                    body = prop_body.Value.ToString();
                                }

                                //If we have all the properties, call!
                                if (to != null && subject != null && body != null)
                                {
                                    await SendEmail(mgh, to, subject, body, MicrosoftGraphHelperPath);
                                }
                                else
                                {
                                    tool_call_response_payload = "Due to being unable to collect all three necessary input parameters (to, subject, body), unable to initiate the sending of the email.";
                                }
                            }
                            else
                            {
                                tool_call_response_payload = "Unable to send email because we are not logged in to Microsoft Outlook!";
                            }
                        }
                        else if (tc.ToolName == "search_web")
                        {
                            if (bss != null)
                            {
                                string? search_phrase = null;
                                JProperty? prop_search_phrase = tc.Arguments.Property("search_phrase");
                                if (prop_search_phrase != null)
                                {
                                    search_phrase = prop_search_phrase.Value.ToString();
                                }

                                //Search
                                if (search_phrase != null)
                                {
                                    tool_call_response_payload = await SearchWeb(bss, search_phrase);
                                }
                                else
                                {
                                    tool_call_response_payload = "Unable to trigger web search because the search phase parameter was not successfully provided by the AI model.";
                                }
                            }
                            else
                            {
                                tool_call_response_payload = "Unable to search the web because a Bing Search API key was never provided. Please add one to " + BingSearchApiKeyPath + " and restart AIDA to enable search.";
                            }
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
                        else if (tc.ToolName == "ask_finance_guru")
                        {
                            //Get request
                            string? request = null;
                            JProperty? prop_request = tc.Arguments.Property("request");
                            if (prop_request != null)
                            {
                                request = prop_request.Value.ToString();
                            }

                            //Call to the agent
                            if (request != null)
                            {
                                tool_call_response_payload = await AskFinanceGuru(request);
                            }
                            else
                            {
                                tool_call_response_payload = "Unable to ask Finance Guru because the 'request' parameter to FinanceGuru was not successfully provided by the AI.";
                            }
                        }

                        //Append tool response to messages
                        Message ToolResponseMessage = new Message();
                        ToolResponseMessage.Role = Role.tool;
                        ToolResponseMessage.ToolCallID = tc.ID;
                        ToolResponseMessage.Content = tool_call_response_payload;
                        a.Messages.Add(ToolResponseMessage);

                        //Confirm completion of tool call
                        AnsiConsole.MarkupLine("[gray][italic]complete[/][/]");
                    }

                    //Prompt right away (do not ask for user for input yet)
                    goto Prompt;
                }
                

                
            } //END INFINITE CHAT
            


        }


        //////// TOOLS /////////
        
        public static async Task<string> CheckTemperature(float latitude, float longitude)
        {
            string url = "https://api.open-meteo.com/v1/forecast?latitude=" + latitude.ToString() + "&longitude=" + longitude.ToString() + "&current=temperature_2m&temperature_unit=fahrenheit";
            HttpClient hc = new HttpClient();
            HttpResponseMessage resp = await hc.GetAsync(url);
            string content = await resp.Content.ReadAsStringAsync();
            if (resp.StatusCode != HttpStatusCode.OK)
            {
                throw new Exception("Request to open-meteo.com to get temperature return code '" + resp.StatusCode.ToString() + "'. Msg: " + content);
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

        public static async Task RefreshMicrosoftGraphAccessTokensIfExpiredAsync(MicrosoftGraphHelper mgh, string MicrosoftGraphHelperLocalFilePath)
        {
            //Check if credentials need to be refreshed
            if (mgh.AccessTokenHasExpired())
            {

                //Refresh
                try
                {
                    await mgh.RefreshAccessTokenAsync();
                }
                catch (Exception ex)
                {
                    AnsiConsole.MarkupLine("[red]Refreshing of graph token failed: " + ex.Message + "[/]");
                }

                //Save updated credentials to file
                System.IO.File.WriteAllText(MicrosoftGraphHelperLocalFilePath, JsonConvert.SerializeObject(mgh, Formatting.Indented));
            }
        }

        public static async Task<string> ScheduleReminder(MicrosoftGraphHelper mgh, string reminder_name, DateTime time, string MicrosoftGraphHelperLocalFilePath)
        {
            try
            {
                await RefreshMicrosoftGraphAccessTokensIfExpiredAsync(mgh, MicrosoftGraphHelperLocalFilePath);
            }
            catch (Exception ex)
            {
                return "Unable to set reminder due to refreshing of Microsoft Graph Access tokens failing. Error message: " + ex.Message;
            }

            //Set subject
            OutlookEvent ev = new OutlookEvent();
            ev.Subject = reminder_name;
            
            //Set start and end time time
            TimeZoneInfo estZone = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
            DateTime utcTime = TimeZoneInfo.ConvertTimeToUtc(time, estZone);
            ev.StartUTC = utcTime;
            ev.EndUTC = ev.StartUTC.AddMinutes(15); //15 minute appointment

            //Schedule
            try
            {
                await mgh.CreateOutlookEventAsync(ev);
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine("[red]Scheduling of reminder failed: " + ex.Message + "[/]");
                return "Attempt to schedule reminder failed. There was an issue when creating it. Error message: " + ex.Message;
            }
            
            return "Reminder '" + reminder_name + "' successfully scheduled for '" + time.ToString() + "' EST ('" + time.ToString() + "' UTC). When confirming this with the user, explicitly confirm the reminder name and date/time it was scheduled for."; 
        }


        public static async Task<string> SendEmail(MicrosoftGraphHelper mgh, string to, string subject, string body, string MicrosoftGraphHelperLocalFilePath)
        {
            try
            {
                await RefreshMicrosoftGraphAccessTokensIfExpiredAsync(mgh, MicrosoftGraphHelperLocalFilePath);
            }
            catch (Exception ex)
            {
                return "Unable to set send email due to refreshing of Microsoft Graph Access tokens failing. Error message: " + ex.Message;
            }

            //Create email
            OutlookEmailMessage email = new OutlookEmailMessage();
            email.ToRecipients.Add(to);
            email.Subject = subject;
            email.Content = body;
            email.ContentType = OutlookEmailMessageContentType.Text;

            //Send the email
            try
            {
                await mgh.SendOutlookEmailMessageAsync(email);
            }
            catch (Exception ex)
            {
                return "Sending outlook email failed! Error message: " + ex.Message;
            }


            return "Email sent successfully.";
        }

        public static async Task<string> SearchWeb(BingSearchService bss, string search_phrase)
        {
            //Search
            BingSearchResult[] results;
            try
            {
                results = await bss.SearchAsync(search_phrase);
            }
            catch (Exception ex)
            {
                return "Bing search failed! Error message: " + ex.Message;
            }

            
            //provide response
            return JsonConvert.SerializeObject(results);
        }

        public static async Task<string> ReadWebpage(string url)
        {
            HttpClient hc = new HttpClient();
            HttpRequestMessage req = new HttpRequestMessage();
            req.Method = HttpMethod.Get;
            req.RequestUri = new Uri(url);
            req.Headers.Add("User-Agent", "AIDA/1.0.0");
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

        public static async Task<string> AskFinanceGuru(string request)
        {
            //Add the msg
            FinanceGuru.Messages.Add(new Message(Role.user, request));

            //Call
            CallFinanceGuru:
            Message AiResponse = await FinanceGuru.PromptAsync();
            FinanceGuru.Messages.Add(AiResponse); //Add the message/tool call (whatever the AI responded with!) to message history

            //Did it call tools?
            if (AiResponse.ToolCalls.Length > 0)
            {
                foreach (ToolCall tc in AiResponse.ToolCalls)
                {
                    string ToolResponseToProvide = "";

                    //Retrieve the necessary data
                    if (tc.ToolName == "check_stock_price")
                    {
                        JProperty? prop_stock_symbol = tc.Arguments.Property("stock_symbol");
                        if (prop_stock_symbol != null)
                        {
                            string stock_symbol = prop_stock_symbol.Value.ToString();
                            try
                            {
                                Equity e = Equity.Create(stock_symbol);
                                await e.DownloadSummaryAsync();
                                ToolResponseToProvide = JsonConvert.SerializeObject(e);
                            }
                            catch (Exception ex)
                            {
                                ToolResponseToProvide = "There was an error while retrieving stock data from Yahoo Finance! Error message: " + ex.Message;
                            }

                        }
                        else
                        {
                            ToolResponseToProvide = "There was an error within FinanceGuru: AIDA did not specify the stock price to check properly!";
                        }
                    }

                    //Give the agent back that tool call response
                    Message ToolResponse = new Message();
                    ToolResponse.Role = Role.tool;
                    ToolResponse.ToolCallID = tc.ID;
                    ToolResponse.Content = ToolResponseToProvide;
                    FinanceGuru.Messages.Add(ToolResponse);
                }
                goto CallFinanceGuru; //re-prompt now that we gave it what it needs.
            }

            //Return the response back
            if (AiResponse.Content != null)
            {
                return AiResponse.Content;
            }
            else
            {
                return "There was an error while doing what you wanted... I am speechless.";
            }
        }

        public static string ReadFile(string path)
        {
            Console.WriteLine("Got here!");

            //Does file exist?
            if (System.IO.File.Exists(path) == false)
            {
                return "File with path '" + path + "' does not exist!";
            } 

            //Handle based on what type of file it is
            if (path.ToLower().EndsWith(".pdf"))
            {
                //READ PDF CONTENT!
                return "Cannot read a PDF right now, sorry.";
            }
            else //every other file
            {
                return System.IO.File.ReadAllText(path);
            }
        }


    }
}