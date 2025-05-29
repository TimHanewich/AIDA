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
using HtmlAgilityPack;
using System.Reflection;
using TimHanewich.AgentFramework;
using Yahoo.Finance;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;
using System.Security.Cryptography;
using System.IO.Compression;

namespace AIDA
{
    public class Program
    {
        public static void Main(string[] args)
        {
            RunAsync().Wait();
        }

        //GLOBAL VARIABLES
        public static Agent FinanceGuru { get; set; } = new Agent();

        public static async Task RunAsync()
        {

            //Ensure ConsoleOutput is in UTF-8 so it can show bullet points
            //I noticed when you publish and run the exe, it defaults to System.Text.OSEncoding as the ConsoleEncoding
            //When it goes to OSEncoding, the bullet points do not print
            Console.OutputEncoding = System.Text.Encoding.UTF8;

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

            //Add tool: read file
            Tool tool_readfile = new Tool("read_file", "Read the contents of a file of any type (txt, pdf, word document, etc.) from the user's computer");
            tool_readfile.Parameters.Add(new ToolInputParameter("file_path", "The path to the file on the computer, for example 'C:\\Users\\timh\\Downloads\\notes.txt' or '.\\notes.txt' or 'notes.txt'"));
            a.Tools.Add(tool_readfile);

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

            //Add tool: Open Folder
            Tool tool_OpenFolder = new Tool("open_folder", "Open a folder (directory) to see its contents (files and child folders).");
            tool_OpenFolder.Parameters.Add(new ToolInputParameter("folder_path", "Path of the folder, i.e. 'C:\\Users\\timh\\Downloads\\MyFolder' or '/home/tim/Downloads/MyFolder/'"));
            a.Tools.Add(tool_OpenFolder);

            //Setting: AI message color
            string AI_MSG_COLOR = "navyblue"; //the spectre color all AI responses are in (https://spectreconsole.net/appendix/colors)

            //Add welcoming message
            string opening_msg = "Hi, I'm AIDA, and I'm here to help! What can I do for you?";
            a.Messages.Add(new Message(Role.assistant, opening_msg));
            AnsiConsole.MarkupLine("[bold][" + AI_MSG_COLOR + "]" + opening_msg + "[/][/]");

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
            while (true)
            {
            //Collect input
            Input:

                //Collect the raw input
                Console.WriteLine();
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
                    AnsiConsole.MarkupLine("[bold]config[/] - print the path of the configuration directory, where settings files are stored.");
                    AnsiConsole.MarkupLine("[bold]tools[/] - list all tools AIDA has available to it.");
                    AnsiConsole.MarkupLine("[bold]save[/] - Save chat history to a local file.");
                    AnsiConsole.MarkupLine("[bold]load[/] - Save chat history to a local file.");
                    goto Input;
                }
                if (input.ToLower() == "tokens")
                {

                    //Print tokens
                    AnsiConsole.MarkupLine("[blue][underline]Cumulative Tokens so Far[/][/]");
                    AnsiConsole.MarkupLine("[blue]Prompt tokens: [bold]" + a.CumulativePromptTokens.ToString("#,##0") + "[/][/]");
                    AnsiConsole.MarkupLine("[blue]Completion tokens: [bold]" + a.CumulativeCompletionTokens.ToString("#,##0") + "[/][/]");
                    Console.WriteLine();

                    //Print costs
                    float input_cost_per_1M = 2.00f; //in US dollars
                    float output_cost_per_1M = 8.00f; //in US dollars
                    float input_costs = (input_cost_per_1M / 1000000f) * a.CumulativePromptTokens;
                    float output_costs = (output_cost_per_1M / 1000000f) * a.CumulativeCompletionTokens;
                    AnsiConsole.MarkupLine("[blue][underline]Token Cost Estimates[/][/]");
                    AnsiConsole.MarkupLine("[blue]Input token costs: [bold]$" + input_costs.ToString("#,##0.00") + "[/][/]");
                    AnsiConsole.MarkupLine("[blue]Output token costs: [bold]$" + output_costs.ToString("#,##0.00") + "[/][/]");
                    Console.WriteLine();

                    //print the Cost Assumptions
                    AnsiConsole.MarkupLine("[gray][underline]Cost Assumptions Used[/][/]");
                    AnsiConsole.MarkupLine("[gray]Input = $" + input_cost_per_1M.ToString("#,##0.00") + " per 1M tokens[/]");
                    AnsiConsole.MarkupLine("[gray]Output = $" + output_cost_per_1M.ToString("#,##0.00") + " per 1M tokens[/]");

                    goto Input;
                }
                else if (input.ToLower() == "config") //Where the config files are
                {
                    AnsiConsole.MarkupLine("[underline]Config File Directory[/]");
                    AnsiConsole.MarkupLine(ConfigDirectory);
                    Console.WriteLine();
                    AnsiConsole.MarkupLine("[underline]Azure OpenAI Endpoint[/]");
                    AnsiConsole.MarkupLine(azoai.URL);
                    goto Input;
                }
                else if (input.ToLower() == "tools")
                {
                    AnsiConsole.MarkupLine("[underline]AIDA's Available Tools[/]");
                    foreach (Tool t in a.Tools)
                    {
                        AnsiConsole.MarkupLine("[bold][blue]" + t.Name + "[/][/] - [gray]" + t.Description + "[/]");
                    }
                    goto Input;
                }
                else if (input.ToLower() == "clear")
                {
                    a.Messages.Clear(); //clear the message history
                    a.Messages.Add(new Message(Role.system, sysmsg)); //but add the system message back (need that!)
                    AnsiConsole.MarkupLine("[blue][bold]Chat history cleared.[/][/]");
                    goto Input;
                }
                else if (input.ToLower().Trim() == "save")
                {
                    string FileName = "chat-" + Guid.NewGuid().ToString().Replace("-", "") + ".json";
                    string DownloadsPath = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
                    string SavePath = Path.Combine(DownloadsPath, FileName);

                    //Save
                    AnsiConsole.Markup("[gray]saving...[/] ");
                    System.IO.File.WriteAllText(SavePath, JsonConvert.SerializeObject(a.Messages, Formatting.Indented));
                    AnsiConsole.MarkupLine("[green]success![/]");

                    //Say it was successful
                    Console.WriteLine();
                    AnsiConsole.MarkupLine("[green]Chat history saved to:[/]");
                    AnsiConsole.MarkupLine("[green]" + SavePath + "[/]");

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
                    a.Messages = messages.ToList();
                    AnsiConsole.MarkupLine("[green]" + messages.Length.ToString("#,##0") + " messages loaded from " + Markup.Escape(System.IO.Path.GetFileName(FilePath)) + "![/]");

                    //Print everything
                    foreach (Message msg in a.Messages)
                    {
                        if (msg.Role == Role.user)
                        {
                            Console.WriteLine();
                            Console.WriteLine("> " + msg.Content);
                            Console.WriteLine();
                        }
                        else if (msg.Role == Role.assistant)
                        {
                            if (msg.Content != null)
                            {
                                PrintAIMessage(msg.Content, AI_MSG_COLOR);
                            }
                        }
                    }

                    goto Input;
                }
                
                //It did not trigger a special command, so add it to the history, it will be passed to the AI!
                a.Messages.Add(new Message(Role.user, input));

            //Prompt
            Prompt:

                //Prompt the model
                AnsiConsole.Markup("[gray][italic]thinking... [/][/]");
                Message response;
                try
                {
                    response = await a.PromptAsync(9999);
                }
                catch (Exception ex)
                {
                    AnsiConsole.MarkupLine("[red]Uh oh! There was an issue when prompting the underlying model. Message: " + Markup.Escape(ex.Message) + "[/]");
                    Console.WriteLine();
                    Console.WriteLine();
                    AnsiConsole.Markup("[italic][gray]Press enter to try again... [/][/]");
                    Console.ReadLine();
                    goto Prompt;
                }

                //Add it
                a.Messages.Add(response); //Add response to message array
                Console.WriteLine();

                //Write content if there is some
                if (response.Content != null)
                {
                    if (response.Content != "")
                    {
                        PrintAIMessage(response.Content, AI_MSG_COLOR);
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

        //Utilities below

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


    }
}