using System;
using Newtonsoft.Json.Linq;
using Spectre.Console;
using System.Reflection;
using TimHanewich.Foundry;
using TimHanewich.AgentFramework;
using TimHanewich.Foundry.OpenAI.Responses;

namespace AIDA
{
    public class Program
    {
        //The agent (from TimHanewich.AgentFramework)
        public static TimHanewich.AgentFramework.Agent AidaAgent = null!;

        //Session-level token tracking (survives /clear)
        public static int SessionInputTokens = 0;
        public static int SessionOutputTokens = 0;

        public static void Main(string[] args)
        {
            RunAsync().Wait();
        }

        public static TimHanewich.AgentFramework.Agent CreateAgent()
        {
            var agent = new TimHanewich.AgentFramework.Agent(Tools.GetSystemPrompt());
            RegisterTools(agent);

            //Event handlers
            agent.InferenceRequested += OnInferenceRequested;
            agent.InferenceReceived += OnInferenceReceived;
            agent.ExecutableFunctionInvoked += OnToolInvoked;
            agent.WebSearchInvoked += OnWebSearch;

            return agent;
        }

        private static void OnToolInvoked(ExecutableFunction ef, JObject arguments)
        {
            //AnsiConsole.Markup("[gray][italic]calling '" + ef.Name + "'... [/][/]");
            AnsiConsole.Markup("[bold]" + ef.Name + "[/]... ");
        }

        private static void OnWebSearch(string query)
        {
            AnsiConsole.MarkupLine("[bold]Web Search[/]: [gray]'" + query+ "'...[/] ");
        }

        private static void OnInferenceRequested()
        {
            AnsiConsole.Markup("[gray][italic]thinking... [/][/]");
        }

        private static void OnInferenceReceived(int input_tokens_consumed, int output_tokens_consumed)
        {
            AnsiConsole.MarkupLine("[gray][italic]complete[/][/]");
        }

        public static void RegisterTools(TimHanewich.AgentFramework.Agent agent)
        {
            agent.Tools.Clear();
            agent.Tools.Add(new CheckCurrentTimeTool());
            agent.Tools.Add(new ReadFileTool());
            agent.Tools.Add(new WriteFileTool());
            agent.Tools.Add(new EditFileTool());
            agent.Tools.Add(new DeleteFileTool());
            agent.Tools.Add(new ExploreDirectoryTool());
            agent.Tools.Add(new CreateDirectoryTool());
            agent.Tools.Add(new DeleteDirectoryTool());
            agent.Tools.Add(new WebFetchTool());
            agent.Tools.Add(new ViewImageTool());

            if (AIDASettings.Load().ShellEnabled)
            {
                agent.Tools.Add(new ShellTool());
            }

            agent.WebSearchEnabled = AIDASettings.Load().WebSearchEnabled;
        }

        public static async Task ConfigureAgentConnectionAsync(TimHanewich.AgentFramework.Agent agent)
        {
            AIDASettings settings = AIDASettings.Load();

            if (settings.FoundryUrl != null)
            {
                agent.FoundryResource = new FoundryResource(settings.FoundryUrl);

                if (settings.ApiKey != null)
                {
                    agent.FoundryResource.ApiKey = settings.ApiKey;
                }
                else if (settings.TenantID != null && settings.ClientID != null && settings.ClientSecret != null)
                {
                    bool NeedNewToken = true;
                    if (settings.AuthenticatedTokenCredentials != null)
                    {
                        if (settings.AuthenticatedTokenCredentials.Expires >= DateTime.UtcNow)
                        {
                            agent.FoundryResource.AccessToken = settings.AuthenticatedTokenCredentials.AccessToken;
                            NeedNewToken = false;
                        }
                    }

                    if (NeedNewToken)
                    {
                        await Tools.FoundryAuthAsync(settings);
                        if (AIDASettings.Load().AuthenticatedTokenCredentials != null)
                        {
                            agent.FoundryResource.AccessToken = AIDASettings.Load().AuthenticatedTokenCredentials.AccessToken;
                        }
                        Console.WriteLine();
                    }
                }
            }

            if (settings.ModelName != null)
            {
                agent.Model = settings.ModelName;
            }
            if (settings.VerbosityLevel != null)
            {
                agent.VerbosityLevel = settings.VerbosityLevel.Value;
            }
            if (settings.ReasoningEffortLevel != null)
            {
                agent.ReasoningEffortLevel = settings.ReasoningEffortLevel.Value;
            }
        }

        public static async Task RunAsync()
        {

            #region "Setup"

            //Ensure ConsoleOutput is in UTF-8 so it can show bullet points
            Console.OutputEncoding = System.Text.Encoding.UTF8;

            //Does config directory exist? if not, make it
            if (System.IO.Directory.Exists(Tools.ConfigDirectoryPath) == false)
            {
                System.IO.Directory.CreateDirectory(Tools.ConfigDirectoryPath);
            }

            //Set up agent
            AidaAgent = CreateAgent();

            #endregion

            //Add welcoming message
            string opening_msg = "Hi, I'm AIDA, and I'm here to help! What can I do for you?";
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
                    AnsiConsole.MarkupLine("[bold]/settings[/] - Open AIDA's settings menu");
                    AnsiConsole.MarkupLine("[bold]/tools[/] - list all tools AIDA has available to it.");
                    AnsiConsole.MarkupLine("[bold]/auth[/] - authenticate into Foundry if using Service Principal.");
                    AnsiConsole.MarkupLine("[bold]/stats[/] - view usage statistics.");
                    Console.WriteLine();
                    goto Input;
                }
                if (input.ToLower() == "/settings")
                {
                    SettingsMenu();
                    Console.WriteLine();
                    goto Input;
                }
                else if (input.ToLower() == "/tools")
                {
                    AnsiConsole.MarkupLine("[underline]AIDA's Available Tools[/]");
                    foreach (ExecutableFunction ef in AidaAgent.Tools)
                    {
                        AnsiConsole.MarkupLine("[bold][blue]" + ef.Name + "[/][/] - [gray]" + ef.Description + "[/]");
                    }
                    Console.WriteLine();
                    goto Input;
                }
                else if (input.ToLower() == "/clear")
                {
                    AidaAgent = CreateAgent();
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
                    s.PrintReport(SessionInputTokens, SessionOutputTokens);
                    goto Input;
                }

                //Configure the agent's connection and tools before prompting
                await ConfigureAgentConnectionAsync(AidaAgent);
                RegisterTools(AidaAgent);

                //Track tokens before calling for stats later
                int prevInput = AidaAgent.InputTokensConsumed;
                int prevOutput = AidaAgent.OutputTokensConsumed;

                //Prompt the model, get response
                string response = null!;
                try
                {
                    response = await AidaAgent.PromptAsync(input);
                }
                catch (Exception ex)
                {
                    Console.WriteLine();
                    AnsiConsole.MarkupLine("[red]Uh oh! There was an issue when prompting the underlying model. Message: " + Markup.Escape(ex.Message) + "[/]");
                    Console.WriteLine();
                    Console.WriteLine();
                    AnsiConsole.Markup("[italic][gray]Press enter to try another input... [/][/]");
                    Console.ReadLine();
                }

                //If successfull, print
                if (response != null)
                {
                    //Print
                    PrintAIMessage(response, AIDASettings.Load().AssistantMessageColor);

                    //new line
                    Console.WriteLine();
                }
                
                //Log consumption (even on failure, tokens may have been consumed)
                int deltaInput = AidaAgent.InputTokensConsumed - prevInput;
                int deltaOutput = AidaAgent.OutputTokensConsumed - prevOutput;
                if (deltaInput > 0 || deltaOutput > 0)
                {
                    SessionInputTokens += deltaInput;
                    SessionOutputTokens += deltaOutput;

                    ConsumptionEvent ce = new ConsumptionEvent();
                    ce.Model = AidaAgent.Model;
                    ce.Timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                    ce.InputTokens = deltaInput;
                    ce.OutputTokens = deltaOutput;
                    Stats s = Stats.Load();
                    s.AddConsumptionEvent(ce);
                    s.Save();
                }

            } //END INFINITE CHAT

        }



        #region "UTILITIES"

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
            //LOAD SETTINGS TO USE AND UPDATE HERE
            AIDASettings SettingsToModify = AIDASettings.Load();

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
                if (SettingsToModify.FoundryUrl != null)
                {
                    AnsiConsole.MarkupLine("Foundry Resource: " + SettingsToModify.FoundryUrl);
                }
                else
                {
                    AnsiConsole.MarkupLine("Foundry Resource: [italic]none[/]");
                }

                //Foundry Auth
                if (SettingsToModify.ApiKey != null)
                {
                    AnsiConsole.MarkupLine("Auth Type: API Key");
                }
                else if (SettingsToModify.TenantID != null && SettingsToModify.ClientID != null && SettingsToModify.ClientSecret != null)
                {
                    AnsiConsole.MarkupLine("Auth Type: Access Token");
                }
                else
                {
                    AnsiConsole.MarkupLine("Auth Type: (unknown)");
                }

                //Model info
                if (SettingsToModify.ModelName != null)
                {
                    string verbosityText = SettingsToModify.VerbosityLevel != null ? SettingsToModify.VerbosityLevel.Value.ToString().ToLower() : "(unselected)";
                    string reasoningText = SettingsToModify.ReasoningEffortLevel != null ? SettingsToModify.ReasoningEffortLevel.Value.ToString().ToLower() : "(unselected)";
                    AnsiConsole.MarkupLine("Model: " + SettingsToModify.ModelName + " (verbosity: " + verbosityText + ", reasoning: " + reasoningText + ")");
                }
                else
                {
                    AnsiConsole.MarkupLine("Model: (none)");
                }

                //Assistant color
                AnsiConsole.MarkupLine("AI Assistant Msg Color: [bold]" + SettingsToModify.AssistantMessageColor + "[/] ([" + SettingsToModify.AssistantMessageColor + "]looks like this[/])");

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
                    SettingsToModify.FoundryUrl = AnsiConsole.Ask<string>("Foundry URL (i.e. https://myfoundry-resource.services.ai.azure.com)?");
    
                    //Ask how they want to authenticate
                    SelectionPrompt<string> FoundryAuthOptions = new SelectionPrompt<string>();
                    FoundryAuthOptions.Title("How do you want to authenticate?");
                    FoundryAuthOptions.AddChoice("API Key");
                    FoundryAuthOptions.AddChoice("Entra ID");
                    string FoundryAuthSelection = AnsiConsole.Prompt(FoundryAuthOptions);

                    //Handle auth
                    if (FoundryAuthSelection == "API Key")
                    {
                        SettingsToModify.ApiKey = AnsiConsole.Ask<string>("What is the API key?");

                        //Clear out any existing entra ID tokens
                        SettingsToModify.AuthenticatedTokenCredentials = null;

                        //Clear out the Entra ID info becuase now we wil use API key
                        SettingsToModify.TenantID = null;
                        SettingsToModify.ClientID = null;
                        SettingsToModify.ClientSecret = null;
                    }
                    else if (FoundryAuthSelection == "Entra ID")
                    {
                        SettingsToModify.TenantID = AnsiConsole.Ask<string>("Tenant ID?");
                        SettingsToModify.ClientID = AnsiConsole.Ask<string>("Client ID?");
                        SettingsToModify.ClientSecret = AnsiConsole.Ask<string>("Client Secret?");
                        
                        //Clear out any existing entra ID tokens
                        SettingsToModify.AuthenticatedTokenCredentials = null;

                        //Clear out any API key because now we will use Entra ID
                        SettingsToModify.ApiKey = null;
                    }
                }
                else if (SettingToDoAnswer == "Update Model")
                {
                    while (true)
                    {
                        Console.WriteLine();
                        string currentModel = Markup.Escape(SettingsToModify.ModelName ?? "(none)");
                        string currentVerbosity = SettingsToModify.VerbosityLevel != null ? Markup.Escape(SettingsToModify.VerbosityLevel.Value.ToString().ToLower()) : "(unselected)";
                        string currentReasoning = SettingsToModify.ReasoningEffortLevel != null ? Markup.Escape(SettingsToModify.ReasoningEffortLevel.Value.ToString().ToLower()) : "(unselected)";
                        AnsiConsole.MarkupLine("[underline]Model Settings[/]");
                        AnsiConsole.MarkupLine("Model: [bold]" + currentModel + "[/]");
                        AnsiConsole.MarkupLine("Verbosity: [bold]" + currentVerbosity + "[/]");
                        AnsiConsole.MarkupLine("Reasoning: [bold]" + currentReasoning + "[/]");
                        Console.WriteLine();

                        SelectionPrompt<string> modelSettingPrompt = new SelectionPrompt<string>();
                        modelSettingPrompt.Title("What would you like to update?");
                        modelSettingPrompt.AddChoice("Model Name");
                        modelSettingPrompt.AddChoice("Verbosity Level");
                        modelSettingPrompt.AddChoice("Reasoning Level");
                        modelSettingPrompt.AddChoice("Done");
                        string modelSettingSelection = AnsiConsole.Prompt(modelSettingPrompt);

                        if (modelSettingSelection == "Model Name")
                        {
                            SettingsToModify.ModelName = AnsiConsole.Ask<string>("Model name?");
                        }
                        else if (modelSettingSelection == "Verbosity Level")
                        {
                            SelectionPrompt<Verbosity> verbosityPrompt = new SelectionPrompt<Verbosity>();
                            verbosityPrompt.Title("What verbosity level do you want?");
                            verbosityPrompt.AddChoices(Enum.GetValues<Verbosity>());
                            SettingsToModify.VerbosityLevel = AnsiConsole.Prompt(verbosityPrompt);
                        }
                        else if (modelSettingSelection == "Reasoning Level")
                        {
                            SelectionPrompt<ReasoningEffortLevel> reasoningPrompt = new SelectionPrompt<ReasoningEffortLevel>();
                            reasoningPrompt.Title("What level of reasoning effort do you want?");
                            reasoningPrompt.AddChoices(Enum.GetValues<ReasoningEffortLevel>());
                            SettingsToModify.ReasoningEffortLevel = AnsiConsole.Prompt(reasoningPrompt);
                        }
                        else if (modelSettingSelection == "Done")
                        {
                            break;
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
                        SettingsToModify.AssistantMessageColor = NewColor;
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
                    if (SettingsToModify.WebSearchEnabled)
                    {
                        PackagesQuestion.Select("Web Search (built in)");
                    }
                    if (SettingsToModify.ShellEnabled)
                    {
                        PackagesQuestion.Select("Shell");
                    }

                    //Ask
                    List<string> PackagesToEnable = AnsiConsole.Prompt(PackagesQuestion);

                    //Enable/Disable: Web Search
                    if (PackagesToEnable.Contains("Web Search (built in)"))
                    {
                        SettingsToModify.WebSearchEnabled = true;
                    }
                    else
                    {
                        SettingsToModify.WebSearchEnabled = false;
                    }

                    //Enable/Disable: Shell
                    SettingsToModify.ShellEnabled = PackagesToEnable.Contains("Shell");

                    //Confirm
                    AnsiConsole.MarkupLine("[green][bold]" + PackagesToEnable.Count.ToString() + " packages enabled[/][/]");
                }
                else if (SettingToDoAnswer == "Save & Continue")
                {
                    AnsiConsole.Markup("[gray]Saving settings... [/]");
                    SettingsToModify.Save();
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

        #endregion

    }
}