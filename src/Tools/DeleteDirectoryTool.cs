using TimHanewich.AgentFramework;
using TimHanewich.Foundry.OpenAI.Responses;
using Newtonsoft.Json.Linq;
using Spectre.Console;

namespace AIDA
{
    public class DeleteDirectoryTool : ExecutableFunction
    {
        public DeleteDirectoryTool()
        {
            Name = "delete_directory";
            Description = "Delete an empty directory (folder) from the user's computer. The directory must be empty before it can be deleted.";
            InputParameters.Add(new FunctionInputParameter("path", "The path of the directory to delete."));
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
            if (System.IO.Directory.Exists(path) == false)
            {
                return "Directory at '" + path + "' does not exist!";
            }
            if (System.IO.Directory.GetFiles(path).Length > 0 || System.IO.Directory.GetDirectories(path).Length > 0)
            {
                return "Directory at '" + path + "' is not empty! You must delete all files and sub-directories first using the `delete_file` tool before you can delete this directory.";
            }

            AnsiConsole.Markup("[gray][italic]deleting directory '" + Markup.Escape(path) + "'... [/][/]");
            try
            {
                System.IO.Directory.Delete(path);
                AnsiConsole.MarkupLine("[gray][italic]done[/][/]");
                return "Directory '" + path + "' was successfully deleted.";
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine("[gray][italic]failed[/][/]");
                return "Deletion of directory failed. Exception message: " + ex.Message;
            }
        }
    }
}
