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

                    //Now that it has changed, refresh settings
                    SETTINGS = AIDASettings.Open();

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
                    AGENT.Inputs.Add(new Message(Role.user, Tools.GetSystemPrompt(SETTINGS))); //add the system message back (need that!)
                    AnsiConsole.MarkupLine("[blue][bold]Chat history cleared. Latest prompt.md injected.[/][/]");
                    Console.WriteLine();
                    goto Input;
                }
                else if (input.ToLower() == "/auth")
                {
                    AnsiConsole.MarkupLine("Attempting Microsoft Foundry Authentication... ");
                    await Tools.FoundryAuthAsync(SETTINGS);
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
                            await Tools.FoundryAuthAsync(SETTINGS);

                            //If it was successful
                            if (SETTINGS.AuthenticatedTokenCredentials != null)
                            {
                                AGENT.FoundryConnection.AccessToken = SETTINGS.AuthenticatedTokenCredentials.AccessToken; //Plug in the latest token to the agent for it to use
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
                        if (fc.FunctionName == "write_file")
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

        public static string WriteFile(string file_name, string file_content)
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
                    PackagesQuestion.AddChoice("Shell");

                    //Defaults
                    if (SETTINGS.WebSearchEnabled)
                    {
                        PackagesQuestion.Select("Web Search (built in)");
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
            Function tool_savetxtfile = new Function("write_file", "Create a new file on the user's computer in the current directory.");
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
            Function tool_readwebpage = new Function("web_fetch", "Make HTTP GET call to retrieve the contents of a URL endpoint (i.e. a webpage or document). Use this tool if the user asks you to read a webpage or retrieve something specific.");
            tool_readwebpage.Parameters.Add(new FunctionInputParameter("url", "The specific URL to GET."));
            ToReturn.Add(tool_readwebpage);

            //Add tool: rename file
            Function tool_RenameFile = new Function("rename_file", "Rename a specific file on the user's drive.");
            tool_RenameFile.Parameters.Add(new FunctionInputParameter("path", "The current absolute path of the file."));
            tool_RenameFile.Parameters.Add(new FunctionInputParameter("new_name", "The new name of the file, NOT including the extension."));
            ToReturn.Add(tool_RenameFile);

            //Add tool: view image
            Function tool_ViewImage = new Function("view_image", "View an image that is either on the user's device or available at a URL.");
            tool_ViewImage.Parameters.Add(new FunctionInputParameter("path", "Either the path to the image file on the user's device OR the URL of where the image can be accessed online."));
            ToReturn.Add(tool_ViewImage);

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