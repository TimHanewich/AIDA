using TimHanewich.AgentFramework;
using TimHanewich.Foundry.OpenAI.Responses;
using Newtonsoft.Json.Linq;
using Spectre.Console;

namespace AIDA
{
    public class CreateDirectoryTool : ExecutableFunction
    {
        public CreateDirectoryTool()
        {
            Name = "create_directory";
            Description = "Create a new directory (folder) on the user's computer.";
            InputParameters.Add(new FunctionInputParameter("path", "The path of the directory to create."));
        }

        public override async Task<string> ExecuteAsync(JObject? arguments = null)
        {
            string? path = null;
            if (arguments != null)
            {
                JProperty? prop = arguments.Property("path");
                if (prop != null) path = prop.Value.ToString();
            }

            if (path == null)
            {
                return "You must provide the 'path' parameter!";
            }
            if (System.IO.Directory.Exists(path))
            {
                return "Directory at '" + path + "' already exists!";
            }

            AnsiConsole.Markup("[gray][italic]creating directory '" + Markup.Escape(path) + "'... [/][/]");
            try
            {
                System.IO.Directory.CreateDirectory(path);
                AnsiConsole.MarkupLine("[gray][italic]done[/][/]");
                return "Directory '" + path + "' was successfully created.";
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine("[gray][italic]failed[/][/]");
                return "Creation of directory failed. Exception message: " + ex.Message;
            }
        }
    }
}
