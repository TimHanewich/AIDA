using System;
using CliWrap;
using CliWrap.Buffered;
using System.Runtime.InteropServices;
using Spectre.Console;
using TimHanewich.Foundry;

namespace AIDA
{
    public class Tools
    {
        public static string ConfigDirectoryPath
        {
            get
            {
                return System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AIDA");
            }
        }

        public static string CustomPromptPath
        {
            get
            {
                string ConfigDir = ConfigDirectoryPath;
                string prompt_path = Path.Combine(ConfigDir, "prompt.md");
                if (System.IO.File.Exists(prompt_path) == false) //if it doesnt exist, try to create a blank one
                {
                   try
                    {
                        File.Create(prompt_path).Close();
                    }
                    catch
                    {
                        
                    } 
                }
                return prompt_path;
            }
        }

        public static string GetSystemPrompt(AIDASettings current_settings)
        {
            List<string> SystemMessage = new List<string>();

            //System-injected prompt (AIDA info)
            SystemMessage.Add("You are AIDA, Artificial Intelligence Desktop Assistant. Your role is to be a friendly and helpful assistant. Speak in a playful, lighthearted, and fun manner.");
            SystemMessage.Add("Do not use emojis.");


            //Shell tool?
            if (current_settings.ShellEnabled)
            {

                string ShellSystemPrompt = @"
## Using the `shell` tool
You have a tool available to you called `shell`. This will execute a shell command on the host machine (cmd.exe for Windows or bash for Linux) and will return whatever it returns back (either standard output or standard error).

You can use this tool as needed to accomplish the goal that the user has given you. 

Non-Interactive Only: You cannot run interactive commands. Any command that requires user input (e.g., apt-get without -y, or ssh prompts) will hang and fail. Always use 'silent' or 'assume-yes' flags.

State Persistence: Each tool call is a fresh shell instance. Commands like cd will not persist across different tool calls. To run multiple commands in the same context, join them with &&.

Output Handling: Both success and error messages are returned. If a command fails, analyze the error output to troubleshoot and attempt a corrected command.
                
";

                //Add platform
                if (OnWindows())
                {
                    ShellSystemPrompt = ShellSystemPrompt + "The environment you are working in is Windows, so use cmd.exe friendly commands.";
                }
                else if (OnLinux())
                {
                    ShellSystemPrompt = ShellSystemPrompt + "The environment you are working in is Linux, so use bash friendly commands.";
                }

                //Add
                SystemMessage.Add(ShellSystemPrompt);
            }
            
            //Is there a custom prompt in prompt.md?
            if (System.IO.File.Exists(CustomPromptPath))
            {
                string custom_prompt = System.IO.File.ReadAllText(CustomPromptPath);
                if (custom_prompt != string.Empty)
                {
                    SystemMessage.Add("## Additional Information" + "\n" + custom_prompt);
                }
            }

            //Construct as one.
            string sysmsg = "";
            foreach (string s in SystemMessage)
            {
                sysmsg = sysmsg + s + "\n\n";
            }
            sysmsg = sysmsg.Substring(0, sysmsg.Length - 2);

            return sysmsg;
        }
            
        public static async Task<string> ExecuteShellAsync(string command)
        {
            //Set up command
            Command cmd;
            if (OnWindows())
            {
                cmd = Cli.Wrap("cmd.exe");
                cmd = cmd.WithArguments("/c " + command);
            }
            else if (OnLinux())
            {
                cmd = Cli.Wrap("/bin/bash");
                cmd = cmd.WithArguments("-c \"" + command + "\"");
            }
            else
            {
                throw new Exception("Unable to execute shell script... Unable to determine if on Windows or Linux.");
            }
            
            //Do not fail if there is an issue
            cmd = cmd.WithValidation(CommandResultValidation.None); //do not throw an exception if there is an issue

            //Execute
            BufferedCommandResult bcr = await cmd.ExecuteBufferedAsync();

            //Collect results
            string ToReturn = string.Empty;
            if (bcr.StandardOutput != "" && bcr.StandardError != "")
            {
                ToReturn = "OUTPUT:" + "\n" + bcr.StandardError + "\n\n" + "ERRORS:" + "\n" + bcr.StandardError;
            }
            else if (bcr.StandardOutput != "")
            {
                ToReturn = bcr.StandardOutput;
            }
            else if (bcr.StandardError != "")
            {
                ToReturn = bcr.StandardError;
            }

            return ToReturn;
        }

        public static async Task FoundryAuthAsync(AIDASettings current_settings)
        {
            //If it is set up for API-Key auth, void out
            if (current_settings.ApiKey != null)
            {
                AnsiConsole.MarkupLine("[yello]Authentication to Foundry not needed - API key access is selected (not Service Principal).[/]");
                return;
            }
            else if (current_settings.TenantID == null || current_settings.ClientID == null || current_settings.ClientSecret == null)
            {
                AnsiConsole.MarkupLine("[red]Cannot authenticate to Foundry without a TenantID, ClientID, and ClientSecret! You must provide those.[/]");
                return;   
            }

            //Clear out old auth
            AnsiConsole.Markup("Clearing out token... ");
            current_settings.AuthenticatedTokenCredentials = null;
            AnsiConsole.MarkupLine("cleared.");


            //Authenticate now
            AnsiConsole.MarkupLine("Going to attempt to request access token for tenant '" + current_settings.TenantID + "' and client '" + current_settings.ClientID + "' with secret of " + current_settings.ClientSecret.Length.ToString() + " characters.");
            AnsiConsole.Markup("Requesting new access token... ");
            EntraAuthenticationHandler auth = new EntraAuthenticationHandler();
            auth.TenantID = current_settings.TenantID;
            auth.ClientID = current_settings.ClientID;
            auth.ClientSecret = current_settings.ClientSecret;
            try
            {
                current_settings.AuthenticatedTokenCredentials = await auth.AuthenticateAsync();
                AnsiConsole.MarkupLine("[green]success![/]");
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine("[red]Authentication failed! Msg: " + Markup.Escape(ex.Message) + "[/]");
            }

            //If it was successful
            if (current_settings.AuthenticatedTokenCredentials != null)
            {
                TimeSpan UntilExpiration = current_settings.AuthenticatedTokenCredentials.Expires - DateTime.UtcNow;
                AnsiConsole.MarkupLine("[gray][italic]expires: " + current_settings.AuthenticatedTokenCredentials.Expires.ToLocalTime().ToString() + " (in " + UntilExpiration.TotalHours.ToString("#,##0.0") + " hours)[/][/]");

                //Save
                AnsiConsole.Markup("Saving... ");
                current_settings.Save();
                AnsiConsole.MarkupLine("saved.");
            }

            //Line break
            Console.WriteLine();
        }



        //Are we on windows?
        public static bool OnWindows()
        {
            return RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        }

        //Are we on linux?
        public static bool OnLinux()
        {
            return RuntimeInformation.IsOSPlatform(OSPlatform.Linux);
        }
    }
}