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
using TimHanewich.Foundry;
using TimHanewich.Foundry.OpenAI.Responses;

namespace AIDA
{
    public class Program
    {
        #region "GLOBAL VARIABLES"

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

            //Set up main AGENT
            AGENT = new Agent();

            #endregion

            //Add system message
            AGENT.Inputs.Add(new Message(Role.developer, Tools.GetSystemPrompt(AIDASettings.Load())));

            //Add welcoming message
            string opening_msg = "Hi, I'm AIDA, and I'm here to help! What can I do for you?";
            AGENT.Inputs.Add(new Message(Role.assistant, opening_msg));
            AnsiConsole.MarkupLine("[bold][" + AIDASettings.Load().AssistantMessageColor + "]" + opening_msg + "[/][/]");

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
                if (input.ToLower() == "/help")
                {
                    AnsiConsole.MarkupLine("Here are the commands you can use:");
                    Console.WriteLine();
                    AnsiConsole.MarkupLine("[bold]/clear[/] - clear the chat history.");
                    AnsiConsole.MarkupLine("[bold]/tokens[/] - check token consumption for this session.");
                    AnsiConsole.MarkupLine("[bold]/settings[/] - Open AIDA's settings menu");
                    AnsiConsole.MarkupLine("[bold]/tools[/] - list all tools AIDA has available to it.");
                    AnsiConsole.MarkupLine("[bold]/auth[/] - authenticate into Foundry if using Service Principal.");
                    AnsiConsole.MarkupLine("[bold]/stats[/] - view usage statistics.");
                    Console.WriteLine();
                    goto Input;
                }
                if (input.ToLower() == "/tokens")
                {

                    //Print tokens
                    AnsiConsole.MarkupLine("[blue][underline]Tokens Consumed in This Sesssion so Far[/][/]");
                    AnsiConsole.MarkupLine("[blue]Prompt tokens: [bold]" + AGENT.CumulativeInputTokens.ToString("#,##0") + "[/][/]");
                    AnsiConsole.MarkupLine("[blue]Completion tokens: [bold]" + AGENT.CumulativeOutputTokens.ToString("#,##0") + "[/][/]");
                    Console.WriteLine();

                    Console.WriteLine();
                    goto Input;
                }
                else if (input.ToLower() == "/settings") //Where the config files are
                {

                    //Present settings menu and allow them to change things
                    SettingsMenu();
                    Console.WriteLine();
                    goto Input;
                }
                else if (input.ToLower() == "/tools")
                {
                    AnsiConsole.MarkupLine("[underline]AIDA's Available Tools[/]");
                    foreach (Function f in DetermineAvailableFunctions())
                    {
                        AnsiConsole.MarkupLine("[bold][blue]" + f.Name + "[/][/] - [gray]" + f.Description + "[/]");
                    }
                    Console.WriteLine();
                    goto Input;
                }
                else if (input.ToLower() == "/clear")
                {
                    AGENT.ClearHistory();
                    AGENT.Inputs.Add(new Message(Role.user, Tools.GetSystemPrompt(AIDASettings.Load()))); //add the system message back (need that!)
                    AnsiConsole.MarkupLine("[blue][bold]Chat history cleared. Latest prompt.md injected.[/][/]");
                    Console.WriteLine();
                    goto Input;
                }
                else if (input.ToLower() == "/auth")
                {
                    AnsiConsole.MarkupLine("Attempting Microsoft Foundry Authentication... ");
                    await Tools.FoundryAuthAsync(AIDASettings.Load());
                    goto Input;
                }
                else if (input.ToLower() == "/stats")
                {
                    Stats s = Stats.Load();
                    s.PrintReport();
                    goto Input;
                }

                //It did not trigger a special command, so add it to the history, it will be passed to the AI!
                AGENT.Inputs.Add(new Message(Role.user, input));

            //Prompt
            Prompt:

                //Configure the agent's foundry connection
                if (AIDASettings.Load().FoundryUrl != null)
                {
                    AGENT.FoundryConnection = new FoundryResource(AIDASettings.Load().FoundryUrl);
                    
                    //If we are using API key auth, plug that in
                    if (AIDASettings.Load().ApiKey != null)
                    {
                        AGENT.FoundryConnection.ApiKey = AIDASettings.Load().ApiKey;
                    }
                    else if (AIDASettings.Load().TenantID != null && AIDASettings.Load().ClientID != null && AIDASettings.Load().ClientSecret != null) // if instead we are using entra ID auth
                    {
                        //Do we have a non-expired token right now?
                        bool NeedNewToken = true;
                        if (AIDASettings.Load().AuthenticatedTokenCredentials != null)
                        {
                            if (AIDASettings.Load().AuthenticatedTokenCredentials.Expires >= DateTime.UtcNow) // if it is NOT expired yet
                            {
                                AGENT.FoundryConnection.AccessToken = AIDASettings.Load().AuthenticatedTokenCredentials.AccessToken;
                                NeedNewToken = false;
                            }
                        }

                        //If we need a new token, get it
                        if (NeedNewToken)
                        {
                            await Tools.FoundryAuthAsync(AIDASettings.Load());

                            //If it was successful
                            if (AIDASettings.Load().AuthenticatedTokenCredentials != null)
                            {
                                AGENT.FoundryConnection.AccessToken = AIDASettings.Load().AuthenticatedTokenCredentials.AccessToken; //Plug in the latest token to the agent for it to use
                            }

                            //Line break
                            Console.WriteLine();
                        }
                    }
                }

                //Configure the agent's model
                if (AIDASettings.Load().ModelName != null)
                {
                    AGENT.ModelName = AIDASettings.Load().ModelName;
                }

                #region "Plug in tools & functions"

                //CLEAR TOOLS (so we don't re-add them)
                //The clearing and re-adding process will happen each time so they can update the tools available on the fly
                AGENT.Functions.Clear();

                //Add them back
                AGENT.Functions = DetermineAvailableFunctions().ToList();

                //Built-in tools: web search
                AGENT.WebSearchEnabled = AIDASettings.Load().WebSearchEnabled;

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
                            PrintAIMessage(msg.Text, AIDASettings.Load().AssistantMessageColor);
                            Console.WriteLine();
                        }
                    }
                    else if (ex is FunctionCall fc) // if it is a function call
                    {
                        AnsiConsole.Markup("[gray][italic]calling tool '" + fc.FunctionName + "'... [/][/]");
                        string tool_call_response_payload = "";

                        //Call to the tool and save the response from that tool
                        if (fc.FunctionName == "write_file")
                        {
                            //Get file name
                            string file_name = "dummy.txt";
                            JProperty? prop_file_name = fc.Arguments.Property("path");
                            if (prop_file_name != null)
                            {
                                file_name = prop_file_name.Value.ToString();
                            }

                            //Get file content
                            string file_content = "(dummy content)";
                            JProperty? prop_file_content = fc.Arguments.Property("content");
                            if (prop_file_content != null)
                            {
                                file_content = prop_file_content.Value.ToString();
                            }

                            //Save file
                            AnsiConsole.Markup("[gray][italic]writing '" + Markup.Escape(file_name) + "'... [/][/]");
                            tool_call_response_payload = WriteFile(file_name, file_content);
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

                            AnsiConsole.Markup("[gray][italic]reading '" + Markup.Escape(file_path) + "'... [/][/]");
                            tool_call_response_payload = ReadFile(file_path);
                        }
                        else if (fc.FunctionName == "check_current_time")
                        {
                            tool_call_response_payload = "The current date and time is " + DateTime.Now.ToString();
                        }
                        else if (fc.FunctionName == "web_fetch")
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
                        else if (fc.FunctionName == "edit_file")
                        {
                            //Get the 'path' parameter
                            string? path = null;
                            JProperty? prop_path = fc.Arguments.Property("path");
                            if (prop_path != null)
                            {
                                path = prop_path.Value.ToString();
                            }

                            //Get the 'old_string' parameter
                            string? old_string = null;
                            JProperty? prop_old_string = fc.Arguments.Property("old_string");
                            if (prop_old_string != null)
                            {
                                old_string = prop_old_string.Value.ToString();
                            }

                            //Get the 'new_string' parameter
                            string? new_string = null;
                            JProperty? prop_new_string = fc.Arguments.Property("new_string");
                            if (prop_new_string != null)
                            {
                                new_string = prop_new_string.Value.ToString();
                            }

                            //Get the 'replace_all' parameter
                            bool replace_all = true;
                            JProperty? prop_replace_all = fc.Arguments.Property("replace_all");
                            if (prop_replace_all != null)
                            {
                                try
                                {
                                    replace_all = bool.Parse(prop_replace_all.Value.ToString());
                                }
                                catch
                                {
                                    replace_all = true;
                                }
                            }

                            //Handle
                            if (path != null && old_string != null && new_string != null)
                            {
                                AnsiConsole.Markup("[gray][italic]editing '" + Markup.Escape(path) + "'... [/][/]");
                                tool_call_response_payload = EditFile(path, old_string, new_string, replace_all);
                            }
                            else
                            {
                                tool_call_response_payload = "You must provide 'path', 'old_string', and 'new_string' parameters!";
                            }
                        }
                        else if (fc.FunctionName == "delete_file")
                        {
                            //Get the 'path' parameter
                            string? path = null;
                            JProperty? prop_path = fc.Arguments.Property("path");
                            if (prop_path != null)
                            {
                                path = prop_path.Value.ToString();
                            }

                            //Handle
                            if (path == null)
                            {
                                tool_call_response_payload = "You must provide the 'path' parameter!";
                            }
                            else if (System.IO.File.Exists(path) == false)
                            {
                                tool_call_response_payload = "File at '" + path + "' does not exist!";
                            }
                            else
                            {
                                try
                                {
                                    AnsiConsole.Markup("[gray][italic]deleting '" + Markup.Escape(path) + "'... [/][/]");
                                    System.IO.File.Delete(path);
                                    tool_call_response_payload = "File '" + path + "' was successfully deleted.";
                                }
                                catch (Exception ex2)
                                {
                                    tool_call_response_payload = "Deletion of file failed. Exception message: " + ex2.Message;
                                }
                            }
                        }
                        else if (fc.FunctionName == "explore_directory")
                        {
                            //Get the 'path' parameter
                            string? path = null;
                            JProperty? prop_path = fc.Arguments.Property("path");
                            if (prop_path != null)
                            {
                                path = prop_path.Value.ToString();
                            }

                            //Handle
                            if (path == null)
                            {
                                tool_call_response_payload = "You must provide the 'path' parameter!";
                            }
                            else if (System.IO.Directory.Exists(path) == false)
                            {
                                tool_call_response_payload = "Directory at '" + path + "' does not exist!";
                            }
                            else
                            {
                                AnsiConsole.Markup("[gray][italic]exploring '" + Markup.Escape(path) + "'... [/][/]");
                                try
                                {
                                    List<string> entries = new List<string>();

                                    //Get directories
                                    string[] dirs = System.IO.Directory.GetDirectories(path);
                                    foreach (string d in dirs)
                                    {
                                        entries.Add("[DIR] " + System.IO.Path.GetFileName(d));
                                    }

                                    //Get files
                                    string[] files = System.IO.Directory.GetFiles(path);
                                    foreach (string f in files)
                                    {
                                        entries.Add("[FILE] " + System.IO.Path.GetFileName(f));
                                    }

                                    //Construct response
                                    if (entries.Count == 0)
                                    {
                                        tool_call_response_payload = "The directory is empty.";
                                    }
                                    else
                                    {
                                        tool_call_response_payload = "Contents of '" + path + "':" + "\n" + string.Join("\n", entries);
                                    }
                                }
                                catch (Exception ex2)
                                {
                                    tool_call_response_payload = "Exploring directory failed. Exception message: " + ex2.Message;
                                }
                            }
                        }
                        else if (fc.FunctionName == "create_directory")
                        {
                            //Get the 'path' parameter
                            string? path = null;
                            JProperty? prop_path = fc.Arguments.Property("path");
                            if (prop_path != null)
                            {
                                path = prop_path.Value.ToString();
                            }

                            //Handle
                            if (path == null)
                            {
                                tool_call_response_payload = "You must provide the 'path' parameter!";
                            }
                            else if (System.IO.Directory.Exists(path))
                            {
                                tool_call_response_payload = "Directory at '" + path + "' already exists!";
                            }
                            else
                            {
                                AnsiConsole.Markup("[gray][italic]creating directory '" + Markup.Escape(path) + "'... [/][/]");
                                try
                                {
                                    System.IO.Directory.CreateDirectory(path);
                                    tool_call_response_payload = "Directory '" + path + "' was successfully created.";
                                }
                                catch (Exception ex2)
                                {
                                    tool_call_response_payload = "Creation of directory failed. Exception message: " + ex2.Message;
                                }
                            }
                        }
                        else if (fc.FunctionName == "delete_directory")
                        {
                            //Get the 'path' parameter
                            string? path = null;
                            JProperty? prop_path = fc.Arguments.Property("path");
                            if (prop_path != null)
                            {
                                path = prop_path.Value.ToString();
                            }

                            //Handle
                            if (path == null)
                            {
                                tool_call_response_payload = "You must provide the 'path' parameter!";
                            }
                            else if (System.IO.Directory.Exists(path) == false)
                            {
                                tool_call_response_payload = "Directory at '" + path + "' does not exist!";
                            }
                            else if (System.IO.Directory.GetFiles(path).Length > 0 || System.IO.Directory.GetDirectories(path).Length > 0)
                            {
                                tool_call_response_payload = "Directory at '" + path + "' is not empty! You must delete all files and sub-directories first using the `delete_file` tool before you can delete this directory.";
                            }
                            else
                            {
                                AnsiConsole.Markup("[gray][italic]deleting directory '" + Markup.Escape(path) + "'... [/][/]");
                                try
                                {
                                    System.IO.Directory.Delete(path);
                                    tool_call_response_payload = "Directory '" + path + "' was successfully deleted.";
                                }
                                catch (Exception ex2)
                                {
                                    tool_call_response_payload = "Deletion of directory failed. Exception message: " + ex2.Message;
                                }
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
                                AnsiConsole.Markup("[gray][italic]running shell '" + Markup.Escape(cmd) + "'... [/][/]");
                                string response = await Tools.ExecuteShellAsync(cmd);
                                tool_call_response_payload = response;
                            }
                            else
                            {
                                tool_call_response_payload = "You must provide the 'command' parameter! It wasn't provided.";
                            }
                        }
                        else if (fc.FunctionName == "view_image")
                        {
                            //Get path
                            string path = "";
                            JProperty? prop_path = fc.Arguments.Property("path");
                            if (prop_path != null)
                            {
                                path = prop_path.Value.ToString();
                            }

                            //Handle
                            if (path != "")
                            {
                                AnsiConsole.Markup("[gray][italic]adding image at '" + path + "'... [/][/]");
                                if (System.IO.File.Exists(path)) // it is a file on-device
                                {
                                    //Try loading the image 
                                    try
                                    {
                                        Message img_msg = new Message();
                                        img_msg.Role = Role.user;
                                        img_msg.Text = "This is the image at '" + path + "':";
                                        InputImage ii = InputImage.FromFile(path);
                                        img_msg.Images.Add(ii);
                                        AGENT.Inputs.Add(img_msg); //add it as a message to go in next time.
                                    }
                                    catch (Exception ex2)
                                    {
                                        tool_call_response_payload = "Loading of image at '" + path + "' failed: " + ex2.Message;
                                    }
                                }
                                else //may be a URL
                                {
                                    Message img_msg = new Message();
                                    img_msg.Role = Role.user;
                                    img_msg.Text = "This is the image at '" + path + "':";
                                    InputImage ii = InputImage.FromURL(path);
                                    img_msg.Images.Add(ii);
                                    AGENT.Inputs.Add(img_msg); //add it as a message to go in next time.
                                } 

                                //Fill in response
                                tool_call_response_payload = "Image at '" + path + "' has been added by the user."; //we say 'by the user' here because the message that is under is a 'user' role message ('developer' role can't be used to provide images)
                            }
                            else
                            {
                                tool_call_response_payload = "'path' parameter was empty!";
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

        public static string WriteFile(string path, string file_content)
        {
            string? DestinationDirectory = Path.GetDirectoryName(path);
            if (DestinationDirectory == null)
            {
                return "Unable to determine destination directory from the path you provided. Are you sure it is valid?";
            }
            if (Directory.Exists(DestinationDirectory) == false)
            {
                return "Path invalid! Destination directory does not exist";
            }
            System.IO.File.WriteAllText(path, file_content);
            return "File successfully saved to '" + path + "'.";
        }

        public static string EditFile(string path, string old_string, string new_string, bool replace_all = true)
        {
            //Exist check
            if (System.IO.File.Exists(path) == false)
            {
                return "File at '" + path + "' does not exist!";
            }

            //Get content
            string content;
            try
            {
                content = System.IO.File.ReadAllText(path);
            }
            catch (Exception ex)
            {
                return "There was an issue opening the file: " + ex.Message;
            }

            //Handle
            string[] split = old_string.Split(old_string);
            int occurences = split.Length - 1;
            if (occurences == 0)
            {
                return "No edits made. There were no occurences of the old_string you provided in the file content.";
            }
            else if (occurences > 1)
            {
                if (replace_all == false)
                {
                    return "There are multiple occurences of the old_string you provided in the file content and you did not have 'replace_all' enabled. Please expand the context of the 'old_string' (make it unique) to clearly indicate what portion you want to edit."; 
                }
            }

            //Replace
            content = content.Replace(old_string, new_string);

            //Write back
            System.IO.File.WriteAllText(path, content);

            //return
            return "File edit was successful.";
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
                AnsiConsole.MarkupLine("[bold][underline]AIDA AIDASettings.Load() MENU[/][/]");

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
                if (AIDASettings.Load().FoundryUrl != null)
                {
                    AnsiConsole.MarkupLine("Foundry Resource: " + AIDASettings.Load().FoundryUrl);
                }
                else
                {
                    AnsiConsole.MarkupLine("Foundry Resource: [italic]none[/]");
                }

                //Foundry Auth
                if (AIDASettings.Load().ApiKey != null)
                {
                    AnsiConsole.MarkupLine("Auth Type: API Key");
                }
                else if (AIDASettings.Load().TenantID != null && AIDASettings.Load().ClientID != null && AIDASettings.Load().ClientSecret != null)
                {
                    AnsiConsole.MarkupLine("Auth Type: Access Token");
                }
                else
                {
                    AnsiConsole.MarkupLine("Auth Type: (unknown)");
                }

                //Model name
                if (AIDASettings.Load().ModelName != null)
                {
                    AnsiConsole.MarkupLine("Model: " + AIDASettings.Load().ModelName);
                }
                else
                {
                    AnsiConsole.MarkupLine("Model: (none)");
                }

                //Assistant color
                AnsiConsole.MarkupLine("AI Assistant Msg Color: [bold]" + AIDASettings.Load().AssistantMessageColor + "[/] ([" + AIDASettings.Load().AssistantMessageColor + "]looks like this[/])");

                //Ask what to do
                Console.WriteLine();
                SelectionPrompt<string> SettingToDo = new SelectionPrompt<string>();
                SettingToDo.Title("What do you want to do?");
                SettingToDo.AddChoice("Update Foundry Connection Info");
                SettingToDo.AddChoice("Update Model");
                SettingToDo.AddChoice("Change Assistant Message Color");
                SettingToDo.AddChoice("Enable/Disable Tools");
                SettingToDo.AddChoice("Save & Continue");
                string SettingToDoAnswer = AnsiConsole.Prompt(SettingToDo);

                //Handle what to do
                if (SettingToDoAnswer == "Update Foundry Connection Info")
                {
                    //Get foundry URL
                    AIDASettings.Load().FoundryUrl = AnsiConsole.Ask<string>("Foundry URL (i.e. https://myfoundry-resource.services.ai.azure.com)?");
    
                    //Ask how they want to authenticate
                    SelectionPrompt<string> FoundryAuthOptions = new SelectionPrompt<string>();
                    FoundryAuthOptions.Title("How do you want to authenticate?");
                    FoundryAuthOptions.AddChoice("API Key");
                    FoundryAuthOptions.AddChoice("Entra ID");
                    string FoundryAuthSelection = AnsiConsole.Prompt(FoundryAuthOptions);

                    //Handle auth
                    if (FoundryAuthSelection == "API Key")
                    {
                        AIDASettings.Load().ApiKey = AnsiConsole.Ask<string>("What is the API key?");

                        //Clear out any existing entra ID tokens
                        AIDASettings.Load().AuthenticatedTokenCredentials = null;

                        //Clear out the Entra ID info becuase now we wil use API key
                        AIDASettings.Load().TenantID = null;
                        AIDASettings.Load().ClientID = null;
                        AIDASettings.Load().ClientSecret = null;
                    }
                    else if (FoundryAuthSelection == "Entra ID")
                    {
                        AIDASettings.Load().TenantID = AnsiConsole.Ask<string>("Tenant ID?");
                        AIDASettings.Load().ClientID = AnsiConsole.Ask<string>("Client ID?");
                        AIDASettings.Load().ClientSecret = AnsiConsole.Ask<string>("Client Secret?");
                        
                        //Clear out any existing entra ID tokens
                        AIDASettings.Load().AuthenticatedTokenCredentials = null;

                        //Clear out any API key because now we will use Entra ID
                        AIDASettings.Load().ApiKey = null;
                    }
                }
                else if (SettingToDoAnswer == "Update Model")
                {
                    string model_name = AnsiConsole.Ask<string>("Model name?");
                    AIDASettings.Load().ModelName = model_name;
                    AnsiConsole.MarkupLine("Model updated to '" + model_name + "'");
                }
                else if (SettingToDoAnswer == "Change Assistant Message Color")
                {
                    AnsiConsole.MarkupLine("Visit here to see the available colors: [bold]https://spectreconsole.net/appendix/colors[/]");
                    string NewColor = AnsiConsole.Ask<string>("New color?");
                    try
                    {
                        AnsiConsole.MarkupLine("Future AI messages will be in [" + NewColor + "]this color[/]");
                        AIDASettings.Load().AssistantMessageColor = NewColor;
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
                    PackagesQuestion.AddChoice("Shell");

                    //Defaults
                    if (AIDASettings.Load().WebSearchEnabled)
                    {
                        PackagesQuestion.Select("Web Search (built in)");
                    }
                    if (AIDASettings.Load().ShellEnabled)
                    {
                        PackagesQuestion.Select("Shell");
                    }

                    //Ask
                    List<string> PackagesToEnable = AnsiConsole.Prompt(PackagesQuestion);

                    //Enable/Disable: Web Search
                    if (PackagesToEnable.Contains("Web Search (built in)"))
                    {
                        AIDASettings.Load().WebSearchEnabled = true;
                    }
                    else
                    {
                        AIDASettings.Load().WebSearchEnabled = false;
                    }

                    //Enable/Disable: Shell
                    AIDASettings.Load().ShellEnabled = PackagesToEnable.Contains("Shell");

                    //Confirm
                    AnsiConsole.MarkupLine("[green][bold]" + PackagesToEnable.Count.ToString() + " packages enabled[/][/]");
                }
                else if (SettingToDoAnswer == "Save & Continue")
                {
                    AnsiConsole.Markup("[gray]Saving settings... [/]");
                    AIDASettings.Load().Save();
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

            //Add tool: check current time
            Function tool_checkcurrenttime = new Function("check_current_time", "Check the current date and time right now.");
            ToReturn.Add(tool_checkcurrenttime);

            //Add tool: read file
            Function tool_readfile = new Function("read_file", "Read the contents of a file of any type (txt, pdf, word document, etc.) from the user's computer");
            tool_readfile.Parameters.Add(new FunctionInputParameter("file_path", "The path to the file on the computer, for example 'C:\\Users\\timh\\Downloads\\notes.txt' or '.\\notes.txt' or 'notes.txt'"));
            ToReturn.Add(tool_readfile);

            //Add tool: write file
            Function tool_savetxtfile = new Function("write_file", "Create a new file on the user's computer in the current directory.");
            tool_savetxtfile.Parameters.Add(new FunctionInputParameter("path", "The path of the file to write."));
            tool_savetxtfile.Parameters.Add(new FunctionInputParameter("content", "The content of the file."));
            ToReturn.Add(tool_savetxtfile);

            //Add tool: edit file
            Function tool_EditFile = new Function("edit_file", "Edit an existing file on the user's computer by replacing a specific string with a new string.");
            tool_EditFile.Parameters.Add(new FunctionInputParameter("path", "The path of the file to edit."));
            tool_EditFile.Parameters.Add(new FunctionInputParameter("old_string", "The existing string in the file to find and replace."));
            tool_EditFile.Parameters.Add(new FunctionInputParameter("new_string", "The new string to replace the old string with."));
            tool_EditFile.Parameters.Add(new FunctionInputParameter("replace_all", "If true, replace all occurrences. If false, only replace if the old_string is unique in the file. Defaults to true."));
            ToReturn.Add(tool_EditFile);

            //Add tool: delete file
            Function tool_DeleteFile = new Function("delete_file", "Delete a file from the user's computer.");
            tool_DeleteFile.Parameters.Add(new FunctionInputParameter("path", "The path of the file to delete."));
            ToReturn.Add(tool_DeleteFile);

            //Add tool: explore directory
            Function tool_ExploreDirectory = new Function("explore_directory", "Explore the contents of a directory (folder) on the user's computer, listing out all files and sub-directories contained in it.");
            tool_ExploreDirectory.Parameters.Add(new FunctionInputParameter("path", "The path of the directory to explore."));
            ToReturn.Add(tool_ExploreDirectory);

            //Add tool: create directory
            Function tool_CreateDirectory = new Function("create_directory", "Create a new directory (folder) on the user's computer.");
            tool_CreateDirectory.Parameters.Add(new FunctionInputParameter("path", "The path of the directory to create."));
            ToReturn.Add(tool_CreateDirectory);

            //Add tool: delete directory
            Function tool_DeleteDirectory = new Function("delete_directory", "Delete an empty directory (folder) from the user's computer. The directory must be empty before it can be deleted.");
            tool_DeleteDirectory.Parameters.Add(new FunctionInputParameter("path", "The path of the directory to delete."));
            ToReturn.Add(tool_DeleteDirectory);

            //Add tool: open web page
            Function tool_readwebpage = new Function("web_fetch", "Make HTTP GET call to retrieve the contents of a URL endpoint (i.e. a webpage or document). Use this tool if the user asks you to read a webpage or retrieve something specific.");
            tool_readwebpage.Parameters.Add(new FunctionInputParameter("url", "The specific URL to GET."));
            ToReturn.Add(tool_readwebpage);

            //Add tool: view image
            Function tool_ViewImage = new Function("view_image", "View an image that is either on the user's device or available at a URL.");
            tool_ViewImage.Parameters.Add(new FunctionInputParameter("path", "Either the path to the image file on the user's device OR the URL of where the image can be accessed online."));
            ToReturn.Add(tool_ViewImage);

            //Is shell enabled?
            if (AIDASettings.Load().ShellEnabled)
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