using TimHanewich.AgentFramework;
using TimHanewich.Foundry.OpenAI.Responses;
using Newtonsoft.Json.Linq;
using Spectre.Console;

namespace AIDA
{
    public class ShellTool : ExecutableFunction
    {
        public ShellTool()
        {
            Name = "shell";
            Description = "Executes a single shell command on the host machine (Windows/cmd.exe or Linux/bash) and returns the combined standard output and standard error. Use this for file system operations, running compilers, or checking system status.";
            InputParameters.Add(new FunctionInputParameter("command", "The shell command to execute."));
        }

        public override async Task<string> ExecuteAsync(JObject? arguments = null)
        {
            string? cmd = null;
            if (arguments != null)
            {
                JProperty? prop = arguments.Property("command");
                if (prop != null) cmd = prop.Value.ToString();
            }

            if (cmd == null)
            {
                return "You must provide the 'command' parameter! It wasn't provided.";
            }

            AnsiConsole.Markup("[gray][italic]running shell '" + Markup.Escape(cmd) + "'... [/][/]");
            string response = await Tools.ExecuteShellAsync(cmd);
            AnsiConsole.MarkupLine("[gray][italic]done[/][/]");
            return response;
        }
    }
}
